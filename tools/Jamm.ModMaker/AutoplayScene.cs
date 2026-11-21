using UndertaleModLib;
using UndertaleModLib.Compiler;
using UndertaleModLib.Models;

namespace Jamm.ModMaker;

/// <summary>
/// Bench scene for the autoplay crash hunter. The bot itself sends real keyboard input from the VPS runner
/// (tools/vps/autoplay.sh); this probe only closes the new-game menus and appends the game state to
/// <c>jamm_autoplay.csv</c> every two seconds so a crash report shows what the game was doing.
/// </summary>
internal static class AutoplayScene
{
    private const string Probe = "obj_jamm_autoplay";

    internal static bool Requested(string[] args)
    {
        int index = Array.IndexOf(args, "--scene");
        return index >= 3 && args.ElementAtOrDefault(index + 1) == "autoplay";
    }

    internal static void Apply(UndertaleData data)
    {
        if (data.Rooms.ByName("NewWorld") is null || data.GameObjects.ByName("Girl") is null)
            throw new InvalidDataException("The autoplay scene needs the legacy NewWorld room and Girl object.");
        var probe = ModObjects.Create(data, Probe, persistent: true);
        var compiler = new CompileGroup(data);
        string dismiss = string.Join('\n', new[] { "GameModeMenu", "GM_Classic", "GM_Realism", "Message_Start" }
            .Where(name => data.GameObjects.ByName(name) is not null)
            .Select(name => $"with ({name}) instance_destroy();"));
        string lure = data.GameObjects.ByName("Lure") is not null
            ? "if (instance_exists(Lure)) { jamm_ap_ls = Lure.state; jamm_ap_ly = Lure.y; }" : "";
        compiler.QueueCodeReplace(probe.EventHandlerFor(EventType.Create, 0u, data), """
            jamm_ap_start = -1;
            jamm_ap_next = 0;
            jamm_ap_dismissed = 0;
            var f = file_text_open_write("jamm_autoplay.csv");
            file_text_write_string(f, "ms,room,fps,x,y,dollars,level,lure_state,lure_y,instances");
            file_text_writeln(f);
            file_text_close(f);
            """);
        compiler.QueueCodeReplace(probe.EventHandlerFor(EventType.Step, (uint)EventSubtypeStep.EndStep, data), $$"""
            if (jamm_ap_start < 0) jamm_ap_start = current_time;
            if (room == NewWorld && instance_exists(SaveManager) && SaveManager.loaded && !jamm_ap_dismissed
                && current_time - jamm_ap_start > 5000)
            {
                {{dismiss}}
                if (instance_exists(Girl)) Girl.startMessageShown = 1;
                jamm_ap_dismissed = 1;
            }
            if (current_time < jamm_ap_next) exit;
            jamm_ap_next = current_time + 2000;
            var jamm_ap_ls = -1, jamm_ap_ly = 0, jamm_ap_x = 0, jamm_ap_y = 0, jamm_ap_d = 0, jamm_ap_lv = 0;
            if (instance_exists(Girl)) { jamm_ap_x = Girl.x; jamm_ap_y = Girl.y; jamm_ap_d = Girl.dollars; jamm_ap_lv = Girl.level; }
            {{lure}}
            var f = file_text_open_append("jamm_autoplay.csv");
            file_text_write_string(f, string(current_time - jamm_ap_start) + "," + room_get_name(room) + "," + string(fps) + ","
                + string(round(jamm_ap_x)) + "," + string(round(jamm_ap_y)) + "," + string_format(jamm_ap_d, 0, 0) + ","
                + string(jamm_ap_lv) + "," + string(jamm_ap_ls) + "," + string(round(jamm_ap_ly)) + "," + string(instance_count));
            file_text_writeln(f);
            file_text_close(f);
            """);
        ModObjects.PlaceInstance(data, "NewWorld", probe, first: false);
        ModObjects.Compile(compiler);
    }
}
