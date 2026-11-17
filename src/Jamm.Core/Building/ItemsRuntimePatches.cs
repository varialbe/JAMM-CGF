using Jamm.Core.Patching;

namespace Jamm.Core.Building;

internal static class ItemsRuntimePatches
{
    internal static void Apply(
        List<PlannedAsset> assets,
        IReadOnlyDictionary<string, CodeText> baseline,
        IReadOnlySet<string>? availableObjects)
    {
        bool HasObj(string name) => availableObjects is null || availableObjects.Contains(name);

        PatchRequired("gml_Object_SaveManager_Step_0", PatchSaveStep);
        PatchRequired("gml_Object_SaveManager_Other_13", text => PatchSaveLoad(text, HasObj("BoatData"), HasObj("RodData"), HasObj("LureData"), HasObj("ToolData"), HasObj("GearData")));
        PatchRequired("gml_Object_SaveManager_Other_14", text => PatchSaveWrite(text, writeBoat: true, HasObj("BoatData"), HasObj("RodData"), HasObj("LureData"), HasObj("ToolData"), HasObj("GearData")));
        PatchRequired("gml_Object_SaveManager_Other_15", text => PatchSaveWrite(text, writeBoat: false, HasObj("BoatData"), HasObj("RodData"), HasObj("LureData"), HasObj("ToolData"), HasObj("GearData")));

        PatchOptional("gml_Object_RodCard_Other_11", PatchRodCardDraw);
        PatchOptional("gml_Object_Rod_Other_10", PatchRodEquip);
        PatchOptional("gml_Object_ToolCard_Other_11", PatchToolCardDraw);
        PatchOptional("gml_Object_BoatCard_Other_11", PatchBoatCardDraw);
        PatchOptional("gml_Object_LureCard_Other_11", PatchLureCardDraw);
        PatchOptional("gml_Object_BoatCard_Other_10", PatchBoatCardClick);
        PatchOptional("gml_Object_ToolCard_Other_10", PatchToolCardClick);
        PatchOptional("gml_Object_Girl_Other_10", PatchGirlGearRefresh);
        PatchOptional("gml_Object_Gear_Other_15", PatchGearStats);
        PatchOptional("gml_Object_Rocket_Step_2", PatchRocketStep);
        PatchOptional("gml_Script_start_quest_dist", PatchQuestDist);
        PatchOptional("gml_Script_start_quest_rods", PatchQuestRods);
        PatchOptional("gml_Object_Rod_KeyPress_88", PatchRodCycleForward);
        PatchOptional("gml_Script_boat_was_unlocked", PatchBoatAchievement);
        PatchOptional("gml_Script_rod_was_unlocked", PatchRodAchievement);
        PatchOptional("gml_Script_lure_was_purchased", PatchLureAchievement);
        PatchOptional("gml_Script_tool_was_purchased", PatchToolAchievement);

        void PatchRequired(string name, Func<string, string> patcher)
        {
            string? text = Current(name);
            if (text is null)
                throw new InvalidDataException($"Items API requires code '{name}', which is missing from the archive.");
            string updated = patcher(text);
            if (updated != text) Put(name, updated);
        }

        void PatchOptional(string name, Func<string, string> patcher)
        {
            string? text = Current(name);
            if (text is null) return;
            string updated = patcher(text);
            if (updated != text) Put(name, updated);
        }

        string? Current(string name)
        {
            int idx = assets.FindIndex(a => a.Family == "code" && a.Name == name);
            if (idx >= 0) return assets[idx].Patch is null ? null : assets[idx].Gml;
            return baseline.TryGetValue(name, out var before) ? before.Text : null;
        }

        void Put(string name, string text)
        {
            int idx = assets.FindIndex(a => a.Family == "code" && a.Name == name);
            if (idx >= 0) { assets[idx] = assets[idx] with { Gml = text }; return; }
            var before = baseline.GetValueOrDefault(name);
            var after = before is null ? new CodeText(text, "gml", 0, 0, null, []) : before with { Text = text };
            var snapshot = AssetFields.Json(new { });
            assets.Add(new("code", name, 0, new AssetPatch(name, before is null ? null : snapshot, snapshot, [], before, after), snapshot, text));
        }
    }

    private static string PatchSaveStep(string text)
    {
        if (text.Contains("jamm_api_init(", StringComparison.Ordinal)) return text;
        return ReplaceOnce(text, "gml_Object_SaveManager_Step_0", "if (!loaded)", "jamm_api_init();\nif (!loaded)");
    }

