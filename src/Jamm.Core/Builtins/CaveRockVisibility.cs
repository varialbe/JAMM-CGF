using UndertaleModLib;
using UndertaleModLib.Compiler;
using UndertaleModLib.Models;

namespace Jamm.ModMaker;

/// <summary>Replaces CaveRock's square-root visibility check with a squared-distance test.</summary>
internal static class CaveRockVisibility
{
    private const string DistanceCheck = "if (point_distance(x, y, Camera.x, Camera.y) > 5000)";

    /// <summary>Queues the guarded CaveRock transform when its exact source expression is present.</summary>
    internal static string? Queue(UndertaleData data, CompileGroup compiler)
    {
        var code = data.Code.ByName("gml_Object_CaveRock_Step_0");
        if (code is null) return null;

        string source = ModObjects.Decompile(data, code);
        if (source.Split(DistanceCheck, StringSplitOptions.None).Length != 2) return null;

        const string squaredCheck = "var jamm_perf_cave_dx = x - Camera.x;\n"
            + "var jamm_perf_cave_dy = y - Camera.y;\n"
            + "var jamm_perf_cave_d2 = (jamm_perf_cave_dx * jamm_perf_cave_dx) + (jamm_perf_cave_dy * jamm_perf_cave_dy);\n"
            + "if (jamm_perf_cave_d2 > 25000000.1 || (jamm_perf_cave_d2 > 24999999.9 && point_distance(x, y, Camera.x, Camera.y) > 5000))";
        compiler.QueueCodeReplace(code, source.Replace(DistanceCheck, squaredCheck, StringComparison.Ordinal));
        return "CaveRock_Step_0: squared visibility-distance check with exact near-boundary fallback.";
    }

    /// <summary>Applies the visibility transform as an independent build-time Perf edit.</summary>
    internal static string? Apply(UndertaleData data, List<UndertaleCode> edits)
    {
        var compiler = new CompileGroup(data);
        string? decision = Queue(data, compiler);
        if (decision is null) return null;

        ModObjects.Compile(compiler);
        if (data.Code.ByName("gml_Object_CaveRock_Step_0") is { } code && !edits.Contains(code))
            edits.Add(code);
        return decision;
    }
}
