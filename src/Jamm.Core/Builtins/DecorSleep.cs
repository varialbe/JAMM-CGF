using System.Globalization;
using UndertaleModLib;
using UndertaleModLib.Compiler;
using UndertaleModLib.Models;

namespace Jamm.ModMaker;

/// <summary>
/// Installs generic, pattern-based off-view sleeping for static decoration objects
/// (CaveRock, SceneItem, Weed, Dangler, GrottoObject, AbstractLight, and modded equivalents).
/// Controlled by <c>[Perf] DecorSleep=1</c> in <c>jamm_perf.ini</c>, independent of F8.
/// </summary>
public static class DecorSleep
{
    /// <summary>Persistent controller object that tracks camera bounds and wakes/sleeps decor in bounded batches.</summary>
    public const string Controller = "obj_jamm_decor_sleep";

    private const int WakeMargin = 1600;
    private const int SleepMargin = 2000;
    private const int BatchSize = 512;

    private static readonly string[] ScriptNames =
    [
        "jamm_ds_register", "jamm_ds_rebuild", "jamm_ds_sweep"
    ];

    /// <summary>Applies DecorSleep to a final GM1 archive using default-enabled INI settings.</summary>
    public static IReadOnlyList<string> Apply(UndertaleData data) => Apply(data, []);

