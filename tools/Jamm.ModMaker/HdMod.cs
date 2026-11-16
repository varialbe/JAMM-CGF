using UndertaleModLib;
using UndertaleModLib.Compiler;
using UndertaleModLib.Models;
using UndertaleModLib.Util;

namespace Jamm.ModMaker;

/// <summary>
/// "HD" mod: 2x AI-upscaled texture pages (drawn at the original size, so nothing changes in layout),
/// rendering through an aspect-correct application surface at the native content resolution.
/// </summary>
public static class HdMod
{
    /// <summary>Name of the persistent HD runtime controller.</summary>
    public const string ObjectName = "obj_jamm_hd";

    /// <summary>Adds native aspect-correct fullscreen rendering and optionally replaces all safe pages.</summary>
    public static void Apply(UndertaleData data, string? upscaledPages, bool debug = false)
    {
        if (upscaledPages is not null) ReplacePages(data, upscaledPages);

        // The runner letterboxes the enabled application surface and maps room mouse coordinates.
        data.GeneralInfo.Info |= UndertaleGeneralInfo.InfoFlags.Scale;
        data.Options.WindowColor = 0;
        data.GeneralInfo.Info |= UndertaleGeneralInfo.InfoFlags.Interpolate;
        var compiler = new CompileGroup(data);
        var hd = ModObjects.Create(data, ObjectName, persistent: true);
        compiler.QueueCodeReplace(hd.EventHandlerFor(EventType.Create, 0u, data), Create(debug));
        compiler.QueueCodeReplace(hd.EventHandlerFor(EventType.Draw, (uint)EventSubtypeDraw.DrawGUI, data), Toast);
        compiler.QueueCodeReplace(hd.EventHandlerFor(EventType.Step, (uint)EventSubtypeStep.EndStep, data), Step(debug));
        compiler.QueueCodeReplace(hd.EventHandlerFor(EventType.Step, (uint)EventSubtypeStep.BeginStep, data), Resize);
        compiler.QueueCodeReplace(hd.EventHandlerFor(EventType.Other, (uint)EventSubtypeOther.RoomStart, data), RoomStart);
        compiler.QueueCodeReplace(hd.EventHandlerFor(EventType.Draw, (uint)EventSubtypeDraw.DrawGUIEnd, data), debug ? DebugShots : "");
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
        jamm_hd_fullscreen = ini_read_real("HD", "Fullscreen", 1);
        ini_write_real("HD", "Fullscreen", jamm_hd_fullscreen);
        {{(debug ? "jamm_hd_window = ini_read_real(\"HD\", \"DebugWindowScale\", 1);" : "jamm_hd_window = 1;")}}
        ini_close();
        // MSAA only applies to the back buffer, not the application surface. Ignore old AA settings.
        application_surface_enable(true);
        jamm_hd_window = max(0.5, min(3, jamm_hd_window));
        window_set_size(1200 * jamm_hd_window, 720 * jamm_hd_window);
        window_set_colour(c_black);
        window_set_fullscreen(jamm_hd_fullscreen);
        jamm_hd_ready = false;
        jamm_hd_toast_until = current_time + 4000;
        jamm_hd_text = "HD mod: 2x textures, native resolution";
        jamm_hd_shots = 0;
        jamm_hd_capture_wait = 2;
        jamm_hd_capture_fullscreen = window_get_fullscreen();
        jamm_hd_started = current_time;
        jamm_hd_log_at = current_time;
        jamm_hd_log_count = 0;
        jamm_hd_frame_us = 0;
        jamm_hd_frames = 0;
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
            jamm_hd_capture_wait = 2;
            if (!jamm_hd_fullscreen)
                window_set_size(1200 * jamm_hd_window, 720 * jamm_hd_window);
            ini_open("jamm_hd.ini");
            ini_write_real("HD", "Fullscreen", jamm_hd_fullscreen);
            ini_close();
            {{(debug ? "jamm_hd_shots = 2;" : "")}}
        }
        """ + Resize;

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
        jamm_hd_ww = -1;
        jamm_hd_wh = -1;
        if (!surface_exists(application_surface)) exit;
        """ + Transform;

    private const string Transform = """
        jamm_hd_ww = max(1, window_get_width());
        jamm_hd_wh = max(1, window_get_height());
        jamm_hd_k = min(jamm_hd_ww / 1200, jamm_hd_wh / 720);
        surface_resize(application_surface, max(1, round(1200 * jamm_hd_k)), max(1, round(720 * jamm_hd_k)));
        for (jamm_hd_i = 0; jamm_hd_i < 8; jamm_hd_i += 1)
        {
            view_xport[jamm_hd_i] = jamm_hd_bx[jamm_hd_i] * jamm_hd_k;
            view_yport[jamm_hd_i] = jamm_hd_by[jamm_hd_i] * jamm_hd_k;
            view_wport[jamm_hd_i] = jamm_hd_bw[jamm_hd_i] * jamm_hd_k;
            view_hport[jamm_hd_i] = jamm_hd_bh[jamm_hd_i] * jamm_hd_k;
        }
        display_set_gui_size(1200, 720);
        // Wait through the next complete draw after changing the surface.
        jamm_hd_capture_wait = 2;
        """;