    private static string PatchSaveLoad(
        string text, bool hasBoatData, bool hasRodData, bool hasLureData, bool hasToolData, bool hasGearData)
    {
        if (text.Contains("jamm_items_load(", StringComparison.Ordinal)) return text;
        const string entry = "gml_Object_SaveManager_Other_13";
        text = ReplaceOnce(text, entry, "if (sf > 0 || lf > 0)\n{", "if (sf > 0 || lf > 0)\n{\n    jamm_items_ensure_base();");
        text = ClampDataLoops(text, entry, hasBoatData, hasRodData, hasLureData, hasToolData, hasGearData);
        if (hasGearData)
        {
            text = ReplaceOnce(text, entry,
                "gear_id = ini_read_real(\"Main\", \"gear\", -1);\n    if (gear_id >= 0)",
                "gear_id = ini_read_real(\"Main\", \"gear\", -1);\n    if ((!ini_section_exists(\"JAMM_Items\") || !ini_key_exists(\"JAMM_Items\", \"eq:hat\")) && gear_id >= 0 && gear_id < GearData.jamm_base_count)");
        }
        if (hasRodData)
        {
            text = ReplaceOnce(text, entry,
                "active_rod = ini_read_real(\"Main\", \"activerod\", -1);\n    if (active_rod >= 0)",
                "active_rod = ini_read_real(\"Main\", \"activerod\", -1);\n    if (active_rod >= 0 && active_rod < RodData.jamm_base_count)");
        }
        if (hasBoatData)
        {
            text = ReplaceOnce(text, entry,
                "var bi = ini_read_real(\"Main\", \"boat\", -1);",
                """
                var bi = ini_read_real("Main", "boat", -1);
                    if (ini_section_exists("JAMM_Items") && ini_key_exists("JAMM_Items", "eq:boat"))
                    {
                        bi = jamm_item_find("boat", ini_read_string("JAMM_Items", "eq:boat", ""));
                    }
                    else if (bi >= BoatData.jamm_base_count)
                    {
                        bi = -1;
                    }
                """);
            text = ReplaceOnce(text, entry,
                "instance_create(0, 0, BoatData.object[bi]);",
                "var jamm_boat;\n        jamm_boat = instance_create(0, 0, BoatData.object[bi]);\n        jamm_boat.boat_index = bi;");
        }
        text = ReplaceOnce(text, entry,
            "}\nelse\n{\n    with (Analytics)",
            "    jamm_items_load();\n}\nelse\n{\n    with (Analytics)");
        return text;
    }

    private static string PatchSaveWrite(
        string text, bool writeBoat, bool hasBoatData, bool hasRodData, bool hasLureData, bool hasToolData, bool hasGearData)
    {
        if (text.Contains("jamm_items_save(", StringComparison.Ordinal)) return text;
        string entry = writeBoat ? "gml_Object_SaveManager_Other_14" : "gml_Object_SaveManager_Other_15";
        text = ReplaceOnce(text, entry, "if (!saving_locked)\n{", "if (!saving_locked)\n{\n    jamm_items_ensure_base();");
        text = ClampDataLoops(text, entry, hasBoatData, hasRodData, hasLureData, hasToolData, hasGearData);
        if (hasRodData)
        {
            text = ReplaceOnce(text, entry,
                "ini_write_real(\"Main\", \"activerod\", Rod.rod_id);",
                "if (Rod.rod_id < RodData.jamm_base_count) { ini_write_real(\"Main\", \"activerod\", Rod.rod_id); } else { ini_write_real(\"Main\", \"activerod\", 0); }");
        }
        if (hasGearData)
        {
            text = ReplaceOnce(text, entry,
                "if (gear_id >= 0)",
                "if (gear_id >= 0 && gear_id < GearData.jamm_base_count)");
        }
        if (hasBoatData && writeBoat)
        {
            text = ReplaceOnce(text, entry,
                "ini_write_real(\"Main\", \"boat\", Boat.boat_index);",
                "if (Boat.boat_index < BoatData.jamm_base_count) { ini_write_real(\"Main\", \"boat\", Boat.boat_index); } else { ini_write_real(\"Main\", \"boat\", -1); }");
        }
        text = ReplaceOnce(text, entry,
            "ini_close();\n    ini_open(\"meta.ini\");",
            "jamm_items_save();\n    ini_close();\n    ini_open(\"meta.ini\");");
        return text;
    }