    internal static IReadOnlyList<string> Apply(
        UndertaleData data,
        List<UndertaleCode> edits,
        bool enabledByDefault = true)
    {
        var decisions = new List<string>();
        if (data.GameObjects.ByName(Controller) is not null)
        {
            decisions.Add(Decision("runtime already installed"));
            return decisions;
        }
        if (data.GeneralInfo.Major != 1 || data.GeneralInfo.BytecodeVersion != 16
            || data.Rooms.ByName("NewWorld") is null
            || data.GameObjects.ByName("Camera") is null)
        {
            decisions.Add(Decision("skipped (required GameMaker 1.4 layout, NewWorld, or Camera missing)"));
            return decisions;
        }
        if (ScriptNames.Any(n => data.Scripts.ByName(n) is not null
            || data.Code.ByName("gml_Script_" + n) is not null
            || data.Functions.ByName(n) is not null))
        {
            decisions.Add(Decision("skipped (jamm_ds_* script names already in use)"));
            return decisions;
        }

        var report = DecorSleepAnalysis.Analyze(data);
        var covered = report.Objects.Where(o => o.Covered).ToArray();
        var skipped = report.Objects.Where(o => !o.Covered).ToArray();

        if (covered.Length == 0)
        {
            decisions.Add(Decision("skipped (no eligible static decoration objects found)"));
            return decisions;
        }

        var compiler = new CompileGroup(data);
        var patchedCode = new HashSet<UndertaleCode>();
        var coveredSet = covered.Select(c => c.GameObject).ToHashSet();

        // Patch eligible Step entries with an early-exit guard.
        var stepPatches = covered
            .SelectMany(c => c.StepPatches)
            .Where(sp => sp.Kind != DecorStepKind.Empty)
            .GroupBy(sp => sp.Code)
            .Select(g => g.First())
            .ToArray();

        foreach (var sp in stepPatches)
        {
            string original = ModObjects.Decompile(data, sp.Code);
            string guard = sp.Kind == DecorStepKind.CameraCullOnly
                // The controller owns camera culling for managed instances; skipped descendants
                // retain the original Camera logic. Avoid the redundant per-instance visibility read.
                ? "if (global.jamm_ds_enabled && jamm_ds_managed) exit;\n"
                : "if (global.jamm_ds_enabled && !jamm_ds_vis) exit;\n";
            Queue(sp.Code, guard + original);
        }

        if (report.LightAnchorCode is { } lightAnchor)
        {
            string original = ModObjects.Decompile(data, lightAnchor);
            string suffix = """
                if (global.jamm_ds_enabled && instance_exists(light))
                {
                    if (variable_instance_exists(light, "jamm_ds_vis") && !light.jamm_ds_vis)
                        light.visible = false;
                }
                """;
            Queue(lightAnchor, original + "\n" + suffix);
        }

        // Initialize jamm_ds_vis / jamm_ds_managed on any object that can reach a guarded Step event.
        var guardedStepSet = stepPatches.Select(s => s.Code).ToHashSet();
        InitializeStepObjects(data, report, coveredSet, guardedStepSet, Queue, patchedCode);

        // Add helper scripts and controller object.
        foreach (string scriptName in ScriptNames)
        {
            var code = UndertaleCode.CreateEmptyEntry(data, "gml_Script_" + scriptName);
            data.Scripts.Add(new UndertaleScript { Name = data.Strings.MakeString(scriptName), Code = code });
            Queue(code, ScriptBody(scriptName));
        }

        var controller = ModObjects.Create(data, Controller, persistent: true);
        controller.Visible = true;
        AddEvent(EventType.Create, 0u, CreateGml(data, report, enabledByDefault));
        AddEvent(EventType.Destroy, 0u, DestroyGml());
        AddEvent(EventType.Other, (uint)EventSubtypeOther.RoomStart, RoomStartGml());
        AddEvent(EventType.Step, (uint)EventSubtypeStep.BeginStep, BeginStepGml());
        AddEvent(EventType.Draw, (uint)EventSubtypeDraw.DrawBegin, DrawBeginGml());

        ModObjects.Compile(compiler);

        foreach (var room in data.Rooms)
            ModObjects.PlaceInstance(data, room.Name.Content, controller, first: true);

        int deactSafeCount = covered.Count(c => c.DeactivationSafe);
        decisions.Add(Decision($"enabled (default {(enabledByDefault ? "on" : "off")}, {covered.Length} object(s) covered [{deactSafeCount} deactivatable, {covered.Length - deactSafeCount} active-guarded], {stepPatches.Length} Step handler(s) guarded, {report.RootSweepObjects.Count} sweep root(s))"));

        // Emit compact per-family/object decisions for covered and notable skipped objects.
        foreach (var group in covered.GroupBy(c => c.Reason).OrderByDescending(g => g.Count()))
        {
            string sample = string.Join(", ", group.Take(6).Select(c => c.GameObject.Name.Content));
            string more = group.Count() > 6 ? $", +{group.Count() - 6} more" : "";
            decisions.Add($"[CGF-PERF-DECOR] covered ({group.Count()}): {group.Key} [{sample}{more}]");
        }
        foreach (var s in skipped.Where(s => s.GameObject.ParentId is { } p && (coveredSet.Contains(p) || p.Name.Content is "CaveRock" or "SceneItem" or "AbstractLight" or "Rock" or "Collidable" or "GrottoObject")))
        {
            decisions.Add($"[CGF-PERF-DECOR] skipped {s.GameObject.Name.Content}: {s.Reason}");
        }

        return decisions;

        void AddEvent(EventType type, uint subtype, string body)
            => Queue(controller.EventHandlerFor(type, subtype, data), body);

        void Queue(UndertaleCode code, string body)
        {
            if (patchedCode.Add(code))
            {
                edits.Add(code);
                compiler.QueueCodeReplace(code, body);
            }
        }
    }

