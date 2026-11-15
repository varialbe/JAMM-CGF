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
            text.AppendLine($"File: {Safe(i.Path)}");
            text.AppendLine($"Size: {i.FileSize:N0} bytes  SHA-256: {i.Sha256}");
            text.AppendLine($"Game: {Safe(i.GameName)}  Display name: {Safe(i.DisplayName)}");
            text.AppendLine($"GameMaker GEN8: {i.GameMakerVersion}");
            if (i.DetectedGameMakerVersion != i.GameMakerVersion)
                text.AppendLine($"UTML detected: {i.DetectedGameMakerVersion} ({i.DetectedBranch}; format lower bound)");
            text.AppendLine($"Bytecode: {i.BytecodeVersion}  Build: {i.GameMakerVersion.Split('.')[^1]}  Timestamp (Unix seconds): {i.Timestamp}");
            text.AppendLine(Mode(i));
            foreach (var count in i.Counts) text.AppendLine($"  {count.Key,-22} {count.Value,8:N0}");
        }
        if (file.Error is not null) text.AppendLine(Safe(file.Error));
        foreach (var warning in file.Warnings) text.AppendLine($"Warning: {Safe(warning)}");
        return text.ToString();
    }

    internal static string Comparison(ModComparison comparison)
    {
        StringBuilder text = new();
        text.AppendLine($"\nMod: {Safe(comparison.ModPath)}");
        text.AppendLine($"Same GameMaker version: {Yes(comparison.SameGameMakerVersion)}; bytecode: {Yes(comparison.SameBytecodeVersion)}; GEN8 name: {Yes(comparison.SameGameName)}");
        text.AppendLine($"{"Asset type",-18} {"Identical",10} {"Changed",10} {"Added",10} {"Removed",10}");
        foreach (var type in comparison.AssetTypes)
            text.AppendLine($"{type.AssetType,-18} {type.Identical,10:N0} {type.Changed,10:N0} {type.Added,10:N0} {type.Removed,10:N0}");
        text.AppendLine(Conclusion(comparison));
        return text.ToString();
    }

    private static string Conclusion(ModComparison c)
    {
        var code = c.AssetTypes.FirstOrDefault(x => x.AssetType == "code");
        int total = c.AssetTypes.Sum(x => x.OriginalCount);
        int same = c.AssetTypes.Sum(x => x.Identical);
        string codeSummary = code is null ? "code N/A (fingerprints unavailable)"
            : code.OriginalCount == 0 ? "code N/A (original has 0 code entries)"
            : $"code {code.Identical}/{code.OriginalCount} identical ({c.OriginalCodeUnchangedPercent:F2}%)";
        string assetSummary = c.AssetTypes.Count == 0 ? "original assets N/A (fingerprints unavailable)"
            : $"original assets unchanged {same}/{total} ({c.OriginalAssetsUnchangedPercent:F2}%)";
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
                text.AppendLine($"{Path.GetFileName(i.Path)}: {Mode(i)}, GEN8 {i.GameMakerVersion}, UTML {i.DetectedGameMakerVersion}, bytecode {i.BytecodeVersion}, code {i.Counts["code"]}.");
        }
        foreach (var comparison in result.Comparisons)
            text.AppendLine($"{Path.GetFileName(comparison.ModPath)}: {Conclusion(comparison)}");
        text.AppendLine($"UTML warnings: {result.Files.Sum(x => x.Warnings.Count(w => !w.StartsWith("Fingerprint: ", StringComparison.Ordinal)))}.");
        text.AppendLine($"Fingerprint warnings: {result.Files.Sum(x => x.Warnings.Count(w => w.StartsWith("Fingerprint: ", StringComparison.Ordinal)))}.");
        text.AppendLine($"Report: {report}");
        return text.ToString();
    }

    internal static string Markdown(Phase0Result result)
    {
        StringBuilder text = new();
        text.AppendLine("# Phase 0 — feasibility check\n");
        text.AppendLine("Inputs were opened read-only. This report contains metadata, counts and hashes, with no extracted game code, dialogue, images or audio. A base verdict estimates similarity; it does not establish that a conflict-free merge is possible.\n");
        text.AppendLine("## Inspection\n");
        foreach (var file in result.Files)
        {
            text.AppendLine($"### {Md(Path.GetFileName(file.Path))}\n");
            text.AppendLine($"Path: {Md(file.Path)}\n");
            if (file.Error is not null) text.AppendLine($"**Read failure:** {Md(file.Error)}\n");
            if (file.Inspection is { } i)
            {
                text.AppendLine("| Field | Value |\n| --- | --- |");
                text.AppendLine($"| Size (bytes) | {i.FileSize} |\n| SHA-256 | {i.Sha256} |");
                text.AppendLine($"| GEN8 game name | {Md(i.GameName)} |\n| Display name | {Md(i.DisplayName)} |");
                text.AppendLine($"| GameMaker GEN8 | {i.GameMakerVersion} |\n| UTML detected version | {i.DetectedGameMakerVersion} ({i.DetectedBranch}; format lower bound) |");
                text.AppendLine($"| Bytecode | {i.BytecodeVersion} |\n| Build (GEN8) | {i.GameMakerVersion.Split('.')[^1]} |\n| Timestamp (Unix seconds) | {i.Timestamp} |\n| Compilation | {Mode(i)} |\n");
                text.AppendLine("| Asset type | Count |\n| --- | ---: |");
                foreach (var count in i.Counts) text.AppendLine($"| {count.Key} | {count.Value} |");
                text.AppendLine();
            }
            text.AppendLine($"Warnings (UTML and fingerprint notices): {file.Warnings.Count}.\n");
            foreach (string warning in file.Warnings) text.AppendLine($"- {Md(warning)}");
            text.AppendLine();
        }
        text.AppendLine("## Comparison with original\n");
        foreach (var c in result.Comparisons)
        {
            text.AppendLine($"### {Md(Path.GetFileName(c.ModPath))}\n");
            text.AppendLine($"Same GameMaker version (stored and detected): **{Yes(c.SameGameMakerVersion)}**; same bytecode: **{Yes(c.SameBytecodeVersion)}**; same GEN8 game name: **{Yes(c.SameGameName)}**.\n");
            text.AppendLine("| Asset type | Identical | Changed | Added | Removed |\n| --- | ---: | ---: | ---: | ---: |");
            foreach (var type in c.AssetTypes)
                text.AppendLine($"| {type.AssetType} | {type.Identical} | {type.Changed} | {type.Added} | {type.Removed} |");
            text.AppendLine($"\n**{Conclusion(c)}**\n");
        }
        text.AppendLine("## Method and limits\n");
        text.AppendLine("Assets match by ordinal name, strings by value (SHA-256 identifiers in JSON), texture pages by index. Duplicate names/values retain multiplicity; exact fingerprints pair first. Overall unchanged percentage is identical/original across the twelve compared families, including strings; additions do not enter the denominator. The verdict uses only original code entries: at least 80% identical = LIKELY SAME BASE; 40% to below 80% = POSSIBLY DIFFERENT VERSION; below 40% = DIFFERENT BASE. YYC, empty CODE or unsupported bytecode cannot establish a mergeable base.\n");
        text.AppendLine("Fingerprints use SHA-256: code hashes UTML instruction disassembly with relative jumps and entry offset/argument count, without decompilation; sprites hash dimensions, origin, frame count, ordered decoded BGRA frame rectangles, placement geometry, masks and flags; objects hash sprite/parent/mask names, flags/physics and event types/subtypes/code names; rooms hash size, flags and ordered instance object names, positions/transforms and creation-code names; sounds hash flags, playback metadata and embedded audio bytes; texture pages hash embedded encoded texture data; scripts hash referenced code name/content and constructor flag; variables hash instance type; functions hash classification; fonts hash glyph metrics/kerning and texture region; backgrounds hash flags, texture region and tileset metadata. Variables/functions omit table IDs and usage chains. Atlas repacking may change texture-page results while leaving sprite regions identical.\n");
        text.AppendLine("This is a feasibility fingerprint, not an exhaustive serialized diff. Room layers/tiles, event action parameters, external audio files/groups and non-bitmap sprite formats are outside Phase 0's content model. External textures and non-bitmap sprite fingerprints fail explicitly. Out-of-bounds texture regions produce warnings and hash available pixels with transparent padding plus an invalid-region marker; those sprite matches have limited confidence. UTML's detected version is a format lower bound. Version/name mismatches are reported separately from the code similarity verdict.\n");
        return text.ToString().TrimEnd() + Environment.NewLine;
    }
}
