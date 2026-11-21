using System.Globalization;
using Jamm.Core.Patching;
using UndertaleModLib;
using UndertaleModLib.Models;

namespace Jamm.Core.Building;

internal static class ValueGuard
{
    private const string RuntimeScript = "jamm_guard_stat";
    private const string DynamicFishScript = "jamm_guard_fish_sale";
    private const string Appraisal = "gml_Script_fish_appraise";
    private const double Headroom = 10;
    private const double PeerRatio = 5;
    private const double Extreme = 1e9;
    private const double TrivialSpeed = 10_000;
    private const string Sale = "fish.sale";
    private const string Xp = "fish.xp";

    private sealed record Candidate(string Entry, GuardGml Gml, GuardGml.Span Span, string Stat, string Item,
        string Fingerprint, double? Literal, GuardGml.Write? Write = null, string? DynamicFishTarget = null)
    {
        internal string Identity => Stat + "/" + Item;
        internal string Display => Stat is Sale or Xp ? Item + (Stat == Xp ? " XP_VALUE" : "") : $"{Item} {Stat}";
        internal bool IsPrice => Stat.StartsWith("price.", StringComparison.Ordinal);
        internal bool IsSpeed => Stat is "rod.reel_speed" or "lure.sink_speed" or "lure.modified_reel_speed";
    }

    private sealed record Edit(int Start, int End, Func<string, string> Transform);

