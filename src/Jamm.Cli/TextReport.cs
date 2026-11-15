using System.Text;
using Jamm.Core;

namespace Jamm.Cli;

internal static class TextReport
{
    private static string Safe(string text) => text.Replace("\r", " ").Replace("\n", " ").Replace("\u001b", "");
    private static string Md(string text) => Safe(text).Replace("&", "&amp;").Replace("<", "&lt;")
        .Replace(">", "&gt;").Replace("|", "&#124;").Replace("`", "&#96;");
    private static string Yes(bool value) => value ? "yes" : "NO";
    private static string Mode(GameInspection info) => info.IsYyc
        ? "YYC / no bytecode: merging is impossible (no bytecode)"
        : !info.BytecodeSupported ? "VM: merging is impossible with this unsupported bytecode version"
        : "VM (bytecode available)";

    internal static string Inspection(FileAnalysis file)
    {
        StringBuilder text = new();
        if (file.Inspection is { } i)
        {
            text.AppendLine($"File: {Safe(i.FileName)}");
            text.AppendLine($"Size: {i.FileSize:N0} bytes  SHA-256: {i.Sha256}");
            text.AppendLine($"Game: {Safe(i.GameName)}  Display name: {Safe(i.DisplayName)}");
            text.AppendLine($"GameMaker GEN8: {i.GameMakerVersion}");
            if (i.DetectedGameMakerVersion != i.GameMakerVersion)
                text.AppendLine($"UTML detected: {i.DetectedGameMakerVersion} ({i.DetectedBranch}; format lower bound)");
            text.AppendLine($"Bytecode: {i.BytecodeVersion}  Build: {i.GameMakerVersion.Split('.')[^1]}  Timestamp (Unix seconds): {i.Timestamp}");
            text.AppendLine(Mode(i));
            foreach (var count in i.Counts) text.AppendLine($"  {count.Key.Label(),-22} {count.Value,8:N0}");
        }
        if (file.Error is not null) text.AppendLine(Safe(file.Error));
        foreach (var warning in file.Warnings) text.AppendLine($"Warning: {Safe(warning)}");
        return text.ToString();
    }

    internal static string Comparison(ModComparison comparison)
    {
        StringBuilder text = new();
        text.AppendLine($"\nMod: {Safe(comparison.ModFileName)}");
        text.AppendLine($"Same GameMaker version: {Yes(comparison.SameGameMakerVersion)}; bytecode: {Yes(comparison.SameBytecodeVersion)}; GEN8 name: {Yes(comparison.SameGameName)}");
        text.AppendLine($"{"Asset type",-18} {"Identical",10} {"Changed",10} {"Added",10} {"Removed",10}");
        foreach (var type in comparison.AssetTypes)
            text.AppendLine($"{type.AssetType.Label(),-18} {type.Identical,10:N0} {type.Changed,10:N0} {type.Added,10:N0} {type.Removed,10:N0}");
        text.AppendLine(Conclusion(comparison));
        return text.ToString();
    }

    private static string Conclusion(ModComparison c)
    {
        if (!c.HasComparableBytecode) return c.Verdict;
        var code = c.AssetTypes.FirstOrDefault(x => x.AssetType == AssetKind.Code);
        int total = c.AssetTypes.Where(x => x.AssetType.IsNamedAsset()).Sum(x => x.OriginalCount);
        int same = c.AssetTypes.Where(x => x.AssetType.IsNamedAsset()).Sum(x => x.Identical);
        string codeSummary = code is null ? "code N/A (fingerprints unavailable)"
            : code.OriginalCount == 0 ? "code N/A (original has 0 code entries)"
            : $"code {code.Identical}/{code.OriginalCount} identical ({c.OriginalCodeUnchangedPercent:F2}%)";
        string assetSummary = total == 0 ? "named original assets N/A (no fingerprints)"
            : $"named original assets unchanged {same}/{total} ({c.OriginalAssetsUnchangedPercent:F2}%)";
        return $"{c.Verdict}: {codeSummary}; "
            + $"{assetSummary}.";
    }

