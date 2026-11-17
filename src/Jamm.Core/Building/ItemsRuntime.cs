using Jamm.Core.Merging;
using Jamm.Core.Patching;
using UndertaleModLib;
using UndertaleModLib.Models;

namespace Jamm.Core.Building;

internal static class ItemsRuntime
{
    private static readonly HashSet<string> RegistrationFunctions = new(StringComparer.Ordinal)
    {
        "jamm_boat_register",
        "jamm_rod_register",
        "jamm_lure_register",
        "jamm_tool_register",
        "jamm_hat_register"
    };

    internal static BuildPlan PreparePlan(
        BuildPlan plan,
        IReadOnlyDictionary<string, CodeText> baseline,
        IReadOnlyDictionary<string, string[]>? baseNames = null)
    {
        var initScripts = DiscoverInitScripts(plan, baseline);
        if (initScripts.Length == 0 && !HasRegistrationCall(plan))
            return plan;

        var assets = plan.Assets.ToList();
        var decisions = plan.Decisions.ToList();
        var (availableObjects, availableScripts) = ResolveAvailableAssets(assets, baseNames);

        ItemsRuntimePatches.Apply(assets, baseline, availableObjects);

        string apiInit = ItemsRuntimeScripts.BuildApiInit(initScripts, availableObjects);
        Put(ItemsRuntimeScripts.ApiInitCode, apiInit);

        var scripts = ItemsRuntimeScripts.BuildScripts(availableObjects, availableScripts);
        foreach (var (script, body) in scripts)
        {
            string codeName = "gml_Script_" + script;
            int idx = assets.FindIndex(a => a.Family == "code" && a.Name == codeName);
            if (idx >= 0)
            {
                assets[idx] = assets[idx] with { Gml = body };
            }
            else if (body != ItemsRuntimeScripts.DefaultScripts[script])
            {
                Put(codeName, body);
            }
        }

        string initList = initScripts.Length == 0 ? "none" : string.Join(", ", initScripts);
        decisions.Add($"Items API: injected runtime ({initScripts.Length} init script(s): {initList}) and [JAMM_Items] persistence.");
        return plan with { Assets = assets, Decisions = decisions };

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

    internal static void Inject(UndertaleData data, Dictionary<string, string> recompile)
    {
        if (!recompile.TryGetValue(ItemsRuntimeScripts.ApiInitCode, out var apiInit)) return;
        EnsureScript(ItemsRuntimeScripts.ApiInitName, apiInit);
        foreach (var (script, defaultBody) in ItemsRuntimeScripts.DefaultScripts)
        {
            string codeName = "gml_Script_" + script;
            string body = recompile.TryGetValue(codeName, out var plannedBody) ? plannedBody : defaultBody;
            EnsureScript(script, body);
        }

        void EnsureScript(string script, string body)
        {
            string codeName = "gml_Script_" + script;
            var code = data.Code.ByName(codeName) ?? UndertaleCode.CreateEmptyEntry(data, codeName);
            var existing = data.Scripts.ByName(script);
            if (existing is null)
                data.Scripts.Add(new UndertaleScript { Name = data.Strings.MakeString(script), Code = code });
            else
                existing.Code = code;
            recompile[codeName] = body;
        }
    }

    internal static string[] DiscoverInitScripts(BuildPlan plan, IReadOnlyDictionary<string, CodeText> baseline)
    {
        HashSet<string> removed = new(StringComparer.Ordinal);
        foreach (var asset in plan.Assets.Where(a => a.Patch is null))
        {
            if (asset.Family == "scripts" && asset.Name.StartsWith("jamm_init_", StringComparison.Ordinal))
                removed.Add(asset.Name);
            else if (asset.Family == "code" && asset.Name.StartsWith("gml_Script_jamm_init_", StringComparison.Ordinal))
                removed.Add(asset.Name["gml_Script_".Length..]);
        }

        Dictionary<string, int> sources = new(StringComparer.Ordinal);
        foreach (string key in baseline.Keys)
        {
            if (!key.StartsWith("gml_Script_jamm_init_", StringComparison.Ordinal)) continue;
            string script = key["gml_Script_".Length..];
            if (!removed.Contains(script)) sources[script] = -1;
        }
        foreach (var asset in plan.Assets.Where(a => a.Family == "scripts" && a.Patch is not null
            && a.Name.StartsWith("jamm_init_", StringComparison.Ordinal)))
        {
            if (!removed.Contains(asset.Name)) sources[asset.Name] = asset.Source;
        }
        foreach (var asset in plan.Assets.Where(a => a.Family == "code" && a.Patch is not null
            && a.Name.StartsWith("gml_Script_jamm_init_", StringComparison.Ordinal)))
        {
            string script = asset.Name["gml_Script_".Length..];
            if (!removed.Contains(script)) sources[script] = asset.Source;
        }

        return sources
            .OrderBy(p => p.Value)
            .ThenBy(p => p.Key, StringComparer.Ordinal)
            .Select(p => p.Key)
            .ToArray();
    }

    private static bool HasRegistrationCall(BuildPlan plan)
    {
        foreach (var asset in plan.Assets)
        {
            if (asset.Family != "code" || asset.Patch is null || asset.Gml is null) continue;
            if (asset.Name.StartsWith("gml_Script_jamm_", StringComparison.Ordinal)
                && !asset.Name.StartsWith("gml_Script_jamm_init_", StringComparison.Ordinal))
                continue;
            var tokens = GmlReferences.Tokens(asset.Gml);
            for (int i = 0; i + 1 < tokens.Count; i++)
            {
                if (tokens[i].Kind == 'i' && RegistrationFunctions.Contains(tokens[i].Value)
                    && tokens[i + 1].Value == "(" && (i == 0 || tokens[i - 1].Value != "."))
                    return true;
            }
        }
        return false;
    }

    private static (IReadOnlySet<string>? Objects, IReadOnlySet<string>? Scripts) ResolveAvailableAssets(
        IReadOnlyList<PlannedAsset> assets,
        IReadOnlyDictionary<string, string[]>? baseNames)
    {
        if (baseNames is null) return (null, null);
        return (Collect("objects"), Collect("scripts"));

        HashSet<string> Collect(string family)
        {
            var set = baseNames.TryGetValue(family, out var names)
                ? new HashSet<string>(names, StringComparer.Ordinal)
                : new HashSet<string>(StringComparer.Ordinal);
            foreach (var asset in assets.Where(a => a.Family == family))
            {
                if (asset.Patch is null) set.Remove(asset.Name);
                else set.Add(asset.Name);
            }
            return set;
        }
    }
}
