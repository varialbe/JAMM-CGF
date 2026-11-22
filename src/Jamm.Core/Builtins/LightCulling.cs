using System.Globalization;
using System.Text.RegularExpressions;
using UndertaleModLib;
using UndertaleModLib.Compiler;
using UndertaleModLib.Models;

namespace Jamm.ModMaker;

/// <summary>
/// Viewport culling for <c>AbstractLight</c> draws and <c>LightingSystem</c> subtraction passes.
/// Controlled by <c>[Perf] LightCulling=1</c> in <c>jamm_perf.ini</c>, independent of F8.
/// </summary>
public static class LightCulling
{
    /// <summary>Persistent controller object injected into archives with LightCulling enabled.</summary>
    public const string Controller = "obj_jamm_light_cull";

    private const int MarginPx = 128;

    /// <summary>Applies LightCulling to a final GM1 archive using default-enabled INI settings.</summary>
    public static IReadOnlyList<string> Apply(UndertaleData data) => Apply(data, edits: null, enabledByDefault: true);

    /// <summary>Applies LightCulling and records every modified <see cref="UndertaleCode"/> resource.</summary>
    public static IReadOnlyList<string> Apply(UndertaleData data, List<UndertaleCode>? edits, bool enabledByDefault = true)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (data.GameObjects.ByName(Controller) is not null)
            return ["LightCulling already present; skipped."];

        var lightingSystem = data.GameObjects.ByName("LightingSystem");
        var absLight = data.GameObjects.ByName("AbstractLight");
        var drawCode = FindDrawCode(data, lightingSystem);
        var startRoom = data.Rooms.ByName("NewWorld") ?? data.Rooms.FirstOrDefault();
        if (lightingSystem is null || absLight is null || drawCode is null || startRoom is null)
            return ["LightCulling skipped (missing LightingSystem/AbstractLight/room layout)."];

        int absIndex = data.GameObjects.IndexOf(absLight);
        var lightModes = CollectLightScaleModes(data, absLight);
        int maxObjId = lightModes.Keys.DefaultIfEmpty(absIndex).Max();

        string originalDrawGml = ModObjects.Decompile(data, drawCode);
        (string patchedDrawGml, int patchedLoops) = RewriteLightingWithBlocks(originalDrawGml, absIndex);

        var controller = ModObjects.Create(data, Controller, persistent: true);
        controller.Visible = true;
        controller.Depth = -999998;

        var compiler = new CompileGroup(data);
        if (patchedLoops > 0)
            compiler.QueueCodeReplace(drawCode, patchedDrawGml);

        var createCode = controller.EventHandlerFor(EventType.Create, 0u, data);
        var destroyCode = controller.EventHandlerFor(EventType.Destroy, 0u, data);
        var roomStartCode = controller.EventHandlerFor(EventType.Other, (uint)EventSubtypeOther.RoomStart, data);
        var roomEndCode = controller.EventHandlerFor(EventType.Other, (uint)EventSubtypeOther.RoomEnd, data);
        var drawBeginCode = controller.EventHandlerFor(EventType.Draw, (uint)EventSubtypeDraw.DrawBegin, data);
        var postDrawCode = controller.EventHandlerFor(EventType.Draw, (uint)EventSubtypeDraw.PostDraw, data);
        var drawGuiBeginCode = controller.EventHandlerFor(EventType.Draw, (uint)EventSubtypeDraw.DrawGUIBegin, data);

        compiler.QueueCodeReplace(createCode, CreateGml(data.Sprites.Count, maxObjId, lightModes, enabledByDefault));
        compiler.QueueCodeReplace(destroyCode, RestoreAndResetGml());
        compiler.QueueCodeReplace(roomStartCode, RestoreAndResetGml());
        compiler.QueueCodeReplace(roomEndCode, RestoreAndResetGml());
        compiler.QueueCodeReplace(drawBeginCode, DrawBeginGml());
        compiler.QueueCodeReplace(postDrawCode, RestoreAndResetGml());
        compiler.QueueCodeReplace(drawGuiBeginCode, RestoreAndResetGml());
        ModObjects.Compile(compiler);

        if (edits is not null)
        {
            if (patchedLoops > 0) AddUnique(edits, drawCode);
            AddUnique(edits, createCode);
            AddUnique(edits, destroyCode);
            AddUnique(edits, roomStartCode);
            AddUnique(edits, roomEndCode);
            AddUnique(edits, drawBeginCode);
            AddUnique(edits, postDrawCode);
            AddUnique(edits, drawGuiBeginCode);
        }

