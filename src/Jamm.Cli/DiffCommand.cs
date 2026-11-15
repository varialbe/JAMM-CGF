using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using Jamm.Core;
using Jamm.Core.Diffing;
using Jamm.Core.Patching;

namespace Jamm.Cli;

internal static class DiffCommand
{
    internal static int Run(string[] arguments)
    {
        List<string> paths = [];
        Dictionary<string, string> options = new(StringComparer.Ordinal);
        bool force = false, json = false;
        for (int i = 0; i < arguments.Length; i++)
        {
            string arg = arguments[i];
            if (arg == "--force") { if (force) throw new ArgumentException("--force was given twice."); force = true; }
            else if (arg == "--json-summary") { if (json) throw new ArgumentException("--json-summary was given twice."); json = true; }
            else if (arg is "--name" or "--out" or "--show")
            {
                if (++i == arguments.Length || arguments[i].StartsWith("--", StringComparison.Ordinal))
                    throw new ArgumentException($"{arg} needs a value.");
                if (!options.TryAdd(arg, arguments[i])) throw new ArgumentException($"{arg} was given twice.");
            }
            else if (arg.StartsWith("--", StringComparison.Ordinal)) throw new ArgumentException($"Unknown option '{arg}'.");
            else paths.Add(arg);
        }
        if (paths.Count != 2) throw new ArgumentException("diff needs exactly an original and a mod file. See 'cgfmm --help'.");
        string output = options.GetValueOrDefault("--out", Path.GetFileNameWithoutExtension(paths[1]) + ".cgfmm-patch.json");
        OutputFile.Validate(output, paths);
        var timer = Stopwatch.StartNew();
        ModPatch? patch = null;
        bool reused = false;
        // Cache lookup reads only the input bytes, never UTML models or the decompiler.
        string baseHash = Hash(paths[0]), modHash = Hash(paths[1]);
        if (!force && File.Exists(output))
        {
            try
            {
                var candidate = PatchJson.Deserialize(File.ReadAllText(output));
                if (candidate.Base.Sha256 == baseHash && candidate.Mod.Sha256 == modHash)
                { patch = candidate; reused = true; }
            }
            catch (Exception ex) when (ex is JsonException or InvalidDataException or NotSupportedException)
            { Console.Error.WriteLine($"Cache '{Path.GetFileName(output)}' cannot be reused; recomputing ({ex.Message})."); }
        }
        try
        {
            if (patch is null)
            {
                using var original = Load(paths[0]);
                using var mod = Load(paths[1]);
                try { patch = PatchBuilder.Create(original, mod, options.GetValueOrDefault("--name"), Progress); }
                catch (NotComparableException) { throw; }
                catch (Exception ex) when (ex is not OutOfMemoryException)
                { throw new InvalidDataException($"Could not analyse {original.Inspection.FileName} against {mod.Inspection.FileName}: {ex.Message} Check the file versions or try another complete copy of the mod.", ex); }
                // Detect inputs replaced during analysis; an old cache hash must not name different bytes.
                if (patch.Base.Sha256 != baseHash || patch.Mod.Sha256 != modHash)
                    throw new IOException("An input changed during analysis. Retry with stable copies of the files.");
                OutputFile.Write(output, PatchJson.Serialize(patch), paths);
            }
            else if (options.TryGetValue("--name", out string? name) && patch.Mod.Name != name)
            {
                patch = patch with { Mod = patch.Mod with { Name = name } };
                OutputFile.Write(output, PatchJson.Serialize(patch), paths);
            }
        }
        catch (NotComparableException ex) { Console.Error.WriteLine($"cgfmm: {ex.Message}"); return 2; }
        timer.Stop();
        var counts = patch.Changes.ToDictionary(x => x.Key, x => new { added = x.Value.Added.Count,
            changed = x.Value.Changed.Count, removed = x.Value.Removed.Count }, StringComparer.Ordinal);
        if (json)
            Console.WriteLine(JsonSerializer.Serialize(new { patchPath = Path.GetFullPath(output), reused,
                elapsedSeconds = timer.Elapsed.TotalSeconds, warnings = patch.Warnings, changes = counts }, new JsonSerializerOptions { WriteIndented = true }));
        else
        {
            Console.WriteLine($"{patch.Mod.Name}: {(reused ? "Reusing cached patch" : "Created patch")}");
            Console.WriteLine($"{"Asset kind",-17} {"Added",7} {"Changed",7} {"Removed",7}");
            foreach (var (kind, count) in counts) Console.WriteLine($"{kind,-17} {count.added,7} {count.changed,7} {count.removed,7}");
            Console.WriteLine($"Patch: {Path.GetFullPath(output)}");
            Console.WriteLine($"Elapsed: {timer.Elapsed.TotalSeconds:F3} s; warnings: {patch.Warnings.Count}");
            foreach (string warning in patch.Warnings) Console.Error.WriteLine(warning);
        }
        if (options.TryGetValue("--show", out string? asset)) Show(patch, asset, json, paths[0]);
        return 0;
    }

    private static LoadedGameFile Load(string path)
    {
        try { return GameFileLoader.Load(path, Progress); }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        { throw new InvalidDataException($"Could not read {Path.GetFileName(path)}: {ex.Message} Check that it is a complete, readable game archive.", ex); }
    }

    private static void Progress(string message) => Console.Error.WriteLine(message);
    private static string Hash(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        return Convert.ToHexStringLower(SHA256.HashData(stream));
    }

    private static void Show(ModPatch patch, string name, bool json, string basePath)
    {
        var writer = json ? Console.Error : Console.Out;
        bool found = false;
        foreach (var (kind, changes) in patch.Changes)
        {
            foreach (var asset in changes.Added.Concat(changes.Changed).Where(x => x.Name == name))
            {
                found = true;
                writer.WriteLine($"{kind}: {name}");
                if (asset.ModCode is { } code)
                {
                    writer.WriteLine($"Text format: {asset.BaseCode?.Format ?? "absent"} -> {code.Format}");
                    writer.Write(LineDiff.Unified(asset.BaseCode?.Text ?? "", code.Text, name));
                }
                foreach (var field in asset.Fields)
                    writer.WriteLine($"{field.Path}: {field.Base?.GetRawText() ?? "<absent>"} -> {field.Mod?.GetRawText() ?? "<absent>"}");
            }
            if (changes.Removed.Contains(name, StringComparer.Ordinal))
            {
                found = true;
                writer.WriteLine($"{kind}: removed {name}");
                using var original = Load(basePath);
                if (original.Inspection.Sha256 != patch.Base.Sha256)
                    throw new IOException("The base changed while displaying a removal. Retry with the patch's original archive.");
                if (kind == "code")
                {
                    var code = PatchBuilder.ReadCode(original, name, Progress);
                    writer.WriteLine($"Text format: {code.Format} -> absent");
                    writer.Write(LineDiff.Unified(code.Text, "", name));
                }
                var before = PatchBuilder.ReadAsset(original, Enum.Parse<AssetKind>(kind, ignoreCase: true), name, Progress);
                writer.WriteLine($"/: {before.GetRawText()} -> <absent>");
            }
        }
        if (!found) writer.WriteLine($"'{name}' has no recorded changes in this patch. Choose a changed, added or removed asset name.");
    }
}