    private static string ClampDataLoops(
        string text, string entry, bool hasBoatData, bool hasRodData, bool hasLureData, bool hasToolData, bool hasGearData)
    {
        if (hasRodData) text = Clamp("RodData", "nItems");
        if (hasBoatData) text = Clamp("BoatData", "nItems");
        if (hasLureData) text = Clamp("LureData", "nItems");
        if (hasToolData) text = Clamp("ToolData", "nItems");
        if (hasGearData) text = Clamp("GearData", "n_gear");
        return text;

        string Clamp(string dataObj, string countVar) => ReplaceOnce(text, entry,
            $"with ({dataObj})\n    {{\n        for (i = 0; i < {countVar}; i++)",
            $"with ({dataObj})\n    {{\n        for (i = 0; i < jamm_base_count; i++)");
    }

    private static string PatchRodCardDraw(string text)
    {
        if (text.Contains("RodData.disp", StringComparison.Ordinal)) return text;
        return ReplaceOnce(text, "gml_Object_RodCard_Other_11",
            "draw_sprite_ext(spr_rod, item_id, x + 42, y + 116, 0.5, 0.5, 45, c_white, 1);",
            """
            if (variable_instance_exists(RodData.id, "disp") && RodData.disp[item_id] > -1)
            {
                draw_sprite_ext(RodData.disp[item_id], 0, x + 42, y + 116, 0.5, 0.5, 45, c_white, 1);
            }
            else
            {
                draw_sprite_ext(spr_rod, min(item_id, max(0, sprite_get_number(spr_rod) - 1)), x + 42, y + 116, 0.5, 0.5, 45, c_white, 1);
            }
            """);
    }

    private static string PatchRodEquip(string text)
    {
        if (text.Contains("RodData.disp", StringComparison.Ordinal)) return text;
        return ReplaceOnce(text, "gml_Object_Rod_Other_10",
            "image_index = rod_id;",
            """
            if (variable_instance_exists(RodData.id, "disp") && RodData.disp[rod_id] > -1)
            {
                sprite_index = RodData.disp[rod_id];
                image_index = 0;
            }
            else
            {
                sprite_index = spr_rod;
                image_index = min(rod_id, max(0, sprite_get_number(spr_rod) - 1));
            }
            """);
    }

    private static string PatchToolCardDraw(string text)
    {
        if (text.Contains("ToolData.disp", StringComparison.Ordinal)) return text;
        return ReplaceOnce(text, "gml_Object_ToolCard_Other_11",
            "draw_sprite_ext(spr_tools, item_id, x + 76, y + 82, 1, 1, 0, c_white, 1);",
            """
            if (variable_instance_exists(ToolData.id, "disp") && ToolData.disp[item_id] > -1)
            {
                draw_sprite_ext(ToolData.disp[item_id], 0, x + 76, y + 82, 1, 1, 0, c_white, 1);
            }
            else
            {
                draw_sprite_ext(spr_tools, min(item_id, max(0, sprite_get_number(spr_tools) - 1)), x + 76, y + 82, 1, 1, 0, c_white, 1);
            }
            """);
    }

    private static string PatchBoatCardDraw(string text)
    {
        if (text.Contains("BoatData.disp[item_id] > -1", StringComparison.Ordinal)) return text;
        return ReplaceOnce(text, "gml_Object_BoatCard_Other_11",
            "draw_sprite_ext(BoatData.disp[item_id], 0, x + 76, y + 85, 1, 1, 0, c_white, 1);",
            """
            if (BoatData.disp[item_id] > -1)
            {
                draw_sprite_ext(BoatData.disp[item_id], 0, x + 76, y + 85, 1, 1, 0, c_white, 1);
            }
            """);
    }

    private static string PatchLureCardDraw(string text)
    {
        if (text.Contains("LureData.disp[item_id] > -1", StringComparison.Ordinal)) return text;
        return ReplaceOnce(text, "gml_Object_LureCard_Other_11",
            "draw_sprite_ext(LureData.disp[item_id], 0, x + 76, y + 85, 1, 1, 0, c_white, 1);",
            """
            if (LureData.disp[item_id] > -1)
            {
                draw_sprite_ext(LureData.disp[item_id], 0, x + 76, y + 85, 1, 1, 0, c_white, 1);
            }
            """);
    }

