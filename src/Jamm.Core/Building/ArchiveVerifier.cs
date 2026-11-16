using System.Collections;
using System.Reflection;
using Jamm.Core.Patching;
using UndertaleModLib;
using UndertaleModLib.Compiler;
using UndertaleModLib.Models;

namespace Jamm.Core.Building;

/// <summary>A successfully reloaded and validated archive.</summary>
public sealed record VerificationResult(GameInspection Inspection, int DecompiledEntries, IReadOnlyList<string> Warnings, int SemanticEntries = 0);

/// <summary>Reloads archives, validates references and round-trips every root GML entry through the compiler in memory.</summary>
public static class ArchiveVerifier
{
    /// <summary>Verifies an archive; throws a contextual error if a load, reference or code check fails.</summary>
    public static VerificationResult Verify(string path, GameInspection? expected = null,
        IReadOnlyList<PlannedAsset>? plan = null, Action<string>? progress = null,
        IReadOnlyDictionary<string, IReadOnlyList<ResolvedCodeReference>>? expectedReferences = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        progress?.Invoke($"Verifying {Path.GetFileName(path)}: reload, references, decompile and compile");
        using var loaded = GameFileLoader.Load(path);
        if (expected is not null) PatchBuilder.Validate(expected, loaded.Inspection);
        if (loaded.Inspection.IsYyc || !loaded.Inspection.BytecodeSupported)
            throw new InvalidDataException($"Cannot verify '{Path.GetFileName(path)}': VM bytecode is unavailable or unsupported. Use the legacy 2023 build.");
        if (plan is not null)
        {
            var tables = ResourceCatalog.Tables(loaded.Data);
            foreach (var asset in plan.Where(a => tables.ContainsKey(a.Family)))
                if ((ResourceCatalog.Find(tables[asset.Family], asset.Name) is not null) != (asset.Patch is not null))
                    throw new InvalidDataException($"Build plan mismatch in '{Path.GetFileName(path)}': {asset.Family}/{asset.Name} has the wrong presence.");
        }
        if (expectedReferences is not null)
            foreach (string missing in expectedReferences.Keys.Except(loaded.Data.Code.Where(c => c.ParentEntry is null).Select(c => c.Name.Content), StringComparer.Ordinal))
                throw new InvalidDataException($"Semantic reference verification failed: code '{missing}' is missing after archive reload. No output was published.");
        List<string> warnings = [.. loaded.Warnings];
        ValidateReferences(loaded.Data, warnings.Add);
        var names = SemanticReferences.Names(loaded.Data);
        var reader = new CodeDecompiler(loaded.Data, Path.GetFileName(path), warnings.Add, AssetSymbols.Ambiguous([names]));
        var compiler = new CompileGroup(loaded.Data);
        int count = 0;
        foreach (var code in loaded.Data.Code.Where(c => c.ParentEntry is null))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var text = reader.Read(code);
            if (text.Format != "gml") throw new InvalidDataException($"Verification failed for '{Path.GetFileName(path)}', code '{code.Name.Content}': GML decompilation failed. No verified build is available.");
            if (expectedReferences is not null)
            {
                if (!expectedReferences.TryGetValue(code.Name.Content, out var references))
                    throw new InvalidDataException($"Unexpected code '{code.Name.Content}' after archive reload. No output was published.");
                SemanticReferences.Verify(code.Name.Content, references, SemanticReferences.Resolve(text.Text, code.Name.Content, names));
            }
            compiler.QueueCodeReplace(code, AssetSymbols.Link(text.Text, code.Name.Content, names)); count++;
        }
        Compile(compiler, Path.GetFileName(path));
        return new(loaded.Inspection, count, warnings, expectedReferences?.Count ?? 0);
    }

    internal static void ValidateSemantics(UndertaleData data, IReadOnlyDictionary<string, IReadOnlyList<ResolvedCodeReference>> expected, CancellationToken cancellationToken = default)
    {
        var reader = new CodeDecompiler(data, "merged archive", _ => { }, AssetSymbols.Ambiguous([SemanticReferences.Names(data)]));
        var names = SemanticReferences.Names(data);
        foreach (var code in data.Code.Where(c => c.ParentEntry is null))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!expected.TryGetValue(code.Name.Content, out var references))
                throw new InvalidDataException($"Missing semantic expectations for code '{code.Name.Content}'. No output was published.");
            var text = reader.Read(code);
            if (text.Format != "gml") throw new InvalidDataException($"Cannot verify references in code '{code.Name.Content}': decompilation failed.");
            SemanticReferences.Verify(code.Name.Content, references, SemanticReferences.Resolve(text.Text, code.Name.Content, names));
        }
    }

    internal static void Compile(CompileGroup compiler, string context)
    {
        var result = compiler.Compile();
        if (!result.Successful)
            throw new InvalidDataException($"Compilation failed in {context}: " + string.Join("; ", result.Errors.Select(e =>
                $"code '{e.Code.Name.Content}': {e.GenerateDetailedMessage()}")) + ". Fix the selected GML/resolution; no output was published.");
    }

    internal static void ValidateReferences(UndertaleData data, Action<string>? warning = null)
    {
        HashSet<object> resources = new(ReferenceEqualityComparer.Instance);
        var roots = ResourceCatalog.Tables(data).Values.SelectMany(t => t.Cast<object>())
            .Concat(data.Strings).Concat(data.Variables).Concat(data.Functions)
            .Concat(data.EmbeddedTextures).Concat(data.TexturePageItems).Concat(data.EmbeddedAudio)
            .Concat(data.AudioGroups ?? []).Concat(data.CodeLocals ?? []).ToArray();
        resources.UnionWith(roots);
        HashSet<object> visited = new(ReferenceEqualityComparer.Instance);
        HashSet<UndertaleTexturePageItem> referencedItems = new(ReferenceEqualityComparer.Instance);
        foreach (object root in roots) Visit(root, root is UndertaleNamedResource named ? named.Name?.Content ?? root.GetType().Name : root.GetType().Name, true);
        Visit(data.GeneralInfo, "GEN8");
        Visit(data.GlobalInitScripts, "global initialization scripts");
        Visit(data.GameEndScripts, "game end scripts");
        foreach (var page in data.TexturePageItems)
        {
            if (page.TexturePage is null)
            {
                if (page.SourceWidth != 0 || page.SourceHeight != 0)
                {
                    if (referencedItems.Contains(page))
                        throw new InvalidDataException($"Texture item '{page.Name?.Content}' is referenced but has pixels and no embedded page. JAMM cannot safely reconstruct its texture; choose another mod while support is investigated.");
                    warning?.Invoke($"Unused texture item '{page.Name?.Content}' has pixels but no embedded page; preserved as-is.");
                }
                continue; // Empty null page items are valid placeholders in the supplied mods.
            }
            if (!resources.Contains(page.TexturePage))
                throw new InvalidDataException("A texture item references a missing embedded texture page.");
            var texture = page.TexturePage.TextureData;
            if (page.SourceX + page.SourceWidth > texture.Width || page.SourceY + page.SourceHeight > texture.Height)
                warning?.Invoke($"Texture item '{page.Name?.Content}' extends beyond its texture page; source geometry is preserved and needs an in-game check.");
        }
        foreach (var obj in data.GameObjects)
            foreach (var collision in obj.Events[4])
                if (collision.EventSubtype >= data.GameObjects.Count)
                    throw new InvalidDataException($"Object '{obj.Name.Content}' has an invalid collision object index {collision.EventSubtype}.");
        void Visit(object? value, string context, bool root = false)
        {
            if (!root && value is UndertaleTexturePageItem textureItem) referencedItems.Add(textureItem);
            if (value is null || value is string || value is Delegate || value.GetType().IsValueType) return;
            if (!root && (value is UndertaleNamedResource || value is UndertaleString))
            {
                if (!resources.Contains(value)) throw new InvalidDataException($"Invalid resource/string reference in '{context}' ({value.GetType().Name}). Keep its dependency in the build.");
                return;
            }
            if (!visited.Add(value)) return;
            if (value is IEnumerable list) { foreach (object? item in list) Visit(item, context); return; }
            if (value.GetType().Namespace?.StartsWith("UndertaleModLib", StringComparison.Ordinal) != true) return;
            for (Type? type = value.GetType(); type is not null && type != typeof(object); type = type.BaseType)
                foreach (var field in type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                {
                    if (value is UndertaleEmbeddedTexture or UndertaleTexturePageItem or UndertaleEmbeddedAudio && field.Name.Contains("<Name>", StringComparison.Ordinal)) continue;
                    Visit(field.GetValue(value), context);
                }
        }
    }
}