    private static void InitializeStepObjects(
        UndertaleData data,
        DecorSleepReport report,
        HashSet<UndertaleGameObject> coveredSet,
        HashSet<UndertaleCode> guardedStepSet,
        Action<UndertaleCode, string> queue,
        HashSet<UndertaleCode> patchedCode)
    {
        bool UsesGuardedStep(UndertaleGameObject obj)
        {
            for (var cur = obj; cur is not null; cur = cur.ParentId)
            {
                foreach (var ev in cur.Events[(int)EventType.Step])
                {
                    if (ev.Actions.Any(a => a.CodeId is not null && guardedStepSet.Contains(a.CodeId)))
                        return true;
                }
            }
            return false;
        }

        // Objects that need jamm_ds_vis initialized in Create_0 (including skipped children inheriting a guarded Step).
        var needsInit = new HashSet<UndertaleGameObject>();
        foreach (var plan in report.Objects)
        {
            if (UsesGuardedStep(plan.GameObject))
                needsInit.Add(plan.GameObject);
        }

        foreach (var obj in needsInit)
        {
            bool isCovered = coveredSet.Contains(obj);
            bool parentInInit = obj.ParentId is not null && needsInit.Contains(obj.ParentId);
            var createCode = obj.Events[(int)EventType.Create]
                .FirstOrDefault(e => e.EventSubtype == 0u)?.Actions.FirstOrDefault()?.CodeId;

            if (createCode is null)
            {
                if (parentInInit && isCovered == coveredSet.Contains(obj.ParentId!))
                    continue;
                // Create a Create_0 event that preserves inheritance if parent has Create_0.
                string init = isCovered
                    ? "jamm_ds_vis = true;\njamm_ds_managed = true;\njamm_ds_sleep_tick = 0;\n"
                    : "jamm_ds_vis = true;\njamm_ds_managed = false;\njamm_ds_sleep_tick = 0;\n";
                if (obj.ParentId is not null) init = "action_inherited();\n" + init;
                queue(obj.EventHandlerFor(EventType.Create, 0u, data), init);
                continue;
            }

            if (patchedCode.Contains(createCode)) continue;

            string text = ModObjects.Decompile(data, createCode);
            bool callsInherited = text.Contains("action_inherited()", StringComparison.Ordinal)
                || text.Contains("event_inherited()", StringComparison.Ordinal);
            if (parentInInit && callsInherited && isCovered == coveredSet.Contains(obj.ParentId!))
                continue;

            string prefix = isCovered
                ? "jamm_ds_vis = true;\njamm_ds_managed = true;\njamm_ds_sleep_tick = 0;\n"
                : "jamm_ds_vis = true;\njamm_ds_managed = false;\njamm_ds_sleep_tick = 0;\n";
            // Place after action_inherited() if present so child overrides win.
            if (callsInherited)
            {
                string updated = text.Replace("action_inherited();", "action_inherited();\n" + prefix.TrimEnd(), StringComparison.Ordinal);
                if (updated == text)
                    updated = text.Replace("event_inherited();", "event_inherited();\n" + prefix.TrimEnd(), StringComparison.Ordinal);
                queue(createCode, updated == text ? prefix + text : updated);
            }
            else
            {
                queue(createCode, prefix + text);
            }
        }
    }