    internal static void Apply(UndertaleData data, UndertaleData vanilla, IReadOnlyDictionary<string, CodeText> vanillaCode,
        IReadOnlyDictionary<string, CodeText> layerCode, Dictionary<string, string> recompile, Dictionary<string, string> generated,
        BuildPlan plan, IReadOnlyList<ModPatch> patches, ValueGuardOptions options, List<string> decisions,
        string? baseMod = null)
    {
        if (options is { FishValueCap: 0, FairPlay: false }) return;
        var entryNames = data.Code.Select(c => c.Name.Content).ToHashSet(StringComparer.Ordinal);
        var live = layerCode.Where(p => entryNames.Contains(p.Key) && !recompile.ContainsKey(p.Key))
            .ToDictionary(p => p.Key, p => new GuardGml(p.Value.Text), StringComparer.Ordinal);
        foreach (var (name, text) in recompile) live[name] = new GuardGml(text);
        var baseline = vanillaCode.ToDictionary(p => p.Key, p => new GuardGml(p.Value.Text), StringComparer.Ordinal);
        var currentContext = new ValueGuardContext(data, live);
        var baseContext = new ValueGuardContext(vanilla, baseline);
        var vanillaValues = baseline.SelectMany(p => Read(p.Key, p.Value, baseContext)).ToArray();
        var baseValues = vanillaValues.GroupBy(x => (x.Entry, x.Identity, x.Fingerprint))
            .ToDictionary(g => g.Key, g => g.Count());
        var maxima = vanillaValues.Where(v => v.Literal is > 0)
            .GroupBy(v => v.Stat).ToDictionary(g => g.Key, g => g.Max(v => v.Literal!.Value));
        var paid = vanillaValues.Where(v => v.IsPrice && v.Literal is > 0)
            .GroupBy(v => v.Identity).ToDictionary(g => g.Key, g => g.Last().Literal!.Value);
        var selected = plan.Assets.Where(a => a.Family == "code").ToDictionary(a => a.Name, StringComparer.Ordinal);
        var sources = patches.Select(p => p.Changes.TryGetValue("code", out var code)
            ? code.Changed.Concat(code.Added).ToDictionary(a => a.Name, StringComparer.Ordinal) : []).ToArray();
        Dictionary<(int Mod, string Entry), HashSet<(string Identity, string Fingerprint)>> sourceWrites = [];
        var candidates = live.SelectMany(p => Read(p.Key, p.Value, currentContext))
            .Select(value => (Value: value, Changed: Changed(value)))
            .Select(c => (c.Value, c.Changed, Mod: c.Changed ? Source(c.Value) : "vanilla")).ToArray();
        var peers = candidates.Where(c => c.Changed && c.Value.Literal is > 0)
            .GroupBy(c => (c.Mod, c.Value.Stat))
            .ToDictionary(g => g.Key, g => g.GroupBy(c => c.Value.Item)
                .Select(item => (Item: item.Key, Max: item.Max(c => c.Value.Literal!.Value)))
                .OrderByDescending(item => item.Max).Take(2).ToArray());

        Dictionary<string, List<Edit>> edits = new(StringComparer.Ordinal);
        bool needsRuntime = false, needsFishRuntime = false;
        foreach (var (value, changed, mod) in candidates)
        {
            double? literal = value.Literal;
            int line = value.Gml.Tokens[value.Span.First].Line;
            string location = $"{value.Display} ({mod}, {value.Entry} line {line})";
            if (value.Stat == Sale)
            {
                if (options.FishValueCap == 0) continue;
                string cap = options.FishValueCap.ToString(CultureInfo.InvariantCulture);
                if (value.DynamicFishTarget is { } target)
                {
                    if (currentContext.FishObjects.Any() && changed && (literal is null || literal > options.FishValueCap)
                        && Wrap(value, expression => $"{DynamicFishScript}({target}, {expression}, {cap})"))
                    {
                        needsFishRuntime = true;
                        decisions.Add($"[CGF-VALUE-001] Runtime fish sale cap for {location}: maximum {Format(options.FishValueCap)} for fish targets");
                    }
                }
                else if (literal > options.FishValueCap)
                {
                    Replace(value, cap);
                    decisions.Add($"[CGF-VALUE-001] Capped fish sale values above {Format(options.FishValueCap)}: {value.Item} {Format(literal.Value)} -> {Format(options.FishValueCap)} ({mod}) in {value.Entry}");
                }
                else if (literal is null && changed && Wrap(value, expression => $"min({expression}, {cap})"))
                    decisions.Add($"[CGF-VALUE-001] Runtime fish sale cap for {location}: computed value -> maximum {Format(options.FishValueCap)}");
                continue;
            }
            if (!changed) continue;
            bool highXp = value.Stat == Xp && literal > ValueGuardOptions.DefaultFishValueCap;
            if (!options.FairPlay)
            {
                if (highXp) Keep(literal!.Value, location, "Fair-Play is off");
                continue;
            }

            double vanillaMax = maxima.GetValueOrDefault(value.Stat);
            double peer = peers.GetValueOrDefault((mod, value.Stat))?
                .FirstOrDefault(p => p.Item != value.Item).Max ?? 0;
            double headroom = vanillaMax * Headroom;
            double progression = value.IsSpeed ? headroom : Math.Max(headroom, peer * PeerRatio);
            double paidPrice = paid.GetValueOrDefault(value.Identity);
            double limit = value.IsSpeed ? headroom : Math.Max(headroom, peer);
            if (literal is double number)
            {
                if (value.IsPrice && number <= 0 && paidPrice > 0)
                    Clamp(value, number, paidPrice, location, "vanilla item has a paid price");
                else if (value.IsPrice && number < 0)
                    Keep(number, location, "no paid vanilla item at this slot");
                else if (vanillaMax > 0 && number > headroom && IsPlaceholder(number))
                    Clamp(value, number, headroom, location, "placeholder magnitude exceeds vanilla headroom");
                else if (vanillaMax > 0 && number > progression && (number >= Extreme || value.IsSpeed && number >= TrivialSpeed))
                    Clamp(value, number, value.IsSpeed ? headroom : limit, location, value.IsSpeed
                        ? "gameplay-trivial speed exceeds vanilla headroom" : "extreme value exceeds vanilla headroom and the mod's other items");
                else if (highXp || vanillaMax > 0 && number >= headroom || vanillaMax == 0 && (number >= Extreme || IsPlaceholder(number)))
                    Keep(number, location, vanillaMax == 0 ? "no comparable positive vanilla stat"
                        : number <= headroom ? "at the vanilla headroom boundary"
                        : number <= progression ? "supported by the mod's other items" : "isolated value without a clear debug signature");
                continue;
            }

            if (vanillaMax == 0 && !value.IsPrice) continue;
            double threshold = Math.Max(value.IsSpeed ? TrivialSpeed : Extreme, progression);
            if (Wrap(value, expression => $"{RuntimeScript}({expression}, {GmlNumber(threshold)}, {GmlNumber(limit)}, {GmlNumber(headroom)}, {GmlNumber(paidPrice)})"))
            {
                needsRuntime = true;
                decisions.Add($"[CGF-FAIR-001] Runtime guard for {location}: computed value -> {Format(limit)} if extreme or {Format(headroom)} if a placeholder{(paidPrice > 0 ? $" or {Format(paidPrice)} if nonpositive" : "")}");
            }
        }

        // The canonical appraisal applies gear multipliers after BASE_VALUE is read.
        // Add the final sale cap after those multipliers, before the value can reach the sale UI.
        if (options.FishValueCap > 0 && live.TryGetValue(Appraisal, out var appraisal))
        {
            AddEdit(Appraisal, appraisal.Text.Length, appraisal.Text.Length, _ => $"\nvalue = min(value, {options.FishValueCap.ToString(CultureInfo.InvariantCulture)});\n");
        }

        foreach (var (entry, changes) in edits)
        {
            string text = live[entry].Text;
            var applied = new List<(int Start, int Delta)>();
            foreach (var edit in changes.OrderByDescending(e => e.Start).ThenBy(e => e.End))
            {
                // Nested registrations and writes can both be wrapped in the same expression.
                int end = edit.End + applied.Where(a => a.Start >= edit.Start && a.Start < edit.End).Sum(a => a.Delta);
                string replacement = edit.Transform(text[edit.Start..end]);
                text = text[..edit.Start] + replacement + text[end..];
                applied.Add((edit.Start, replacement.Length - (end - edit.Start)));
            }
            generated[entry] = recompile[entry] = text;
        }
        if (needsFishRuntime)
        {
            string matches = string.Join(" || ", currentContext.FishObjects
                .Where(name => name == "Fish" || !currentContext.Inherits(name, "Fish"))
                .Select(name => $"object_index == {AssetSymbols.Symbol("objects", name)}"));
            if (currentContext.IsObject("Fish"))
                matches += $" || object_is_ancestor(object_index, {AssetSymbols.Symbol("objects", "Fish")})";
            PutScript(data, DynamicFishScript, $$"""
                var jamm_value;
                jamm_value = argument1;
                if (instance_exists(argument0))
                {
                    with (argument0)
                    {
                        if ({{matches}}) jamm_value = min(jamm_value, argument2);
                    }
                }
                return jamm_value;
                """, recompile, generated);
        }
        if (needsRuntime)
            PutScript(data, RuntimeScript, """
                if (!is_real(argument0)) return argument0;
                if (argument4 > 0 && argument0 <= 0) return argument4;
                if (argument3 > 0 && argument0 > argument3 && (argument0 == 9999999 || argument0 == 999999999)) return argument3;
                if (argument2 > 0 && argument0 >= argument1) return argument2;
                return argument0;
                """, recompile, generated);

        bool Changed(Candidate value)
        {
            var key = (value.Entry, value.Identity, value.Fingerprint);
            if (baseValues.GetValueOrDefault(key) == 0) return true;
            baseValues[key]--;
            return false;
        }

        string Source(Candidate value)
        {
            string? fallback = baseMod;
            if (selected.TryGetValue(value.Entry, out var asset) && asset.Source >= 0 && asset.Source < patches.Count)
                fallback = ModName(patches[asset.Source]);
            for (int i = patches.Count - 1; i >= 0; i--)
            {
                if (sources[i].GetValueOrDefault(value.Entry)?.ModCode is not { } after) continue;
                if (!sourceWrites.TryGetValue((i, value.Entry), out var writes))
                {
                    writes = Read(value.Entry, new GuardGml(after.Text), currentContext)
                        .Select(v => (v.Identity, v.Fingerprint)).ToHashSet();
                    sourceWrites[(i, value.Entry)] = writes;
                }
                if (writes.Contains((value.Identity, value.Fingerprint))) return ModName(patches[i]);
            }
            return fallback ?? ModName(patches[0]);
        }

        void AddEdit(string entry, int start, int end, Func<string, string> transform)
        {
            if (!edits.TryGetValue(entry, out var list)) edits[entry] = list = [];
            list.Add(new(start, end, transform));
        }

        void Replace(Candidate value, string replacement) =>
            AddEdit(value.Entry, value.Gml.Start(value.Span), value.Gml.End(value.Span), _ => replacement);

        bool Wrap(Candidate value, Func<string, string> wrapper)
        {
            if (value.Write is not { Operator: not "=" } write)
            {
                AddEdit(value.Entry, value.Gml.Start(value.Span), value.Gml.End(value.Span), wrapper);
                return true;
            }
            // A compound write is evaluated once. Repeat only a simple lvalue with a constant index.
            if (write.Index is { } index && value.Gml.Number(index) is null) return false;
            int first = write.Qualifier is null ? write.At : write.At - 2;
            string target = value.Gml.Source(new(first, value.Span.First - 1));
            AddEdit(value.Entry, value.Gml.Tokens[value.Span.First - 1].Start, value.Gml.End(value.Span),
                statement => $"= {wrapper($"{target} {write.Operator[0]} ({statement[(statement.IndexOf('=') + 1)..].TrimStart()})")}");
            return true;
        }

        void Clamp(Candidate value, double old, double replacement, string location, string why)
        {
            Replace(value, GmlNumber(replacement));
            decisions.Add($"[CGF-FAIR-001] {location}: {Format(old)} -> {Format(replacement)} ({why})");
        }

        void Keep(double number, string location, string why) =>
            decisions.Add($"INFO [CGF-FAIR-002] possible debug value kept: {location} = {Format(number)} ({why})");
    }

