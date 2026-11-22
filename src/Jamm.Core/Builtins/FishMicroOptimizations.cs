using UndertaleModLib;
using UndertaleModLib.Compiler;
using UndertaleModLib.Models;

namespace Jamm.ModMaker;

/// <summary>Applies narrow, guarded fish-step source optimizations for opt-in Perf profiles.</summary>
public static class FishMicroOptimizations
{
    private const string Direction = "point_direction(x, y, pal.x, pal.y)";
    private const string LureDistance = "point_distance(x, y, Lure.x, Lure.y)";

    /// <summary>Queues the fish_flock direction cache and records the changed code entry.</summary>
    public static string Queue(UndertaleData data, CompileGroup compiler, List<UndertaleCode>? edits = null)
    {
        List<string> decisions =
        [
            QueueLungeDistance(data, compiler, edits),
            QueueFlockDirection(data, compiler, edits)
        ];
        return string.Join("; ", decisions);
    }

    private static string QueueLungeDistance(UndertaleData data, CompileGroup compiler, List<UndertaleCode>? edits)
    {
        var lunge = data.Code.ByName("gml_Script_fish_lunge");
        var check = data.Code.ByName("gml_Script_check_lunge");
        if (lunge is null || check is null)
            return "[CGF-PERF-MICRO] fish_lunge/check_lunge: skipped (script missing)";

        string lungeSource = ModObjects.Decompile(data, lunge);
        string checkSource = ModObjects.Decompile(data, check);
        const string distanceAssignment = LureDistance + ";";
        const string checkCall = "if (check_lunge())";
        const string distancePredicate = "if (" + LureDistance + " <";
        if (lungeSource.Split(distanceAssignment, StringSplitOptions.None).Length != 2
            || lungeSource.Split(checkCall, StringSplitOptions.None).Length != 2
            || checkSource.Split(distancePredicate, StringSplitOptions.None).Length != 2)
            return "[CGF-PERF-MICRO] fish_lunge/check_lunge: skipped (distance pattern differs)";

        // Reuse only when fish_lunge is the sole visible caller in the merged source.
        // Dynamic or mod-specific callers keep the original zero-argument contract.
        foreach (var candidate in data.Code)
        {
            if (candidate == lunge || candidate == check) continue;
            if (ModObjects.Decompile(data, candidate).Contains("check_lunge", StringComparison.Ordinal))
                return $"[CGF-PERF-MICRO] fish_lunge/check_lunge: skipped (another merged caller: {candidate.Name.Content})";
        }

        lungeSource = lungeSource.Replace(checkCall, "if (check_lunge(distance_to_lure))", StringComparison.Ordinal);
        checkSource = checkSource.Replace(distancePredicate, "if (argument0 <", StringComparison.Ordinal);
        compiler.QueueCodeReplace(lunge, lungeSource);
        compiler.QueueCodeReplace(check, checkSource);
        edits?.Add(lunge);
        edits?.Add(check);
        return "[CGF-PERF-MICRO] fish_lunge/check_lunge: reused the per-fish lure distance";
    }

    private static string QueueFlockDirection(UndertaleData data, CompileGroup compiler, List<UndertaleCode>? edits)
    {
        var flock = data.Code.ByName("gml_Script_fish_flock");
        if (flock is null)
            return "[CGF-PERF-MICRO] fish_flock: skipped (script missing)";

        string source = ModObjects.Decompile(data, flock);
        if (source.Contains("jamm_perf_flock_direction", StringComparison.Ordinal))
            return "[CGF-PERF-MICRO] fish_flock: skipped (temporary name already exists)";
        if (source.Split(Direction, StringSplitOptions.None).Length != 3)
            return "[CGF-PERF-MICRO] fish_flock: skipped (expected two identical direction calls)";

        const string firstVelocity = "hspeed += lengthdir_x(impulse * argument1, " + Direction + ");";
        const string secondVelocity = "vspeed += lengthdir_y(impulse * argument1, " + Direction + ");";
        if (source.Split(firstVelocity, StringSplitOptions.None).Length != 2
            || source.Split(secondVelocity, StringSplitOptions.None).Length != 2)
            return "[CGF-PERF-MICRO] fish_flock: skipped (velocity update pattern differs)";
        int guard = source.IndexOf("if (pal > 0)", StringComparison.Ordinal);
        int guardOpen = guard < 0 ? -1 : source.IndexOf('{', guard);
        int guardClose = guardOpen < 0 ? -1 : source.IndexOf('}', guardOpen + 1);
        int velocity = source.IndexOf(firstVelocity, StringComparison.Ordinal);
        if (guardOpen < 0 || guardClose < velocity)
            return "[CGF-PERF-MICRO] fish_flock: skipped (direction calculation is outside the pal guard)";

        source = source.Replace(firstVelocity,
                "var jamm_perf_flock_direction = " + Direction + ";\n"
                + "hspeed += lengthdir_x(impulse * argument1, jamm_perf_flock_direction);", StringComparison.Ordinal)
            .Replace("lengthdir_y(impulse * argument1, " + Direction + ")",
                "lengthdir_y(impulse * argument1, jamm_perf_flock_direction)", StringComparison.Ordinal);
        compiler.QueueCodeReplace(flock, source);
        edits?.Add(flock);
        return "[CGF-PERF-MICRO] fish_flock: cached the shared direction";
    }
}