    private static string PatchBoatCardClick(string text)
    {
        if (text.Contains(".boat_index = item_id", StringComparison.Ordinal)) return text;
        return ReplaceOnce(text, "gml_Object_BoatCard_Other_10",
            "instance_create(0, 0, BoatData.object[item_id]);",
            "var jamm_boat;\n            jamm_boat = instance_create(0, 0, BoatData.object[item_id]);\n            jamm_boat.boat_index = item_id;");
    }

    private static string PatchToolCardClick(string text)
    {
        if (text.Contains("ToolData.message[item_id] > -1", StringComparison.Ordinal)) return text;
        const string entry = "gml_Object_ToolCard_Other_10";
        text = ReplaceOnce(text, entry,
            "with (ToolData.item[item_id])",
            "if (!instance_exists(ToolData.item[item_id])) { var jamm_ti; jamm_ti = instance_create(0, 0, ToolData.item[item_id]); jamm_ti.active = 0; }\n    with (ToolData.item[item_id])");
        text = ReplaceOnce(text, entry,
            "instance_create(0, 0, ToolData.message[item_id]);",
            "if (ToolData.message[item_id] > -1) { instance_create(0, 0, ToolData.message[item_id]); }");
        return text;
    }

    private static string PatchGirlGearRefresh(string text)
    {
        if (text.Contains("LureData.nItems", StringComparison.Ordinal)) return text;
        const string entry = "gml_Object_Girl_Other_10";
        text = ReplaceOnce(text, entry,
            """
                gear_lure_scalar[0] = 1;
                gear_lure_scalar[1] = 1;
                gear_lure_scalar[2] = 1;
                gear_lure_scalar[3] = 1;
                gear_lure_scalar[4] = 1;
                gear_lure_scalar[5] = 1;
            """,
            """
                var jamm_ln, jamm_li;
                jamm_ln = 6;
                if (instance_exists(LureData)) { jamm_ln = LureData.nItems; }
                for (jamm_li = 0; jamm_li < jamm_ln; jamm_li += 1) { gear_lure_scalar[jamm_li] = 1; }
            """);
        text = ReplaceOnce(text, entry,
            """
                gear_lure_scalar[0] = gear.lure_scalar[0];
                gear_lure_scalar[1] = gear.lure_scalar[1];
                gear_lure_scalar[2] = gear.lure_scalar[2];
                gear_lure_scalar[3] = gear.lure_scalar[3];
                gear_lure_scalar[4] = gear.lure_scalar[4];
            """,
            """
                var jamm_ln2, jamm_li2, jamm_glen;
                jamm_ln2 = 6;
                if (instance_exists(LureData)) { jamm_ln2 = LureData.nItems; }
                jamm_glen = array_length_1d(gear.lure_scalar);
                for (jamm_li2 = 0; jamm_li2 < jamm_ln2; jamm_li2 += 1) {
                    if (jamm_li2 < jamm_glen) { gear_lure_scalar[jamm_li2] = gear.lure_scalar[jamm_li2]; }
                    else { gear_lure_scalar[jamm_li2] = 1; }
                }
            """);
        return text;
    }

    private static string PatchGearStats(string text)
    {
        if (text.Contains("LureData.nItems", StringComparison.Ordinal)) return text;
        return ReplaceOnce(text, "gml_Object_Gear_Other_15",
            """
            lure_scalar[0] = 1;
            lure_scalar[1] = 1;
            lure_scalar[2] = 1;
            lure_scalar[3] = 1;
            lure_scalar[4] = 1;
            """,
            """
            var jamm_ln, jamm_li;
            jamm_ln = 6;
            if (instance_exists(LureData)) { jamm_ln = LureData.nItems; }
            for (jamm_li = 0; jamm_li < jamm_ln; jamm_li += 1) { lure_scalar[jamm_li] = 1; }
            """);
    }

    private static string PatchRocketStep(string text)
    {
        if (text.Contains("clamp(Rod.rod_id", StringComparison.Ordinal)) return text;
        return ReplaceAll(text, "gml_Object_Rocket_Step_2", "target_distance[Rod.rod_id]", "target_distance[clamp(Rod.rod_id, 0, 5)]");
    }