    private static string CreateGml(UndertaleData data, DecorSleepReport report, bool enabledByDefault)
    {
        var lines = new List<string>
        {
            "if (instance_number(object_index) > 1) { instance_destroy(); exit; }",
            "ini_open(\"jamm_perf.ini\");",
            $"global.jamm_ds_enabled = ini_read_real(\"Perf\", \"DecorSleep\", {(enabledByDefault ? 1 : 0)}) != 0;",
            $"if (!ini_key_exists(\"Perf\", \"DecorSleep\") && global.jamm_ds_enabled) ini_write_real(\"Perf\", \"DecorSleep\", 1);",
            "ini_close();",
            "jamm_ds_owner = true;",
            "global.jamm_ds_tick = 0;",
            "global.jamm_ds_built = false;",
            "global.jamm_ds_room = -1;",
            "global.jamm_ds_static_chk = -1;",
            "global.jamm_ds_dyn_chk = -1;",
            "global.jamm_ds_deact_n = 0;",
            "global.jamm_ds_prev_sonar = false;",
            "global.jamm_ds_count = 0;",
            "global.jamm_ds_cursor = 0;",
            "global.jamm_ds_cam_x = -999999;",
            "global.jamm_ds_cam_y = -999999;",
            "global.jamm_ds_vw = 0;",
            "global.jamm_ds_vh = 0;",
            "global.jamm_ds_obj_weed = asset_get_index(\"Weed\");",
            "global.jamm_ds_obj_dangler = asset_get_index(\"Dangler\");",
            "global.jamm_ds_obj_lure = asset_get_index(\"Lure\");",
            "global.jamm_ds_obj_sonar = asset_get_index(\"SonarPanel\");",
            "global.jamm_ds_obj_flags = ds_map_create();",
            "global.jamm_ds_obj_rate = ds_map_create();",
            "global.jamm_ds_obj_lure_alpha = ds_map_create();"
        };

        var covered = report.Objects.Where(o => o.Covered).ToArray();
        for (int i = 0; i < covered.Length; i++)
        {
            var p = covered[i];
            int flags = 1;
            if (p.ManageVisible) flags |= 2;
            if (p.Parallax == DecorParallaxKind.SceneItem) flags |= 4;
            else if (p.Parallax == DecorParallaxKind.GrottoObject) flags |= 8;
            if (p.DeactivationSafe) flags |= 16;

            var anglePatch = p.StepPatches.FirstOrDefault(s => s.Kind == DecorStepKind.AngleAccumPulse);
            if (anglePatch is not null) flags |= 32;
            if (p.StepPatches.Any(s => s.Kind == DecorStepKind.AlphaLerpLure)) flags |= 64;

            string objName = p.GameObject.Name.Content;
            lines.Add($"var o{i} = asset_get_index(\"{objName}\");");
            lines.Add($"if (o{i} >= 0) {{ ds_map_add(global.jamm_ds_obj_flags, o{i}, {flags});"
                + (anglePatch is not null ? string.Create(CultureInfo.InvariantCulture, $" ds_map_add(global.jamm_ds_obj_rate, o{i}, {anglePatch.Rate});") : "")
                + " }");
        }

        var deactPlans = covered.Where(o => o.DeactivationSafe).Select(o => o.GameObject).ToArray();
        var staticRoots = new List<UndertaleGameObject>();
        var dynRoots = new List<UndertaleGameObject>();
        foreach (var root in report.RootSweepObjects)
        {
            if (deactPlans.Any(d => DecorSleepAnalysis.Inherits(d, root)))
                staticRoots.Add(root);
            else
                dynRoots.Add(root);
        }

        lines.Add($"global.jamm_ds_sroot_count = {staticRoots.Count};");
        for (int i = 0; i < staticRoots.Count; i++)
            lines.Add($"global.jamm_ds_sroots[{i}] = asset_get_index(\"{staticRoots[i].Name.Content}\");");

        lines.Add($"global.jamm_ds_droot_count = {dynRoots.Count};");
        for (int i = 0; i < dynRoots.Count; i++)
            lines.Add($"global.jamm_ds_droots[{i}] = asset_get_index(\"{dynRoots[i].Name.Content}\");");

        lines.Add($"global.jamm_ds_deact_count = {report.DeactivatableObjects.Count};");
        for (int i = 0; i < report.DeactivatableObjects.Count; i++)
            lines.Add($"global.jamm_ds_deact_objs[{i}] = asset_get_index(\"{report.DeactivatableObjects[i].Name.Content}\");");

        return string.Join("\n", lines);
    }

    private static string DestroyGml() => """
        if (variable_instance_exists(id, "jamm_ds_owner") && jamm_ds_owner)
        {
            ds_map_destroy(global.jamm_ds_obj_flags);
            ds_map_destroy(global.jamm_ds_obj_rate);
            ds_map_destroy(global.jamm_ds_obj_lure_alpha);
        }
        """;

    private static string RoomStartGml() => """
        global.jamm_ds_built = false;
        global.jamm_ds_room = room;
        global.jamm_ds_static_chk = -1;
        global.jamm_ds_dyn_chk = -1;
        global.jamm_ds_deact_n = 0;
        global.jamm_ds_prev_sonar = false;
        global.jamm_ds_count = 0;
        global.jamm_ds_cursor = 0;
        global.jamm_ds_cam_x = -999999;
        global.jamm_ds_cam_y = -999999;
        """;

    private static string BeginStepGml() => """
        global.jamm_ds_tick += 1;
        if (!global.jamm_ds_enabled) exit;
        if (room != NewWorld || !instance_exists(Camera)) exit;
        jamm_ds_sweep(false);
        """;

    private static string DrawBeginGml() => """
        if (view_current != 0) exit;
        if (!global.jamm_ds_enabled) exit;
        if (room != NewWorld || !instance_exists(Camera)) exit;
        var cx = view_xview[0] + (view_wview[0] * 0.5);
        var cy = view_yview[0] + (view_hview[0] * 0.5);
        if (abs(cx - global.jamm_ds_cam_x) > 350 || abs(cy - global.jamm_ds_cam_y) > 350 || abs(view_wview[0] - global.jamm_ds_vw) > 100 || abs(view_hview[0] - global.jamm_ds_vh) > 100)
            jamm_ds_sweep(true);
        """;

