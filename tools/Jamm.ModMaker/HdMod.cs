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
    public const string ObjectName = "obj_jamm_hd";

    public static void Apply(UndertaleData data, string? upscaledPages, bool debug = false)
    {
        if (upscaledPages is not null) ReplacePages(data, upscaledPages);

        var compiler = new CompileGroup(data);
        var hd = ModObjects.Create(data, ObjectName, persistent: true);
        compiler.QueueCodeReplace(hd.EventHandlerFor(EventType.Create, 0u, data), Create(debug));
        compiler.QueueCodeReplace(hd.EventHandlerFor(EventType.Draw, (uint)EventSubtypeDraw.DrawGUI, data), Toast);
        if (debug)
            compiler.QueueCodeReplace(hd.EventHandlerFor(EventType.Draw, (uint)EventSubtypeDraw.DrawGUIEnd, data), DebugShots);
        ModObjects.Compile(compiler);
        foreach (string room in new[] { "NewWorld", "room11" })
            ModObjects.PlaceInstance(data, room, hd, first: true);
    }

    /// <summary>Swaps in 2x pages; every item on such a page samples a 2x source rectangle into its unchanged target size.</summary>
    private static void ReplacePages(UndertaleData data, string directory)
    {
        var safe = TexturePages.Describe(data).Where(p => p.Upscale).Select(p => p.Index).ToHashSet();
        int replaced = 0;
        for (int i = 0; i < data.EmbeddedTextures.Count; i++)
        {
            string file = Path.Combine(directory, $"page_{i}.png");
            if (!safe.Contains(i) || !File.Exists(file)) continue;
            var page = data.EmbeddedTextures[i];
            var image = GMImage.FromPng(File.ReadAllBytes(file), verifyHeader: true);
            int factor = image.Width / page.TextureData.Image.Width;
            if (factor != 2 || image.Height != page.TextureData.Image.Height * 2)
                throw new InvalidDataException($"{file} must be exactly twice the size of texture page {i}.");
            page.TextureData.Image = image;
            foreach (var item in data.TexturePageItems.Where(t => t.TexturePage == page))
            {
                item.SourceX = (ushort)(item.SourceX * 2);
                item.SourceY = (ushort)(item.SourceY * 2);
                item.SourceWidth = (ushort)(item.SourceWidth * 2);
                item.SourceHeight = (ushort)(item.SourceHeight * 2);
            }
            replaced++;
        }
        Console.WriteLine($"Upscaled {replaced} texture pages (font pages stay at original size).");
    }

    private static string Create(bool debug) => $$"""
        if (instance_number(object_index) > 1)
        {
            instance_destroy();
            exit;
        }
        persistent = true;
        ini_open("jamm_hd.ini");
        jamm_hd_aa = ini_read_real("HD", "AntiAliasing", 8);
        jamm_hd_native = ini_read_real("HD", "NativeResolution", 1);
        {{(debug ? "jamm_hd_window = ini_read_real(\"HD\", \"DebugWindowScale\", 1);" : "jamm_hd_window = 1;")}}
        ini_close();
        // Draw straight to the back buffer: the scene is rendered at the window's own resolution and the
        // multisampled back buffer smooths every edge (the application surface supports neither).
        if (jamm_hd_native)
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
        if (jamm_hd_window != 1)
            window_set_size(1200 * jamm_hd_window, 720 * jamm_hd_window);
        jamm_hd_toast_until = current_time + 4000;
        jamm_hd_text = "HD mod: 2x textures, native resolution, " + string(jamm_hd_level) + "x MSAA";
        jamm_hd_shots = 0;
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

    private const string DebugShots = """
        if (current_time > 30000 && jamm_hd_shots < 1)
        {
            screen_save("jamm_hd_shot.png");
            jamm_hd_shots += 1;
        }
        """;
}
