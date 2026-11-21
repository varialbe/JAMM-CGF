using System.Text;
using Jamm.Core.Merging;
using Jamm.Core.Patching;
using UndertaleModLib;
using UndertaleModLib.Models;

namespace Jamm.Core.Building;

/// <summary>
/// Injects <c>jamm_fish_register</c>, display-name resolution for mod fish, and a standalone
/// <c>jamm_api_init</c> bootstrap when <c>gml_Script_jamm_api_init</c> is not already provided.
/// </summary>
internal static class FishRuntime
{
    internal const string RegisterScript = "jamm_fish_register";
    internal const string RegisterCode = "gml_Script_" + RegisterScript;
    internal const string ApiInitScript = "jamm_api_init";
    internal const string ApiInitCode = "gml_Script_" + ApiInitScript;
    internal const string SaveManagerStep = "gml_Object_SaveManager_Step_0";
    internal const string FishObjectToName = "gml_Script_fish_object_to_name";

    internal static BuildPlan PreparePlan(BuildPlan plan, IReadOnlyDictionary<string, CodeText> baseline, UndertaleData? original = null)
    {
        if (!UsesFishApi(plan, baseline))
            return plan;

        var assets = plan.Assets.ToList();
        var decisions = plan.Decisions.ToList();

        bool hasCatalogData = original?.GameObjects.ByName("CatalogData") is not null
            || HasCode(assets, baseline, CatalogMerge.Create);
        bool hasSaveManager = original?.GameObjects.ByName("SaveManager") is not null
            || HasCode(assets, baseline, SaveManagerStep);

        Put(assets, baseline, RegisterCode, BuildFishRegisterGml(hasCatalogData));

        if (hasCatalogData && HasCode(assets, baseline, FishObjectToName))
        {
            string current = Current(assets, baseline, FishObjectToName);
            if (!current.Contains("jamm_fish_names", StringComparison.Ordinal))
            {
                Put(assets, baseline, FishObjectToName,
                    """
                    var jamm_obj, jamm_found;
                    jamm_obj = argument0;
                    jamm_found = "";
                    if (instance_exists(CatalogData))
                    {
                        with (CatalogData)
                        {
                            if (variable_instance_exists(id, "jamm_fish_names"))
                            {
                                if (ds_map_exists(jamm_fish_names, jamm_obj))
                                {
                                    jamm_found = ds_map_find_value(jamm_fish_names, jamm_obj);
                                }
                            }
                        }
                    }
                    if (jamm_found != "") return jamm_found;
                    """ + "\n" + current);
            }
        }

        if (hasCatalogData && HasCode(assets, baseline, CatalogMerge.Create))
        {
            string create = Current(assets, baseline, CatalogMerge.Create);
            if (!create.Contains("jamm_catalog_legacy", StringComparison.Ordinal))
                Put(assets, baseline, CatalogMerge.Create, AppendLegacyMapMarker(create));
        }

        bool hasApiInit = HasCode(assets, baseline, ApiInitCode);
        if (!hasApiInit)
        {
            var initScripts = DiscoverInitScripts(assets, baseline);
            if (initScripts.Count > 0)
            {
                var initBody = new StringBuilder();
                foreach (string script in initScripts)
                    initBody.Append(script).AppendLine("();");
                initBody.AppendLine("return 0;");
                Put(assets, baseline, ApiInitCode, initBody.ToString());

                if (hasSaveManager && HasCode(assets, baseline, SaveManagerStep))
                {
                    string step = Current(assets, baseline, SaveManagerStep);
                    if (!step.Contains(ApiInitScript, StringComparison.Ordinal))
                    {
                        string guard = hasCatalogData
                            ? """
                              if (instance_exists(CatalogData))
                              {
                                  var jamm_run_init;
                                  jamm_run_init = 0;
                                  with (CatalogData)
                                  {
                                      if (!variable_instance_exists(id, "jamm_api_done"))
                                      {
                                          jamm_api_done = 1;
                                          jamm_run_init = 1;
                                      }
                                  }
                                  if (jamm_run_init)
                                  {
                                      jamm_api_init();
                                  }
                              }
                              """
                            : """
                              if (!variable_instance_exists(id, "jamm_api_done"))
                              {
                                  jamm_api_done = 1;
                                  jamm_api_init();
                              }
                              """;
                        Put(assets, baseline, SaveManagerStep, guard + "\n" + step);
                    }
                }
                decisions.Add($"Fish API: injected jamm_fish_register and standalone jamm_api_init ({initScripts.Count} init script(s)).");
            }
            else
            {
                decisions.Add("Fish API: injected jamm_fish_register.");
            }
        }
        else
        {
            decisions.Add("Fish API: injected jamm_fish_register (using existing jamm_api_init).");
        }

        return plan with { Assets = assets, Decisions = decisions };
    }