    internal static string Summary(Phase0Result result, string report)
    {
        StringBuilder text = new();
        text.AppendLine($"Phase 0: {result.Files.Count(x => x.Error is null)}/{result.Files.Count} files read successfully.");
        foreach (var file in result.Files)
        {
            if (file.Error is not null) text.AppendLine(Safe(file.Error));
            else if (file.Inspection is { } i)
                text.AppendLine($"{Path.GetFileName(i.FileName)}: {Mode(i)}, GEN8 {i.GameMakerVersion}, UTML {i.DetectedGameMakerVersion}, bytecode {i.BytecodeVersion}, code {i.Counts[AssetKind.Code]}.");
        }
        foreach (var comparison in result.Comparisons)
            text.AppendLine($"{Path.GetFileName(comparison.ModFileName)}: {Conclusion(comparison)}");
        text.AppendLine($"UTML warnings: {result.Files.Sum(x => x.Warnings.Count(w => !w.StartsWith("Fingerprint: ", StringComparison.Ordinal)))}.");
        text.AppendLine($"Fingerprint warnings: {result.Files.Sum(x => x.Warnings.Count(w => w.StartsWith("Fingerprint: ", StringComparison.Ordinal)))}.");
        text.AppendLine($"Report: {Path.GetFileName(report)}");
        return text.ToString();
    }

