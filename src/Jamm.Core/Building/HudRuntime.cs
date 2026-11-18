using Jamm.Core.Merging;
using Jamm.Core.Patching;
using UndertaleModLib;
using UndertaleModLib.Models;

namespace Jamm.Core.Building;

// Queue in normal Draw, measure with the captured font, then allocate in Draw End.
// Neither the simulation clock nor instance depth determines the lane order.
internal static class HudRuntime
{
    internal const string Quest = "gml_Object_Quest_Draw_0";
    internal const string Experience = "gml_Object_ExperienceBar_Draw_0";
    internal const string Begin = "gml_Object_QuestEngine_Draw_72";
    internal const string End = "gml_Object_QuestEngine_Draw_73";
    internal static readonly IReadOnlyDictionary<string, string> Scripts = new Dictionary<string, string>
    {
        ["jamm_hud_set_font"] = "global.jamm_hud_font = argument0; draw_set_font(argument0);",
        ["jamm_hud_set_halign"] = "global.jamm_hud_halign = argument0; draw_set_halign(argument0);",
        ["jamm_hud_set_valign"] = "global.jamm_hud_valign = argument0; draw_set_valign(argument0);",
        ["jamm_hud_begin"] = """
            if (!variable_global_exists("jamm_hud_font")) global.jamm_hud_font = -1;
            if (!variable_global_exists("jamm_hud_halign")) global.jamm_hud_halign = fa_left;
            if (!variable_global_exists("jamm_hud_valign")) global.jamm_hud_valign = fa_top;
            global.jamm_hud_count = 0;
            var jamm_vw, jamm_vh, jamm_cancel, jamm_handler, jamm_cy, jamm_ctop, jamm_cright;
            jamm_vw = view_wview[view_current];
            if (jamm_vw <= 0) jamm_vw = 800;
            jamm_vh = view_hview[view_current];
            if (jamm_vh <= 0) jamm_vh = 480;
            global.jamm_hud_x = view_xview[view_current] + jamm_vw - 25;
            global.jamm_hud_cursor = view_yview[view_current] + jamm_vh - 28;
            if (instance_exists(ExperienceBar) && ExperienceBar.offset_y < jamm_vh) global.jamm_hud_cursor = min(global.jamm_hud_cursor, view_yview[view_current] + ExperienceBar.offset_y - 4);
            if (view_current == 0) {
                jamm_cancel = asset_get_index("icon_Cancel");
                if (jamm_cancel > -1 && instance_exists(jamm_cancel) && jamm_cancel.visible) {
                    jamm_handler = asset_get_index("Handler");
                    jamm_cy = jamm_cancel.y;
                    if (jamm_handler > -1 && instance_exists(jamm_handler) && variable_instance_exists(jamm_cancel, "ORIGIN_Y")) jamm_cy = min(jamm_cy, jamm_cancel.ORIGIN_Y);
                    jamm_ctop = jamm_cy;
                    jamm_cright = jamm_cancel.x;
                    if (jamm_cancel.sprite_index >= 0 && sprite_exists(jamm_cancel.sprite_index)) {
                        jamm_ctop = jamm_cy - (sprite_get_yoffset(jamm_cancel.sprite_index) * abs(jamm_cancel.image_yscale));
                        jamm_cright = jamm_cancel.x - (sprite_get_xoffset(jamm_cancel.sprite_index) * abs(jamm_cancel.image_xscale)) + (sprite_get_width(jamm_cancel.sprite_index) * abs(jamm_cancel.image_xscale));
                        if (jamm_cancel.bbox_bottom > jamm_cancel.bbox_top) jamm_ctop = min(jamm_ctop, jamm_cancel.bbox_top);
                        if (jamm_cancel.bbox_right > jamm_cancel.bbox_left) jamm_cright = max(jamm_cright, jamm_cancel.bbox_right);
                    }
                    if (jamm_ctop >= view_yview[view_current] && jamm_ctop < view_yview[view_current] + jamm_vh && jamm_cright >= view_xview[view_current] + jamm_vw - 260) {
                        global.jamm_hud_cursor = min(global.jamm_hud_cursor, jamm_ctop - 8);
                    }
                }
            }
            """,
        ["jamm_hud_reserve"] = """
            var jamm_h;
            jamm_h = max(0, argument0);
            global.jamm_hud_cursor -= jamm_h;
            global.jamm_hud_y = global.jamm_hud_cursor + jamm_h / 2;
            global.jamm_hud_cursor -= 4;
            return global.jamm_hud_y;
            """,
        ["jamm_hud_text"] = """
            if (view_current != 0) return 0;
            var jamm_n;
            jamm_n = global.jamm_hud_count;
            global.jamm_hud_rows[jamm_n,0] = argument0;
            global.jamm_hud_rows[jamm_n,1] = argument1;
            global.jamm_hud_rows[jamm_n,2] = string(argument2);
            global.jamm_hud_rows[jamm_n,3] = argument3;
            global.jamm_hud_rows[jamm_n,4] = argument4;
            global.jamm_hud_rows[jamm_n,5] = argument5;
            global.jamm_hud_rows[jamm_n,6] = global.jamm_hud_font;
            global.jamm_hud_rows[jamm_n,7] = draw_get_color();
            global.jamm_hud_rows[jamm_n,8] = draw_get_alpha();
            global.jamm_hud_count += 1;
            if (argument1 == 1) draw_set_color(Colors.ui_light);
            return 0;
            """,
        ["jamm_hud_flush"] = """
            if (view_current != 0) return 0;
            var jamm_font, jamm_color, jamm_alpha, jamm_ha, jamm_va;
            var jamm_lane, jamm_n, jamm_text, jamm_sx, jamm_sy, jamm_angle, jamm_h, jamm_y, jamm_shadow;
            jamm_font = global.jamm_hud_font; jamm_color = draw_get_color(); jamm_alpha = draw_get_alpha();
            jamm_ha = global.jamm_hud_halign; jamm_va = global.jamm_hud_valign;
            draw_set_halign(fa_right); draw_set_valign(fa_middle);
            for (jamm_lane = 0; jamm_lane <= 2; jamm_lane += 1) {
                for (jamm_n = 0; jamm_n < global.jamm_hud_count; jamm_n += 1) {
                    if (global.jamm_hud_rows[jamm_n,0] == jamm_lane) {
                        jamm_text = global.jamm_hud_rows[jamm_n,2];
                        jamm_sx = global.jamm_hud_rows[jamm_n,3]; jamm_sy = global.jamm_hud_rows[jamm_n,4];
                        jamm_angle = global.jamm_hud_rows[jamm_n,5];
                        draw_set_font(global.jamm_hud_rows[jamm_n,6]);
                        draw_set_color(global.jamm_hud_rows[jamm_n,7]);
                        draw_set_alpha(global.jamm_hud_rows[jamm_n,8]);
                        jamm_shadow = 0;
                        if (global.jamm_hud_rows[jamm_n,1] == 1) jamm_shadow = 2;
                        jamm_h = max(16, abs(string_height(jamm_text) * jamm_sy * dcos(jamm_angle)) + abs(string_width(jamm_text) * jamm_sx * dsin(jamm_angle)) + jamm_shadow);
                        jamm_y = jamm_hud_reserve(jamm_h) - jamm_shadow / 2;
                        if (global.jamm_hud_rows[jamm_n,1] == 1) draw_text_shadowed(global.jamm_hud_x, jamm_y, jamm_text);
                        else draw_text_transformed(global.jamm_hud_x, jamm_y, jamm_text, jamm_sx, jamm_sy, jamm_angle);
                    }
                }
            }
            global.jamm_hud_count = 0;
            draw_set_font(jamm_font); draw_set_color(jamm_color); draw_set_alpha(jamm_alpha);
            draw_set_halign(jamm_ha); draw_set_valign(jamm_va);
            return 0;
            """
    };

