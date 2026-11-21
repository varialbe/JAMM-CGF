using UndertaleModLib;
using UndertaleModLib.Compiler;
using UndertaleModLib.Models;

namespace Jamm.ModMaker;

internal static class MaizeScene
{
    internal static void Apply(UndertaleData data)
    {
        var world = data.Rooms.ByName("NewWorld");
        if (world is null || !world.GameObjects.Any(i => i.ObjectDefinition?.Name.Content == "Maize"))
            throw new InvalidDataException("The Maize scene needs a legacy NewWorld room with a placed Maize.");

        var probe = ModObjects.Create(data, "obj_jamm_bench_maize", persistent: false);
        var compiler = new CompileGroup(data);
        compiler.QueueCodeReplace(probe.EventHandlerFor(EventType.Create, 0u, data), """
            bench_fish_id = noone;
            bench_steps = 0;
            bench_target_changes = 0;
            bench_initial_degree = -1;
            """);
        compiler.QueueCodeReplace(probe.EventHandlerFor(EventType.Step, (uint)EventSubtypeStep.BeginStep, data), """
            if (!instance_exists(SaveManager)) exit;
            SaveManager.saving_locked = 1;
            if (!SaveManager.loaded) exit;
            if (!instance_exists(SonarPanel)) instance_create(0, 0, SonarPanel);
            if (!instance_exists(SonarPanelAdvanced)) instance_create(0, 0, SonarPanelAdvanced);
            SonarPanel.active = 1;
            SonarPanel.drawOverride = 1;
            SonarPanelAdvanced.active = 1;
            Girl.hide_ui = 0;

            if (bench_fish_id == noone)
            {
                instance_activate_object(Maize);
                if (!instance_exists(Maize)) exit;
                bench_fish_id = instance_find(Maize, 0);
                var node = bench_fish_id.target_node;
                show_debug_message("MAIZE sonar opened; initial target " + string(node));
                if (!instance_exists(node)) node = instance_nearest(bench_fish_id.x, bench_fish_id.y, NavNode);
                if (instance_exists(node))
                {
                    bench_fish_id.x = node.x;
                    bench_fish_id.y = node.y;
                    bench_initial_degree = ds_list_size(node.edges);
                    show_debug_message("MAIZE benchmark starts at node " + string(node.x) + "," + string(node.y)
                        + "; edges " + string(bench_initial_degree));
                }
            }
            instance_activate_object(bench_fish_id);
            if (instance_exists(bench_fish_id)) bench_fish_id.deactivated = 0;
            """);
        compiler.QueueCodeReplace(probe.EventHandlerFor(EventType.Step, (uint)EventSubtypeStep.EndStep, data), """
            if (bench_fish_id < 0 || !instance_exists(bench_fish_id)) exit;
            SonarPanel.cave_sonar = 1;
            view_xview[3] = bench_fish_id.x - 1000;
            view_yview[3] = bench_fish_id.y - 1000;
            view_wview[3] = 2000;
            view_hview[3] = 2000;
            """);
        compiler.QueueCodeReplace(probe.EventHandlerFor(EventType.Draw, (uint)EventSubtypeDraw.DrawGUIEnd, data), """
            if (bench_steps < 120 || !SonarPanelAdvanced.advancedDrawEnabled
                || !view_visible[3] || !instance_exists(bench_fish_id)) exit;
            var log = file_text_open_write("jamm_bench_maize.csv");
            file_text_write_string(log, "maize_steps,target_changes,initial_edges,sonar_active,target");
            file_text_writeln(log);
            file_text_write_string(log, string(bench_steps) + "," + string(bench_target_changes)
                + "," + string(bench_initial_degree) + "," + string(SonarPanelAdvanced.advancedDrawEnabled)
                + "," + string(bench_fish_id.target_node));
            file_text_writeln(log);
            file_text_close(log);
            game_end();
            """);
        Instrument("gml_Object_NavBrain_Other_4", "", """
            with (NavNode)
                show_debug_message("MAIZE graph " + string(x) + "," + string(y) + "," + string(ds_list_size(edges)));
            """);
        Instrument("gml_Object_Maize_Other_10", """
            var jamm_bench_old_target = target_node;
            with (obj_jamm_bench_maize)
                if (bench_fish_id == other.id) bench_steps += 1;
            """, """
            with (obj_jamm_bench_maize)
            {
                if (bench_fish_id == other.id && jamm_bench_old_target != other.target_node)
                {
                    bench_target_changes += 1;
                    if (bench_target_changes <= 4)
                        show_debug_message("MAIZE target " + string(jamm_bench_old_target) + " -> " + string(other.target_node));
                }
            }
            """);
        ModObjects.PlaceInstance(data, "NewWorld", probe, first: false);
        ModObjects.Compile(compiler);

        void Instrument(string name, string before, string after)
        {
            var code = data.Code.ByName(name)
                ?? throw new InvalidDataException($"The Maize scene requires {name}.");
            compiler.QueueCodeReplace(code, before + "\n" + ModObjects.Decompile(data, code) + "\n" + after);
        }
    }
}