    private const string Resize = """
        if (!jamm_hd_ready || !surface_exists(application_surface)) exit;
        // Poll every frame, including after fullscreen switches: the runner may settle
        // window dimensions asynchronously. Also recover a recreated/reset surface.
        if (window_get_width() == jamm_hd_ww && window_get_height() == jamm_hd_wh
            && surface_get_width(application_surface) == max(1, round(1200 * jamm_hd_k))
            && surface_get_height(application_surface) == max(1, round(720 * jamm_hd_k))) exit;
        """ + Transform;

    private const string DebugShots = """
        if (jamm_hd_log_count < 10)
        {
            jamm_hd_frame_us += delta_time;
            jamm_hd_frames += 1;
            if (current_time - jamm_hd_log_at >= 1000)
            {
                jamm_hd_log = file_text_open_append("out/phase6b/jamm_hd_timing.txt");
                file_text_write_string(jamm_hd_log, "sample=" + string(jamm_hd_log_count + 1)
                    + " k=" + string(jamm_hd_k)
                    + " surface=" + string(surface_get_width(application_surface)) + "x" + string(surface_get_height(application_surface))
                    + " delta_time_avg_us=" + string(jamm_hd_frame_us / max(1, jamm_hd_frames)));
                file_text_writeln(jamm_hd_log);
                for (jamm_hd_i = 0; jamm_hd_i < 8; jamm_hd_i += 1)
                {
                    file_text_write_string(jamm_hd_log, "view " + string(jamm_hd_i) + " visible=" + string(view_visible[jamm_hd_i])
                        + " port=" + string(view_xport[jamm_hd_i]) + "," + string(view_yport[jamm_hd_i])
                        + " " + string(view_wport[jamm_hd_i]) + "x" + string(view_hport[jamm_hd_i]));
                    file_text_writeln(jamm_hd_log);
                }
                file_text_close(jamm_hd_log);
                jamm_hd_log_count += 1;
                jamm_hd_log_at = current_time;
                jamm_hd_frame_us = 0;
                jamm_hd_frames = 0;
            }
        }
        // A mode change can complete after End Step; defer until another frame has drawn.
        if (window_get_fullscreen() != jamm_hd_capture_fullscreen)
        {
            jamm_hd_capture_fullscreen = window_get_fullscreen();
            jamm_hd_capture_wait = 2;
        }
        if (jamm_hd_capture_wait > 0 && surface_exists(application_surface))
        {
            jamm_hd_capture_wait -= 1;
            if (jamm_hd_capture_wait == 0)
            {
                if (window_get_fullscreen()) jamm_hd_mode = "fullscreen";
                else jamm_hd_mode = "windowed";
                surface_save(application_surface, "out/phase6b/jamm_hd_surface_" + jamm_hd_mode + ".png");
                screen_save("out/phase6b/jamm_hd_" + jamm_hd_mode + ".png");
                jamm_hd_log = file_text_open_append("out/phase6b/jamm_hd_sizes.txt");
                file_text_write_string(jamm_hd_log, "capture " + jamm_hd_mode
                    + " surface=" + string(surface_get_width(application_surface)) + "x" + string(surface_get_height(application_surface))
                    + " window=" + string(window_get_width()) + "x" + string(window_get_height())
                    + " display=" + string(display_get_width()) + "x" + string(display_get_height())
                    + " GUI=" + string(display_get_gui_width()) + "x" + string(display_get_gui_height())
                    + " k=" + string(jamm_hd_k));
                file_text_writeln(jamm_hd_log);
                file_text_close(jamm_hd_log);
            }
        }
        // current_time is runner uptime, so use elapsed time since this instance's Create.
        // Capture at the end of GUI drawing, after the application surface has been composed.
        if (jamm_hd_shots < 2 && current_time - jamm_hd_started > 4000)
        {
            if (jamm_hd_shots == 0)
                jamm_hd_mode = "windowed";
            else
                jamm_hd_mode = "fullscreen";
            surface_save(application_surface, "out/phase6b/jamm_hd_surface_" + jamm_hd_mode + ".png");
            screen_save("out/phase6b/jamm_hd_" + jamm_hd_mode + ".png");
            jamm_hd_log = file_text_open_append("out/phase6b/jamm_hd_sizes.txt");
            file_text_write_string(jamm_hd_log, jamm_hd_mode + " surface=" + string(surface_get_width(application_surface)) + "x" + string(surface_get_height(application_surface)) + " window=" + string(window_get_width()) + "x" + string(window_get_height())
                + " display=" + string(display_get_width()) + "x" + string(display_get_height())
                + " GUI=" + string(display_get_gui_width()) + "x" + string(display_get_gui_height())
                + " k=" + string(jamm_hd_k));
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
            jamm_hd_capture_wait = 2;
            if (jamm_hd_shots == 1)
                window_set_fullscreen(true);
            else
                window_set_fullscreen(jamm_hd_fullscreen);
        }
        """;
}
