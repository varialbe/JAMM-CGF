using Jamm.Core.Building;
using Jamm.Core.Management;
using Jamm.Core.Patching;
using UndertaleModLib.Models;

namespace Jamm.Core.Browsing;

/// <summary>Only proven archive facts, tied to exact input bytes and analyzer revision.</summary>
public sealed record ModFacts(string FileHash, string BaseHash, int AnalyzerVersion, string Compatibility, string Reason, string[] Adds);

/// <summary>Analyzes read-only archives through the existing semantic patch/build pipeline.</summary>
public sealed class ModFactAnalyzer(ManagerStore store)
{
    /// <summary>Invalidates cached facts when extraction or compatibility rules change.</summary>
    public const int Version = 1;

    /// <summary>Computes facts without importing or enabling a mod.</summary>
    public ModFacts Analyze(string archive, CancellationToken token)
    {
        string basePath = store.Config().Base ?? throw new InvalidOperationException("Choose a game base in Settings to check content.");
        string hash = BuildFiles.Hash(archive), baseHash = BuildFiles.Hash(basePath);
        string cache = store.Path("analysis", $"{hash}-{baseHash}-{Version}.json");
        if (File.Exists(cache))
        {
            try { return store.Read<ModFacts>(cache); }
            catch (InvalidDataException) { File.Delete(cache); }
        }
        token.ThrowIfCancellationRequested();
        using var original = GameFileLoader.Load(basePath);
        using var mod = GameFileLoader.Load(archive);
        ModFacts result;
        if (mod.Inspection.IsYyc || !mod.Inspection.BytecodeSupported)
            result = new(hash, baseHash, Version, "Incompatible", "This game uses an unsupported engine or has no editable game code.", []);
        else
        {
            ModPatch patch;
            try { patch = PatchBuilder.Create(original, mod, "Content check", _ => token.ThrowIfCancellationRequested()); }
            catch (Exception ex) when (ex is InvalidDataException or NotComparableException or ArgumentException)
            {
                result = new(hash, baseHash, Version, "Incompatible", "The patch cannot apply: " + ex.Message, []);
                token.ThrowIfCancellationRequested(); store.Write(cache, result); return result;
            }
            var facts = Extract(patch, mod.Data.GameObjects);
            string output = store.Path("analysis", Guid.NewGuid().ToString("N") + ".win");
            try
            {
                ArchiveBuilder.Build(basePath, [new(patch, archive)], output, cancellationToken: token);
                bool match = original.Inspection.Timestamp == mod.Inspection.Timestamp
                    && original.Inspection.GameMakerVersion == mod.Inspection.GameMakerVersion
                    && original.Inspection.BytecodeVersion == mod.Inspection.BytecodeVersion;
                result = new(hash, baseHash, Version, match ? "Compatible" : "Unsure", match
                    ? "Patch built and verified against the configured base; engine and build information match."
                    : "Patch built and verified against this base, but the source build differs. Test in the separate game copy."
                        + (mod.Inspection.Timestamp == 1692057761 ? " The source uses the 2023-08-15 build." : ""), facts);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) when (ex is InvalidDataException or NotComparableException or ArgumentException)
            { result = new(hash, baseHash, Version, "Incompatible", "The patch cannot apply: " + ex.Message, facts); }
            finally { if (File.Exists(output)) File.Delete(output); }
        }
        token.ThrowIfCancellationRequested(); store.Write(cache, result); return result;
    }

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
