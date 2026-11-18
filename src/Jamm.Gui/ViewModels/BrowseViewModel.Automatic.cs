using System.Text.Json;
using Jamm.Core.Browsing;
using Jamm.Core.Building;
using Jamm.Core.Management;

namespace Jamm.Gui.ViewModels;

public sealed partial class BrowseViewModel
{
    /// <summary>Loads shared verdicts in bulk, then uses cached local facts without analyzing archives.</summary>
    public async Task ApplyAutomaticFactsAsync(CancellationToken token = default)
    {
        if (store.Config().Base is not { } path || !File.Exists(path)) return;
        string hash = await Task.Run(() => BuildFiles.Hash(path), token);
        var index = await new SharedCompatibility(store, http).LoadIndexAsync(hash, token);
        var metadata = await Task.Run(() => new ModLibrary(store).Metadata().ToArray(), token);
        ModFacts? Cached(ManagedMod mod)
        {
            if (mod.BaseSha256 != hash) return null;
            string file = Path.Combine(store.Root, "analysis", $"{mod.Sha256}-{hash}-{ModFactAnalyzer.Version}.json");
            try
            {
                var facts = File.Exists(file) ? JsonSerializer.Deserialize<ModFacts>(File.ReadAllText(file), new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) : null;
                return facts?.FileHash == mod.Sha256 && facts.BaseHash == hash && facts.AnalyzerVersion == ModFactAnalyzer.Version ? facts : null;
            }
            catch (Exception ex) when (ex is IOException or JsonException) { return null; }
        }
        foreach (var mod in metadata)
        {
            var check = index?.Checks.FirstOrDefault(c => c.Facts.FileHash == mod.Sha256)
                ?? index?.Checks.FirstOrDefault(c => mod.Origins.Any(o => o.Provider == c.Provider && o.FileId == c.FileId));
            var facts = check?.Facts.FileHash == mod.Sha256 ? check.Facts : Cached(mod) ?? check?.Facts;
            if (facts is null) continue;
            var row = owner.Mods.FirstOrDefault(r => r.Id == mod.Id);
            if (row is not null) row.Summary = (facts.Adds.Length == 0 ? "No proven additions." : "Adds\n" + string.Join("\n", facts.Adds))
                + "\nCompatibility: " + facts.Compatibility + ": " + facts.Reason;
        }
        foreach (var row in all)
        {
            row.Adds = "Content summary unavailable. Select to check.";
            row.CompatibilityIcon = "?"; row.CompatibilityColor = "#D6A500";
            row.CompatibilityReason = index is null
                ? "Unsure: no current shared compatibility export is available for this game base. Select this mod to check its download locally."
                : "Unsure: no analyzer 6 result for this mod in the current shared export. Select this mod to check its download locally.";
            var local = metadata.Where(m => m.Origins.Any(o => o.Catalog == row.Mod.Catalog && o.ModId == row.Mod.Id)).ToArray();
            // Only use catalog IDs explicitly supplied by the publisher; never display-name matches.
            var checks = index?.Checks.Where(c => c.ExternalId == row.Mod.Id || local.Any(m => m.Sha256 == c.Facts.FileHash
                || m.Origins.Any(o => o.Provider == c.Provider && o.FileId == c.FileId))).ToArray() ?? [];
            if (checks.Length == 0 && index is { Checks.Length: > 0 })
            {
                try
                {
                    var resolution = await resolver.ResolveAsync(row.Mod, token);
                    var chosen = ModFileSelection.Choose(resolution.Files);
                    if (chosen is not null) checks = index.Checks.Where(c => c.Provider == chosen.Provider && c.FileId == chosen.Id).ToArray();
                }
                catch (Exception ex) when (ex is HttpRequestException or IOException or InvalidDataException or TimeoutException) { }
            }
            ModFacts? facts = checks.Length == 1 ? checks[0].Facts : null;
            bool localFacts = false;
            if (local.Length == 1 && Cached(local[0]) is { } cached && (facts is null || facts.FileHash != local[0].Sha256))
            { facts = cached; localFacts = true; }
            if (facts is not null) ShowFacts(row, facts, checks.Length == 1 && !localFacts ? "checked by JAMM server" : "checked on this PC");
            if (local.Length == 1 && !row.HasSanitizedInfo && owner.Mods.FirstOrDefault(m => m.Id == local[0].Id) is { HasSanitizedInfo: true } localRow)
            {
                row.RemovedPerformanceCount = localRow.RemovedPerformanceCount;
                row.RemovedHudCount = localRow.RemovedHudCount;
                row.RemovedGameplayCount = localRow.RemovedGameplayCount;
                row.SanitizedDetails = localRow.SanitizedDetails;
            }
        }
    }
}