    internal static string Markdown(Phase0Result result)
    {
        StringBuilder text = new();
        text.AppendLine("# Phase 0 — feasibility check\n");
        text.AppendLine("Inputs were opened read-only. This report contains metadata, counts and hashes, with no extracted game code, dialogue, images or audio. A base verdict estimates similarity; it does not establish that a conflict-free merge is possible.\n");
        text.AppendLine("## Conclusion\n");
        foreach (var file in result.Files.Where(x => x.Inspection is { IsYyc: true }))
        {
            var info = file.Inspection!;
            string built = info.Timestamp <= 253402300799
                ? DateTimeOffset.FromUnixTimeSeconds((long)info.Timestamp).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture)
                : "unknown date";
            text.AppendLine($"{Md(file.FileName)} is YYC (no bytecode), detected GameMaker {info.DetectedGameMakerVersion}, built {built} UTC. It cannot supply code for merging.\n");
        }
        var vmMods = result.Files.Skip(1).Where(x => x.Error is null && x.Inspection is { IsYyc: false, BytecodeSupported: true }).ToArray();
        if (vmMods.Length > 0 && vmMods.All(x => x.Inspection!.BytecodeVersion == 16))
            text.AppendLine("The inspected mods use legacy GameMaker 1.4 bytecode 16. They need a matching legacy data.win and Cat Goes Fishing.exe runner. See [Getting the bytecode version](../legacy-version.md).\n");
        var peerComparisons = vmMods.SelectMany(a => vmMods.Where(b => !ReferenceEquals(a, b))
            .Select(b => AssetComparer.Compare(a, b))).ToArray();
        if (peerComparisons.Length > 0)
        {
            text.AppendLine($"Mod code is approximately {peerComparisons.Average(x => x.OriginalCodeUnchangedPercent):F0}% identical between mods (directional range {peerComparisons.Min(x => x.OriginalCodeUnchangedPercent):F2}–{peerComparisons.Max(x => x.OriginalCodeUnchangedPercent):F2}%). The next step is to compare against a legacy bytecode base.\n");
        }
        text.AppendLine("## Inspection\n");
        foreach (var file in result.Files)
        {
            text.AppendLine($"### {Md(Path.GetFileName(file.FileName))}\n");
            text.AppendLine($"File: {Md(file.FileName)}\n");
            if (file.Error is not null) text.AppendLine($"**Failure:** {Md(file.Error)}\n");
            if (file.Inspection is { } i)
            {
                text.AppendLine("| Field | Value |\n| --- | --- |");
                text.AppendLine($"| Size (bytes) | {i.FileSize} |\n| SHA-256 | {i.Sha256} |");
                text.AppendLine($"| GEN8 game name | {Md(i.GameName)} |\n| Display name | {Md(i.DisplayName)} |");
                text.AppendLine($"| GameMaker GEN8 | {i.GameMakerVersion} |\n| UTML detected version | {i.DetectedGameMakerVersion} ({i.DetectedBranch}; format lower bound) |");
                text.AppendLine($"| Bytecode | {i.BytecodeVersion} |\n| Build (GEN8) | {i.GameMakerVersion.Split('.')[^1]} |\n| Timestamp (Unix seconds) | {i.Timestamp} |\n| Compilation | {Mode(i)} |\n");
                text.AppendLine("| Asset type | Count |\n| --- | ---: |");
                foreach (var count in i.Counts) text.AppendLine($"| {count.Key.Label()} | {count.Value} |");
                text.AppendLine();
            }
            text.AppendLine($"Warnings (UTML and fingerprint notices): {file.Warnings.Count}.\n");
            foreach (string warning in file.Warnings) text.AppendLine($"- {Md(warning)}");
            text.AppendLine();
        }
        text.AppendLine("## Comparison with original\n");
        foreach (var c in result.Comparisons)
        {
            text.AppendLine($"### {Md(Path.GetFileName(c.ModFileName))}\n");
            text.AppendLine($"Same GameMaker version (stored and detected): **{Yes(c.SameGameMakerVersion)}**; same bytecode: **{Yes(c.SameBytecodeVersion)}**; same GEN8 game name: **{Yes(c.SameGameName)}**.\n");
            text.AppendLine("| Asset type | Identical | Changed | Added | Removed |\n| --- | ---: | ---: | ---: | ---: |");
            foreach (var type in c.AssetTypes)
                text.AppendLine($"| {type.AssetType.Label()} | {type.Identical} | {type.Changed} | {type.Added} | {type.Removed} |");
            text.AppendLine($"\n**{Conclusion(c)}**\n");
        }
        text.AppendLine("## Method and limits\n");
        text.AppendLine("Assets match by ordinal name, strings by value (SHA-256 identifiers in JSON), texture pages by index. Duplicate names/values retain multiplicity; exact fingerprints pair first. Overall unchanged percentage is identical/original over named assets only (code, scripts, sprites, objects, rooms, sounds, fonts and backgrounds); strings and texture pages are reported separately and additions do not enter the denominator. The verdict uses only original code entries: at least 80% identical = LIKELY SAME BASE; 40% to below 80% = POSSIBLY DIFFERENT VERSION; below 40% = DIFFERENT BASE. YYC, empty CODE or unsupported bytecode cannot establish a mergeable base: code, scripts, variables and functions are skipped if either input lacks supported bytecode, and no unchanged percentage or base verdict is emitted.\n");
        text.AppendLine("Fingerprints use SHA-256: code hashes UTML instruction disassembly with relative jumps and entry offset/argument count, without decompilation; sprites hash dimensions, origin, frame count, ordered decoded BGRA frame rectangles, placement geometry, masks and flags; objects hash sprite/parent/mask names, flags/physics and event types/subtypes/code names; rooms hash size, flags, position-keyed instances/transforms/creation-code names, views/backgrounds, legacy tiles and GMS2 layer metadata with instance, tile and background elements (plus legacy tiles and sprites in asset layers); sounds hash flags, playback metadata and embedded audio bytes; texture pages hash embedded encoded texture data; scripts hash referenced code name/content and constructor flag; variables hash instance type; functions hash classification; fonts hash glyph metrics/kerning and texture region; backgrounds hash flags, texture region and tileset metadata. Variables/functions omit table IDs and usage chains. Atlas repacking may change texture-page results while leaving sprite regions identical.\n");
        text.AppendLine("This is a feasibility fingerprint, not an exhaustive serialized diff. Event action parameters, sequences, particles, text/effect layers and external audio files/groups are outside Phase 0's content model. External textures, Spine/SWF sprites and out-of-range texture regions produce warnings and deterministic unsupported:&lt;reason&gt; fingerprints without aborting the file. Equal unsupported markers do not establish equal content. Scripts referencing code absent from the CODE table use a stable missing-code marker and a warning. Only one decoded texture page is cached at a time. UTML's detected version is a format lower bound. Version/name mismatches are reported separately from the code similarity verdict.\n");
        return text.ToString().TrimEnd() + Environment.NewLine;
    }
}