    private static string PatchQuestDist(string text)
    {
        if (text.Contains("clamp(Girl.bestRod", StringComparison.Ordinal)) return text;
        const string entry = "gml_Script_start_quest_dist";
        text = ReplaceOnce(text, entry, "dist[Girl.bestRod]", "dist[clamp(Girl.bestRod, 0, 5)]");
        text = ReplaceOnce(text, entry, "dpth[Girl.bestRod]", "dpth[clamp(Girl.bestRod, 0, 5)]");
        return text;
    }

    private static string PatchQuestRods(string text)
    {
        if (text.Contains("clamp(index", StringComparison.Ordinal)) return text;
        return ReplaceOnce(text, "gml_Script_start_quest_rods",
            "RodData.cost[index]",
            "RodData.cost[clamp(index, 0, RodData.nItems - 1)]");
    }

    private static string PatchRodCycleForward(string text)
    {
        const string anchor = """
                    while (rid < RodData.nItems)
                    {
                        if (RodData.unlocked[rid])
                        {
                            rod_id = rid;
                            event_perform(ev_other, ev_user0);
                            break;
                        }
                        rid--;
                    }
            """;
        if (!text.Contains("rid--;", StringComparison.Ordinal))
        {
            if (text.Contains("rid++;", StringComparison.Ordinal)) return text;
        }
        return ReplaceOnce(text, "gml_Object_Rod_KeyPress_88", anchor, anchor.Replace("rid--;", "rid++;", StringComparison.Ordinal));
    }

    private static string PatchBoatAchievement(string text)
    {
        if (text.Contains("BoatData.jamm_base_count", StringComparison.Ordinal)) return text;
        return ReplaceOnce(text, "gml_Script_boat_was_unlocked",
            "steam_set_achievement(\"boat_\" + string(argument0));",
            "if (argument0 < BoatData.jamm_base_count) { steam_set_achievement(\"boat_\" + string(argument0)); }");
    }

    private static string PatchRodAchievement(string text)
    {
        if (text.Contains("RodData.jamm_base_count", StringComparison.Ordinal)) return text;
        return ReplaceOnce(text, "gml_Script_rod_was_unlocked",
            "steam_set_achievement(\"rod_\" + string(argument0));",
            "if (argument0 < RodData.jamm_base_count) { steam_set_achievement(\"rod_\" + string(argument0)); }");
    }

    private static string PatchLureAchievement(string text)
    {
        if (text.Contains("LureData.jamm_base_count", StringComparison.Ordinal)) return text;
        return ReplaceOnce(text, "gml_Script_lure_was_purchased",
            "steam_inc_stat(\"lp_\" + string(argument0 + 1), 1);\nsteam_set_achievement(\"lure_\" + string(argument0));",
            "if (argument0 < LureData.jamm_base_count) { steam_inc_stat(\"lp_\" + string(argument0 + 1), 1); steam_set_achievement(\"lure_\" + string(argument0)); }");
    }

    private static string PatchToolAchievement(string text)
    {
        if (text.Contains("ToolData.jamm_base_count", StringComparison.Ordinal)) return text;
        return ReplaceOnce(text, "gml_Script_tool_was_purchased",
            "steam_set_achievement(\"tool_\" + string(argument0 + 1));",
            "if (argument0 < ToolData.jamm_base_count) { steam_set_achievement(\"tool_\" + string(argument0 + 1)); }");
    }

    private static string ReplaceOnce(string text, string entry, string anchor, string replacement)
    {
        int first = text.IndexOf(anchor, StringComparison.Ordinal);
        if (first < 0)
            throw new InvalidDataException($"Cannot patch '{entry}' for Items API: expected anchor text not found ({anchor.Split('\n')[0]}).");
        int second = text.IndexOf(anchor, first + anchor.Length, StringComparison.Ordinal);
        if (second >= 0)
            throw new InvalidDataException($"Cannot patch '{entry}' for Items API: expected anchor text matched multiple times ({anchor.Split('\n')[0]}).");
        return string.Concat(text.AsSpan(0, first), replacement, text.AsSpan(first + anchor.Length));
    }

    private static string ReplaceAll(string text, string entry, string anchor, string replacement)
    {
        if (!text.Contains(anchor, StringComparison.Ordinal))
            throw new InvalidDataException($"Cannot patch '{entry}' for Items API: expected anchor text not found ({anchor}).");
        return text.Replace(anchor, replacement, StringComparison.Ordinal);
    }
}
