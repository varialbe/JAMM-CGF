using System.Text.Json.Serialization;
using Jamm.Core.Building;
using Jamm.Core.Management;
using Jamm.Core.Patching;
using UndertaleModLib.Models;

namespace Jamm.Core.Browsing;

/// <summary>Only proven archive facts, tied to exact input bytes and analyzer revision.</summary>
public sealed record ModFacts(string FileHash, string BaseHash, int AnalyzerVersion, string Compatibility, string Reason, string[] Adds,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<SanitizedChange>? RemovedPerformanceChanges = null);

/// <summary>Analyzes read-only archives through the existing semantic patch/build pipeline.</summary>
public sealed class ModFactAnalyzer(ManagerStore store, AnalysisOptions? options = null)
{
    /// <summary>Invalidates cached facts when extraction, sanitizer or compatibility rules change.</summary>
    public const int Version = 6;

    /// <summary>Computes facts without importing or enabling a mod.</summary>
    public ModFacts Analyze(string archive, CancellationToken token) => Analyze(archive, token, null);

    /// <summary>Computes facts while reporting cancellable pipeline steps.</summary>
    public ModFacts Analyze(string archive, CancellationToken token, Action<string>? progress)
    {
        string basePath = store.Config().Base ?? throw new InvalidOperationException("Choose a game base in Settings to check content.");
        progress?.Invoke("Fingerprinting game content");
        token.ThrowIfCancellationRequested();
        string hash = BuildFiles.Hash(archive), baseHash = BuildFiles.Hash(basePath);
        string cache = store.Path("analysis", $"{hash}-{baseHash}-{Version}.json");
        if (File.Exists(cache))
        {
            try { return store.Read<ModFacts>(cache); }
            catch (InvalidDataException) { File.Delete(cache); }
        }
        token.ThrowIfCancellationRequested();
        progress?.Invoke("Reading game base");
        using var original = GameFileLoader.Load(basePath, cancellationToken: token);
        progress?.Invoke("Reading mod archive");
        using var mod = GameFileLoader.Load(archive, cancellationToken: token);
        ModFacts result;
        if (mod.Inspection.IsYyc || !mod.Inspection.BytecodeSupported)
            result = new(hash, baseHash, Version, "Incompatible", "This game uses an unsupported engine or has no editable game code.", []);
        else
        {
            ModPatch patch;
            try { patch = PatchBuilder.Create(original, mod, "Content check", message => { token.ThrowIfCancellationRequested(); progress?.Invoke(message); }, (options ?? AnalysisOptions.Default) with { DataDirectory = store.Root }); }
            catch (Exception ex) when (ex is InvalidDataException or NotComparableException or ArgumentException)
            {
                result = new(hash, baseHash, Version, "Incompatible", "The patch cannot apply: " + ex.Message, []);
                result = WithSource(result, mod.Inspection.Timestamp);
                token.ThrowIfCancellationRequested(); store.Write(cache, result); return result;
            }
            var sanitized = ModSanitizer.Sanitize(patch);
            patch = sanitized.Patch;
            var removedChanges = sanitized.Report.Removed.Count > 0 ? sanitized.Report.Removed : null;
            var facts = Extract(patch, mod.Data.GameObjects);
            string output = store.Path("analysis", Guid.NewGuid().ToString("N") + ".win");
            try
            {
                ArchiveBuilder.Build(basePath, [new(patch, archive)], output, progress: progress, cancellationToken: token, options: (options ?? AnalysisOptions.Default) with { DataDirectory = store.Root });
                // A successful verified build proves applicability. GEN8 timestamps alone do not
                // establish a different code ancestor: mods necessarily change game code.
                string date = SourceDate(mod.Inspection.Timestamp);
                bool legacyTimestampOnly = original.Inspection.Timestamp == mod.Inspection.Timestamp
                    || original.Inspection.BytecodeVersion == 16 && original.Inspection.GameMakerVersion == mod.Inspection.GameMakerVersion
                    && SourceDate(original.Inspection.Timestamp).StartsWith("2023-", StringComparison.Ordinal)
                    && date.StartsWith("2023-", StringComparison.Ordinal);
                result = new(hash, baseHash, Version, legacyTimestampOnly ? "Compatible" : "Unsure",
                    "Patch built and verified against the configured base. Source build: " + date
                    + (legacyTimestampOnly ? " (legacy build; GEN8 timestamp differences alone do not imply incompatibility)."
                        : " (source differs from the known legacy build; code ancestry cannot be established)."), facts, removedChanges);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) when (ex is InvalidDataException or NotComparableException or ArgumentException)
            { result = new(hash, baseHash, Version, ToolFailure(ex) ? "Tool limitation" : "Incompatible",
                (ToolFailure(ex) ? "JAMM could not process this mod (tool limitation): " : "The patch cannot apply: ") + ex.Message, facts, removedChanges); }
            finally { if (File.Exists(output)) File.Delete(output); }
        }
        result = WithSource(result, mod.Inspection.Timestamp);
        token.ThrowIfCancellationRequested(); store.Write(cache, result); return result;
    }

    private static ModFacts WithSource(ModFacts facts, ulong timestamp) => facts.Reason.Contains("Source build:", StringComparison.Ordinal)
        ? facts : facts with { Reason = facts.Reason + " Source build: " + SourceDate(timestamp) + "." };
    private static bool ToolFailure(Exception ex) => ex.Message.Contains("compil", StringComparison.OrdinalIgnoreCase)
        || ex.Message.Contains("semantic reference", StringComparison.OrdinalIgnoreCase)
        || ex.Message.Contains("is referenced but has pixels and no embedded page", StringComparison.Ordinal);
    private static string SourceDate(ulong timestamp) => timestamp <= 253402300799
        ? DateTimeOffset.FromUnixTimeSeconds((long)timestamp).ToString("yyyy-MM-dd HH:mm 'UTC'") : "unknown (invalid GEN8 timestamp)";

    /// <summary>Counts added resources and fish proven by an exact Fish ancestor; no name heuristics.</summary>
    public static string[] Extract(ModPatch patch, IEnumerable<UndertaleGameObject> objects)
    {
        List<string> facts = [];
        var added = patch.Changes["objects"].Added.Select(a => a.Name).ToHashSet(StringComparer.Ordinal);
        int fish = objects.Count(o => added.Contains(o.Name.Content) && DescendsFromFish(o));
        if (fish > 0) facts.Add($"{fish} fish species");
        foreach (var (family, label) in new[] { ("rooms", "rooms / areas"), ("sprites", "sprites"), ("sounds", "sounds"), ("objects", "game objects") })
            if (patch.Changes[family].Added.Count is > 0 and var count) facts.Add($"{count} {label}");
        if (patch.Changes["code"].Changed.Count is > 0 and var changed) facts.Add($"{changed} changed game code entries");
        return facts.ToArray();
    }
    private static bool DescendsFromFish(UndertaleGameObject item)
    {
        HashSet<UndertaleGameObject> seen = [];
        for (var parent = item.ParentId; parent is not null && seen.Add(parent); parent = parent.ParentId)
            if (parent.Name.Content == "Fish") return true;
        return false;
    }
}
