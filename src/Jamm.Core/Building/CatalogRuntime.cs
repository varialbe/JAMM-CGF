using System.Text;
using Jamm.Core.Merging;
using UndertaleModLib;
using UndertaleModLib.Models;

namespace Jamm.Core.Building;

internal static class CatalogRuntime
{
    internal static readonly IReadOnlyDictionary<string, string> Scripts = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["jamm_catalog_find"] = """
            var jamm_a, jamm_s;
            for (jamm_a = 0; jamm_a < array_height_2d(CatalogData.object); jamm_a += 1) {
                for (jamm_s = 0; jamm_s < array_length_2d(CatalogData.object, jamm_a); jamm_s += 1) {
                    if (CatalogData.object[jamm_a,jamm_s] == argument0) return jamm_s;
                }
            }
            return -1;
            """,
        ["jamm_catalog_area"] = """
            var jamm_a, jamm_s;
            for (jamm_a = 0; jamm_a < array_height_2d(CatalogData.object); jamm_a += 1) {
                for (jamm_s = 0; jamm_s < array_length_2d(CatalogData.object, jamm_a); jamm_s += 1) {
                    if (CatalogData.object[jamm_a,jamm_s] == argument0) return jamm_a;
                }
            }
            return -1;
            """,
        ["jamm_catalog_register"] = """
            var jamm_a, jamm_s, jamm_existing, jamm_fish, jamm_label, jamm_description;
            jamm_a = argument0; jamm_fish = argument1; jamm_label = argument2; jamm_description = argument3;
            if (jamm_a < 0 || jamm_a > 3 || floor(jamm_a) != jamm_a || !object_exists(jamm_fish)) return -1;
            jamm_existing = jamm_catalog_find(jamm_fish);
            if (jamm_existing >= 0) {
                if (jamm_catalog_area(jamm_fish) != jamm_a) return -1;
                return jamm_existing;
            }
            jamm_s = array_length_2d(CatalogData.object, jamm_a);
            CatalogData.object[jamm_a,jamm_s] = jamm_fish;
            CatalogData.name[jamm_a,jamm_s] = jamm_label;
            CatalogData.map[jamm_a,jamm_s] = jamm_s;
            CatalogData.desc[jamm_a,jamm_s] = jamm_description;
            CatalogData.desc_caught[jamm_a,jamm_s] = jamm_description;
            CatalogData.desc_seen[jamm_a,jamm_s] = jamm_description;
            CatalogData.desc_hidden[jamm_a,jamm_s] = "Little is known about this fish.";
            CatalogData.desc_n[jamm_a,jamm_s] = jamm_description;
            CatalogData.desc_e[jamm_a,jamm_s] = jamm_description;
            CatalogData.exotic[jamm_a,jamm_s] = 0;
            if (argument_count > 4) CatalogData.desc_caught[jamm_a,jamm_s] = argument4;
            if (argument_count > 5) CatalogData.desc_seen[jamm_a,jamm_s] = argument5;
            if (argument_count > 6) CatalogData.desc_hidden[jamm_a,jamm_s] = argument6;
            if (argument_count > 7) CatalogData.desc_n[jamm_a,jamm_s] = argument7;
            if (argument_count > 8) CatalogData.desc_e[jamm_a,jamm_s] = argument8;
            if (argument_count > 9) CatalogData.exotic[jamm_a,jamm_s] = argument9;
            CatalogData.weight[jamm_a,jamm_s] = 1;
            CatalogData.life[jamm_a,jamm_s] = -1;
            CatalogData.count[jamm_a,jamm_s] = -1;
            CatalogData.hide[jamm_a,jamm_s] = 0;
            CatalogData.max_pop[jamm_a,jamm_s] = instance_number(jamm_fish);
            CatalogData.pop[jamm_a,jamm_s] = 0;
            CatalogData.live[jamm_a,jamm_s] = 0;
            CatalogData.extinct[jamm_a,jamm_s] = 0;
            CatalogData.jamm_catalog_legacy[jamm_a,jamm_s] = -1;
            CatalogData.catalog_id_updated = 0;
            return jamm_s;
            """,
        ["jamm_catalog_read"] = """
            var jamm_prefix, jamm_a, jamm_s, jamm_fallback, jamm_key, jamm_legacy;
            jamm_prefix = argument0; jamm_a = argument1; jamm_s = argument2; jamm_fallback = argument3;
            jamm_key = jamm_prefix + ":" + object_get_name(CatalogData.object[jamm_a,jamm_s]);
            if (ini_key_exists("JAMM_Catalog", jamm_key)) return ini_read_real("JAMM_Catalog", jamm_key, jamm_fallback);
            if (ini_section_exists("JAMM_Catalog")) return jamm_fallback;
            jamm_legacy = CatalogData.jamm_catalog_legacy[jamm_a,jamm_s];
            if (jamm_legacy >= 0) return ini_read_real("Catalog", jamm_prefix + string(jamm_a) + string(jamm_legacy), jamm_fallback);
            if (!CatalogData.jamm_catalog_notice && ini_section_exists("Catalog")) {
                CatalogData.jamm_catalog_notice = 1;
                show_message("JAMM: Legacy mod catalog progress cannot be identified safely. It has been kept in the save, but new mod catalog entries start undiscovered. Unambiguous base progress is retained. Keep a backup of this save for the original mod profile.");
            }
            return jamm_fallback;
            """
    };

    internal static void Inject(UndertaleData data, Dictionary<string, string> recompile)
    {
        if (!recompile.TryGetValue(CatalogMerge.Create, out var create) || !create.Contains("jamm_catalog_legacy", StringComparison.Ordinal)) return;
        foreach (var (script, text) in Scripts)
        {
            string name = "gml_Script_" + script;
            var code = data.Code.FirstOrDefault(c => c.Name.Content == name) ?? UndertaleCode.CreateEmptyEntry(data, name);
            var existing = data.Scripts.FirstOrDefault(s => s.Name.Content == script);
            if (existing is null) data.Scripts.Add(new UndertaleScript { Name = data.Strings.MakeString(script), Code = code });
            else existing.Code = code;
            recompile[name] = text;
        }
    }

    internal static BuildPlan PreparePlan(BuildPlan plan, IReadOnlyDictionary<string, Jamm.Core.Patching.CodeText> baseline)
    {
        if (!plan.Assets.Any(a => a.Name == CatalogMerge.Create && a.Gml?.Contains("jamm_catalog_legacy", StringComparison.Ordinal) == true)) return plan;
        var assets = plan.Assets.ToList();
        foreach (var (name, before) in baseline.Where(p => p.Key.StartsWith("gml_Object_SaveManager_", StringComparison.Ordinal)))
        {
            int index = assets.FindIndex(a => a.Family == "code" && a.Name == name);
            string text = index >= 0 ? assets[index].Gml ?? "" : before.Text;
            string rewritten = RewriteSave(text);
            if (rewritten == text) continue;
            if (index >= 0) assets[index] = assets[index] with { Gml = rewritten };
            else
            {
                var snapshot = Jamm.Core.Patching.AssetFields.Json(new { });
                var patch = new Jamm.Core.Patching.AssetPatch(name, snapshot, snapshot, [], before, before with { Text = rewritten });
                assets.Add(new("code", name, 0, patch, snapshot, rewritten));
            }
        }
        foreach (var (script, body) in Scripts)
        {
            int index = assets.FindIndex(a => a.Family == "code" && a.Name == "gml_Script_" + script);
            if (index >= 0) assets[index] = assets[index] with { Gml = body };
        }
        return plan with { Assets = assets };
    }

    internal static string RewriteSave(string text)
    {
        var t = GmlReferences.Tokens(text);
        List<(int Start, int Length, string Value)> edits = [];
        for (int i = 0; i + 4 < t.Count; i++)
        {
            if (t[i].Value is not ("ini_read_real" or "ini_write_real") || t[i + 1].Value != "(" || t[i + 2].Value != "Catalog") continue;
            int depth = 1, end = i + 2;
            for (; end < t.Count; end++) { if (t[end].Value == "(") depth++; if (t[end].Value == ")" && IfClosing(ref depth)) break; }
            if (end == t.Count) throw new InvalidDataException("Cannot adapt Catalog save call. Regenerate the archive's GML patch.");
            string call = text[t[i].Start..(t[end].Start + t[end].Length)];
            // The audited engine uses these exact expressions; reject unknown formats rather than guessing.
            string? prefix = new[] { "f", "li", "pop" }.FirstOrDefault(p => call.Contains("\"" + p + "\" + string(i) + string(j)", StringComparison.Ordinal));
            if (prefix is null) throw new InvalidDataException("Catalog save format is unsupported. Use the legacy 2023 save scripts or add an explicit save adapter.");
            string expression = "\"" + prefix + "\" + string(i) + string(j)";
            string updated = t[i].Value == "ini_write_real"
                ? call.Replace("\"Catalog\"", "\"JAMM_Catalog\"", StringComparison.Ordinal).Replace(expression, "\"" + prefix + ":\" + object_get_name(object[i, j])", StringComparison.Ordinal)
                : call.Replace("ini_read_real(\"Catalog\", " + expression + ", ", "jamm_catalog_read(\"" + prefix + "\", i, j, ", StringComparison.Ordinal);
            if (updated == call) throw new InvalidDataException("Catalog save call could not be adapted. Restore standard save scripts.");
            edits.Add((t[i].Start, t[end].Start + t[end].Length - t[i].Start, updated));
            i = end;
        }
        StringBuilder result = new(text);
        foreach (var edit in edits.OrderByDescending(e => e.Start)) result.Remove(edit.Start, edit.Length).Insert(edit.Start, edit.Value);
        return result.ToString();
    }
    private static bool IfClosing(ref int depth) { depth--; return depth == 0; }
}
