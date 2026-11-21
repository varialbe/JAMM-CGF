using System.Text.RegularExpressions;
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
        bool hasBoatData = HasObj("BoatData");
        bool hasRodData = HasObj("RodData");
        bool hasLureData = HasObj("LureData");
        bool hasToolData = HasObj("ToolData");
        bool hasGearData = HasObj("GearData");
        bool hasUpgradeData = HasObj("UpgradeData");

        PatchRequired("gml_Object_SaveManager_Step_0", PatchSaveStep);
        PatchRequired("gml_Object_SaveManager_Other_13", text => PatchSaveLoad(text, hasBoatData, hasRodData, hasLureData, hasToolData, hasGearData, hasUpgradeData));
        PatchRequired("gml_Object_SaveManager_Other_14", text => PatchSaveWrite(text, writeBoat: true, hasBoatData, hasRodData, hasLureData, hasToolData, hasGearData, hasUpgradeData));
        PatchRequired("gml_Object_SaveManager_Other_15", text => PatchSaveWrite(text, writeBoat: false, hasBoatData, hasRodData, hasLureData, hasToolData, hasGearData, hasUpgradeData));

        PatchOptional("gml_Object_RodCard_Other_11", PatchRodCardDraw);
        PatchOptional("gml_Object_Rod_Other_10", text => PatchRodUpgrades(PatchRodEquip(text), hasUpgradeData));
        PatchOptional("gml_Object_ToolCard_Other_11", PatchToolCardDraw);
        PatchOptional("gml_Object_BoatCard_Other_11", PatchBoatCardDraw);
        PatchOptional("gml_Object_LureCard_Other_11", PatchLureCardDraw);
        PatchOptional("gml_Object_BoatCard_Other_10", PatchBoatCardClick);
        PatchOptional("gml_Object_ToolCard_Other_10", PatchToolCardClick);
        PatchOptional("gml_Object_Girl_Other_10", text => PatchGirlGearRefresh(text, hasLureData, hasUpgradeData));
        PatchOptional("gml_Object_Gear_Other_15", text => PatchGearStats(text, hasLureData, hasUpgradeData));
        PatchOptional("gml_Object_Rocket_Step_2", PatchRocketStep);
        PatchOptional("gml_Script_start_quest_dist", PatchQuestDist);
        PatchOptional("gml_Script_start_quest_rods", PatchQuestRods);
        PatchOptional("gml_Object_Rod_KeyPress_88", PatchRodCycleForward);
        PatchOptional("gml_Script_boat_was_unlocked", PatchBoatAchievement);
        PatchOptional("gml_Script_rod_was_unlocked", PatchRodAchievement);
        PatchOptional("gml_Script_lure_was_purchased", PatchLureAchievement);
        PatchOptional("gml_Script_tool_was_purchased", PatchToolAchievement);
        if (hasUpgradeData)
        {
            PatchOptional("gml_Object_UpgradeCard_Other_11", PatchUpgradeCardDraw);
            PatchOptional("gml_Object_UpgradeSlot_Other_11", PatchUpgradeSlotDraw);
            PatchOptional("gml_Object_UpgradeData_Draw_0", PatchUpgradeDataDraw);
            PatchOptional("gml_Object_UpgradeCard_Other_10", PatchUpgradeCardClick);
            PatchOptional("gml_Script_upgrade_was_purchased", PatchUpgradeAchievement);
        }

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
        string text, bool hasBoatData, bool hasRodData, bool hasLureData, bool hasToolData, bool hasGearData, bool hasUpgradeData)
    {
        if (text.Contains("jamm_items_load(", StringComparison.Ordinal)) return text;
        const string entry = "gml_Object_SaveManager_Other_13";
        text = ReplaceOnce(text, entry, "if (sf > 0 || lf > 0)\n{", "if (sf > 0 || lf > 0)\n{\n    jamm_items_ensure_base();");
        text = ClampDataLoops(text, entry, hasBoatData, hasRodData, hasLureData, hasToolData, hasGearData, hasUpgradeData);
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
        string text, bool writeBoat, bool hasBoatData, bool hasRodData, bool hasLureData, bool hasToolData, bool hasGearData, bool hasUpgradeData)
    {
        if (text.Contains("jamm_items_save(", StringComparison.Ordinal)) return text;
        string entry = writeBoat ? "gml_Object_SaveManager_Other_14" : "gml_Object_SaveManager_Other_15";
        text = ReplaceOnce(text, entry, "if (!saving_locked)\n{", "if (!saving_locked)\n{\n    jamm_items_ensure_base();");
        text = ClampDataLoops(text, entry, hasBoatData, hasRodData, hasLureData, hasToolData, hasGearData, hasUpgradeData);
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
        string text, string entry, bool hasBoatData, bool hasRodData, bool hasLureData, bool hasToolData, bool hasGearData, bool hasUpgradeData)
    {
        if (hasRodData) text = Clamp("RodData", "nItems");
        if (hasBoatData) text = Clamp("BoatData", "nItems");
        if (hasLureData) text = Clamp("LureData", "nItems");
        if (hasToolData) text = Clamp("ToolData", "nItems");
        if (hasGearData) text = Clamp("GearData", "n_gear");
        if (hasUpgradeData) text = ClampUpgradeLoop(text, entry, hasRodData);
        return text;

        string Clamp(string dataObj, string countVar) => ReplaceOnce(text, entry,
            $"with ({dataObj})\n    {{\n        for (i = 0; i < {countVar}; i++)",
            $"with ({dataObj})\n    {{\n        for (i = 0; i < jamm_base_count; i++)");
    }

    private static string ClampUpgradeLoop(string text, string entry, bool hasRodData)
    {
        var match = Regex.Match(text, @"\bwith\s*\(\s*UpgradeData\s*\)\s*\{");
        if (!match.Success) return text;
        int open = match.Index + match.Length - 1;
        int depth = 1, pos = open + 1;
        while (pos < text.Length && depth > 0)
        {
            char c = text[pos++];
            if (c == '"')
            {
                while (pos < text.Length && text[pos] != '"')
                {
                    if (text[pos] == '\\' && pos + 1 < text.Length) pos++;
                    pos++;
                }
                if (pos < text.Length) pos++;
                continue;
            }
            if (c == '{') depth++;
            else if (c == '}') depth--;
        }
        if (depth != 0)
            throw new InvalidDataException($"Cannot patch '{entry}' for Items API: malformed 'with (UpgradeData)' block.");
        string block = text[open..pos];
        block = RegexReplaceOnce(block, entry,
            new Regex(@"for\s*\(\s*i\s*=\s*0\s*;\s*i\s*(?:<=\s*\d+|<\s*(?:\d+|nItems|array_length_1d\(unlocks\)))\s*;\s*i\s*(?:\+\+|\+=\s*1)\s*\)"),
            "for (i = 0; i < jamm_base_count; i++)",
            "with (UpgradeData) unlock loop");
        string rodBound = hasRodData ? "min(RodData.jamm_base_count, array_height_2d(upg))" : "array_height_2d(upg)";
        block = RegexReplaceOnce(block, entry,
            new Regex(@"for\s*\(\s*ix\s*=\s*0\s*;\s*ix\s*(?:<=\s*\d+|<\s*(?:\d+|RodData\.nItems|array_height_2d\(upg\)))\s*;\s*ix\s*(?:\+\+|\+=\s*1)\s*\)"),
            $"for (ix = 0; ix < {rodBound}; ix++)",
            "with (UpgradeData) rod-slot loop");
        var iyRegex = new Regex(@"for\s*\(\s*iy\s*=\s*0\s*;\s*iy\s*(?:<=\s*\d+|<\s*(?:\d+|array_length_2d\(upg,\s*ix\)|jamm_max_slots|min\(\s*\d+\s*,\s*array_length_2d\(upg,\s*ix\)\s*\)))\s*;\s*iy\s*(?:\+\+|\+=\s*1)\s*\)");
        if (iyRegex.IsMatch(block))
        {
            block = RegexReplaceOnce(block, entry, iyRegex,
                "for (iy = 0; iy < min(10, array_length_2d(upg, ix)); iy++)",
                "with (UpgradeData) slot-column loop");
        }
        return string.Concat(text.AsSpan(0, open), block, text.AsSpan(pos));
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

    private static string PatchRodUpgrades(string text, bool hasUpgradeData)
    {
        if (!hasUpgradeData || text.Contains("jamm_upg", StringComparison.Ordinal)) return text;
        if (!text.Contains("RodData.disp", StringComparison.Ordinal) && !text.Contains("image_index = rod_id;", StringComparison.Ordinal))
            throw new InvalidDataException("Cannot patch 'gml_Object_Rod_Other_10' for Items API: expected rod equip anchor not found.");
        var vanillaSlotLoop = new Regex(@"for\s*\(\s*i\s*=\s*0\s*;\s*i\s*(?:<=\s*(?:7|8)|<\s*(?:8|9))\s*;\s*i\s*(?:\+\+|\+=\s*1)\s*\)(?=\s*\{[\s\S]{0,400}?UpgradeData\.upg\s*\[\s*rod_id\s*,\s*i\s*\])");
        if (vanillaSlotLoop.IsMatch(text))
        {
            text = RegexReplaceOnce(text, "gml_Object_Rod_Other_10", vanillaSlotLoop,
                "for (i = 0; i < array_length_2d(UpgradeData.upg, rod_id); i++)",
                "Rod_Other_10 upgrade slot loop");
        }
        return text.TrimEnd() + "\n" + """
            if (instance_exists(UpgradeData))
            {
                var jamm_un, jamm_ui, jamm_si, jamm_ulen, jamm_uid, jamm_app, jamm_stk;
                jamm_un = array_length_1d(UpgradeData.unlocks);
                for (jamm_ui = 0; jamm_ui < jamm_un; jamm_ui += 1)
                {
                    jamm_upg[jamm_ui] = 0;
                }
                if ((!variable_instance_exists(id, "allowUpgrades") || allowUpgrades) && variable_instance_exists(id, "rod_id") && rod_id >= 0 && rod_id < array_height_2d(UpgradeData.upg))
                {
                    jamm_ulen = array_length_2d(UpgradeData.upg, rod_id);
                    for (jamm_si = 0; jamm_si < jamm_ulen; jamm_si += 1)
                    {
                        jamm_uid = UpgradeData.upg[rod_id, jamm_si];
                        if (jamm_uid >= 0 && jamm_uid < jamm_un)
                        {
                            jamm_upg[jamm_uid] += 1;
                        }
                    }
                }
                if (variable_instance_exists(UpgradeData.id, "jamm_is_api") && variable_instance_exists(UpgradeData.id, "jamm_apply"))
                {
                    for (jamm_ui = 0; jamm_ui < jamm_un; jamm_ui += 1)
                    {
                        if (jamm_ui < array_length_1d(UpgradeData.jamm_is_api) && UpgradeData.jamm_is_api[jamm_ui] && jamm_upg[jamm_ui] > 0)
                        {
                            jamm_app = UpgradeData.jamm_apply[jamm_ui];
                            if (jamm_app > -1 && script_exists(jamm_app))
                            {
                                jamm_stk = jamm_upg[jamm_ui];
                                script_execute(jamm_app, jamm_stk);
                            }
                        }
                    }
                }
            }
            """;
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

    private static readonly Regex GirlUpgradeResetLoopRegex = new(
        @"for\s*\(\s*i\s*=\s*0\s*;\s*i\s*(?:<=|<)\s*\d+\s*;\s*i\s*(?:\+\+|\+=\s*1)\s*\)\s*\{\s*gear_upgrade_scalar\[i\]\s*=\s*1;\s*\}");
    private static readonly Regex GirlUpgradeCopyLoopRegex = new(
        @"for\s*\(\s*i\s*=\s*0\s*;\s*i\s*(?:<=|<)\s*\d+\s*;\s*i\s*(?:\+\+|\+=\s*1)\s*\)\s*\{\s*gear_upgrade_scalar\[i\]\s*=\s*gear\.upgrade_scalar\[i\];\s*\}");
    private static readonly Regex GearUpgradeResetLoopRegex = new(
        @"for\s*\(\s*i\s*=\s*0\s*;\s*i\s*(?:<=|<)\s*\d+\s*;\s*i\s*(?:\+\+|\+=\s*1)\s*\)\s*\{\s*upgrade_scalar\[i\]\s*=\s*1;\s*\}");

    private const string GirlUpgradeResetLoopReplacement = """
        var jamm_un, jamm_ui;
            jamm_un = 18;
            if (instance_exists(UpgradeData)) { jamm_un = array_length_1d(UpgradeData.unlocks); }
            for (jamm_ui = 0; jamm_ui < jamm_un; jamm_ui += 1) { gear_upgrade_scalar[jamm_ui] = 1; }
        """;

    private const string GirlUpgradeCopyLoopReplacement = """
        var jamm_un2, jamm_ui2, jamm_uglen;
            jamm_un2 = 18;
            if (instance_exists(UpgradeData)) { jamm_un2 = array_length_1d(UpgradeData.unlocks); }
            jamm_uglen = array_length_1d(gear.upgrade_scalar);
            for (jamm_ui2 = 0; jamm_ui2 < jamm_un2; jamm_ui2 += 1) {
                if (jamm_ui2 < jamm_uglen) { gear_upgrade_scalar[jamm_ui2] = gear.upgrade_scalar[jamm_ui2]; }
                else { gear_upgrade_scalar[jamm_ui2] = 1; }
            }
        """;

    private const string GearUpgradeResetLoopReplacement = """
        var jamm_un, jamm_ui;
        jamm_un = 18;
        if (instance_exists(UpgradeData)) { jamm_un = array_length_1d(UpgradeData.unlocks); }
        for (jamm_ui = 0; jamm_ui < jamm_un; jamm_ui += 1) { upgrade_scalar[jamm_ui] = 1; }
        """;

    internal static string PatchGirlUpgradeScalarLoops(string text, bool requireAnchors = true)
    {
        const string entry = "gml_Object_Girl_Other_10";
        if (!text.Contains("gear_upgrade_scalar", StringComparison.Ordinal))
            return text;

        if (requireAnchors)
        {
            if (text.Contains("UpgradeData.unlocks", StringComparison.Ordinal))
                return text;
            text = RegexReplaceOnce(text, entry, GirlUpgradeResetLoopRegex, GirlUpgradeResetLoopReplacement, "gear_upgrade_scalar reset loop");
            text = RegexReplaceOnce(text, entry, GirlUpgradeCopyLoopRegex, GirlUpgradeCopyLoopReplacement, "gear_upgrade_scalar copy loop");
            return text;
        }

        if (!text.Contains("jamm_un", StringComparison.Ordinal))
            text = RegexReplaceIfSingle(text, GirlUpgradeResetLoopRegex, GirlUpgradeResetLoopReplacement);
        if (!text.Contains("jamm_un2", StringComparison.Ordinal))
            text = RegexReplaceIfSingle(text, GirlUpgradeCopyLoopRegex, GirlUpgradeCopyLoopReplacement);
        return text;
    }

    internal static string PatchGearUpgradeScalarLoop(string text, bool requireAnchors = true)
    {
        const string entry = "gml_Object_Gear_Other_15";
        if (!text.Contains("upgrade_scalar", StringComparison.Ordinal)
            || text.Contains("UpgradeData.unlocks", StringComparison.Ordinal))
            return text;

        return requireAnchors
            ? RegexReplaceOnce(text, entry, GearUpgradeResetLoopRegex, GearUpgradeResetLoopReplacement, "upgrade_scalar reset loop")
            : RegexReplaceIfSingle(text, GearUpgradeResetLoopRegex, GearUpgradeResetLoopReplacement);
    }

    private static string PatchGirlGearRefresh(string text, bool hasLureData, bool hasUpgradeData)
    {
        const string entry = "gml_Object_Girl_Other_10";
        if (hasLureData && !text.Contains("LureData.nItems", StringComparison.Ordinal)
            && (text.Contains("gear_lure_scalar", StringComparison.Ordinal) || !text.Contains("gear_upgrade_scalar", StringComparison.Ordinal)))
        {
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
        }
        if (hasUpgradeData)
            text = PatchGirlUpgradeScalarLoops(text, requireAnchors: true);
        return text;
    }

    private static string PatchGearStats(string text, bool hasLureData, bool hasUpgradeData)
    {
        const string entry = "gml_Object_Gear_Other_15";
        if (hasLureData && !text.Contains("LureData.nItems", StringComparison.Ordinal)
            && (text.Contains("lure_scalar", StringComparison.Ordinal) || !text.Contains("upgrade_scalar", StringComparison.Ordinal)))
        {
            text = ReplaceOnce(text, entry,
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
        if (hasUpgradeData)
            text = PatchGearUpgradeScalarLoop(text, requireAnchors: true);
        return text;
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

    private static string PatchUpgradeAchievement(string text)
    {
        if (text.Contains("UpgradeData.jamm_base_count", StringComparison.Ordinal)) return text;
        return ReplaceOnce(text, "gml_Script_upgrade_was_purchased",
            "steam_set_achievement(\"upg_\" + string(argument0));",
            "if (argument0 < UpgradeData.jamm_base_count) { steam_set_achievement(\"upg_\" + string(argument0)); }");
    }

    private static string PatchUpgradeCardDraw(string text)
    {
        if (text.Contains("UpgradeData.disp", StringComparison.Ordinal)) return text;
        return ReplaceOnce(text, "gml_Object_UpgradeCard_Other_11",
            "draw_sprite_ext(spr_upgradeicons, 1 + card_id, icon_x, icon_y, icon_scale, icon_scale, 0, c_white, 1);",
            BuildUpgradeIconDraw("card_id"));
    }

    private static string PatchUpgradeSlotDraw(string text)
    {
        if (text.Contains("UpgradeData.disp", StringComparison.Ordinal)) return text;
        return ReplaceOnce(text, "gml_Object_UpgradeSlot_Other_11",
            "draw_sprite_ext(spr_upgradeicons, 1 + UpgradeData.upg[Rod.rod_id, slot_id], icon_x, icon_y, icon_scale, icon_scale, 0, c_white, 1);",
            BuildUpgradeIconDraw("UpgradeData.upg[Rod.rod_id, slot_id]"));
    }

    private static string PatchUpgradeDataDraw(string text)
    {
        if (text.Contains("UpgradeData.disp", StringComparison.Ordinal)) return text;
        var slotLoop = new Regex(@"for\s*\(\s*i\s*=\s*0\s*;\s*i\s*(?:<=\s*(?:7|8)|<\s*(?:8|9))\s*;\s*i\s*(?:\+\+|\+=\s*1)\s*\)(?=\s*\{[\s\S]{0,400}?UpgradeData\.upg\s*\[\s*Rod\.rod_id\s*,\s*i\s*\])");
        if (slotLoop.IsMatch(text))
        {
            text = RegexReplaceOnce(text, "gml_Object_UpgradeData_Draw_0", slotLoop,
                "for (i = 0; i < array_length_2d(UpgradeData.upg, Rod.rod_id); i++)",
                "UpgradeData_Draw_0 slot loop");
        }
        return ReplaceOnce(text, "gml_Object_UpgradeData_Draw_0",
            "draw_sprite_ext(spr_upgradeicons, 1 + UpgradeData.upg[Rod.rod_id, i], icon_x, icon_y, icon_scale, icon_scale, 0, c_white, 1);",
            BuildUpgradeIconDraw("UpgradeData.upg[Rod.rod_id, i]"));
    }

    private static string BuildUpgradeIconDraw(string idExpr) => $$"""
        var jamm_uid, jamm_spr, jamm_sc, jamm_sub;
        jamm_uid = {{idExpr}};
        if (jamm_uid > -1 && variable_instance_exists(UpgradeData.id, "disp") && jamm_uid < array_length_1d(UpgradeData.disp) && UpgradeData.disp[jamm_uid] > -1)
        {
            jamm_spr = UpgradeData.disp[jamm_uid];
            jamm_sc = min(sprite_get_width(spr_upgradeicons) / max(1, sprite_get_width(jamm_spr)), sprite_get_height(spr_upgradeicons) / max(1, sprite_get_height(jamm_spr))) * icon_scale;
            draw_sprite_ext(jamm_spr, 0, icon_x + (sprite_get_xoffset(jamm_spr) * jamm_sc), icon_y + (sprite_get_yoffset(jamm_spr) * jamm_sc), jamm_sc, jamm_sc, 0, c_white, 1);
        }
        else
        {
            jamm_sub = 1 + jamm_uid;
            if (jamm_sub < 0 || jamm_sub >= sprite_get_number(spr_upgradeicons))
            {
                jamm_sub = 0;
            }
            draw_sprite_ext(spr_upgradeicons, jamm_sub, icon_x, icon_y, icon_scale, icon_scale, 0, c_white, 1);
        }
        """;

    private static string PatchUpgradeCardClick(string text)
    {
        if (text.Contains("jamm_max_stacks", StringComparison.Ordinal)) return text;
        const string entry = "gml_Object_UpgradeCard_Other_10";
        var anchor = new Regex(@"if\s*\(\s*!allowPurchase\s*\)\s*\{\s*instance_create\(\s*0\s*,\s*0\s*,\s*Message_TooManyUpgrades\s*\);\s*\}");
        return RegexReplaceOnce(text, entry, anchor, """
            if (variable_instance_exists(UpgradeData.id, "jamm_is_api") && card_id >= 0 && card_id < array_length_1d(UpgradeData.jamm_is_api) && UpgradeData.jamm_is_api[card_id])
            {
                if (UpgradeData.jamm_max_stacks[card_id] > 0 && Rod.rod_id >= 0 && Rod.rod_id < array_height_2d(UpgradeData.upg))
                {
                    var jamm_stacks, jamm_si, jamm_slen;
                    jamm_stacks = 0;
                    jamm_slen = array_length_2d(UpgradeData.upg, Rod.rod_id);
                    for (jamm_si = 0; jamm_si < jamm_slen; jamm_si += 1)
                    {
                        if (UpgradeData.upg[Rod.rod_id, jamm_si] == card_id)
                        {
                            jamm_stacks += 1;
                        }
                    }
                    if (jamm_stacks >= UpgradeData.jamm_max_stacks[card_id])
                    {
                        allowPurchase = 0;
                    }
                }
            }
            if (!allowPurchase)
            {
                instance_create(0, 0, Message_TooManyUpgrades);
            }
            """, "Message_TooManyUpgrades check");
    }

    private static string RegexReplaceOnce(string text, string entry, Regex pattern, string replacement, string anchorLabel)
    {
        var matches = pattern.Matches(text);
        if (matches.Count == 0)
            throw new InvalidDataException($"Cannot patch '{entry}' for Items API: expected anchor text not found ({anchorLabel}).");
        if (matches.Count > 1)
            throw new InvalidDataException($"Cannot patch '{entry}' for Items API: expected anchor text matched multiple times ({anchorLabel}).");
        var m = matches[0];
        return string.Concat(text.AsSpan(0, m.Index), replacement, text.AsSpan(m.Index + m.Length));
    }

    private static string RegexReplaceIfSingle(string text, Regex pattern, string replacement)
    {
        var matches = pattern.Matches(text);
        if (matches.Count != 1) return text;
        var m = matches[0];
        return string.Concat(text.AsSpan(0, m.Index), replacement, text.AsSpan(m.Index + m.Length));
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
