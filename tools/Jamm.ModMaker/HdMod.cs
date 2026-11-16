using UndertaleModLib;
using UndertaleModLib.Compiler;
using UndertaleModLib.Models;
using UndertaleModLib.Util;

namespace Jamm.ModMaker;

/// <summary>
/// "HD" mod: 2x AI-upscaled texture pages (drawn at the original size, so nothing changes in layout),
/// rendering at the real window resolution instead of a 1200x720 surface, and multisample anti-aliasing.
/// </summary>
public static class HdMod
{
    /// <summary>Name of the persistent HD runtime controller.</summary>
    public const string ObjectName = "obj_jamm_hd";

    /// <summary>Adds native aspect-correct fullscreen rendering and optionally replaces all safe pages.</summary>
    public static void Apply(UndertaleData data, string? upscaledPages, bool debug = false)
    {
        if (upscaledPages is not null) ReplacePages(data, upscaledPages);

        // Keep the preference for surface-enabled paths; native back-buffer drawing needs explicit ports.
        data.GeneralInfo.Info |= UndertaleGeneralInfo.InfoFlags.Scale;
        data.Options.WindowColor = 0;
        // Two one-pixel, otherwise empty ports pin the runner's visible-port bounds to the
        // entire window. Offsetting only the content ports would leave their bounds too small.
        if (data.Rooms.Any(r => r.Views.Skip(6).Any(v => v.Enabled)))
            throw new InvalidDataException("HD needs unused views 6 and 7 for fullscreen bounds. Use the supported legacy original.");
        var compiler = new CompileGroup(data);
        foreach (var code in data.Code.Where(c => c.Name.Content.Contains("_Draw_")).ToArray())
        {
            string name = code.Name.Content;
            if (name.EndsWith("_Draw_0") || name.EndsWith("_Draw_72") || name.EndsWith("_Draw_73"))
                compiler.QueueCodeReplace(code, "if (view_current >= 6) exit;\n" + ModObjects.Decompile(data, code));
        }
        var hd = ModObjects.Create(data, ObjectName, persistent: true);
        compiler.QueueCodeReplace(hd.EventHandlerFor(EventType.Create, 0u, data), Create(debug));
        compiler.QueueCodeReplace(hd.EventHandlerFor(EventType.Draw, (uint)EventSubtypeDraw.DrawGUI, data), Toast);
        compiler.QueueCodeReplace(hd.EventHandlerFor(EventType.Step, (uint)EventSubtypeStep.EndStep, data), Step(debug));
        compiler.QueueCodeReplace(hd.EventHandlerFor(EventType.Step, (uint)EventSubtypeStep.BeginStep, data), Input);
        compiler.QueueCodeReplace(hd.EventHandlerFor(EventType.Other, (uint)EventSubtypeOther.RoomStart, data), RoomStart);
        compiler.QueueCodeReplace(hd.EventHandlerFor(EventType.Draw, (uint)EventSubtypeDraw.PreDraw, data), PreDraw);
        compiler.QueueCodeReplace(hd.EventHandlerFor(EventType.Draw, (uint)EventSubtypeDraw.DrawGUIEnd, data), Bars + (debug ? DebugShots : ""));
        ModObjects.Compile(compiler);
        foreach (string room in new[] { "NewWorld", "room11" })
            ModObjects.PlaceInstance(data, room, hd, first: true);
    }

    /// <summary>Swaps in 2x pages; every item on such a page samples a 2x source rectangle into its unchanged target size.</summary>
    private static void ReplacePages(UndertaleData data, string directory)
    {
        var safe = TexturePages.Describe(data).Where(p => p.Upscale).ToList();
        foreach (var info in safe)
        {
            string file = Path.Combine(directory, $"page_{info.Index}.png");
            if (!File.Exists(file))
                throw new InvalidDataException($"Missing HD texture '{file}'. Supply all {safe.Count} safe 2x pages with --upscaled-pages.");
            var page = data.EmbeddedTextures[info.Index];
            var image = GMImage.FromPng(File.ReadAllBytes(file), verifyHeader: true);
            if (image.Width != info.Width * 2 || image.Height != info.Height * 2)
                throw new InvalidDataException($"HD texture '{file}' is {image.Width}x{image.Height}; expected exactly {info.Width * 2}x{info.Height * 2}. Regenerate this page at 2x.");
            page.TextureData.Image = image;
            foreach (var item in data.TexturePageItems.Where(t => t.TexturePage == page))
            {
                item.SourceX = checked((ushort)(item.SourceX * 2));
                item.SourceY = checked((ushort)(item.SourceY * 2));
                item.SourceWidth = checked((ushort)(item.SourceWidth * 2));
                item.SourceHeight = checked((ushort)(item.SourceHeight * 2));
            }
        }
        Console.WriteLine($"Upscaled {safe.Count} texture pages; font and tile pages stay at original size.");
    }