    internal static BuildPlan PreparePlan(BuildPlan plan, IReadOnlyDictionary<string, CodeText> baseline)
    {
        if (!baseline.ContainsKey(Quest) || !baseline.ContainsKey(Experience)) return plan;
        var assets = plan.Assets.ToList();
        var decisions = plan.Decisions.ToList();
        foreach (string name in baseline.Keys.Concat(assets.Where(a => a.Family == "code").Select(a => a.Name)).Distinct().Order(StringComparer.Ordinal))
        {
            int index = assets.FindIndex(a => a.Family == "code" && a.Name == name);
            if (index >= 0 && assets[index].Patch is null) continue;
            string text = index >= 0 ? assets[index].Gml! : baseline[name].Text;
            int count = 0;
            string rewritten = name.EndsWith("_Draw_0", StringComparison.Ordinal)
                ? HudTextAdapter.Rewrite(name, text, baseline.GetValueOrDefault(name)?.Text ?? "", out count) : text;
            rewritten = TrackDrawState(rewritten);
            if (rewritten != text) Put(name, rewritten);
            if (count > 0) decisions.Add($"HUD layout: adapted {count} text call(s) in {name}; bottom-to-top lanes: level, mod, quests.");
        }
        Put(Begin, "jamm_hud_begin();\n" + Current(Begin));
        Put(End, Current(End) + "\njamm_hud_flush();");
        return plan with { Assets = assets, Decisions = decisions };

        string Current(string name) => assets.FirstOrDefault(a => a.Family == "code" && a.Name == name)?.Gml ?? baseline.GetValueOrDefault(name)?.Text ?? "";
        void Put(string name, string text)
        {
            int index = assets.FindIndex(a => a.Family == "code" && a.Name == name);
            if (index >= 0) { assets[index] = assets[index] with { Gml = text }; return; }
            var before = baseline.GetValueOrDefault(name);
            var after = before is null ? new CodeText(text, "gml", 0, 0, null, []) : before with { Text = text };
            var snapshot = AssetFields.Json(new { });
            assets.Add(new("code", name, 0, new AssetPatch(name, before is null ? null : snapshot, snapshot, [], before, after), snapshot, text));
        }
    }

