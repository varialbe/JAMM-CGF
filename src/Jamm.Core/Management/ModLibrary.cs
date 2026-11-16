using System.IO.Compression;
using System.Text.Json;
using Jamm.Core.Building;
using Jamm.Core.Patching;

namespace Jamm.Core.Management;

/// <summary>Imports immutable local mod copies, computes patches and reports exact-base compatibility.</summary>
public sealed class ModLibrary(ManagerStore store)
{
    internal string Archive(ManagedMod mod) => store.Path("mods", ManagerStore.Name(mod.Id), "data.win");
    internal string PatchPath(ManagedMod mod) => store.Path("patches", ManagerStore.Name(mod.Id) + ".cgfmm-patch.json");

    /// <summary>Reads a cached patch only when both complete archive identities still match.</summary>
    public ModPatch Patch(ManagedMod mod)
    {
        var patch = PatchJson.Deserialize(File.ReadAllText(PatchPath(mod)));
        if (patch.Base.Sha256 != mod.BaseSha256 || patch.Mod.Sha256 != mod.Sha256 || patch.Mod.Name != mod.Name
            || BuildFiles.Hash(Archive(mod)) != mod.Sha256)
            throw new InvalidDataException($"Mod '{mod.Name}' or its patch changed. Remove and add the original mod again.");
        return patch;
    }

    internal IReadOnlyList<ManagedMod> All() => Directory.EnumerateDirectories(store.Path("mods"))
        .Where(p => !Path.GetFileName(p).StartsWith('.')).Order(StringComparer.Ordinal)
        .Select(p => ReadMod(Path.GetFileName(p))).ToArray();

    /// <summary>Reads library identities and provenance without loading game archives.</summary>
    public IReadOnlyList<ManagedMod> Metadata() => All();

    private ManagedMod ReadMod(string id)
    {
        ManagerStore.Name(id);
        var mod = store.Read<ManagedMod>(store.Path("mods", id, "mod.json"));
        if (mod.Id != id || string.IsNullOrWhiteSpace(mod.Name) || string.IsNullOrWhiteSpace(mod.Sha256))
            throw new InvalidDataException($"Library metadata for '{id}' is invalid. Remove the damaged entry and add the source mod again.");
        return mod;
    }

    /// <summary>Finds a mod by exact ID, unique display name or source filename stem.</summary>
    public ManagedMod Find(string query)
    {
        var all = All();
        var exact = all.FirstOrDefault(m => m.Id == query);
        if (exact is not null) return exact;
        var matches = all.Where(m => m.Name == query || Path.GetFileNameWithoutExtension(m.SourceFileName) == query).ToArray();
        return matches.Length == 1 ? matches[0] : throw new ArgumentException(matches.Length == 0
            ? $"Mod '{query}' was not found. Use 'cgfmm list' to see library IDs."
            : $"Mod name '{query}' is ambiguous. Use its full ID from 'cgfmm list'.");
    }