    private static string Create(bool debug) => $$"""
        if (instance_number(object_index) > 1)
        {
            instance_destroy();
            exit;
        }
        persistent = true;
        ini_open("jamm_hd.ini");
        jamm_hd_aa = ini_read_real("HD", "AntiAliasing", 4);
        jamm_hd_fullscreen = ini_read_real("HD", "Fullscreen", 1);
        ini_write_real("HD", "Fullscreen", jamm_hd_fullscreen);
        {{(debug ? "jamm_hd_window = ini_read_real(\"HD\", \"DebugWindowScale\", 1);" : "jamm_hd_window = 1;")}}
        ini_close();
        // Draw straight to the back buffer: the scene is rendered at the window's own resolution and the
        // multisampled back buffer smooths every edge (the application surface supports neither).
        application_surface_enable(false);
        jamm_hd_level = 0;
        if (jamm_hd_aa >= 8 && (display_aa & 8))
            jamm_hd_level = 8;
        else if (jamm_hd_aa >= 4 && (display_aa & 4))
            jamm_hd_level = 4;
        else if (jamm_hd_aa >= 2 && (display_aa & 2))
            jamm_hd_level = 2;
        if (jamm_hd_level > 0)
            display_reset(jamm_hd_level, false);
        jamm_hd_window = max(0.5, min(3, jamm_hd_window));
        window_set_size(1200 * jamm_hd_window, 720 * jamm_hd_window);
        window_set_colour(c_black);
        window_set_fullscreen(jamm_hd_fullscreen);
        jamm_hd_ready = false;
        jamm_hd_toast_until = current_time + 4000;
        jamm_hd_text = "HD mod: 2x textures, native resolution, " + string(jamm_hd_level) + "x MSAA";
        jamm_hd_shots = 0;
        jamm_hd_started = current_time;
        {{(debug ? "directory_create(\"out\"); directory_create(\"out/phase6b\"); window_set_fullscreen(false);" : "")}}
        """;

    private const string Toast = """
        if (current_time < jamm_hd_toast_until)
        {
            draw_set_halign(fa_left);
            draw_set_valign(fa_top);
            draw_set_alpha(min(1, (jamm_hd_toast_until - current_time) / 500));
            draw_set_color(c_black);
            draw_rectangle(8, 44, 20 + string_width(jamm_hd_text), 56 + string_height(jamm_hd_text), false);
            draw_set_color(c_white);
            draw_text(14, 50, jamm_hd_text);
            draw_set_alpha(1);
        }
        """;

    private static string Step(bool debug) => $$"""
        if (keyboard_check_pressed(vk_f11))
        {
            jamm_hd_fullscreen = !window_get_fullscreen();
            window_set_fullscreen(jamm_hd_fullscreen);
            if (!jamm_hd_fullscreen)
                window_set_size(1200 * jamm_hd_window, 720 * jamm_hd_window);
            ini_open("jamm_hd.ini");
            ini_write_real("HD", "Fullscreen", jamm_hd_fullscreen);
            ini_close();
            {{(debug ? "jamm_hd_shots = 2;" : "")}}
        }
        """;

    // Room Start runs after the runner has restored this room's view definitions. Never use
    // the previous frame's scaled ports as a new baseline (including hidden minimap/sonar ports).
    private const string RoomStart = """
        for (jamm_hd_i = 0; jamm_hd_i < 8; jamm_hd_i += 1)
        {
            jamm_hd_bx[jamm_hd_i] = view_xport[jamm_hd_i];
            jamm_hd_by[jamm_hd_i] = view_yport[jamm_hd_i];
            jamm_hd_bw[jamm_hd_i] = view_wport[jamm_hd_i];
            jamm_hd_bh[jamm_hd_i] = view_hport[jamm_hd_i];
        }
        jamm_hd_ready = true;
        """ + Transform;

    private const string Transform = """
        jamm_hd_ww = max(1, window_get_width());
        jamm_hd_wh = max(1, window_get_height());
        jamm_hd_k = min(jamm_hd_ww / 1200, jamm_hd_wh / 720);
        jamm_hd_ox = (jamm_hd_ww - 1200 * jamm_hd_k) / 2;
        jamm_hd_oy = (jamm_hd_wh - 720 * jamm_hd_k) / 2;
        for (jamm_hd_i = 0; jamm_hd_i < 8; jamm_hd_i += 1)
        {
            view_xport[jamm_hd_i] = jamm_hd_ox + jamm_hd_bx[jamm_hd_i] * jamm_hd_k;
            view_yport[jamm_hd_i] = jamm_hd_oy + jamm_hd_by[jamm_hd_i] * jamm_hd_k;
            view_wport[jamm_hd_i] = jamm_hd_bw[jamm_hd_i] * jamm_hd_k;
            view_hport[jamm_hd_i] = jamm_hd_bh[jamm_hd_i] * jamm_hd_k;
        }
        // Reserve unused 6/7 as empty one-pixel bounds anchors. Their draw events exit above;
        // GUI End masks their background pixels. No input port covers the rest of either bar.
        for (jamm_hd_i = 6; jamm_hd_i < 8; jamm_hd_i += 1)
        {
            view_visible[jamm_hd_i] = (jamm_hd_ox > 0 || jamm_hd_oy > 0);
            view_xport[jamm_hd_i] = (jamm_hd_i - 6) * (jamm_hd_ww - 1);
            view_yport[jamm_hd_i] = (jamm_hd_i - 6) * (jamm_hd_wh - 1);
            view_wport[jamm_hd_i] = 1;
            view_hport[jamm_hd_i] = 1;
            view_wview[jamm_hd_i] = 1;
            view_hview[jamm_hd_i] = 1;
            // Any fallback room mouse mapping at these pixels lies outside the game world.
            view_xview[jamm_hd_i] = -1000000;
            view_yview[jamm_hd_i] = -1000000;
        }
        // size() overrides maximise(), so reset it FIRST. The latter is available in bytecode 16.
        display_set_gui_size(1200, 720);
        display_set_gui_maximise(jamm_hd_k, jamm_hd_k, jamm_hd_ox, jamm_hd_oy);
        """;