    internal static void Inject(UndertaleData data, Dictionary<string, string> recompile,
        Dictionary<string, string> generated, long fishValueCap)
    {
        if (!recompile.ContainsKey(RegisterCode) && data.Scripts.ByName(RegisterScript) is null)
            return;

        bool hasCatalogData = data.GameObjects.ByName("CatalogData") is not null;
        if (fishValueCap > 0)
            generated[RegisterCode] = recompile[RegisterCode] = BuildFishRegisterGml(hasCatalogData, fishValueCap);
        else if (!recompile.ContainsKey(RegisterCode))
            recompile[RegisterCode] = BuildFishRegisterGml(hasCatalogData);

        EnsureScript(data, RegisterScript, RegisterCode);

        if (recompile.ContainsKey(ApiInitCode))
            EnsureScript(data, ApiInitScript, ApiInitCode);

        if (hasCatalogData && data.Scripts.ByName("jamm_catalog_register") is null && !recompile.ContainsKey("gml_Script_jamm_catalog_register"))
        {
            foreach (var (script, text) in CatalogRuntime.Scripts)
            {
                string codeName = "gml_Script_" + script;
                EnsureScript(data, script, codeName);
                recompile[codeName] = text;
            }
        }
    }

    private static bool UsesFishApi(BuildPlan plan, IReadOnlyDictionary<string, CodeText> baseline)
    {
        HashSet<string> removedCode = plan.Assets
            .Where(a => a.Family == "code" && a.Patch is null)
            .Select(a => a.Name)
            .ToHashSet(StringComparer.Ordinal);

        foreach (var asset in plan.Assets.Where(a => a.Family == "code" && a.Patch is not null))
        {
            if (asset.Name == RegisterCode) return true;
            if (asset.Gml is not null && GmlReferences.Identifiers(asset.Gml).Contains(RegisterScript, StringComparer.Ordinal))
                return true;
        }

        foreach (var (name, code) in baseline)
        {
            if (removedCode.Contains(name)) continue;
            if (plan.Assets.Any(a => a.Family == "code" && a.Name == name)) continue;
            if (name == RegisterCode) return true;
            if (GmlReferences.Identifiers(code.Text).Contains(RegisterScript, StringComparer.Ordinal))
                return true;
        }

        return false;
    }

    private static List<string> DiscoverInitScripts(IReadOnlyList<PlannedAsset> assets, IReadOnlyDictionary<string, CodeText> baseline)
    {
        HashSet<string> removed = assets
            .Where(a => a.Family == "code" && a.Patch is null)
            .Select(a => a.Name)
            .ToHashSet(StringComparer.Ordinal);

        var ordered = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var asset in assets
            .Where(a => a.Family == "code" && a.Patch is not null && a.Name.StartsWith("gml_Script_jamm_init_", StringComparison.Ordinal))
            .OrderBy(a => a.Source)
            .ThenBy(a => a.Name, StringComparer.Ordinal))
        {
            string script = asset.Name["gml_Script_".Length..];
            if (seen.Add(script)) ordered.Add(script);
        }

        foreach (string name in baseline.Keys
            .Where(k => k.StartsWith("gml_Script_jamm_init_", StringComparison.Ordinal) && !removed.Contains(k))
            .OrderBy(k => k, StringComparer.Ordinal))
        {
            string script = name["gml_Script_".Length..];
            if (seen.Add(script)) ordered.Add(script);
        }