    /// <summary>Copies a .win or the single nested data.win in a zip, then immediately patches against the configured base.</summary>
    public AddModResult Add(string source, string? name = null, Action<string>? progress = null, CancellationToken cancellationToken = default, ModOrigin? origin = null)
    {
        var reportProgress = progress;
        progress = message => { cancellationToken.ThrowIfCancellationRequested(); reportProgress?.Invoke(message); };
        cancellationToken.ThrowIfCancellationRequested();
        string basePath = store.Config().Base ?? throw new ArgumentException("Set the legacy base first: cgfmm config set base <original data.win>.");
        string sourceName = Path.GetFileName(source);
        string displayName = name ?? Path.GetFileNameWithoutExtension(source);
        if (string.IsNullOrWhiteSpace(displayName)) throw new ArgumentException("The mod needs a nonempty name.");
        if (displayName.Contains(',') || displayName.Any(char.IsControl))
            throw new ArgumentException("Mod names cannot contain commas or control characters. Use --name with a readable name so it can be selected with --mods.");
        string stage = store.Path("mods", ".import-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(stage);
        string copy = Path.Combine(stage, "data.win");
        string? publishedPatch = null;
        try
        {
            if (source.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)) CopyZip(source, copy, cancellationToken);
            else if (source.EndsWith(".win", StringComparison.OrdinalIgnoreCase))
            {
                using var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read);
                using var output = new FileStream(copy, FileMode.CreateNew, FileAccess.Write);
                input.CopyTo(output);
            }
            else throw new ArgumentException($"Cannot add '{sourceName}'. Choose a .win archive or a .zip containing one data.win.");
            string hash = BuildFiles.Hash(copy);
            var existing = All().FirstOrDefault(m => m.Sha256 == hash);
            if (existing is not null)
            {
                Patch(existing);
                if (existing.BaseSha256 != BuildFiles.Hash(basePath))
                    throw new InvalidDataException($"'{existing.Name}' was patched against another base. Restore that base configuration, or remove this mod from its profiles and re-add it.");
                if (origin is not null && !existing.Origins.Contains(origin))
                {
                    existing = existing with { Origins = [.. existing.Origins, origin] };
                    cancellationToken.ThrowIfCancellationRequested();
                    store.Write(store.Path("mods", existing.Id, "mod.json"), existing);
                }
                return new(existing, ["This exact archive is already in the library; its existing name is retained."], true);
            }
            if (All().Any(m => m.Name == displayName)) throw new ArgumentException($"Mod name '{displayName}' already exists. Use --name with a unique name.");
            using var original = GameFileLoader.Load(basePath, progress);
            using var mod = GameFileLoader.Load(copy, progress);
            List<string> notices = CompatibilityWarnings(original.Inspection, mod.Inspection).ToList();
            ModPatch patch;
            try { patch = PatchBuilder.Create(original, mod, displayName, progress, AnalysisOptions.Default with { DataDirectory = store.Root }); }
            catch (Exception ex) when (ex is ArgumentException or NotComparableException or InvalidDataException)
            { throw new InvalidDataException($"Cannot add '{sourceName}': {string.Join(" ", notices)} {ex.Message} Choose a compatible legacy VM mod and base.", ex); }
            // Patch identity describes the .win bytes; metadata separately retains the supplied zip filename.
            patch = patch with { Mod = patch.Mod with { FileName = source.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) ? "data.win" : sourceName } };
            if (BuildFiles.Hash(basePath) != patch.Base.Sha256 || hash != patch.Mod.Sha256)
                throw new IOException("An input changed during import. Retry with stable copies.");
            var entry = new ManagedMod(hash, displayName, sourceName, hash, DateTimeOffset.UtcNow, patch.Base.Sha256,
                patch.Mod.Gen8Timestamp, patch.Mod.GmVersion, patch.Mod.Bytecode) { Origins = origin is null ? [] : [origin] };
            store.Write(store.Path("mods", Path.GetFileName(stage), "mod.json"), entry);
            string patchPath = PatchPath(entry);
            if (File.Exists(patchPath)) throw new IOException("An orphaned patch already exists for this mod. Remove it from the data directory and retry.");
            publishedPatch = patchPath;
            store.WriteText(publishedPatch, PatchJson.Serialize(patch));
            cancellationToken.ThrowIfCancellationRequested();
            Directory.Move(stage, store.Path("mods", entry.Id));
            return new(entry, notices.Concat(patch.Warnings).Distinct(StringComparer.Ordinal).ToArray(), false);
        }
        catch
        {
            if (publishedPatch is not null && Directory.Exists(stage) && File.Exists(publishedPatch)) File.Delete(publishedPatch);
            throw;
        }
        finally { if (Directory.Exists(stage)) Directory.Delete(stage, true); }
    }

    /// <summary>Lists timestamp/version compatibility separately from exact patch-base hash compatibility.</summary>
    public IReadOnlyList<ModListEntry> List()
    {
        var all = All();
        if (all.Count == 0) return [];
        string? basePath = store.Config().Base;
        using var original = basePath is null ? null : GameFileLoader.Load(basePath);
        return all.Select(m =>
        {
            bool hash = m.BaseSha256 == original?.Inspection.Sha256;
            bool timestamp = m.Gen8Timestamp == original?.Inspection.Timestamp;
            bool version = m.GmVersion == original?.Inspection.GameMakerVersion && m.Bytecode == original?.Inspection.BytecodeVersion;
            bool cached;
            try { cached = hash && Patch(m).Base.Sha256 == m.BaseSha256; }
            catch (Exception ex) when (ex is IOException or JsonException or InvalidDataException or NotSupportedException) { cached = false; }
            return new ModListEntry(m, hash, timestamp, version, cached, original is null ? "base not configured"
                : !hash ? "patch targets another base; re-add against this base"
                : !version ? "incompatible GameMaker/bytecode version"
                : !timestamp ? "patched against configured base; source build timestamp differs" : "matching base hash and source timestamp/version");
        }).ToArray();
    }

    /// <summary>Removes an unused mod and its patch; profiles must be deleted first if they use it.</summary>
    public void Remove(string query)
    {
        var mod = Find(query);
        string[] used = new ProfileLibrary(store).List().Where(p => p.Mods.Contains(mod.Id, StringComparer.Ordinal)).Select(p => p.Name).ToArray();
        if (used.Length > 0) throw new IOException($"Mod '{mod.Name}' is used by profiles: {string.Join(", ", used)}. Delete those profiles before removing it.");
        string directory = store.Path("mods", mod.Id);
        // Refuse linked contents before recursive deletion, too.
        foreach (string path in Directory.EnumerateFileSystemEntries(directory))
        {
            BuildFiles.RefuseStorageLink(path);
            if (Directory.Exists(path)) throw new IOException($"Mod '{mod.Name}' contains an unexpected directory. Move it out of the library entry before removing the mod.");
        }
        File.Delete(PatchPath(mod));
        Directory.Delete(directory, true);
    }

    internal static void CopyZip(string source, string destination, CancellationToken cancellationToken)
    {
        using var zip = ZipFile.OpenRead(source);
        List<ZipArchiveEntry> candidates = [];
        long expanded = 0;
        const long limit = 512L * 1048576;
        if (zip.Entries.Count > 4096) throw new InvalidDataException("ZIP has too many entries. Choose a smaller mod archive.");
        foreach (var entry in zip.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (entry.Length > limit - expanded) throw new InvalidDataException("ZIP expanded data exceeds the 512 MB safety limit.");
            expanded += entry.Length;
            string normalized = entry.FullName.Replace('\\', '/');
            if (normalized.StartsWith('/') || normalized.Contains(':') || normalized.Split('/').Any(p => p == "..")
                || (entry.ExternalAttributes >> 16 & 0xF000) == 0xA000)
                throw new InvalidDataException($"Unsafe zip entry '{entry.FullName}' in '{Path.GetFileName(source)}'. Choose a zip without absolute paths, parent traversal or symbolic links.");
            if (normalized.Split('/').Last().Equals("data.win", StringComparison.OrdinalIgnoreCase)) candidates.Add(entry);
        }
        if (candidates.Count != 1) throw new InvalidDataException($"'{Path.GetFileName(source)}' must contain exactly one data.win (nested folders are supported); found {candidates.Count}.");
        using var input = candidates[0].Open();
        using var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write);
        byte[] buffer = new byte[65536]; long copied = 0;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int count = input.Read(buffer); if (count == 0) break; copied += count;
            if (copied > limit) throw new InvalidDataException("Expanded data.win exceeds the 512 MB safety limit.");
            output.Write(buffer, 0, count);
        }
    }

    private static IEnumerable<string> CompatibilityWarnings(GameInspection original, GameInspection mod)
    {
        if (original.Timestamp == mod.Timestamp && original.GameMakerVersion == mod.GameMakerVersion && original.BytecodeVersion == mod.BytecodeVersion) yield break;
        string manifest = mod.Timestamp switch
        {
            1692207312 => "Expected Steam depot 343782 manifest 9039707068725406600 (16 Aug 2023).",
            1692057761 => "Expected Steam depot 343782 manifest 1512054085756769140 (15 Aug 2023 fast); public 2811289735487427888 is near-identical.",
            _ => "See docs/legacy-version.md to locate the matching Steam manifest."
        };
        yield return $"Mod source GEN8 timestamp {mod.Timestamp}, GameMaker {mod.GameMakerVersion}, bytecode {mod.BytecodeVersion} differs from configured base {original.Timestamp}, {original.GameMakerVersion}, bytecode {original.BytecodeVersion}. {manifest} Timestamp differences are included in the patch against the configured base.";
    }
}