    private const string PreDraw = """
        // Pre Draw executes once on the display buffer, before per-view drawing. Clearing here
        // covers the bars too; a Draw-event clear would be clipped to the current port.
        draw_clear(c_black);
        if (!jamm_hd_ready) exit;
        """ + Transform;

    private const string Input = """
        if (!jamm_hd_ready) exit;
        """ + Transform + """
        // The runner can fall back to a view outside its port. Reject bar presses before game
        // Step/mouse events, including the two artificial corner pixels, without warping mouse_x/y.
        jamm_hd_mx = window_mouse_get_x();
        jamm_hd_my = window_mouse_get_y();
        jamm_hd_hit = false;
        for (jamm_hd_i = 0; jamm_hd_i < 6; jamm_hd_i += 1)
        {
            if (view_visible[jamm_hd_i]
                && jamm_hd_mx >= view_xport[jamm_hd_i] && jamm_hd_my >= view_yport[jamm_hd_i]
                && jamm_hd_mx < view_xport[jamm_hd_i] + view_wport[jamm_hd_i]
                && jamm_hd_my < view_yport[jamm_hd_i] + view_hport[jamm_hd_i])
                jamm_hd_hit = true;
        }
        if (!jamm_hd_hit)
        {
            mouse_clear(mb_left);
            mouse_clear(mb_right);
            mouse_clear(mb_middle);
        }
        """;

    private const string Bars = """
        // Mask anchor/background pixels and any GUI overflow using the same logical transform.
        draw_set_alpha(1);
        draw_set_color(c_black);
        if (jamm_hd_ox > 0)
        {
            draw_rectangle(-jamm_hd_ox / jamm_hd_k, -jamm_hd_oy / jamm_hd_k, 0, 720 + jamm_hd_oy / jamm_hd_k, false);
            draw_rectangle(1200, -jamm_hd_oy / jamm_hd_k, 1200 + jamm_hd_ox / jamm_hd_k, 720 + jamm_hd_oy / jamm_hd_k, false);
        }
        if (jamm_hd_oy > 0)
        {
            draw_rectangle(0, -jamm_hd_oy / jamm_hd_k, 1200, 0, false);
            draw_rectangle(0, 720, 1200, 720 + jamm_hd_oy / jamm_hd_k, false);
        }
        draw_set_color(c_white);
        """;

    private const string DebugShots = """
        // current_time is runner uptime, so use elapsed time since this instance's Create.
        // Capture at the end of GUI drawing, after the native back buffer has been composed.
        if (jamm_hd_shots < 2 && current_time - jamm_hd_started > 4000)
        {
            if (jamm_hd_shots == 0)
                jamm_hd_mode = "windowed";
            else
                jamm_hd_mode = "fullscreen";
            screen_save("out/phase6b/jamm_hd_" + jamm_hd_mode + ".png");
            jamm_hd_log = file_text_open_append("out/phase6b/jamm_hd_sizes.txt");
            file_text_write_string(jamm_hd_log, jamm_hd_mode + " window=" + string(window_get_width()) + "x" + string(window_get_height())
                + " display=" + string(display_get_width()) + "x" + string(display_get_height())
                + " GUI=" + string(display_get_gui_width()) + "x" + string(display_get_gui_height())
                + " k=" + string(jamm_hd_k) + " ox=" + string(jamm_hd_ox) + " oy=" + string(jamm_hd_oy));
            file_text_writeln(jamm_hd_log);
            for (jamm_hd_i = 0; jamm_hd_i < 8; jamm_hd_i += 1)
            {
                file_text_write_string(jamm_hd_log, "view " + string(jamm_hd_i) + " visible=" + string(view_visible[jamm_hd_i])
                    + " world=" + string(view_wview[jamm_hd_i]) + "x" + string(view_hview[jamm_hd_i])
                    + " port=" + string(view_xport[jamm_hd_i]) + "," + string(view_yport[jamm_hd_i])
                    + " " + string(view_wport[jamm_hd_i]) + "x" + string(view_hport[jamm_hd_i]));
                file_text_writeln(jamm_hd_log);
            }
            file_text_close(jamm_hd_log);
            jamm_hd_shots += 1;
            jamm_hd_started = current_time;
            if (jamm_hd_shots == 1)
                window_set_fullscreen(true);
            else
                window_set_fullscreen(jamm_hd_fullscreen);
        }
        """;
}