    private static bool IsPlaceholder(double value) => value is 9_999_999 or 999_999_999;
    private static string GmlNumber(double value) => value.ToString("0.###############", CultureInfo.InvariantCulture);
    private static string Format(double value) => value.ToString("#,0.###############", CultureInfo.InvariantCulture);
    private static string ModName(ModPatch patch) => patch.Mod.Name ?? Path.GetFileNameWithoutExtension(patch.Mod.FileName);

    internal static void PutScript(UndertaleData data, string script, string body, Dictionary<string, string> recompile,
        Dictionary<string, string> generated)
    {
        string name = "gml_Script_" + script;
        var entry = data.Code.ByName(name) ?? UndertaleCode.CreateEmptyEntry(data, name);
        if (data.Scripts.ByName(script) is null)
            data.Scripts.Add(new UndertaleScript { Name = data.Strings.MakeString(script), Code = entry });
        generated[name] = recompile[name] = body;
    }

    private static IEnumerable<Candidate> Read(string entry, GuardGml gml, ValueGuardContext context)
    {
        // The Items API copies validated registration arguments into tables and restores save data.
        // Guard the mod's original arguments once at their call sites.
        if (entry.StartsWith("gml_Script_jamm_", StringComparison.Ordinal)
            && ItemsRuntimeScripts.DefaultScripts.ContainsKey(entry["gml_Script_".Length..])) yield break;
        foreach (var write in gml.Writes())
        {
            string field = write.Field.ToLowerInvariant();
            if (field is not ("base_value" or "xp_value" or "cost" or "value" or "cost_base" or "upgrades" or "max_per_rod")
                && !RodFields.Contains(field) && !BoatFields.Contains(field) && !LureFields.Contains(field)
                && !field.StartsWith("gear_", StringComparison.Ordinal)) continue;
            var owners = context.Owners(entry).DefaultIfEmpty(context.EventOwner(entry))
                .Select(owner => gml.Resolve(write.At, write.Qualifier, owner)).Distinct().ToArray();
            var stats = owners.Select(owner => Stat(owner, field, write, gml, context)).Distinct().ToArray();
            string? stat = stats.Length == 1 ? stats[0] : null;
            string? dynamicTarget = null;
            if (stat is null && write.Field == "BASE_VALUE" && entry != FishRuntime.RegisterCode
                && (stats.Contains(Sale) || owners.Any(o => o is null || !context.IsObject(o))))
            {
                stat = Sale;
                dynamicTarget = write.Qualifier is null or "self" or "id" ? "id" : write.Qualifier;
            }
            if (stat is null) continue;
            string item = owners.Length == 1 && owners[0] is { } owner ? owner : entry;
            if (write.Index is { } index) item += "[" + gml.Key(index) + "]";
            yield return new(entry, gml, write.Value, stat, item, write.Operator + gml.Key(write.Value),
                (write.Operator == "=" || stat != Sale && write.Operator == "+=") ? gml.Number(write.Value) : null, write, dynamicTarget);
        }

        foreach (var call in gml.Calls())
        {
            string name = gml.Tokens[call.At].Value;
            if (AssetSymbols.Parse(name, out _, out string decoded)) name = decoded;
            if (call.Arguments.Count == 0) continue;
            string item = gml.ObjectName(call.Arguments[0]) ?? gml.Key(call.Arguments[0]).Trim('"');
            if (name == "jamm_fish_register" && call.Arguments.Count > 1)
                item = gml.ObjectName(call.Arguments[1]) ?? item;
            foreach (var (index, stat) in Registration(name))
            {
                if (index >= call.Arguments.Count) continue;
                var arg = call.Arguments[index];
                yield return new(entry, gml, arg, stat, item, "=" + gml.Key(arg), gml.Number(arg));
            }
        }
    }

