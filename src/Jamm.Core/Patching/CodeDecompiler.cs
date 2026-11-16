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

    private (string Text, string Format) Decompile(UndertaleCode code)
    {
        string text, format;
        try
        {
            if (global is null)
            {
                Building.CgfAssetTypes.Register(data);
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