    // GM1.4 has no font/alignment getters. Track setters archive-wide, including scripts
    // and GUI events, so deferred text retains its font and flush can restore draw state.
    internal static string TrackDrawState(string text)
    {
        var tokens = GmlReferences.Tokens(text);
        var result = new System.Text.StringBuilder(text);
        for (int i = tokens.Count - 2; i >= 0; i--)
            if (tokens[i].Kind == 'i' && tokens[i + 1].Value == "(" && (i == 0 || tokens[i - 1].Value != ".")
                && tokens[i].Value is "draw_set_font" or "draw_set_halign" or "draw_set_valign")
                result.Remove(tokens[i].Start, tokens[i].Length).Insert(tokens[i].Start, "jamm_hud_set_" + tokens[i].Value[9..]);
        return result.ToString();
    }

    internal static void Inject(UndertaleData data, Dictionary<string, string> recompile)
    {
        if (!recompile.ContainsKey(Begin)) return;
        var engine = data.GameObjects.ByName("QuestEngine");
        Attach(EventSubtypeDraw.DrawBegin, Begin);
        Attach(EventSubtypeDraw.DrawEnd, End);
        void Attach(EventSubtypeDraw subtype, string name)
        {
            var events = engine.Events[(int)EventType.Draw];
            var ev = events.FirstOrDefault(e => e.EventSubtype == (uint)subtype);
            if (ev is null) { ev = new() { EventSubtype = (uint)subtype }; events.Add(ev); }
            if (ev.Actions.Count == 0) ev.Actions.Add(new() { ActionName = data.Strings.MakeString("") });
            ev.Actions[0].CodeId = data.Code.ByName(name);
        }
        foreach (var (script, text) in Scripts)
        {
            string name = "gml_Script_" + script;
            var code = data.Code.ByName(name) ?? UndertaleCode.CreateEmptyEntry(data, name);
            var existing = data.Scripts.ByName(script);
            if (existing is null) data.Scripts.Add(new UndertaleScript { Name = data.Strings.MakeString(script), Code = code });
            else existing.Code = code;
            recompile[name] = text;
        }
    }
}