    private static string? Stat(string? owner, string field, GuardGml.Write write, GuardGml gml, ValueGuardContext context)
    {
        if (owner is null) return null;
        if (write.Field == "BASE_VALUE" && context.IsFish(owner)) return Sale;
        if (write.Field == "XP_VALUE" && context.IsFish(owner)) return Xp;
        if (field == "cost" && owner is "RodData" or "BoatData" or "LureData" or "ToolData" or "UpgradeData")
            return "price." + owner;
        if (field == "cost_base" && owner == "UpgradeData") return "price.UpgradeDataBase";
        if (field == "value" && owner == "GearData") return "price.GearData";
        if (field == "max_per_rod" && owner == "UpgradeData") return "upgrade.max_per_rod";
        if (RodFields.Contains(field) && (owner == "RodData" || context.Inherits(owner, "Rod"))) return "rod." + field;
        if (BoatFields.Contains(field) && context.Inherits(owner, "Boat")) return "boat." + field;
        if (LureFields.Contains(field) && context.Inherits(owner, "Lure"))
            return field == "modified_reel_speed" ? "rod.reel_speed" : "lure." + field;
        if (field == "upgrades" && context.Inherits(owner, "Rod") && write.Index is { } index
            && gml.Number(index) is double slot && (slot < 5 || slot > 10))
            return "upgrade.effect[" + GmlNumber(slot) + "]";
        if (field.StartsWith("gear_", StringComparison.Ordinal) && owner == "Girl"
            && (field.EndsWith("_scalar", StringComparison.Ordinal) || field == "gear_bone_enchant"))
            return "upgrade." + field;
        return null;
    }

