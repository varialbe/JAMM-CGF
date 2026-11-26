using System.Text;

namespace Jamm.Core.Building;

internal static class ItemsRuntimeScripts
{
    internal const int JammMaxRodSlots = 16;
    internal const string ApiInitName = "jamm_api_init";
    internal const string ApiInitCode = "gml_Script_" + ApiInitName;
    internal const string ApiControllerName = "obj_jamm_api";
    internal const string ApiControllerCreateCode = "gml_Object_obj_jamm_api_Create_0";
    internal const string ApiControllerStepCode = "gml_Object_obj_jamm_api_Step_1";

    internal static readonly IReadOnlyDictionary<string, string> DefaultScripts =
        BuildScripts(null, null);

    internal static IReadOnlyDictionary<string, string> BuildScripts(
        IReadOnlySet<string>? objects,
        IReadOnlySet<string>? scripts)
    {
        bool HasObj(string name) => objects is null || objects.Contains(name);
        bool HasScr(string name) => scripts is null || scripts.Contains(name);

        return new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["jamm_items_ensure_base"] = BuildEnsureBase(
                HasObj("BoatData"), HasObj("RodData"), HasObj("LureData"),
                HasObj("ToolData"), HasObj("GearData"), HasObj("UpgradeData"),
                HasObj("Girl")),
            ["jamm_item_find"] = BuildItemFind(
                HasObj("BoatData"), HasObj("RodData"), HasObj("LureData"),
                HasObj("ToolData"), HasObj("GearData"), HasObj("UpgradeData")),
            ["jamm_boat_register"] = BuildBoatRegister(HasObj("BoatData")),
            ["jamm_rod_register"] = BuildRodRegister(HasObj("RodData"), HasObj("UpgradeData")),
            ["jamm_lure_register"] = BuildLureRegister(HasObj("LureData"), HasObj("Girl"), HasObj("Gear")),
            ["jamm_tool_register"] = BuildToolRegister(HasObj("ToolData")),
            ["jamm_hat_register"] = BuildHatRegister(HasObj("GearData")),
            ["jamm_upgrade_register"] = BuildUpgradeRegister(HasObj("UpgradeData"), HasObj("Girl"), HasObj("Gear")),
            ["jamm_upgrade_stacks"] = BuildUpgradeStacks(HasObj("UpgradeData"), HasObj("Rod")),
            ["jamm_items_save"] = BuildSave(
                HasObj("BoatData"), HasObj("RodData"), HasObj("LureData"),
                HasObj("ToolData"), HasObj("GearData"), HasObj("UpgradeData"),
                HasObj("Boat"), HasObj("Rod"), HasObj("Girl")),
            ["jamm_items_load"] = BuildLoad(
                HasObj("BoatData"), HasObj("RodData"), HasObj("LureData"),
                HasObj("ToolData"), HasObj("GearData"), HasObj("UpgradeData"),
                HasObj("Boat"), HasObj("Rod"), HasObj("Girl"), HasScr("gear_equip"))
        };
    }

    internal static string BuildApiControllerCreate() => """
        if (instance_number(object_index) > 1)
        {
            instance_destroy();
            exit;
        }
        persistent = true;
        """;

    internal static string BuildApiControllerStep(IReadOnlySet<string>? objects)
    {
        bool HasObj(string name) => objects is null || objects.Contains(name);
        if (!HasObj("UpgradeData") || !HasObj("Rod")) return "jamm_api_ready = 1;";
        return """
            if (!instance_exists(UpgradeData) || !instance_exists(Rod)) exit;
            if (!variable_instance_exists(UpgradeData.id, "jamm_api_done") || !UpgradeData.jamm_api_done) exit;
            if (!variable_instance_exists(UpgradeData.id, "jamm_is_api") || !variable_instance_exists(UpgradeData.id, "jamm_step")) exit;
            if (!variable_instance_exists(Rod.id, "jamm_upg")) exit;
            var jamm_n, jamm_i, jamm_stk, jamm_scr;
            jamm_n = min(array_length_1d(UpgradeData.jamm_is_api), array_length_1d(UpgradeData.jamm_step));
            jamm_n = min(jamm_n, array_length_1d(Rod.jamm_upg));
            for (jamm_i = 0; jamm_i < jamm_n; jamm_i += 1)
            {
                if (UpgradeData.jamm_is_api[jamm_i])
                {
                    jamm_stk = Rod.jamm_upg[jamm_i];
                    if (jamm_stk > 0)
                    {
                        jamm_scr = UpgradeData.jamm_step[jamm_i];
                        if (jamm_scr > -1 && script_exists(jamm_scr))
                        {
                            script_execute(jamm_scr, jamm_stk);
                        }
                    }
                }
            }
            """;
    }

    internal static string BuildApiInit(IReadOnlyList<string> initScripts, IReadOnlySet<string>? objects)
    {
        bool HasObj(string name) => objects is null || objects.Contains(name);
        string[] dataObjects = new[] { "BoatData", "RodData", "LureData", "ToolData", "GearData", "UpgradeData" }
            .Where(HasObj).ToArray();

        StringBuilder sb = new();
        sb.AppendLine("if (!instance_exists(obj_jamm_api)) {");
        sb.AppendLine("    instance_create(0, 0, obj_jamm_api);");
        sb.AppendLine("}");
        sb.AppendLine("var jamm_need;");
        sb.AppendLine("jamm_need = 0;");
        foreach (string dataObj in dataObjects)
        {
            sb.AppendLine($"if (instance_exists({dataObj})) {{");
            sb.AppendLine($"    with ({dataObj}) {{");
            sb.AppendLine("        if (!variable_instance_exists(id, \"jamm_api_done\") || !jamm_api_done) jamm_need = 1;");
            sb.AppendLine("    }");
            sb.AppendLine("}");
        }
        sb.AppendLine("if (!jamm_need) return 0;");
        sb.AppendLine("jamm_items_ensure_base();");
        foreach (string script in initScripts)
            sb.AppendLine($"{script}();");
        foreach (string dataObj in dataObjects)
        {
            sb.AppendLine($"if (instance_exists({dataObj})) {{");
            sb.AppendLine($"    with ({dataObj}) jamm_api_done = 1;");
            sb.AppendLine("}");
        }
        if (HasObj("Rod"))
        {
            sb.AppendLine("if (instance_exists(Rod)) {");
            sb.AppendLine("    with (Rod) { event_perform(ev_other, ev_user0); }");
            sb.AppendLine("}");
        }
        sb.Append("return 1;");
        return sb.ToString();
    }

    private static string BuildEnsureBase(
        bool hasBoatData, bool hasRodData, bool hasLureData,
        bool hasToolData, bool hasGearData, bool hasUpgradeData,
        bool hasGirl)
    {
        StringBuilder sb = new();
        sb.AppendLine("var jamm_i, jamm_ix, jamm_iy, jamm_rlen, jamm_open;");
        if (hasBoatData)
            sb.AppendLine(DataEnsureBlock("BoatData", "nItems", "boat", "unlocked", setDisp: false, setRodSlots: false));
        if (hasRodData)
            sb.AppendLine(DataEnsureBlock("RodData", "nItems", "rod", "unlocked", setDisp: true, setRodSlots: true));
        if (hasLureData)
            sb.AppendLine(DataEnsureBlock("LureData", "nItems", "lure", "unlocked", setDisp: false, setRodSlots: false));
        if (hasToolData)
            sb.AppendLine(DataEnsureBlock("ToolData", "nItems", "tool", "unlocked", setDisp: true, setRodSlots: false));
        if (hasGearData)
            sb.AppendLine(DataEnsureBlock("GearData", "n_gear", "hat", "unlocked", setDisp: false, setRodSlots: false));
        if (hasUpgradeData)
        {
            string rodSlotsEnsure = hasRodData
                ? """
                                if (instance_exists(RodData)) {
                                    for (jamm_ix = 0; jamm_ix < RodData.jamm_base_count; jamm_ix += 1) {
                                        jamm_open = 0;
                                        if (jamm_ix < array_height_2d(upg)) {
                                            jamm_rlen = array_length_2d(upg, jamm_ix);
                                            for (jamm_iy = 0; jamm_iy < jamm_rlen; jamm_iy += 1) {
                                                if (upg[jamm_ix,jamm_iy] != -2) {
                                                    jamm_open += 1;
                                                }
                                            }
                                        }
                                        RodData.jamm_slots[jamm_ix] = jamm_open;
                                    }
                                }
                    """
                : "";
            sb.AppendLine($$"""
                if (instance_exists(UpgradeData)) {
                    with (UpgradeData) {
                        if (!variable_instance_exists(id, "jamm_base_count")) {
                            nItems = array_length_1d(unlocks);
                            jamm_base_count = nItems;
                            for (jamm_i = 0; jamm_i < nItems; jamm_i += 1) {
                                jamm_key[jamm_i] = "base:upgrade" + string(jamm_i);
                                jamm_default_unlocked[jamm_i] = unlocks[jamm_i];
                                disp[jamm_i] = -1;
                                jamm_max_stacks[jamm_i] = -1;
                                jamm_apply[jamm_i] = -1;
                                jamm_step[jamm_i] = -1;
                                jamm_is_api[jamm_i] = 0;
                            }
                            jamm_max_slots = 0;
                            for (jamm_ix = 0; jamm_ix < array_height_2d(upg); jamm_ix += 1) {
                                jamm_rlen = array_length_2d(upg, jamm_ix);
                                if (jamm_rlen > jamm_max_slots) {
                                    jamm_max_slots = jamm_rlen;
                                }
                                for (jamm_iy = 0; jamm_iy < jamm_rlen; jamm_iy += 1) {
                                    jamm_default_upg[jamm_ix,jamm_iy] = upg[jamm_ix,jamm_iy];
                                }
                            }
                {{rodSlotsEnsure}}
                        }
                    }
                }
                """);
            if (hasGirl)
            {
                sb.AppendLine("""
                    if (instance_exists(Girl) && instance_exists(UpgradeData)) {
                        for (jamm_i = 0; jamm_i < UpgradeData.nItems; jamm_i += 1) {
                            if (!variable_instance_exists(Girl.id, "gear_upgrade_scalar") || jamm_i >= array_length_1d(Girl.gear_upgrade_scalar)) {
                                Girl.gear_upgrade_scalar[jamm_i] = 1;
                            }
                        }
                    }
                    """);
            }
        }
        sb.Append("return 1;");
        return sb.ToString();

        static string DataEnsureBlock(string obj, string countField, string type, string unlockField, bool setDisp, bool setRodSlots)
        {
            string dispLine = setDisp ? "\n                disp[jamm_i] = -1;" : "";
            string slotsLine = setRodSlots ? "\n                jamm_slots[jamm_i] = 8;" : "";
            return $$"""
                if (instance_exists({{obj}})) {
                    with ({{obj}}) {
                        if (!variable_instance_exists(id, "jamm_base_count")) {
                            jamm_base_count = {{countField}};
                            for (jamm_i = 0; jamm_i < {{countField}}; jamm_i += 1) {
                                jamm_key[jamm_i] = "base:{{type}}" + string(jamm_i);
                                jamm_default_unlocked[jamm_i] = {{unlockField}}[jamm_i];{{dispLine}}{{slotsLine}}
                            }
                        }
                    }
                }
                """;
        }
    }

    private static string BuildItemFind(
        bool hasBoatData, bool hasRodData, bool hasLureData,
        bool hasToolData, bool hasGearData, bool hasUpgradeData)
    {
        StringBuilder sb = new();
        sb.AppendLine("""
            var jamm_type, jamm_key, jamm_i;
            if (argument_count < 2) return -1;
            jamm_type = argument0;
            jamm_key = argument1;
            if (!is_string(jamm_type) || !is_string(jamm_key) || jamm_key == "") return -1;
            jamm_items_ensure_base();
            """);
        if (hasBoatData) sb.AppendLine(FindBlock("boat", "BoatData", "nItems"));
        if (hasRodData) sb.AppendLine(FindBlock("rod", "RodData", "nItems"));
        if (hasLureData) sb.AppendLine(FindBlock("lure", "LureData", "nItems"));
        if (hasToolData) sb.AppendLine(FindBlock("tool", "ToolData", "nItems"));
        if (hasGearData) sb.AppendLine(FindBlock("hat", "GearData", "n_gear"));
        if (hasUpgradeData) sb.AppendLine(FindBlock("upgrade", "UpgradeData", "nItems"));
        sb.Append("return -1;");
        return sb.ToString();

        static string FindBlock(string type, string obj, string countField) => $$"""
            if (jamm_type == "{{type}}" && instance_exists({{obj}})) {
                for (jamm_i = 0; jamm_i < {{obj}}.{{countField}}; jamm_i += 1) {
                    if ({{obj}}.jamm_key[jamm_i] == jamm_key) return jamm_i;
                }
                return -1;
            }
            """;
    }

    private static string BuildBoatRegister(bool hasBoatData)
    {
        if (!hasBoatData) return "return -1;";
        return """
            var jamm_key, jamm_name, jamm_cost, jamm_disp, jamm_obj, jamm_desc, jamm_unl, jamm_existing, jamm_slot;
            if (argument_count < 6 || !instance_exists(BoatData)) return -1;
            jamm_key = argument0; jamm_name = argument1; jamm_cost = argument2;
            jamm_disp = argument3; jamm_obj = argument4; jamm_desc = argument5;
            if (!is_string(jamm_key) || jamm_key == "" || !is_string(jamm_name) || !is_string(jamm_desc)) return -1;
            if (!is_real(jamm_cost) || jamm_cost < 0 || !is_real(jamm_disp) || !is_real(jamm_obj)) return -1;
            if (jamm_disp != -1 && !sprite_exists(jamm_disp)) return -1;
            if (!object_exists(jamm_obj)) return -1;
            jamm_unl = 0;
            if (argument_count > 6) {
                if (!is_real(argument6)) return -1;
                jamm_unl = argument6;
            }
            jamm_items_ensure_base();
            jamm_existing = jamm_item_find("boat", jamm_key);
            if (jamm_existing > -1) return jamm_existing;
            jamm_slot = BoatData.nItems;
            BoatData.jamm_key[jamm_slot] = jamm_key;
            BoatData.name[jamm_slot] = jamm_name;
            BoatData.cost[jamm_slot] = jamm_cost;
            BoatData.disp[jamm_slot] = jamm_disp;
            BoatData.object[jamm_slot] = jamm_obj;
            BoatData.desc[jamm_slot] = jamm_desc;
            BoatData.unlocked[jamm_slot] = jamm_unl;
            BoatData.jamm_default_unlocked[jamm_slot] = jamm_unl;
            BoatData.nItems = jamm_slot + 1;
            return jamm_slot;
            """;
    }

    private static string BuildRodRegister(bool hasRodData, bool hasUpgradeData)
    {
        if (!hasRodData) return "return -1;";
        string upgradeScanBlock = hasUpgradeData
            ? """
                        if (instance_exists(UpgradeData)) {
                            for (jamm_i = 0; jamm_i < array_height_2d(UpgradeData.upg); jamm_i += 1) {
                                jamm_open = 0;
                                jamm_rlen = array_length_2d(UpgradeData.upg, jamm_i);
                                for (jamm_iy = 0; jamm_iy < jamm_rlen; jamm_iy += 1) {
                                    if (UpgradeData.upg[jamm_i,jamm_iy] != -2) {
                                        jamm_open += 1;
                                    }
                                }
                                if (jamm_open > jamm_best_slots) {
                                    jamm_best_slots = jamm_open;
                                }
                            }
                        }
                """
            : "";
        string upgradeBlock = hasUpgradeData
            ? """
                if (instance_exists(UpgradeData)) {
                    jamm_row_cols = max(8, jamm_slots);
                    for (jamm_iy = 0; jamm_iy < jamm_row_cols; jamm_iy += 1) {
                        if (jamm_iy < jamm_slots) {
                            UpgradeData.upg[jamm_slot,jamm_iy] = -1;
                        } else {
                            UpgradeData.upg[jamm_slot,jamm_iy] = -2;
                        }
                        UpgradeData.object[jamm_slot,jamm_iy] = -1;
                        UpgradeData.jamm_default_upg[jamm_slot,jamm_iy] = UpgradeData.upg[jamm_slot,jamm_iy];
                    }
                    if (!variable_instance_exists(UpgradeData.id, "jamm_max_slots") || jamm_row_cols > UpgradeData.jamm_max_slots) {
                        UpgradeData.jamm_max_slots = jamm_row_cols;
                    }
                }
                """
            : "";
        return $$"""
            var jamm_key, jamm_name, jamm_cost, jamm_cs, jamm_rp, jamm_rpf, jamm_rs, jamm_sd, jamm_disp;
            var jamm_slots, jamm_unl, jamm_existing, jamm_slot, jamm_iy, jamm_best_slots, jamm_i, jamm_open, jamm_rlen, jamm_row_cols;
            if (argument_count < 9 || !instance_exists(RodData)) return -1;
            jamm_key = argument0; jamm_name = argument1; jamm_cost = argument2;
            jamm_cs = argument3; jamm_rp = argument4; jamm_rpf = argument5;
            jamm_rs = argument6; jamm_sd = argument7; jamm_disp = argument8;
            if (!is_string(jamm_key) || jamm_key == "" || !is_string(jamm_name)) return -1;
            if (!is_real(jamm_cost) || jamm_cost < 0 || !is_real(jamm_cs) || !is_real(jamm_rp) || !is_real(jamm_rpf)) return -1;
            if (!is_real(jamm_rs) || !is_real(jamm_sd) || !is_real(jamm_disp)) return -1;
            if (jamm_disp != -1 && !sprite_exists(jamm_disp)) return -1;
            jamm_slots = 8;
            if (argument_count > 9) {
                if (!is_real(argument9)) return -1;
                jamm_slots = argument9;
                if (jamm_slots < -1 || jamm_slots > {{JammMaxRodSlots}} || floor(jamm_slots) != jamm_slots) return -1;
            }
            jamm_unl = 0;
            if (argument_count > 10) {
                if (!is_real(argument10)) return -1;
                jamm_unl = argument10;
            }
            jamm_items_ensure_base();
            jamm_existing = jamm_item_find("rod", jamm_key);
            if (jamm_existing > -1) return jamm_existing;
            if (jamm_slots == -1) {
                jamm_best_slots = 0;
                if (variable_instance_exists(RodData.id, "jamm_slots")) {
                    for (jamm_i = 0; jamm_i < RodData.nItems; jamm_i += 1) {
                        if (jamm_i < array_length_1d(RodData.jamm_slots) && RodData.jamm_slots[jamm_i] > jamm_best_slots) {
                            jamm_best_slots = RodData.jamm_slots[jamm_i];
                        }
                    }
                }
            {{upgradeScanBlock}}
                jamm_slots = min({{JammMaxRodSlots}}, jamm_best_slots + 1);
            }
            jamm_slot = RodData.nItems;
            RodData.jamm_key[jamm_slot] = jamm_key;
            RodData.name[jamm_slot] = jamm_name;
            RodData.cost[jamm_slot] = jamm_cost;
            RodData.cast_speed[jamm_slot] = jamm_cs;
            RodData.rod_power[jamm_slot] = jamm_rp;
            RodData.rod_power_flat[jamm_slot] = jamm_rpf;
            RodData.reel_speed[jamm_slot] = jamm_rs;
            RodData.sinker_distance[jamm_slot] = jamm_sd;
            RodData.disp[jamm_slot] = jamm_disp;
            RodData.jamm_slots[jamm_slot] = jamm_slots;
            RodData.unlocked[jamm_slot] = jamm_unl;
            RodData.jamm_default_unlocked[jamm_slot] = jamm_unl;
            RodData.nItems = jamm_slot + 1;
            {{upgradeBlock}}
            return jamm_slot;
            """;
    }

    private static string BuildLureRegister(bool hasLureData, bool hasGirl, bool hasGear)
    {
        if (!hasLureData) return "return -1;";
        string girlLine = hasGirl ? "\nif (instance_exists(Girl)) Girl.gear_lure_scalar[jamm_slot] = 1;" : "";
        string gearLine = hasGear ? "\nwith (Gear) { lure_scalar[jamm_slot] = 1; }" : "";
        return $$"""
            var jamm_key, jamm_name, jamm_cost, jamm_obj, jamm_disp, jamm_desc, jamm_unl, jamm_existing, jamm_slot;
            if (argument_count < 6 || !instance_exists(LureData)) return -1;
            jamm_key = argument0; jamm_name = argument1; jamm_cost = argument2;
            jamm_obj = argument3; jamm_disp = argument4; jamm_desc = argument5;
            if (!is_string(jamm_key) || jamm_key == "" || !is_string(jamm_name) || !is_string(jamm_desc)) return -1;
            if (!is_real(jamm_cost) || jamm_cost < 0 || !is_real(jamm_obj) || !is_real(jamm_disp)) return -1;
            if (!object_exists(jamm_obj)) return -1;
            if (jamm_disp != -1 && !sprite_exists(jamm_disp)) return -1;
            jamm_unl = 1;
            if (argument_count > 6) {
                if (!is_real(argument6)) return -1;
                jamm_unl = argument6;
            }
            jamm_items_ensure_base();
            jamm_existing = jamm_item_find("lure", jamm_key);
            if (jamm_existing > -1) return jamm_existing;
            jamm_slot = LureData.nItems;
            LureData.jamm_key[jamm_slot] = jamm_key;
            LureData.name[jamm_slot] = jamm_name;
            LureData.cost[jamm_slot] = jamm_cost;
            LureData.item[jamm_slot] = jamm_obj;
            LureData.disp[jamm_slot] = jamm_disp;
            LureData.desc[jamm_slot] = jamm_desc;
            LureData.unlocked[jamm_slot] = jamm_unl;
            LureData.jamm_default_unlocked[jamm_slot] = jamm_unl;
            LureData.nItems = jamm_slot + 1;{{girlLine}}{{gearLine}}
            return jamm_slot;
            """;
    }

    private static string BuildToolRegister(bool hasToolData)
    {
        if (!hasToolData) return "return -1;";
        return """
            var jamm_key, jamm_name, jamm_cost, jamm_quests, jamm_obj, jamm_msg, jamm_disp, jamm_desc;
            var jamm_unl, jamm_existing, jamm_slot, jamm_inst;
            if (argument_count < 8 || !instance_exists(ToolData)) return -1;
            jamm_key = argument0; jamm_name = argument1; jamm_cost = argument2; jamm_quests = argument3;
            jamm_obj = argument4; jamm_msg = argument5; jamm_disp = argument6; jamm_desc = argument7;
            if (!is_string(jamm_key) || jamm_key == "" || !is_string(jamm_name) || !is_string(jamm_desc)) return -1;
            if (!is_real(jamm_cost) || jamm_cost < 0 || !is_real(jamm_quests) || jamm_quests < 0) return -1;
            if (!is_real(jamm_obj) || !is_real(jamm_msg) || !is_real(jamm_disp)) return -1;
            if (!object_exists(jamm_obj)) return -1;
            if (jamm_msg != -1 && !object_exists(jamm_msg)) return -1;
            if (jamm_disp != -1 && !sprite_exists(jamm_disp)) return -1;
            jamm_unl = 0;
            if (argument_count > 8) {
                if (!is_real(argument8)) return -1;
                jamm_unl = argument8;
            }
            jamm_items_ensure_base();
            jamm_existing = jamm_item_find("tool", jamm_key);
            if (jamm_existing > -1) return jamm_existing;
            jamm_slot = ToolData.nItems;
            ToolData.jamm_key[jamm_slot] = jamm_key;
            ToolData.name[jamm_slot] = jamm_name;
            ToolData.cost[jamm_slot] = jamm_cost;
            ToolData.quests[jamm_slot] = jamm_quests;
            ToolData.item[jamm_slot] = jamm_obj;
            ToolData.message[jamm_slot] = jamm_msg;
            ToolData.disp[jamm_slot] = jamm_disp;
            ToolData.desc[jamm_slot] = jamm_desc;
            ToolData.unlocked[jamm_slot] = jamm_unl;
            ToolData.jamm_default_unlocked[jamm_slot] = jamm_unl;
            ToolData.nItems = jamm_slot + 1;
            if (!instance_exists(jamm_obj)) {
                jamm_inst = instance_create(0, 0, jamm_obj);
                if (!variable_instance_exists(jamm_inst, "active")) {
                    jamm_inst.active = 0;
                }
            }
            return jamm_slot;
            """;
    }

    private static string BuildHatRegister(bool hasGearData)
    {
        if (!hasGearData) return "return -1;";
        return """
            var jamm_key, jamm_obj, jamm_val, jamm_w, jamm_rmin, jamm_rmax, jamm_ox, jamm_oy, jamm_myth;
            var jamm_existing, jamm_slot, jamm_tw, jamm_i, jamm_j, jamm_max_id, jamm_max_val, jamm_v;
            if (argument_count < 6 || !instance_exists(GearData)) return -1;
            jamm_key = argument0; jamm_obj = argument1; jamm_val = argument2;
            jamm_w = argument3; jamm_rmin = argument4; jamm_rmax = argument5;
            if (!is_string(jamm_key) || jamm_key == "") return -1;
            if (!is_real(jamm_obj) || !object_exists(jamm_obj)) return -1;
            if (!is_real(jamm_val) || jamm_val < 0 || !is_real(jamm_w) || jamm_w < 0) return -1;
            if (!is_real(jamm_rmin) || !is_real(jamm_rmax)) return -1;
            jamm_ox = 0; jamm_oy = 0; jamm_myth = 0;
            if (argument_count > 6) {
                if (!is_real(argument6)) return -1;
                jamm_ox = argument6;
            }
            if (argument_count > 7) {
                if (!is_real(argument7)) return -1;
                jamm_oy = argument7;
            }
            if (argument_count > 8) {
                if (!is_real(argument8)) return -1;
                jamm_myth = argument8;
            }
            jamm_items_ensure_base();
            jamm_existing = jamm_item_find("hat", jamm_key);
            if (jamm_existing > -1) return jamm_existing;
            jamm_slot = GearData.n_gear;
            GearData.jamm_key[jamm_slot] = jamm_key;
            GearData.gear[jamm_slot] = jamm_obj;
            GearData.value[jamm_slot] = jamm_val;
            GearData.weight[jamm_slot] = jamm_w;
            GearData.range_min[jamm_slot] = jamm_rmin;
            GearData.range_max[jamm_slot] = jamm_rmax;
            GearData.offset_x[jamm_slot] = jamm_ox;
            GearData.offset_y[jamm_slot] = jamm_oy;
            GearData.mythic[jamm_slot] = jamm_myth;
            GearData.unlocked[jamm_slot] = 0;
            GearData.jamm_default_unlocked[jamm_slot] = 0;
            GearData.n_gear = jamm_slot + 1;
            for (jamm_i = 0; jamm_i < GearData.n_gear; jamm_i += 1) {
                jamm_tw[jamm_i] = GearData.weight[jamm_i];
            }
            for (jamm_i = 0; jamm_i < GearData.n_gear; jamm_i += 1) {
                jamm_max_id = 0;
                jamm_max_val = -99;
                for (jamm_j = 0; jamm_j < GearData.n_gear; jamm_j += 1) {
                    jamm_v = jamm_tw[jamm_j];
                    if (GearData.mythic[jamm_j]) jamm_v -= 10;
                    if (jamm_v > jamm_max_val) {
                        jamm_max_val = jamm_v;
                        jamm_max_id = jamm_j;
                    }
                }
                jamm_tw[jamm_max_id] = -100;
                GearData.map[jamm_i] = jamm_max_id;
            }
            return jamm_slot;
            """;
    }

    private static string BuildUpgradeRegister(bool hasUpgradeData, bool hasGirl, bool hasGear)
    {
        if (!hasUpgradeData) return "return -1;";
        string girlLine = hasGirl ? "\nif (instance_exists(Girl)) { Girl.gear_upgrade_scalar[jamm_slot] = 1; }" : "";
        string gearLine = hasGear ? "\nwith (Gear) { upgrade_scalar[jamm_slot] = 1; }" : "";
        return $$"""
            var jamm_key, jamm_name, jamm_desc, jamm_desc2, jamm_cost, jamm_icon, jamm_max, jamm_apply, jamm_step;
            var jamm_existing, jamm_slot;
            if (argument_count < 9 || !instance_exists(UpgradeData)) return -1;
            jamm_key = argument0; jamm_name = argument1; jamm_desc = argument2; jamm_desc2 = argument3;
            jamm_cost = argument4; jamm_icon = argument5; jamm_max = argument6; jamm_apply = argument7; jamm_step = argument8;
            if (!is_string(jamm_key) || jamm_key == "" || !is_string(jamm_name) || !is_string(jamm_desc) || !is_string(jamm_desc2)) return -1;
            if (!is_real(jamm_cost) || jamm_cost < 0 || !is_real(jamm_icon) || !is_real(jamm_max) || !is_real(jamm_apply) || !is_real(jamm_step)) return -1;
            if (jamm_icon != -1 && !sprite_exists(jamm_icon)) return -1;
            if (jamm_max != -1 && (jamm_max < 1 || floor(jamm_max) != jamm_max)) return -1;
            if (jamm_apply != -1 && !script_exists(jamm_apply)) return -1;
            if (jamm_step != -1 && !script_exists(jamm_step)) return -1;
            jamm_items_ensure_base();
            jamm_existing = jamm_item_find("upgrade", jamm_key);
            if (jamm_existing > -1) return jamm_existing;
            jamm_slot = array_length_1d(UpgradeData.unlocks);
            UpgradeData.jamm_key[jamm_slot] = jamm_key;
            UpgradeData.name[jamm_slot] = jamm_name;
            UpgradeData.desc[jamm_slot] = jamm_desc;
            UpgradeData.desc2[jamm_slot] = jamm_desc2;
            UpgradeData.cost[jamm_slot] = jamm_cost;
            UpgradeData.unlocks[jamm_slot] = 1;
            UpgradeData.jamm_default_unlocked[jamm_slot] = 1;
            UpgradeData.disp[jamm_slot] = jamm_icon;
            UpgradeData.jamm_max_stacks[jamm_slot] = jamm_max;
            UpgradeData.jamm_apply[jamm_slot] = jamm_apply;
            UpgradeData.jamm_step[jamm_slot] = jamm_step;
            UpgradeData.jamm_is_api[jamm_slot] = 1;
            UpgradeData.nItems = jamm_slot + 1;{{girlLine}}{{gearLine}}
            return jamm_slot;
            """;
    }

    private static string BuildUpgradeStacks(bool hasUpgradeData, bool hasRod)
    {
        if (!hasUpgradeData || !hasRod) return "return 0;";
        return """
            var jamm_id, jamm_cnt, jamm_si, jamm_slen;
            if (argument_count < 1 || !is_string(argument0) || argument0 == "") return 0;
            if (!instance_exists(UpgradeData) || !instance_exists(Rod)) return 0;
            jamm_id = jamm_item_find("upgrade", argument0);
            if (jamm_id < 0) return 0;
            if (variable_instance_exists(Rod.id, "jamm_upg") && jamm_id < array_length_1d(Rod.jamm_upg)) {
                return Rod.jamm_upg[jamm_id];
            }
            if (variable_instance_exists(Rod.id, "allowUpgrades") && !Rod.allowUpgrades) return 0;
            if (Rod.rod_id < 0 || Rod.rod_id >= array_height_2d(UpgradeData.upg)) return 0;
            jamm_cnt = 0;
            jamm_slen = array_length_2d(UpgradeData.upg, Rod.rod_id);
            for (jamm_si = 0; jamm_si < jamm_slen; jamm_si += 1) {
                if (UpgradeData.upg[Rod.rod_id, jamm_si] == jamm_id) {
                    jamm_cnt += 1;
                }
            }
            return jamm_cnt;
            """;
    }

    private static string BuildSave(
        bool hasBoatData, bool hasRodData, bool hasLureData,
        bool hasToolData, bool hasGearData, bool hasUpgradeData,
        bool hasBoat, bool hasRod, bool hasGirl)
    {
        StringBuilder sb = new();
        sb.AppendLine("""
            var jamm_i, jamm_eq, jamm_ix, jamm_iy, jamm_rkey, jamm_u, jamm_ukey, jamm_obj, jamm_okey;
            if (!instance_exists(SaveManager) || !variable_instance_exists(SaveManager.id, "jamm_items_loaded") || !SaveManager.jamm_items_loaded) return 0;
            jamm_items_ensure_base();
            """);
        if (hasBoatData) sb.AppendLine(SaveUnlockBlock("BoatData", "nItems", "boat", "unlocked"));
        if (hasRodData) sb.AppendLine(SaveUnlockBlock("RodData", "nItems", "rod", "unlocked"));
        if (hasLureData) sb.AppendLine(SaveUnlockBlock("LureData", "nItems", "lure", "unlocked"));
        if (hasToolData) sb.AppendLine(SaveUnlockBlock("ToolData", "nItems", "tool", "unlocked"));
        if (hasGearData) sb.AppendLine(SaveUnlockBlock("GearData", "n_gear", "hat", "unlocked"));
        if (hasUpgradeData) sb.AppendLine(SaveUnlockBlock("UpgradeData", "nItems", "upgrade", "unlocks"));

        if (hasBoatData)
        {
            string boatCheck = hasBoat
                ? """
                    if (instance_exists(Boat) && instance_exists(BoatData)) {
                        if (Boat.boat_index > -1 && Boat.boat_index < BoatData.nItems) {
                            jamm_eq = BoatData.jamm_key[Boat.boat_index];
                        }
                    }
                    """
                : "";
            sb.AppendLine($$"""
                jamm_eq = "";
                {{boatCheck}}
                ini_write_string("JAMM_Items", "eq:boat", jamm_eq);
                """);
        }
        if (hasRodData)
        {
            string rodCheck = hasRod
                ? """
                    if (instance_exists(Rod) && instance_exists(RodData)) {
                        if (Rod.rod_id > -1 && Rod.rod_id < RodData.nItems) {
                            jamm_eq = RodData.jamm_key[Rod.rod_id];
                        }
                    }
                    """
                : "";
            sb.AppendLine($$"""
                jamm_eq = "";
                {{rodCheck}}
                ini_write_string("JAMM_Items", "eq:rod", jamm_eq);
                """);
        }
        if (hasGearData)
        {
            string hatCheck = hasGirl
                ? """
                    if (instance_exists(Girl) && instance_exists(GearData)) {
                        if (Girl.gear_id > -1 && Girl.gear_id < GearData.n_gear) {
                            jamm_eq = GearData.jamm_key[Girl.gear_id];
                        }
                    }
                    """
                : "";
            sb.AppendLine($$"""
                jamm_eq = "";
                {{hatCheck}}
                ini_write_string("JAMM_Items", "eq:hat", jamm_eq);
                """);
        }
        if (hasRodData && hasUpgradeData)
        {
            sb.AppendLine("""
                if (instance_exists(RodData) && instance_exists(UpgradeData)) {
                    for (jamm_ix = 0; jamm_ix < RodData.nItems && jamm_ix < array_height_2d(UpgradeData.upg); jamm_ix += 1) {
                        jamm_rkey = RodData.jamm_key[jamm_ix];
                        for (jamm_iy = 0; jamm_iy < array_length_2d(UpgradeData.upg, jamm_ix); jamm_iy += 1) {
                            jamm_u = UpgradeData.upg[jamm_ix,jamm_iy];
                            jamm_ukey = "";
                            if (jamm_u == -2) {
                                jamm_ukey = "#locked";
                            } else if (jamm_u > -1 && jamm_u < UpgradeData.nItems) {
                                jamm_ukey = UpgradeData.jamm_key[jamm_u];
                            }
                            ini_write_string("JAMM_Items", "slot:" + jamm_rkey + ":" + string(jamm_iy), jamm_ukey);
                            jamm_obj = -1;
                            if (jamm_ix < array_height_2d(UpgradeData.object) && jamm_iy < array_length_2d(UpgradeData.object, jamm_ix)) {
                                jamm_obj = UpgradeData.object[jamm_ix,jamm_iy];
                            }
                            jamm_okey = "";
                            if (jamm_obj > -1 && object_exists(jamm_obj)) {
                                jamm_okey = object_get_name(jamm_obj);
                            }
                            ini_write_string("JAMM_Items", "slotobj:" + jamm_rkey + ":" + string(jamm_iy), jamm_okey);
                        }
                    }
                }
                """);
        }
        sb.Append("return 1;");
        return sb.ToString();

        static string SaveUnlockBlock(string obj, string countField, string type, string unlockField) => $$"""
            if (instance_exists({{obj}})) {
                for (jamm_i = 0; jamm_i < {{obj}}.{{countField}}; jamm_i += 1) {
                    ini_write_real("JAMM_Items", "u:{{type}}:" + {{obj}}.jamm_key[jamm_i], {{obj}}.{{unlockField}}[jamm_i]);
                }
            }
            """;
    }

    private static string BuildLoad(
        bool hasBoatData, bool hasRodData, bool hasLureData,
        bool hasToolData, bool hasGearData, bool hasUpgradeData,
        bool hasBoat, bool hasRod, bool hasGirl, bool hasGearEquip)
    {
        StringBuilder sb = new();
        sb.AppendLine("""
            var jamm_i, jamm_k, jamm_ix, jamm_iy, jamm_rkey, jamm_skey, jamm_uval, jamm_uslot;
            var jamm_okey, jamm_oname, jamm_oid, jamm_eq, jamm_bi, jamm_b, jamm_ri, jamm_hi, jamm_g, jamm_rod_refreshed;
            jamm_items_ensure_base();
            jamm_rod_refreshed = 0;
            if (ini_section_exists("JAMM_Items")) {
            """);

        if (hasBoatData) sb.AppendLine(LoadUnlockBlock("BoatData", "nItems", "boat", "unlocked"));
        if (hasRodData) sb.AppendLine(LoadUnlockBlock("RodData", "nItems", "rod", "unlocked"));
        if (hasLureData) sb.AppendLine(LoadUnlockBlock("LureData", "nItems", "lure", "unlocked"));
        if (hasToolData)
        {
            sb.AppendLine(LoadUnlockBlock("ToolData", "nItems", "tool", "unlocked"));
            sb.AppendLine("""
                    if (instance_exists(ToolData)) {
                        for (jamm_i = 0; jamm_i < ToolData.nItems; jamm_i += 1) {
                            if (instance_exists(ToolData.item[jamm_i])) {
                                if (ToolData.unlocked[jamm_i] > 0) ToolData.item[jamm_i].active = 1;
                                else ToolData.item[jamm_i].active = 0;
                            }
                        }
                    }
                """);
        }
        if (hasGearData) sb.AppendLine(LoadUnlockBlock("GearData", "n_gear", "hat", "unlocked"));
        if (hasUpgradeData) sb.AppendLine(LoadUnlockBlock("UpgradeData", "nItems", "upgrade", "unlocks"));

        if (hasGirl && hasRodData)
        {
            sb.AppendLine("""
                    if (instance_exists(Girl) && instance_exists(RodData)) {
                        Girl.bestRod = 0;
                        for (jamm_i = 0; jamm_i < RodData.nItems; jamm_i += 1) {
                            if (RodData.unlocked[jamm_i] > 0) Girl.bestRod = jamm_i;
                        }
                    }
                """);
        }
        if (hasGirl && hasBoatData)
        {
            sb.AppendLine("""
                    if (instance_exists(Girl) && instance_exists(BoatData)) {
                        Girl.bestBoat = 0;
                        for (jamm_i = 0; jamm_i < BoatData.nItems; jamm_i += 1) {
                            if (BoatData.unlocked[jamm_i] > 0) Girl.bestBoat = jamm_i;
                        }
                    }
                """);
        }
        if (hasRodData && hasUpgradeData)
        {
            sb.AppendLine("""
                    if (instance_exists(RodData) && instance_exists(UpgradeData)) {
                        for (jamm_ix = 0; jamm_ix < RodData.nItems && jamm_ix < array_height_2d(UpgradeData.upg); jamm_ix += 1) {
                            jamm_rkey = RodData.jamm_key[jamm_ix];
                            for (jamm_iy = 0; jamm_iy < array_length_2d(UpgradeData.upg, jamm_ix); jamm_iy += 1) {
                                jamm_skey = "slot:" + jamm_rkey + ":" + string(jamm_iy);
                                if (ini_key_exists("JAMM_Items", jamm_skey)) {
                                    jamm_uval = ini_read_string("JAMM_Items", jamm_skey, "");
                                    if (jamm_uval == "#locked") {
                                            if (!(jamm_ix < RodData.jamm_base_count && jamm_iy < 10 && UpgradeData.upg[jamm_ix,jamm_iy] >= 0)) {
                                                UpgradeData.upg[jamm_ix,jamm_iy] = -2;
                                                UpgradeData.object[jamm_ix,jamm_iy] = -1;
                                            }
                                    } else if (jamm_uval == "") {
                                            if (!(jamm_ix < RodData.jamm_base_count && jamm_iy < 10 && UpgradeData.upg[jamm_ix,jamm_iy] >= 0)) {
                                                UpgradeData.upg[jamm_ix,jamm_iy] = -1;
                                                UpgradeData.object[jamm_ix,jamm_iy] = -1;
                                            }
                                    } else {
                                        jamm_uslot = jamm_item_find("upgrade", jamm_uval);
                                        if (jamm_uslot > -1) {
                                            UpgradeData.upg[jamm_ix,jamm_iy] = jamm_uslot;
                                            jamm_okey = "slotobj:" + jamm_rkey + ":" + string(jamm_iy);
                                            jamm_oname = ini_read_string("JAMM_Items", jamm_okey, "");
                                            if (jamm_oname != "") {
                                                jamm_oid = asset_get_index(jamm_oname);
                                                if (jamm_oid > -1 && asset_get_type(jamm_oname) == asset_object && object_exists(jamm_oid)) {
                                                    UpgradeData.object[jamm_ix,jamm_iy] = jamm_oid;
                                                } else {
                                                    UpgradeData.object[jamm_ix,jamm_iy] = -1;
                                                }
                                            } else {
                                                UpgradeData.object[jamm_ix,jamm_iy] = -1;
                                            }
                                        } else {
                                            UpgradeData.upg[jamm_ix,jamm_iy] = -1;
                                            UpgradeData.object[jamm_ix,jamm_iy] = -1;
                                        }
                                    }
                                } else if (jamm_ix >= RodData.jamm_base_count || jamm_iy >= 10) {
                                    UpgradeData.upg[jamm_ix,jamm_iy] = UpgradeData.jamm_default_upg[jamm_ix,jamm_iy];
                                    UpgradeData.object[jamm_ix,jamm_iy] = -1;
                                } else if (UpgradeData.upg[jamm_ix,jamm_iy] >= UpgradeData.jamm_base_count) {
                                    UpgradeData.upg[jamm_ix,jamm_iy] = -1;
                                    UpgradeData.object[jamm_ix,jamm_iy] = -1;
                                }
                            }
                        }
                    }
                """);
        }
        if (hasBoat && hasBoatData)
        {
            sb.AppendLine("""
                    if (ini_key_exists("JAMM_Items", "eq:boat") && instance_exists(BoatData)) {
                        jamm_eq = ini_read_string("JAMM_Items", "eq:boat", "");
                        jamm_bi = -1;
                        if (jamm_eq != "") jamm_bi = jamm_item_find("boat", jamm_eq);
                        if (jamm_bi > 0 && BoatData.object[jamm_bi] > -1 && object_exists(BoatData.object[jamm_bi])) {
                            if (!instance_exists(Boat) || Boat.boat_index != jamm_bi) {
                                with (Boat) { instance_destroy(); }
                                jamm_b = instance_create(0, 0, BoatData.object[jamm_bi]);
                                jamm_b.boat_index = jamm_bi;
                            }
                        } else {
                            with (Boat) { instance_destroy(); }
                        }
                    }
                """);
        }
        if (hasRod && hasRodData)
        {
            sb.AppendLine("""
                    if (ini_key_exists("JAMM_Items", "eq:rod") && instance_exists(RodData) && instance_exists(Rod)) {
                        jamm_eq = ini_read_string("JAMM_Items", "eq:rod", "");
                        jamm_ri = -1;
                        if (jamm_eq != "") jamm_ri = jamm_item_find("rod", jamm_eq);
                        if (jamm_ri < 0) jamm_ri = 0;
                        Rod.rod_id = jamm_ri;
                        with (Rod) { event_perform(ev_other, ev_user0); }
                        jamm_rod_refreshed = 1;
                    }
                """);
        }
        if (hasGirl && hasGearData)
        {
            // gear_equip is resolved at runtime so the script compiles even in archives without it.
            string equipCall = hasGearEquip
                ? """
                    var jamm_ge = asset_get_index("gear_equip");
                    if (jamm_ge > -1) { with (jamm_g) { script_execute(jamm_ge); } }
                    else { jamm_g.state = 2; Girl.gear = jamm_g; Girl.gear_id = jamm_hi; with (Girl) { event_perform(ev_other, ev_user0); } }
                    """
                : """
                    jamm_g.state = 2;
                    Girl.gear = jamm_g;
                    Girl.gear_id = jamm_hi;
                    with (Girl) { event_perform(ev_other, ev_user0); }
                    """ + (hasRod ? "\n                    with (Rod) { event_perform(ev_other, ev_user0); }\n                    jamm_rod_refreshed = 1;" : "");
            string clearRefresh = """
                    with (Girl) { event_perform(ev_other, ev_user0); }
                    """ + (hasRod ? "\n                    with (Rod) { event_perform(ev_other, ev_user0); }\n                    jamm_rod_refreshed = 1;" : "");
            sb.AppendLine($$"""
                    if (ini_key_exists("JAMM_Items", "eq:hat") && instance_exists(GearData) && instance_exists(Girl)) {
                        jamm_eq = ini_read_string("JAMM_Items", "eq:hat", "");
                        jamm_hi = -1;
                        if (jamm_eq != "") jamm_hi = jamm_item_find("hat", jamm_eq);
                        if (Girl.gear > -1 && instance_exists(Girl.gear)) {
                            Girl.gear.state = 0;
                            with (Girl.gear) { instance_destroy(); }
                        }
                        Girl.gear = -1;
                        Girl.gear_id = -1;
                        if (jamm_hi > -1 && GearData.gear[jamm_hi] > -1 && object_exists(GearData.gear[jamm_hi])) {
                            jamm_g = instance_create(Girl.x, Girl.y, GearData.gear[jamm_hi]);
                            jamm_g.gear_id = jamm_hi;
                            {{equipCall}}
                        } else {
                            {{clearRefresh}}
                        }
                    }
                """);
        }
        sb.AppendLine("} else {");
        if (hasBoatData) sb.AppendLine(LegacyResetBlock("BoatData", "nItems", "unlocked"));
        if (hasRodData) sb.AppendLine(LegacyResetBlock("RodData", "nItems", "unlocked"));
        if (hasLureData) sb.AppendLine(LegacyResetBlock("LureData", "nItems", "unlocked"));
        if (hasToolData)
        {
            sb.AppendLine(LegacyResetBlock("ToolData", "nItems", "unlocked"));
            sb.AppendLine("""
                    if (instance_exists(ToolData)) {
                        for (jamm_i = ToolData.jamm_base_count; jamm_i < ToolData.nItems; jamm_i += 1) {
                            if (instance_exists(ToolData.item[jamm_i])) {
                                if (ToolData.unlocked[jamm_i] > 0) ToolData.item[jamm_i].active = 1;
                                else ToolData.item[jamm_i].active = 0;
                            }
                        }
                    }
                """);
        }
        if (hasGearData) sb.AppendLine(LegacyResetBlock("GearData", "n_gear", "unlocked"));
        if (hasUpgradeData) sb.AppendLine(LegacyResetBlock("UpgradeData", "nItems", "unlocks"));
        if (hasRodData && hasUpgradeData)
        {
            sb.AppendLine("""
                    if (instance_exists(RodData) && instance_exists(UpgradeData)) {
                        for (jamm_ix = 0; jamm_ix < RodData.jamm_base_count && jamm_ix < array_height_2d(UpgradeData.upg); jamm_ix += 1) {
                            for (jamm_iy = 0; jamm_iy < array_length_2d(UpgradeData.upg, jamm_ix); jamm_iy += 1) {
                                if (jamm_iy >= 10) {
                                    UpgradeData.upg[jamm_ix,jamm_iy] = UpgradeData.jamm_default_upg[jamm_ix,jamm_iy];
                                    UpgradeData.object[jamm_ix,jamm_iy] = -1;
                                } else if (UpgradeData.upg[jamm_ix,jamm_iy] >= UpgradeData.jamm_base_count) {
                                    UpgradeData.upg[jamm_ix,jamm_iy] = -1;
                                    UpgradeData.object[jamm_ix,jamm_iy] = -1;
                                }
                            }
                        }
                        for (jamm_ix = RodData.jamm_base_count; jamm_ix < RodData.nItems && jamm_ix < array_height_2d(UpgradeData.upg); jamm_ix += 1) {
                            for (jamm_iy = 0; jamm_iy < array_length_2d(UpgradeData.upg, jamm_ix); jamm_iy += 1) {
                                UpgradeData.upg[jamm_ix,jamm_iy] = UpgradeData.jamm_default_upg[jamm_ix,jamm_iy];
                                UpgradeData.object[jamm_ix,jamm_iy] = -1;
                            }
                        }
                    }
                """);
        }
        sb.AppendLine("}");
        if (hasRod)
        {
            sb.AppendLine("if (!jamm_rod_refreshed && instance_exists(Rod)) {");
            sb.AppendLine("    with (Rod) { event_perform(ev_other, ev_user0); }");
            sb.AppendLine("}");
        }
        sb.AppendLine("if (instance_exists(SaveManager)) SaveManager.jamm_items_loaded = 1;");
        sb.Append("return 1;");
        return sb.ToString();

        static string LoadUnlockBlock(string obj, string countField, string type, string unlockField) => $$"""
                if (instance_exists({{obj}})) {
                    for (jamm_i = 0; jamm_i < {{obj}}.{{countField}}; jamm_i += 1) {
                        jamm_k = "u:{{type}}:" + {{obj}}.jamm_key[jamm_i];
                        jamm_uval = {{obj}}.jamm_default_unlocked[jamm_i];
                        if (ini_key_exists("JAMM_Items", jamm_k)) {
                            jamm_uval = ini_read_real("JAMM_Items", jamm_k, {{obj}}.jamm_default_unlocked[jamm_i]);
                        }
                        if (jamm_i < {{obj}}.jamm_base_count && {{obj}}.{{unlockField}}[jamm_i] > jamm_uval) {
                            jamm_uval = {{obj}}.{{unlockField}}[jamm_i];
                        }
                        {{obj}}.{{unlockField}}[jamm_i] = jamm_uval;
                    }
                }
            """;

        static string LegacyResetBlock(string obj, string countField, string unlockField) => $$"""
                if (instance_exists({{obj}})) {
                    for (jamm_i = {{obj}}.jamm_base_count; jamm_i < {{obj}}.{{countField}}; jamm_i += 1) {
                        {{obj}}.{{unlockField}}[jamm_i] = {{obj}}.jamm_default_unlocked[jamm_i];
                    }
                }
            """;
    }
}
