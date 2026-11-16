using System.Text.RegularExpressions;
using UndertaleModLib;
using UndertaleModLib.Decompiler;

namespace Jamm.ModMaker;

/// <summary>Prints the facts the example mods depend on (room speeds, views, timing code, textures).</summary>
public static class Inspector
{
    public static void Run(UndertaleData data)
    {
        Console.WriteLine($"Game: {data.GeneralInfo.DisplayName?.Content} GM {data.GeneralInfo.Major}.{data.GeneralInfo.Minor}.{data.GeneralInfo.Release}.{data.GeneralInfo.Build} bytecode {data.GeneralInfo.BytecodeVersion}");
        Console.WriteLine($"Window {data.GeneralInfo.DefaultWindowWidth}x{data.GeneralInfo.DefaultWindowHeight}, info flags {data.GeneralInfo.Info}");
        Console.WriteLine($"Options: {data.Options?.Info}");
        Console.WriteLine($"Room order first: {string.Join(", ", data.GeneralInfo.RoomOrder.Take(3).Select(r => r.Resource?.Name?.Content))}");
        foreach (var room in data.Rooms)
        {
            var views = room.Views.Where(v => v.Enabled).Select(v => $"[{v.ViewX},{v.ViewY} {v.ViewWidth}x{v.ViewHeight} -> port {v.PortWidth}x{v.PortHeight} follow {v.ObjectId?.Name?.Content}]");
            var bgs = room.Backgrounds.Where(b => b.Enabled && (b.SpeedX != 0 || b.SpeedY != 0)).Select(b => $"bg spd {b.SpeedX},{b.SpeedY}");
            Console.WriteLine($"Room {room.Name.Content}: speed {room.Speed}, {room.Width}x{room.Height}, persistent {room.Persistent}, inst {room.GameObjects.Count}, views {string.Join(" ", views)} {string.Join(" ", bgs)}");
        }
        Console.WriteLine($"Objects {data.GameObjects.Count}, code {data.Code.Count}, sprites {data.Sprites.Count}, pages {data.EmbeddedTextures.Count}, tpag {data.TexturePageItems.Count}, shaders {data.Shaders?.Count}");
        foreach (var t in data.EmbeddedTextures.Take(40))
            Console.WriteLine($"  page {t.Name?.Content}: {t.TextureData.Width}x{t.TextureData.Height}");

        var context = new GlobalDecompileContext(data);
        var patterns = new Dictionary<string, Regex>
        {
            ["room_speed"] = new(@"\broom_speed\b"),
            ["delta_time"] = new(@"\bdelta_time\b"),
            ["fps"] = new(@"\bfps(_real)?\b"),
            ["view_xview"] = new(@"\bview_[xy]view\b"),
            ["display_reset/aa"] = new(@"\bdisplay_reset\b|\bdisplay_aa\b"),
            ["texture_set_interpolation"] = new(@"\btexture_set_interpolation\b"),
            ["application_surface"] = new(@"\bapplication_surface\b|\bsurface_resize\b"),
            ["instance_deactivate"] = new(@"\binstance_(de)?activate_"),
            ["keyboard_key_press"] = new(@"\bkeyboard_key_(press|release)\b"),
            ["get_timer/current_time"] = new(@"\bget_timer\b|\bcurrent_time\b"),
            ["window_set_size/fullscreen"] = new(@"\bwindow_set_(size|fullscreen)\b"),
            ["background_hspeed"] = new(@"\bbackground_[hv]speed\b"),
        };
        var hits = patterns.Keys.ToDictionary(k => k, _ => new List<string>());
        int failed = 0;
        foreach (var code in data.Code.Where(c => c.ParentEntry is null))
        {
            string text;
            try { text = new Underanalyzer.Decompiler.DecompileContext(context, code, data.ToolInfo.DecompilerSettings).DecompileToString(); }
            catch { failed++; continue; }
            foreach (var (name, regex) in patterns)
                foreach (string line in text.Split('\n').Where(l => regex.IsMatch(l)))
                    hits[name].Add($"{code.Name.Content}: {line.Trim()}");
        }
        Console.WriteLine($"Decompile failures: {failed}");
        foreach (var (name, list) in hits)
        {
            Console.WriteLine($"--- {name}: {list.Count}");
            foreach (string line in list.Take(25)) Console.WriteLine("   " + (line.Length > 170 ? line[..170] : line));
        }
    }
}