    private static readonly HashSet<string> RodFields = new(StringComparer.Ordinal)
    {
        "cast_speed", "backswing_speed", "rod_power", "rod_power_flat", "reel_speed", "sinker_distance",
        "min_reel", "max_reel", "min_speed", "max_speed", "min_power", "max_power"
    };

    private static readonly HashSet<string> BoatFields = new(StringComparer.Ordinal)
    {
        "move_speed", "move_bonus", "fish_limit", "base_speed", "base_limit", "max_x", "boost_radar",
        "deposit_start", "deposit_stop", "deposit_depth"
    };

    private static readonly HashSet<string> LureFields = new(StringComparer.Ordinal)
    {
        "sink_speed", "rise_speed", "sinker_vel", "modified_reel_speed"
    };

    private static (int Index, string Stat)[] Registration(string script) => script switch
    {
        "jamm_fish_register" => [(16, Sale)],
        "jamm_rod_register" => [(2, "price.RodData"), (3, "rod.cast_speed"), (4, "rod.rod_power"),
            (5, "rod.rod_power_flat"), (6, "rod.reel_speed"), (7, "rod.sinker_distance")],
        "jamm_boat_register" => [(2, "price.BoatData")],
        "jamm_lure_register" => [(2, "price.LureData")],
        "jamm_tool_register" => [(2, "price.ToolData")],
        "jamm_hat_register" => [(2, "price.GearData")],
        "jamm_upgrade_register" => [(4, "price.UpgradeData"), (6, "upgrade.max_per_rod")],
        _ => []
    };
}