        return ordered;
    }

    private static string BuildFishRegisterGml(bool hasCatalogData, long fishValueCap = 0)
    {
        string saleValue = fishValueCap > 0 ? FormattableString.Invariant($"min(jamm_sale_value, {fishValueCap})") : "jamm_sale_value";
        string spawnBody = $$"""
            var jamm_key, jamm_obj, jamm_tab, jamm_name, jamm_desc;
            var jamm_x1, jamm_x2, jamm_y1, jamm_y2, jamm_count;
            var jamm_have, jamm_need, jamm_i, jamm_sx, jamm_sy, jamm_inst;
            jamm_key = argument0;
            jamm_obj = argument1;
            jamm_tab = argument2;
            jamm_name = argument3;
            jamm_desc = argument4;
            jamm_x1 = min(argument5, argument6);
            jamm_x2 = max(argument5, argument6);
            jamm_y1 = min(argument7, argument8);
            jamm_y2 = max(argument7, argument8);
            jamm_count = argument9;
            if (!is_string(jamm_key) || jamm_key == "" || !object_exists(jamm_obj)) return -1;
            if (jamm_tab < 0 || jamm_tab > 3 || floor(jamm_tab) != jamm_tab) return -1;
            if (jamm_count < 0) return -1;
            jamm_have = instance_number(jamm_obj);
            jamm_need = max(0, floor(jamm_count) - jamm_have);
            for (jamm_i = 0; jamm_i < jamm_need; jamm_i += 1)
            {
                jamm_sx = jamm_x1 + random(max(0, jamm_x2 - jamm_x1));
                jamm_sy = jamm_y1 + random(max(0, jamm_y2 - jamm_y1));
                jamm_inst = instance_create(jamm_sx, jamm_sy, jamm_obj);
                // instance_create runs the fish's Create event before returning. A fish or
                // another Create hook can destroy it immediately, so only initialize spawn
                // metadata while the returned instance still exists.
                if (instance_exists(jamm_inst))
                {
                    jamm_inst.start_x = jamm_sx;
                    jamm_inst.start_y = jamm_sy;
                    jamm_inst.TRUE_START_X = jamm_sx;
                    jamm_inst.TRUE_START_Y = jamm_sy;
                    jamm_inst.nothungry_x = jamm_sx;
                    jamm_inst.nothungry_y = jamm_sy;
                }
            }
            if (argument_count > 16)
            {
                var jamm_sale_value;
                jamm_sale_value = argument[16];
                if (is_real(jamm_sale_value))
                {
                    with (jamm_obj) BASE_VALUE = {{saleValue}};
                }
            }
            """;

        if (!hasCatalogData)
            return spawnBody + "\nreturn 0;\n";

        return spawnBody + "\n" + """
            if (!instance_exists(CatalogData)) return -1;
            with (CatalogData)
            {
                if (!variable_instance_exists(id, "jamm_fish_names"))
                {
                    jamm_fish_names = ds_map_create();
                }
                ds_map_replace(jamm_fish_names, jamm_obj, jamm_name);
            }
            var jamm_dc, jamm_ds, jamm_dh, jamm_dn, jamm_de, jamm_ex;
            jamm_dc = jamm_desc;
            jamm_ds = jamm_desc;
            jamm_dh = "Little is known about this fish.";
            jamm_dn = jamm_desc;
            jamm_de = jamm_desc;
            jamm_ex = 0;
            if (argument_count > 10) jamm_dc = argument10;
            if (argument_count > 11) jamm_ds = argument11;
            if (argument_count > 12) jamm_dh = argument12;
            if (argument_count > 13) jamm_dn = argument13;
            if (argument_count > 14) jamm_de = argument14;
            if (argument_count > 15) jamm_ex = argument15;
            var jamm_slot;
            jamm_slot = jamm_catalog_register(jamm_tab, jamm_obj, jamm_name, jamm_desc, jamm_dc, jamm_ds, jamm_dh, jamm_dn, jamm_de, jamm_ex);
            if (jamm_slot >= 0)
            {
                with (jamm_obj)
                {
                    catalog_index = jamm_slot;
                    exotic = jamm_ex;
                }
            }
            return jamm_slot;
            """;
    }

    private static string AppendLegacyMapMarker(string create)
    {
        var sb = new StringBuilder(create.TrimEnd());
        sb.AppendLine().AppendLine("jamm_catalog_notice = 0;");
        var tokens = GmlReferences.Tokens(create);
        var seen = new HashSet<(int Area, int Slot)>();
        for (int i = 0; i + 7 < tokens.Count; i++)
        {
            if (tokens[i].Value != "object" || tokens[i + 1].Value != "[" || tokens[i + 3].Value != "," || tokens[i + 5].Value != "]" || tokens[i + 6].Value != "=")
                continue;
            if (int.TryParse(tokens[i + 2].Value, out int a) && int.TryParse(tokens[i + 4].Value, out int s) && seen.Add((a, s)))
                sb.AppendLine($"jamm_catalog_legacy[{a}, {s}] = {s};");
        }
        return sb.ToString();
    }

    private static void EnsureScript(UndertaleData data, string scriptName, string codeName)
    {
        var code = data.Code.FirstOrDefault(c => c.Name.Content == codeName) ?? UndertaleCode.CreateEmptyEntry(data, codeName);
        var existing = data.Scripts.FirstOrDefault(s => s.Name.Content == scriptName);
        if (existing is null)
            data.Scripts.Add(new UndertaleScript { Name = data.Strings.MakeString(scriptName), Code = code });
        else
            existing.Code = code;
    }

    private static bool HasCode(List<PlannedAsset> assets, IReadOnlyDictionary<string, CodeText> baseline, string name)
    {
        int index = assets.FindIndex(a => a.Family == "code" && a.Name == name);
        if (index >= 0) return assets[index].Patch is not null;
        return baseline.ContainsKey(name);
    }

    private static string Current(List<PlannedAsset> assets, IReadOnlyDictionary<string, CodeText> baseline, string name) =>
        assets.FirstOrDefault(a => a.Family == "code" && a.Name == name)?.Gml
        ?? baseline.GetValueOrDefault(name)?.Text
        ?? "";

    private static void Put(List<PlannedAsset> assets, IReadOnlyDictionary<string, CodeText> baseline, string name, string text)
    {
        int index = assets.FindIndex(a => a.Family == "code" && a.Name == name);
        if (index >= 0) { assets[index] = assets[index] with { Gml = text }; return; }
        var before = baseline.GetValueOrDefault(name);
        var after = before is null ? new CodeText(text, "gml", 0, 0, null, []) : before with { Text = text };
        var snapshot = AssetFields.Json(new { });
        assets.Add(new("code", name, 0, new AssetPatch(name, before is null ? null : snapshot, snapshot, [], before, after), snapshot, text));
    }
}
