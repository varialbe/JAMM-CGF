using UndertaleModLib;
using UndertaleModLib.Decompiler;
namespace Jamm.ModMaker;
/// <summary>Decompiles selected code entries (debug helper).</summary>
public static class Dump
{
    public static void Run(UndertaleData data, IEnumerable<string> names)
    {
        var context = new GlobalDecompileContext(data);
        foreach (string name in names)
        {
            var code = data.Code.ByName(name);
            if (code is null) { Console.WriteLine($"// {name}: not found"); continue; }
            Console.WriteLine($"// ===== {name}");
            Console.WriteLine(new Underanalyzer.Decompiler.DecompileContext(context, code, data.ToolInfo.DecompilerSettings).DecompileToString());
        }
    }
}