    private static string ScriptBody(string name) => name switch
    {
        "jamm_ds_register" => """
            var robj = argument0;
            var only_new = argument1;
            if (robj < 0) exit;
            var o_weed = global.jamm_ds_obj_weed;
            var o_dangler = global.jamm_ds_obj_dangler;
            with (robj)
            {
                if (only_new && variable_instance_exists(id, "jamm_ds_tracked")) continue;
                var fl = ds_map_find_value(global.jamm_ds_obj_flags, object_index);
                if (is_undefined(fl)) continue;
                jamm_ds_tracked = true;
                jamm_ds_vis = true;
                jamm_ds_sleep_tick = 0;

                var idx = global.jamm_ds_count;
                global.jamm_ds_ids[idx] = id;
                global.jamm_ds_flags[idx] = fl;
                global.jamm_ds_vis[idx] = true;
                global.jamm_ds_deact[idx] = false;
                global.jamm_ds_bvis[idx] = visible;
                global.jamm_ds_stick[idx] = 0;

                if ((fl & 4) != 0 && variable_instance_exists(id, "home_x") && variable_instance_exists(id, "para_scale"))
                {
                    var sd = depth + 18;
                    if (depth < -18 && (object_index == o_weed || object_index == o_dangler)) sd += 1;
                    if (sd > 18) sd -= 18;
                    if (sd > 0) sd *= 0.1; else sd *= 0.5;
                    global.jamm_ds_hx[idx] = home_x;
                    global.jamm_ds_hy[idx] = home_y;
                    global.jamm_ds_px[idx] = sd * para_scale;
                    global.jamm_ds_py[idx] = sd * 0.1 * para_scale;
                }
                else if ((fl & 8) != 0 && variable_instance_exists(id, "home_x") && variable_instance_exists(id, "para_x"))
                {
                    global.jamm_ds_hx[idx] = home_x;
                    global.jamm_ds_hy[idx] = home_y;
                    global.jamm_ds_px[idx] = para_x;
                    global.jamm_ds_py[idx] = para_y;
                }
                else
                {
                    global.jamm_ds_flags[idx] = fl & (~12);
                    global.jamm_ds_hx[idx] = x;
                    global.jamm_ds_hy[idx] = y;
                    global.jamm_ds_px[idx] = 0;
                    global.jamm_ds_py[idx] = 0;
                }
                global.jamm_ds_sw[idx] = max(abs(sprite_width), 256);
                global.jamm_ds_sh[idx] = max(abs(sprite_height), 256);
                global.jamm_ds_count = idx + 1;
            }
            """,
        "jamm_ds_rebuild" => """
            for (var k = 0; k < global.jamm_ds_count; k += 1)
            {
                if (global.jamm_ds_deact[k])
                {
                    var rid = global.jamm_ds_ids[k];
                    instance_activate_object(rid);
                    global.jamm_ds_deact[k] = false;
                }
            }
            global.jamm_ds_deact_n = 0;
            var s_chk = 0;
            for (var r = 0; r < global.jamm_ds_sroot_count; r += 1)
            {
                var robj = global.jamm_ds_sroots[r];
                if (robj >= 0) s_chk += instance_number(robj);
            }
            var d_chk = 0;
            for (var r = 0; r < global.jamm_ds_droot_count; r += 1)
            {
                var robj = global.jamm_ds_droots[r];
                if (robj >= 0) d_chk += instance_number(robj);
            }
            global.jamm_ds_static_chk = s_chk;
            global.jamm_ds_dyn_chk = d_chk;
            global.jamm_ds_count = 0;
            global.jamm_ds_cursor = 0;
            global.jamm_ds_room = room;
            global.jamm_ds_built = true;
            for (var r = 0; r < global.jamm_ds_sroot_count; r += 1)
                jamm_ds_register(global.jamm_ds_sroots[r], false);
            for (var r = 0; r < global.jamm_ds_droot_count; r += 1)
                jamm_ds_register(global.jamm_ds_droots[r], false);
            """,
        "jamm_ds_sweep" => $$"""
            var force_full = argument0;
            var s_chk = 0;
            for (var r = 0; r < global.jamm_ds_sroot_count; r += 1)
            {
                var robj = global.jamm_ds_sroots[r];
                if (robj >= 0) s_chk += instance_number(robj);
            }
            if (!global.jamm_ds_built || room != global.jamm_ds_room || (s_chk + global.jamm_ds_deact_n) != global.jamm_ds_static_chk)
            {
                jamm_ds_rebuild();
                force_full = true;
            }
            else
            {
                var d_chk = 0;
                for (var r = 0; r < global.jamm_ds_droot_count; r += 1)
                {
                    var robj = global.jamm_ds_droots[r];
                    if (robj >= 0) d_chk += instance_number(robj);
                }
                if (d_chk != global.jamm_ds_dyn_chk)
                {
                    global.jamm_ds_dyn_chk = d_chk;
                    for (var r = 0; r < global.jamm_ds_droot_count; r += 1)
                        jamm_ds_register(global.jamm_ds_droots[r], true);
                }
            }

            var n = global.jamm_ds_count;
            if (n <= 0) exit;

            var vx0 = view_xview[0];
            var vy0 = view_yview[0];
            var vw = view_wview[0];
            var vh = view_hview[0];
            var vx1 = vx0 + vw;
            var vy1 = vy0 + vh;
            var cam_x = Camera.x;
            var cam_y = Camera.y;
            var cx = vx0 + (vw * 0.5);
            var cy = vy0 + (vh * 0.5);
            if (abs(cx - global.jamm_ds_cam_x) > 350 || abs(cy - global.jamm_ds_cam_y) > 350 || abs(vw - global.jamm_ds_vw) > 100 || abs(vh - global.jamm_ds_vh) > 100)
                force_full = true;
            global.jamm_ds_cam_x = cx;
            global.jamm_ds_cam_y = cy;
            global.jamm_ds_vw = vw;
            global.jamm_ds_vh = vh;

            var sonar_on = false;
            if (global.jamm_ds_obj_sonar >= 0 && instance_exists(global.jamm_ds_obj_sonar))
                sonar_on = global.jamm_ds_obj_sonar.active && global.jamm_ds_obj_sonar.shouldDraw;
            if (sonar_on && !global.jamm_ds_prev_sonar)
                force_full = true;
            global.jamm_ds_prev_sonar = sonar_on;

            var v1_on = view_visible[1];
            var v1x0 = view_xview[1];
            var v1y0 = view_yview[1];
            var v1x1 = v1x0 + view_wview[1];
            var v1y1 = v1y0 + view_hview[1];

            var batch = force_full ? n : min(n, {{BatchSize}});
            var cursor = force_full ? 0 : global.jamm_ds_cursor;
            if (cursor >= n) cursor = 0;
            var tick = global.jamm_ds_tick;

            for (var k = 0; k < batch; k += 1)
            {
                if (n <= 0) break;
                if (cursor >= n) cursor = 0;
                var slot = cursor;
                cursor += 1;
                if (cursor >= n) cursor = 0;

                var inst = global.jamm_ds_ids[slot];
                var fl = global.jamm_ds_flags[slot];
                var is_deact = global.jamm_ds_deact[slot];

                if (!is_deact && !instance_exists(inst))
                {
                    var last = n - 1;
                    if (slot < last)
                    {
                        global.jamm_ds_ids[slot] = global.jamm_ds_ids[last];
                        global.jamm_ds_flags[slot] = global.jamm_ds_flags[last];
                        global.jamm_ds_vis[slot] = global.jamm_ds_vis[last];
                        global.jamm_ds_deact[slot] = global.jamm_ds_deact[last];
                        global.jamm_ds_bvis[slot] = global.jamm_ds_bvis[last];
                        global.jamm_ds_stick[slot] = global.jamm_ds_stick[last];
                        global.jamm_ds_hx[slot] = global.jamm_ds_hx[last];
                        global.jamm_ds_hy[slot] = global.jamm_ds_hy[last];
                        global.jamm_ds_px[slot] = global.jamm_ds_px[last];
                        global.jamm_ds_py[slot] = global.jamm_ds_py[last];
                        global.jamm_ds_sw[slot] = global.jamm_ds_sw[last];
                        global.jamm_ds_sh[slot] = global.jamm_ds_sh[last];
                        cursor = slot;
                        k -= 1;
                    }
                    n = last;
                    batch -= 1;
                    global.jamm_ds_count = n;
                    continue;
                }

                var wx, wy, sw, sh;
                if ((fl & 12) != 0)
                {
                    var hx = global.jamm_ds_hx[slot];
                    var hy = global.jamm_ds_hy[slot];
                    wx = hx + ((cam_x - hx) * global.jamm_ds_px[slot]);
                    wy = hy + ((cam_y - hy) * global.jamm_ds_py[slot]);
                }
                else if (is_deact)
                {
                    wx = global.jamm_ds_hx[slot];
                    wy = global.jamm_ds_hy[slot];
                }
                else
                {
                    wx = inst.x;
                    wy = inst.y;
                }

                if (is_deact)
                {
                    sw = global.jamm_ds_sw[slot];
                    sh = global.jamm_ds_sh[slot];
                }
                else
                {
                    sw = max(abs(inst.sprite_width), 256);
                    sh = max(abs(inst.sprite_height), 256);
                }

                var was_vis = global.jamm_ds_vis[slot];
                var m = was_vis ? {{SleepMargin}} : {{WakeMargin}};
                var in_view = (wx + sw >= vx0 - m && wx - sw <= vx1 + m && wy + sh >= vy0 - m && wy - sh <= vy1 + m);
                if (!in_view && v1_on)
                {
                    if (wx + sw >= v1x0 - m && wx - sw <= v1x1 + m && wy + sh >= v1y0 - m && wy - sh <= v1y1 + m)
                        in_view = true;
                }
                if (!in_view && sonar_on && (fl & 4) != 0)
                {
                    if (abs(global.jamm_ds_hx[slot] - cam_x) <= 8200 && abs(global.jamm_ds_hy[slot] - cam_y) <= 8200)
                        in_view = true;
                }

                if (was_vis && !in_view)
                {
                    global.jamm_ds_vis[slot] = false;
                    global.jamm_ds_stick[slot] = tick;
                    global.jamm_ds_bvis[slot] = inst.visible;
                    global.jamm_ds_sw[slot] = sw;
                    global.jamm_ds_sh[slot] = sh;
                    if ((fl & 2) != 0)
                        inst.visible = false;
                    inst.jamm_ds_vis = false;
                    inst.jamm_ds_sleep_tick = tick;
                    if ((fl & 16) != 0)
                    {
                        global.jamm_ds_deact[slot] = true;
                        global.jamm_ds_deact_n += 1;
                        instance_deactivate_object(inst);
                    }
                }
                else if (!was_vis && in_view)
                {
                    if (is_deact)
                    {
                        instance_activate_object(inst);
                        global.jamm_ds_deact[slot] = false;
                        global.jamm_ds_deact_n -= 1;
                    }
                    global.jamm_ds_vis[slot] = true;
                    inst.jamm_ds_vis = true;
                    if ((fl & 2) != 0)
                        inst.visible = global.jamm_ds_bvis[slot];
                    if ((fl & 12) != 0)
                    {
                        inst.x = wx;
                        inst.y = wy;
                    }
                    if ((fl & 32) != 0)
                    {
                        var dt = tick - global.jamm_ds_stick[slot];
                        if (dt > 0)
                        {
                            var rate = ds_map_find_value(global.jamm_ds_obj_rate, inst.object_index);
                            if (!is_undefined(rate)) inst.image_angle += rate * dt;
                        }
                    }
                    if ((fl & 64) != 0)
                    {
                        var dt2 = tick - global.jamm_ds_stick[slot];
                        if (dt2 > 0)
                        {
                            var targ = 1;
                            if (global.jamm_ds_obj_lure >= 0 && instance_exists(global.jamm_ds_obj_lure))
                                if (global.jamm_ds_obj_lure.y > 1295) targ = 0.15;
                            inst.image_alpha = targ + ((inst.image_alpha - targ) * power(0.92, dt2));
                        }
                    }
                }
            }
            global.jamm_ds_cursor = cursor;
            """,
        _ => ""
    };

    private static string Decision(string detail) => $"[CGF-PERF-DECOR] {detail}";
}
