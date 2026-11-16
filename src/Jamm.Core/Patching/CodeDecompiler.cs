using System.Text;
using Jamm.Core.Diffing;
using Underanalyzer.Decompiler;
using UndertaleModLib;
using UndertaleModLib.Decompiler;
using UndertaleModLib.Models;

namespace Jamm.Core.Patching;

internal sealed class CodeDecompiler(UndertaleData data, string fileName, Action<string> warning, IReadOnlySet<string>? ambiguous = null)
{
    private GlobalDecompileContext? global;
    private readonly Dictionary<UndertaleCode, (string Text, string Format)> sources = [];
    internal CodeText Read(UndertaleCode code)
    {
        // UTML child entries reference the root instruction stream. Decompile that stream,
        // preserving the child identity separately rather than returning an empty child body.
        var source = code.ParentEntry ?? code;
        if (!sources.TryGetValue(source, out var result)) result = Decompile(source);
        return new(result.Text, result.Format, code.LocalsCount, code.ArgumentsCount, code.ParentEntry?.Name?.Content,
            code.ChildEntries.Select(x => x.Name.Content).Order(StringComparer.Ordinal).ToArray());
    }

    internal static Dictionary<string, CodeText> ReadAll(UndertaleData data, string label, Action<string> warning,
        IReadOnlySet<string> ambiguous, AnalysisOptions options, Action<string>? progress = null, string? baseHash = null, IEnumerable<UndertaleCode>? selected = null)
    {
        var clock = System.Diagnostics.Stopwatch.StartNew();
        var cache = new AnalysisCache(options);
        // Ambiguity changes the emitted asset symbols, so it is part of the cache identity.
        string category = "gml:" + System.Text.Json.JsonSerializer.Serialize(ambiguous.Order(StringComparer.Ordinal).ToArray());
        var batch = cache.Get(baseHash, category, () =>
        {
            Building.CgfAssetTypes.Register(data);
            GlobalDecompileContext.BuildGlobalFunctionCache(data);
            var selectedEntries = (baseHash is not null ? data.Code : selected ?? data.Code).ToArray();
            if (selectedEntries.GroupBy(c => c.Name.Content, StringComparer.Ordinal).FirstOrDefault(g => g.Count() > 1) is { } duplicate)
                throw new InvalidDataException($"Duplicate code name '{duplicate.Key}' in {label} cannot be matched safely. Use a mod with unique asset names.");
            var roots = selectedEntries.Select(c => c.ParentEntry ?? c).Distinct().ToArray();
            var texts = new (CodeText Text, string[] Warnings)[roots.Length];
            int completed = 0;
            object gate = new();
            using var readers = new ThreadLocal<(CodeDecompiler Reader, List<string> Warnings)>(() =>
            {
                List<string> messages = [];
                return (new(data, "{archive}", messages.Add, ambiguous)
                { global = new Building.AssetSymbols.Context(data, ambiguous) }, messages);
            });
            AnalysisWork.For(roots.Length, options, i =>
            {
                // A reader owns its context and root-source dictionary. Warnings are replayed in source order.
                var worker = readers.Value;
                worker.Warnings.Clear();
                var text = worker.Reader.Read(roots[i]);
                texts[i] = (text, worker.Warnings.ToArray());
                lock (gate) progress?.Invoke($"Decompiling {label} {++completed}/{roots.Length}");
            });
            var byRoot = roots.Select((r, i) => (r, texts[i].Text)).ToDictionary(p => p.r, p => p.Text);
            var entries = selectedEntries.ToDictionary(c => c.Name.Content, c => byRoot[c.ParentEntry ?? c] with
            {
                LocalsCount = c.LocalsCount, ArgumentsCount = c.ArgumentsCount, Parent = c.ParentEntry?.Name.Content,
                Children = c.ChildEntries.Select(x => x.Name.Content).Order(StringComparer.Ordinal).ToArray()
            }, StringComparer.Ordinal);
            return new SourceBatch(entries, roots.Select((root, i) => (root.Name.Content, texts[i].Warnings))
                .ToDictionary(x => x.Content, x => x.Warnings, StringComparer.Ordinal));
        });
        var requiredRoots = (selected ?? data.Code).Select(c => (c.ParentEntry ?? c).Name.Content).ToHashSet(StringComparer.Ordinal);
        foreach (var messages in batch.Warnings.Where(p => requiredRoots.Contains(p.Key)).Select(p => p.Value))
            foreach (var message in messages) warning(message.Replace("{archive}", label, StringComparison.Ordinal));
        options.Timing?.Invoke("decompile", clock.Elapsed);
        return batch.Entries;
    }

    internal sealed record SourceBatch(Dictionary<string, CodeText> Entries, Dictionary<string, string[]> Warnings);

    private (string Text, string Format) Decompile(UndertaleCode code)
    {
        string text, format;
        try
        {
            if (global is null)
            {
                Building.CgfAssetTypes.Register(data);
                GlobalDecompileContext.BuildGlobalFunctionCache(data);
                global = new Building.AssetSymbols.Context(data, ambiguous ?? new HashSet<string>());
            }
            text = new DecompileContext(global, code).DecompileToString();
            format = "gml";
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            warning($"Decompilation failed for {fileName}, code '{code.Name.Content}' ({ex.GetType().Name}); disassembly is stored instead. Inspect this entry before merging.");
            StringBuilder disassembly = new();
            uint address = 0;
            foreach (var instruction in (code.ParentEntry ?? code).Instructions)
            {
                instruction.ToString(disassembly, code.ParentEntry ?? code, address);
                if (instruction.ValueString is { } reference) disassembly.Length -= reference.GetMarkerSuffix().Length;
                disassembly.Append('\n');
                address += instruction.CalculateInstructionSize();
            }
            text = disassembly.ToString();
            format = "disassembly";
        }
        var result = (LineDiff.Normalize(text), format);
        sources.Add(code, result);
        return result;
    }
}