        startRoom.GameObjects.Add(new UndertaleRoom.GameObject
        {
            InstanceID = data.GeneralInfo.LastObj++,
            ObjectDefinition = controller,
            X = 0,
            Y = 0
        });

        return
        [
            $"LightCulling enabled ({lightModes.Count} light objects, {patchedLoops} LightingSystem draw loops patched, default={(enabledByDefault ? "on" : "off")})."
        ];
    }

    private static UndertaleCode? FindDrawCode(UndertaleData data, UndertaleGameObject? lightingSystem)
    {
        if (lightingSystem is not null)
        {
            var ev = lightingSystem.Events[(int)EventType.Draw].FirstOrDefault(e => e.EventSubtype == 0u);
            var code = ev?.Actions.Select(a => a.CodeId).FirstOrDefault(c => c is not null);
            if (code is not null) return code;
        }
        return data.Code.ByName("gml_Object_LightingSystem_Draw_0");
    }

    private static Dictionary<int, int> CollectLightScaleModes(UndertaleData data, UndertaleGameObject absLight)
    {
        var modes = new Dictionary<int, int>();
        for (int i = 0; i < data.GameObjects.Count; i++)
        {
            var obj = data.GameObjects[i];
            if (!Inherits(obj, absLight)) continue;
            modes[i] = ResolveScaleMode(data, obj);
        }
        return modes;
    }

    private static bool Inherits(UndertaleGameObject obj, UndertaleGameObject ancestor)
    {
        var visited = new HashSet<UndertaleGameObject>();
        for (var cur = obj; cur is not null && visited.Add(cur); cur = cur.ParentId)
        {
            if (ReferenceEquals(cur, ancestor)) return true;
        }
        return false;
    }

    // 1 = image_scale, 2 = image_scale + special_scale, 3 = runtime variable_instance_exists fallback.
    private static int ResolveScaleMode(UndertaleData data, UndertaleGameObject obj)
    {
        bool hasImageScale = false;
        bool hasSpecialScale = false;
        var visited = new HashSet<UndertaleGameObject>();
        for (var cur = obj; cur is not null && visited.Add(cur); cur = cur.ParentId)
        {
            var ev = cur.Events[(int)EventType.Create].FirstOrDefault(e => e.EventSubtype == 0u);
            var code = ev?.Actions.Select(a => a.CodeId).FirstOrDefault(c => c is not null);
            if (code is null) continue;

            string text = ModObjects.Decompile(data, code);
            if (Regex.IsMatch(text, @"\bspecial_scale\b")) hasSpecialScale = true;
            if (Regex.IsMatch(text, @"\bimage_scale\b")) hasImageScale = true;
            if (!text.Contains("action_inherited", StringComparison.Ordinal)
                && !text.Contains("event_inherited", StringComparison.Ordinal))
            {
                break;
            }
        }

        if (hasSpecialScale) return 2;
        if (hasImageScale) return 1;
        return 3;
    }

    private static (string Gml, int Count) RewriteLightingWithBlocks(string gml, int absIndex)
    {
        string targetPattern = $@"\bwith\s*\(\s*(?:{absIndex.ToString(CultureInfo.InvariantCulture)}|AbstractLight)\s*\)\s*\{{";
        var regex = new Regex(targetPattern, RegexOptions.CultureInvariant);

        var output = new System.Text.StringBuilder(gml.Length + 512);
        int cursor = 0;
        int matchCount = 0;

        while (cursor < gml.Length)
        {
            var m = regex.Match(gml, cursor);
            if (!m.Success) break;

            int openBrace = m.Index + m.Length - 1;
            int closeBrace = FindMatchingBrace(gml, openBrace);
            if (closeBrace < 0) break;

            matchCount++;
            if (matchCount > 2)
                break;

            output.Append(gml, cursor, m.Index - cursor);
            string listVar = matchCount == 1 ? "global.jamm_lc_v0" : "global.jamm_lc_v1";
            string countVar = matchCount == 1 ? "global.jamm_lc_v0_n" : "global.jamm_lc_v1_n";
            string innerBody = gml.Substring(openBrace + 1, closeBrace - openBrace - 1);
            string rawWith = gml.Substring(m.Index, closeBrace - m.Index + 1);

            output.AppendLine("if (variable_global_exists(\"jamm_lc_enabled\") && global.jamm_lc_enabled && global.jamm_lc_ready)");
            output.AppendLine("{");
            output.AppendLine($"    var _jamm_lc_n = {countVar};");
            output.AppendLine($"    var _jamm_lc_arr = {listVar};");
            output.AppendLine("    for (var _jamm_lc_i = 0; _jamm_lc_i < _jamm_lc_n; _jamm_lc_i += 1)");
            output.AppendLine("    {");
            output.AppendLine("        with (_jamm_lc_arr[_jamm_lc_i])");
            output.Append("        {").Append(innerBody).AppendLine("}");
            output.AppendLine("    }");
            output.AppendLine("}");
            output.AppendLine("else");
            output.AppendLine("{");
            output.AppendLine(rawWith);
            output.Append("}");

            cursor = closeBrace + 1;
        }

        if (matchCount == 0) return (gml, 0);
        output.Append(gml, cursor, gml.Length - cursor);
        return (output.ToString(), Math.Min(matchCount, 2));
    }

    private static int FindMatchingBrace(string text, int openBraceIndex)
    {
        int depth = 0;
        bool inString = false;
        char quote = '\0';
        for (int i = openBraceIndex; i < text.Length; i++)
        {
            char c = text[i];
            if (inString)
            {
                if (c == '\\' && i + 1 < text.Length) { i++; continue; }
                if (c == quote) inString = false;
                continue;
            }
            if (c == '"' || c == '\'') { inString = true; quote = c; continue; }
            if (c == '/' && i + 1 < text.Length && text[i + 1] == '/')
            {
                int nl = text.IndexOf('\n', i);
                if (nl < 0) return -1;
                i = nl;
                continue;
            }
            if (c == '{') depth++;
            else if (c == '}' && --depth == 0) return i;
        }
        return -1;
    }

    private static string CreateGml(int spriteCount, int maxObjId, Dictionary<int, int> lightModes, bool enabledByDefault)
    {
        int sprCap = Math.Max(spriteCount, 1);
        int objCap = Math.Max(maxObjId + 1, 1);
        var modeLines = string.Join("\n", lightModes
            .OrderBy(kv => kv.Key)
            .Select(kv => $"global.jamm_lc_mode[{kv.Key.ToString(CultureInfo.InvariantCulture)}] = {kv.Value.ToString(CultureInfo.InvariantCulture)};"));

        return $$"""
        if (instance_number(object_index) > 1) { instance_destroy(); exit; }
        ini_open("jamm_perf.ini");
        global.jamm_lc_enabled = ini_read_real("Perf", "LightCulling", {{(enabledByDefault ? 1 : 0)}}) != 0;
        if (!ini_key_exists("Perf", "LightCulling") && global.jamm_lc_enabled) ini_write_real("Perf", "LightCulling", 1);
        ini_close();

        global.jamm_lc_ready = 0;
        global.jamm_lc_v0_n = 0;
        global.jamm_lc_v0[0] = -4;
        global.jamm_lc_v1_n = 0;
        global.jamm_lc_v1[0] = -4;
        global.jamm_lc_hid_n = 0;
        global.jamm_lc_hid[0] = -4;
        global.jamm_lc_total = 0;
        global.jamm_lc_kept = 0;
        global.jamm_lc_culled = 0;

        global.jamm_lc_spr_cap = {{sprCap.ToString(CultureInfo.InvariantCulture)}};
        for (var _s = {{(sprCap - 1).ToString(CultureInfo.InvariantCulture)}}; _s >= 0; _s -= 1)
        {
            global.jamm_lc_mx[_s] = -1;
            global.jamm_lc_my[_s] = -1;
        }

        global.jamm_lc_obj_cap = {{objCap.ToString(CultureInfo.InvariantCulture)}};
        for (var _o = {{(objCap - 1).ToString(CultureInfo.InvariantCulture)}}; _o >= 0; _o -= 1)
        {
            global.jamm_lc_mode[_o] = 3;
        }
        {{modeLines}}
        """;
    }

    private static string DrawBeginGml() => $$"""
        if (!global.jamm_lc_enabled || view_current != 0 || !view_enabled || !view_visible[0]) exit;
        if (global.jamm_lc_hid_n > 0)
        {
            var _hn = global.jamm_lc_hid_n;
            var _ha = global.jamm_lc_hid;
            for (var _hi = 0; _hi < _hn; _hi += 1)
            {
                with (_ha[_hi]) { visible = true; }
            }
            global.jamm_lc_hid_n = 0;
        }

        jamm_v0l = view_xview[0] - {{MarginPx}};
        jamm_v0t = view_yview[0] - {{MarginPx}};
        jamm_v0r = view_xview[0] + view_wview[0] + {{MarginPx}};
        jamm_v0b = view_yview[0] + view_hview[0] + {{MarginPx}};
        jamm_has_v1 = (view_visible[1] != 0);
        if (jamm_has_v1)
        {
            jamm_v1l = view_xview[1] - {{MarginPx}};
            jamm_v1t = view_yview[1] - {{MarginPx}};
            jamm_v1r = view_xview[1] + view_wview[1] + {{MarginPx}};
            jamm_v1b = view_yview[1] + view_hview[1] + {{MarginPx}};
        }
        jamm_n0 = 0;
        jamm_n1 = 0;
        jamm_nh = 0;
        jamm_tot = 0;

        with (AbstractLight)
        {
            if (!visible) continue;
            other.jamm_tot += 1;
            var _keep0 = false;
            var _keep1 = false;
            var _spr = sprite_index;
            if (_spr < 0)
            {
                _keep0 = true;
                _keep1 = other.jamm_has_v1;
            }
            else
            {
                if (_spr >= global.jamm_lc_spr_cap)
                {
                    var _nc = _spr + 128;
                    for (var _k = _nc - 1; _k >= global.jamm_lc_spr_cap; _k -= 1)
                    {
                        global.jamm_lc_mx[_k] = -1;
                        global.jamm_lc_my[_k] = -1;
                    }
                    global.jamm_lc_spr_cap = _nc;
                }
                var _mx = global.jamm_lc_mx[_spr];
                if (_mx < 0)
                {
                    if (!sprite_exists(_spr))
                    {
                        _keep0 = true;
                        _keep1 = other.jamm_has_v1;
                    }
                    else
                    {
                        var _sw = sprite_get_width(_spr);
                        var _sh = sprite_get_height(_spr);
                        var _sxo = sprite_get_xoffset(_spr);
                        var _syo = sprite_get_yoffset(_spr);
                        _mx = max(abs(_sxo), abs(_sw - _sxo));
                        global.jamm_lc_mx[_spr] = _mx;
                        global.jamm_lc_my[_spr] = max(abs(_syo), abs(_sh - _syo));
                    }
                }
                if (!_keep0)
                {
                    var _my = global.jamm_lc_my[_spr];
                    var _sx = max(abs(image_xscale), abs(image_yscale));
                    var _m = 3;
                    if (object_index >= 0 && object_index < global.jamm_lc_obj_cap)
                        _m = global.jamm_lc_mode[object_index];
                    if (_m == 1)
                    {
                        _sx = max(_sx, abs(image_scale));
                    }
                    else if (_m == 2)
                    {
                        _sx = max(_sx, abs(image_scale), abs(special_scale));
                    }
                    else if (_m == 3)
                    {
                        if (variable_instance_exists(id, "image_scale")) _sx = max(_sx, abs(image_scale));
                        if (variable_instance_exists(id, "special_scale")) _sx = max(_sx, abs(special_scale));
                    }
                    var _r = (_mx + _my) * _sx;
                    var _lx = x;
                    var _ly = y;
                    _keep0 = (_lx + _r >= other.jamm_v0l && _lx - _r <= other.jamm_v0r && _ly + _r >= other.jamm_v0t && _ly - _r <= other.jamm_v0b);
                    if (other.jamm_has_v1)
                        _keep1 = (_lx + _r >= other.jamm_v1l && _lx - _r <= other.jamm_v1r && _ly + _r >= other.jamm_v1t && _ly - _r <= other.jamm_v1b);
                }
            }
            if (_keep0) { global.jamm_lc_v0[other.jamm_n0] = id; other.jamm_n0 += 1; }
            if (_keep1) { global.jamm_lc_v1[other.jamm_n1] = id; other.jamm_n1 += 1; }
            if (!_keep0 && !_keep1)
            {
                visible = false;
                global.jamm_lc_hid[other.jamm_nh] = id;
                other.jamm_nh += 1;
            }
        }

        global.jamm_lc_v0_n = jamm_n0;
        global.jamm_lc_v1_n = jamm_n1;
        global.jamm_lc_hid_n = jamm_nh;
        global.jamm_lc_total = jamm_tot;
        global.jamm_lc_kept = jamm_n0;
        global.jamm_lc_culled = jamm_nh;
        global.jamm_lc_ready = 1;
        """;

    private static string RestoreAndResetGml() => """
        global.jamm_lc_ready = 0;
        global.jamm_lc_v0_n = 0;
        global.jamm_lc_v1_n = 0;
        if (global.jamm_lc_hid_n > 0)
        {
            var _hn = global.jamm_lc_hid_n;
            var _ha = global.jamm_lc_hid;
            for (var _hi = 0; _hi < _hn; _hi += 1)
            {
                with (_ha[_hi]) { visible = true; }
            }
            global.jamm_lc_hid_n = 0;
        }
        """;

    private static void AddUnique(List<UndertaleCode> edits, UndertaleCode code)
    {
        if (!edits.Contains(code)) edits.Add(code);
    }
}
