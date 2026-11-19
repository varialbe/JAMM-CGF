using System.Collections;
using System.Globalization;
using System.Text.Json;
using Jamm.Core.Building;
using Jamm.Core.Diffing;
using Jamm.Core.Merging;
using Jamm.Core.Patching;
using Underanalyzer.Decompiler;
using UndertaleModLib;
using UndertaleModLib.Decompiler;
using UndertaleModLib.Models;

namespace Jamm.Core.Acceptance;

/// <summary>
/// Pure read-only analyzer that checks what content arrived from each mod in a merged archive
/// and reports pack-level runtime risks without mutating game archives.
/// </summary>
public static class PackAcceptance
{
    private sealed record LegacyTableSpec(
        string Kind,
        string TableObject,
        string CreateCode,
        string CountVar,
        string[] ObjectFields,
        string[] NameFields,
        string[] LengthFields,
        string ApiFunction,
        int ApiObjectArg,
        int ApiNameArg,
        int? DefaultLoopBound = null);

    private static readonly LegacyTableSpec[] LegacyTables =
    [
        new(AcceptanceKind.Boat, "BoatData", "gml_Object_BoatData_Create_0", "nItems",
            ["object"], ["name"], ["object", "name", "cost", "disp", "desc"],
            "jamm_boat_register", 4, 1),
        new(AcceptanceKind.Rod, "RodData", "gml_Object_RodData_Create_0", "nItems",
            ["object"], ["name"], ["name", "cost", "cast_speed", "rod_power", "rod_power_flat", "reel_speed", "sinker_distance", "disp"],
            "jamm_rod_register", -1, 1),
        new(AcceptanceKind.Lure, "LureData", "gml_Object_LureData_Create_0", "nItems",
            ["object"], ["name"], ["item", "object", "name", "cost", "disp", "desc"],
            "jamm_lure_register", 3, 1),
        new(AcceptanceKind.Tool, "ToolData", "gml_Object_ToolData_Create_0", "nItems",
            ["object"], ["name"], ["item", "object", "name", "cost", "quests", "message", "disp", "desc"],
            "jamm_tool_register", 4, 1),
        new(AcceptanceKind.Hat, "GearData", "gml_Object_GearData_Create_0", "n_gear",
            ["gear", "object"], ["name"], ["gear", "object", "value", "weight", "range_min", "range_max", "offset_x", "offset_y", "mythic", "map"],
            "jamm_hat_register", 1, -1),
        new(AcceptanceKind.Upgrade, "UpgradeData", "gml_Object_UpgradeData_Create_0", "nItems",
            [], ["name"], ["name", "desc", "desc2", "cost", "unlocks", "disp"],
            "jamm_upgrade_register", -1, 1, DefaultLoopBound: 18)
    ];

    private static readonly (string Kind, string Family)[] AddedAssetFamilies =
    [
        (AcceptanceKind.Object, "objects"),
        (AcceptanceKind.Sprite, "sprites"),
        (AcceptanceKind.Sound, "sounds"),
        (AcceptanceKind.Room, "rooms"),
        (AcceptanceKind.Script, "scripts")
    ];

    private static readonly HashSet<string> RuntimeScriptNames = BuildRuntimeScriptSet();

    private static HashSet<string> BuildRuntimeScriptSet()
    {
        var names = new HashSet<string>(StringComparer.Ordinal)
        {
            ItemsRuntimeScripts.ApiInitName,
            FishRuntime.RegisterScript
        };
        names.UnionWith(CatalogRuntime.Scripts.Keys);
        names.UnionWith(HudRuntime.Scripts.Keys);
        names.UnionWith(ItemsRuntimeScripts.DefaultScripts.Keys);
        return names;
    }

    /// <summary>Analyzes per-mod content arrival and pack-level runtime risks for a completed or dry-run build.</summary>
    public static PackAcceptanceReport Analyze(
        UndertaleData baseData,
        IReadOnlyList<AcceptanceModInput> mods,
        BuildPlan plan,
        UndertaleData mergedData,
        IReadOnlyDictionary<string, string>? baseCode = null,
        IReadOnlyList<IReadOnlyDictionary<string, string>>? modCodes = null,
        IReadOnlyDictionary<string, string>? mergedCode = null)
    {
        ArgumentNullException.ThrowIfNull(baseData);
        ArgumentNullException.ThrowIfNull(mods);
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(mergedData);

        var baseReader = new CodeReader(baseData, baseCode, normalizeTypedLiterals: false);
        var modReaders = mods.Select((m, i) => new CodeReader(m.Data, modCodes is not null && i < modCodes.Count ? modCodes[i] : null, m.Patch, normalizeTypedLiterals: false)).ToArray();
        var mergedReader = new CodeReader(mergedData, mergedCode, normalizeTypedLiterals: true);

        var baseTables = TableNameSets(baseData);
        var mergedTables = TableNameSets(mergedData);
        var planByKey = plan.Assets
            .Where(a => a.Patch is not null)
            .GroupBy(a => (a.Family, a.Name))
            .ToDictionary(g => g.Key, g => g.Last());

        var baseCatalogEntries = ReadCatalogEntries(baseReader);
        var baseCatalogBySlot = baseCatalogEntries.GroupBy(e => (e.Area, e.Slot)).ToDictionary(g => g.Key, g => g.Last().Object);
        var baseCatalogObjects = baseCatalogEntries.Select(e => e.Object).ToHashSet(StringComparer.Ordinal);

        var mergedCatalogEntries = ReadCatalogEntries(mergedReader);
        var mergedCatalogLocations = mergedCatalogEntries
            .GroupBy(e => e.Object, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => (g.Last().Area, g.Last().Slot), StringComparer.Ordinal);

        var mergedActiveApiCalls = ScanActiveApiCalls(mergedData, mergedReader);
        var mergedApiFish = mergedActiveApiCalls
            .Where(c => c.Function is "jamm_catalog_register" or "jamm_fish_register")
            .Select(c => c.Identity)
            .Where(id => !string.IsNullOrEmpty(id))
            .ToHashSet(StringComparer.Ordinal);

        var apiRegistrations = new SortedDictionary<string, int>(StringComparer.Ordinal);
        foreach (var call in mergedActiveApiCalls)
            apiRegistrations[call.Function] = apiRegistrations.GetValueOrDefault(call.Function) + 1;

        var baseLegacyTables = LegacyTables.ToDictionary(t => t.Kind, t => ReadLegacyTable(baseReader, t), StringComparer.Ordinal);
        var mergedLegacyTables = LegacyTables.ToDictionary(t => t.Kind, t => ReadLegacyTable(mergedReader, t), StringComparer.Ordinal);

        var discardedHunks = CollectDiscardedHunks(mods, plan);
        var baseObjectNames = baseData.GameObjects.Select(o => o.Name.Content).ToHashSet(StringComparer.Ordinal);
        var baseRooms = baseData.Rooms.ToDictionary(r => r.Name.Content, StringComparer.Ordinal);
        var mergedRooms = mergedData.Rooms.ToDictionary(r => r.Name.Content, StringComparer.Ordinal);

        List<ModAcceptance> modResults = new(mods.Count);
        for (int m = 0; m < mods.Count; m++)
        {
            var mod = mods[m];
            string modName = string.IsNullOrWhiteSpace(mod.Name)
                ? Path.GetFileNameWithoutExtension(mod.FileName)
                : mod.Name;
            var modReader = modReaders[m];
            List<ContentItem> items = [];

            // 1. Added assets: object, sprite, sound, room, script
            foreach (var (kind, family) in AddedAssetFamilies)
            {
                var baseSet = baseTables[family];
                var mergedSet = mergedTables[family];
                var modSet = TableNames(mod.Data, family);
                foreach (string name in modSet.Where(n => !baseSet.Contains(n)).OrderBy(n => n, StringComparer.Ordinal))
                {
                    bool relocated = plan.AssetRenames.Any(r => r.Key.Mod == m && r.Key.Family == family && r.Value == name);
                    items.Add(relocated
                        ? new ContentItem(kind, name, mergedSet.Contains(name) ? AcceptanceStatus.Relocated : AcceptanceStatus.Lost,
                            mergedSet.Contains(name) ? $"Relocated to {name} to preserve both additions" : $"Relocated asset '{name}' is missing from merged archive")
                        : EvaluateAddedAsset(kind, family, name, m, mods, modReaders, mergedSet, planByKey));
                }
            }

            // 2. Fish (CatalogData.object[a, s] + jamm_catalog_register / jamm_fish_register)
            var modApiCalls = ScanActiveApiCalls(mod.Data, modReader, requireInitReachable: false);
            var modCatalogAdded = new Dictionary<string, (int? Area, int? Slot)>(StringComparer.Ordinal);
            foreach (var entry in ReadCatalogEntries(modReader))
            {
                if (baseCatalogBySlot.TryGetValue((entry.Area, entry.Slot), out string? baseObj) && baseObj == entry.Object)
                    continue;
                modCatalogAdded.TryAdd(entry.Object, (entry.Area, entry.Slot));
            }
            foreach (var call in modApiCalls.Where(c => c.Function is "jamm_catalog_register" or "jamm_fish_register"))
            {
                if (!baseCatalogObjects.Contains(call.Identity))
                    modCatalogAdded.TryAdd(call.Identity, (null, null));
            }
            foreach (var (fishObj, loc) in modCatalogAdded.OrderBy(p => p.Key, StringComparer.Ordinal))
            {
                if (mergedCatalogLocations.TryGetValue(fishObj, out var mergedLoc))
                {
                    if (loc.Area is { } sa && loc.Slot is { } ss && (mergedLoc.Area != sa || mergedLoc.Slot != ss))
                        items.Add(new(AcceptanceKind.Fish, fishObj, AcceptanceStatus.Relocated, $"Catalog [{sa}, {ss}] -> [{mergedLoc.Area}, {mergedLoc.Slot}]"));
                    else
                        items.Add(new(AcceptanceKind.Fish, fishObj, AcceptanceStatus.Present, $"Catalog [{mergedLoc.Area}, {mergedLoc.Slot}]"));
                }
                else if (mergedApiFish.Contains(fishObj))
                {
                    items.Add(new(AcceptanceKind.Fish, fishObj, AcceptanceStatus.Present, "Registered via JAMM catalog/fish API"));
                }
                else
                {
                    string detail = loc.Area is { } sa && loc.Slot is { } ss
                        ? $"Missing from merged catalog (was [{sa}, {ss}])"
                        : "Missing from merged catalog and active API registrations";
                    items.Add(new(AcceptanceKind.Fish, fishObj, AcceptanceStatus.Lost, detail));
                }
            }

            // 3. Legacy item tables & JAMM API item registrations
            foreach (var spec in LegacyTables)
            {
                var baseEntries = baseLegacyTables[spec.Kind];
                var modEntries = ReadLegacyTable(modReader, spec);
                var mergedEntries = mergedLegacyTables[spec.Kind];
                var mergedByIdentity = mergedEntries
                    .GroupBy(p => p.Value, StringComparer.Ordinal)
                    .ToDictionary(g => g.Key, g => g.First().Key, StringComparer.Ordinal);
                var mergedApiForKind = mergedActiveApiCalls
                    .Where(c => c.Function == spec.ApiFunction)
                    .Select(c => c.Identity)
                    .ToHashSet(StringComparer.Ordinal);

                var addedEntries = new Dictionary<string, int?>(StringComparer.Ordinal);
                foreach (var (slot, identity) in modEntries.OrderBy(p => p.Key))
                {
                    if (baseEntries.TryGetValue(slot, out string? baseId) && baseId == identity)
                        continue;
                    addedEntries.TryAdd(identity, slot);
                }
                var baseIdSet = baseEntries.Values.ToHashSet(StringComparer.Ordinal);
                foreach (var call in modApiCalls.Where(c => c.Function == spec.ApiFunction))
                {
                    if (!baseIdSet.Contains(call.Identity))
                        addedEntries.TryAdd(call.Identity, null);
                }

                foreach (var (identity, sourceSlot) in addedEntries.OrderBy(p => p.Key, StringComparer.Ordinal))
                {
                    if (mergedByIdentity.TryGetValue(identity, out int mergedSlot))
                    {
                        if (sourceSlot is { } si && mergedSlot != si)
                            items.Add(new(spec.Kind, identity, AcceptanceStatus.Relocated, $"{spec.TableObject} slot {si} -> {mergedSlot}"));
                        else
                            items.Add(new(spec.Kind, identity, AcceptanceStatus.Present, $"{spec.TableObject} slot {mergedSlot}"));
                    }
                    else if (mergedApiForKind.Contains(identity))
                    {
                        items.Add(new(spec.Kind, identity, AcceptanceStatus.Present, $"Registered via {spec.ApiFunction}"));
                    }
                    else
                    {
                        string detail = sourceSlot is { } si
                            ? $"Not found in merged {spec.TableObject} (was slot {si})"
                            : $"Not found in merged {spec.TableObject} or active {spec.ApiFunction} calls";
                        items.Add(new(spec.Kind, identity, AcceptanceStatus.Lost, detail));
                    }
                }
            }

            // 4. Room placements of mod-added or mod-increased objects
            foreach (var modRoom in mod.Data.Rooms.OrderBy(r => r.Name.Content, StringComparer.Ordinal))
            {
                string roomName = modRoom.Name.Content;
                baseRooms.TryGetValue(roomName, out var baseRoom);
                mergedRooms.TryGetValue(roomName, out var mergedRoom);

                var modByObject = GroupInstancesByObject(modRoom);
                var baseByObject = baseRoom is null ? [] : GroupInstancesByObject(baseRoom);
                var mergedByObject = mergedRoom is null ? [] : GroupInstancesByObject(mergedRoom);

                foreach (var (objName, modCoords) in modByObject.OrderBy(p => p.Key, StringComparer.Ordinal))
                {
                    baseByObject.TryGetValue(objName, out var baseCoords);
                    int baseCount = baseCoords?.Count ?? 0;
                    int modCount = modCoords.Count;
                    if (baseObjectNames.Contains(objName) && modCount <= baseCount)
                        continue;
                    if (modCount <= baseCount)
                        continue;

                    string identity = $"{roomName}/{objName}";
                    if (mergedRoom is null)
                    {
                        items.Add(new(AcceptanceKind.Placement, identity, AcceptanceStatus.Lost,
                            $"Room '{roomName}' missing from merged archive (expected {modCount})"));
                        continue;
                    }

                    int mergedCount = mergedByObject.TryGetValue(objName, out var mergedCoords) ? mergedCoords.Count : 0;
                    if (mergedCount >= modCount)
                    {
                        items.Add(new(AcceptanceKind.Placement, identity, AcceptanceStatus.Present,
                            $"mod {modCount} (base {baseCount}), merged {mergedCount}"));
                    }
                    else if (mergedCoords is not null && AllAddedPlacementsPresent(baseCoords, modCoords, mergedCoords))
                    {
                        items.Add(new(AcceptanceKind.Placement, identity, AcceptanceStatus.Present,
                            $"mod +{modCount - baseCount} (mod {modCount}, base {baseCount}), merged {mergedCount}"));
                    }
                    else
                    {
                        items.Add(new(AcceptanceKind.Placement, identity, AcceptanceStatus.Lost,
                            $"merged {mergedCount} < mod {modCount} (base {baseCount})"));
                    }
                }
            }

            // 5. Discarded conflict hunks
            foreach (var hunk in discardedHunks.Where(h => string.Equals(h.Mod, modName, StringComparison.Ordinal))
                .OrderBy(h => h.CodeEntry, StringComparer.Ordinal).ThenBy(h => h.Line))
            {
                items.Add(new(AcceptanceKind.Hunk, $"{hunk.CodeEntry}:{hunk.Line}", AcceptanceStatus.Lost,
                    $"Conflict at {hunk.CodeEntry} line {hunk.Line} won by {hunk.Winner}"));
            }

            modResults.Add(new(modName, mod.FileName, items, modApiCalls.Count));
        }

        // Pack-level runtime risk checks
        List<RuntimeRisk> risks = [];
        CheckTableLengths(mergedReader, risks);
        CheckInstanceDecisions(plan, risks);
        // CGF-INSTANCE-003 stops FishThinner from culling light owners, so the risk only remains when that repair was not applied.
        if (!plan.Decisions.Any(d => d.StartsWith("[CGF-INSTANCE-003]", StringComparison.Ordinal)))
            CheckLightCullRisks(mergedData, mergedReader, risks);

        return new(modResults, risks, apiRegistrations.Count > 0 ? apiRegistrations : null);
    }

    private static ContentItem EvaluateAddedAsset(
        string kind,
        string family,
        string name,
        int modIndex,
        IReadOnlyList<AcceptanceModInput> mods,
        CodeReader[] modReaders,
        HashSet<string> mergedSet,
        Dictionary<(string Family, string Name), PlannedAsset> planByKey)
    {
        if (!mergedSet.Contains(name))
            return new(kind, name, AcceptanceStatus.Lost, $"Added {kind} '{name}' is missing from merged archive");

        if (kind == AcceptanceKind.Script && RuntimeScriptNames.Contains(name))
            return new(kind, name, AcceptanceStatus.Present, "JAMM runtime script active");

        if (!planByKey.TryGetValue((family, name), out var planned) || planned.Source == modIndex)
            return new(kind, name, AcceptanceStatus.Present, $"Present in merged {family}");

        if (planned.Source >= 0 && planned.Source < mods.Count)
        {
            if (AssetsEqual(family, name, mods[modIndex].Data, modReaders[modIndex], mods[planned.Source].Data, modReaders[planned.Source]))
                return new(kind, name, AcceptanceStatus.Present, $"Shared identical addition (selected from {mods[planned.Source].Name})");

            return new(kind, name, AcceptanceStatus.ModifiedByOther, $"Overridden by {mods[planned.Source].Name}");
        }

        return new(kind, name, AcceptanceStatus.Present, $"Present in merged {family}");
    }

    private static bool AssetsEqual(string family, string name, UndertaleData a, CodeReader readerA, UndertaleData b, CodeReader readerB)
    {
        var tableA = ResourceCatalog.Tables(a).GetValueOrDefault(family);
        var tableB = ResourceCatalog.Tables(b).GetValueOrDefault(family);
        if (tableA is null || tableB is null) return false;
        var assetA = ResourceCatalog.Find(tableA, name);
        var assetB = ResourceCatalog.Find(tableB, name);
        if (assetA is null || assetB is null) return false;

        var snapA = new AssetFields(_ => { }, a).Snapshot(assetA);
        var snapB = new AssetFields(_ => { }, b).Snapshot(assetB);
        if (!JsonElement.DeepEquals(snapA, snapB)) return false;

        if (assetA is UndertaleScript scriptA && assetB is UndertaleScript scriptB)
        {
            string? codeA = scriptA.Code is null ? null : readerA.Get(scriptA.Code.Name.Content);
            string? codeB = scriptB.Code is null ? null : readerB.Get(scriptB.Code.Name.Content);
            return string.Equals(codeA, codeB, StringComparison.Ordinal);
        }

        if (assetA is UndertaleGameObject objA && assetB is UndertaleGameObject objB)
        {
            foreach (var codeName in objA.Events.SelectMany(list => list).SelectMany(ev => ev.Actions)
                .Select(act => act.CodeId?.Name.Content).OfType<string>().Distinct(StringComparer.Ordinal))
            {
                if (!string.Equals(readerA.Get(codeName), readerB.Get(codeName), StringComparison.Ordinal))
                    return false;
            }
        }

        return true;
    }

    private static IReadOnlyList<CatalogEntry> ReadCatalogEntries(CodeReader reader)
    {
        string? gml = reader.Get(CatalogMerge.Create);
        if (string.IsNullOrWhiteSpace(gml)) return [];
        try { return CatalogMerge.ReadEntries(gml); }
        catch (InvalidDataException) { return []; }
    }

    private static Dictionary<int, string> ReadLegacyTable(CodeReader reader, LegacyTableSpec spec)
    {
        string? gml = reader.Get(spec.CreateCode);
        if (string.IsNullOrWhiteSpace(gml)) return [];

        var tokens = GmlReferences.Tokens(gml);
        var writes = GmlReferences.IndexWrites(gml);
        Dictionary<int, string> objectValues = [];
        Dictionary<int, string> nameValues = [];

        foreach (var write in writes)
        {
            if (write.Kind != "array" || !int.TryParse(write.Index, NumberStyles.Integer, CultureInfo.InvariantCulture, out int index) || index < 0)
                continue;
            string field = StripPrefix(write.Container, spec.TableObject);
            if (!spec.ObjectFields.Contains(field, StringComparer.Ordinal) && !spec.NameFields.Contains(field, StringComparer.Ordinal))
                continue;

            var rhs = ExtractWriteRhsTokens(tokens, write);
            if (rhs is null || rhs.Count == 0) continue;

            if (spec.ObjectFields.Contains(field, StringComparer.Ordinal))
            {
                if (TryReadIdentifier(rhs, out string objName))
                    objectValues[index] = objName;
            }
            else if (spec.NameFields.Contains(field, StringComparer.Ordinal))
            {
                if (rhs.Count == 1 && rhs[0].Kind == 's' && !string.IsNullOrEmpty(rhs[0].Value))
                    nameValues[index] = rhs[0].Value;
            }
        }

        Dictionary<int, string> entries = [];
        foreach (int index in objectValues.Keys.Concat(nameValues.Keys).Distinct().OrderBy(i => i))
        {
            if (objectValues.TryGetValue(index, out string? objId))
                entries[index] = objId;
            else if (nameValues.TryGetValue(index, out string? nameId))
                entries[index] = nameId;
        }
        return entries;
    }

    private static List<(string Function, string Identity)> ScanActiveApiCalls(
        UndertaleData data,
        CodeReader reader,
        bool requireInitReachable = true)
    {
        HashSet<string>? reachableInits = null;
        if (requireInitReachable)
        {
            string? apiInitGml = reader.Get(ItemsRuntimeScripts.ApiInitCode);
            reachableInits = apiInitGml is null
                ? new HashSet<string>(StringComparer.Ordinal)
                : GmlReferences.Tokens(apiInitGml)
                    .Where(t => t.Kind == 'i' && t.Value.StartsWith("jamm_init_", StringComparison.Ordinal))
                    .Select(t => "gml_Script_" + t.Value)
                    .ToHashSet(StringComparer.Ordinal);
        }

        List<(string Function, string Identity)> calls = [];
        foreach (var code in data.Code.Where(c => c.ParentEntry is null))
        {
            string entry = code.Name.Content;
            if (entry.StartsWith("gml_Script_jamm_", StringComparison.Ordinal))
            {
                bool isInit = entry.StartsWith("gml_Script_jamm_init_", StringComparison.Ordinal);
                if (!isInit && entry != ItemsRuntimeScripts.ApiInitCode)
                    continue;
                if (isInit && reachableInits is not null && !reachableInits.Contains(entry))
                    continue;
            }

            string? gml = reader.Get(entry);
            if (string.IsNullOrEmpty(gml) || !gml.Contains("jamm_", StringComparison.Ordinal))
                continue;

            var tokens = GmlReferences.Tokens(gml);
            for (int i = 0; i + 1 < tokens.Count; i++)
            {
                if (tokens[i].Kind != 'i' || tokens[i + 1].Value != "(" || (i > 0 && tokens[i - 1].Value == "."))
                    continue;

                string fn = tokens[i].Value;
                if (!IsRegistrationFunction(fn))
                    continue;

                var args = ReadCallArguments(tokens, i + 2);
                string? identity = ResolveApiCallIdentity(fn, args, data);
                if (!string.IsNullOrEmpty(identity))
                    calls.Add((fn, identity));
            }
        }
        return calls;
    }

    private static bool IsRegistrationFunction(string name) => name is
        "jamm_catalog_register" or "jamm_fish_register" or
        "jamm_boat_register" or "jamm_rod_register" or "jamm_lure_register" or
        "jamm_tool_register" or "jamm_hat_register" or "jamm_upgrade_register";

    private static string? ResolveApiCallIdentity(string fn, List<List<GmlReferences.Token>> args, UndertaleData data)
    {
        if (fn is "jamm_catalog_register" or "jamm_fish_register")
        {
            if (args.Count > 1 && TryReadObjectReference(args[1], data, out string fishObj))
                return fishObj;
            if (fn == "jamm_fish_register" && args.Count > 0 && TryReadString(args[0], out string fishKey))
                return fishKey;
            return null;
        }

        var spec = LegacyTables.FirstOrDefault(t => t.ApiFunction == fn);
        if (spec is null) return null;
        if (spec.ApiObjectArg >= 0 && args.Count > spec.ApiObjectArg && TryReadObjectReference(args[spec.ApiObjectArg], data, out string objId))
            return objId;
        if (spec.ApiNameArg >= 0 && args.Count > spec.ApiNameArg && TryReadString(args[spec.ApiNameArg], out string nameId))
            return nameId;
        if (args.Count > 0 && TryReadString(args[0], out string keyId))
            return keyId;
        return null;
    }

    private static bool TryReadObjectReference(List<GmlReferences.Token> arg, UndertaleData data, out string objName)
    {
        if (TryReadIdentifier(arg, out objName))
            return true;
        int start = 0, end = arg.Count;
        while (end - start >= 3 && arg[start].Value == "(" && arg[end - 1].Value == ")") { start++; end--; }
        if (end - start == 1 && arg[start].Kind == 'n'
            && int.TryParse(arg[start].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int idx)
            && idx >= 0 && idx < data.GameObjects.Count)
        {
            objName = data.GameObjects[idx].Name.Content;
            return true;
        }
        objName = "";
        return false;
    }

    private static List<List<GmlReferences.Token>> ReadCallArguments(List<GmlReferences.Token> tokens, int start)
    {
        List<List<GmlReferences.Token>> args = [];
        List<GmlReferences.Token> current = [];
        int depth = 0;
        for (int i = start; i < tokens.Count; i++)
        {
            var tok = tokens[i];
            if (tok.Kind == 'p')
            {
                if (depth == 0 && tok.Value == ",")
                {
                    args.Add(current);
                    current = [];
                    continue;
                }
                if (depth == 0 && tok.Value == ")")
                {
                    if (current.Count > 0 || args.Count > 0)
                        args.Add(current);
                    return args;
                }
                if (tok.Value is "(" or "[" or "{") depth++;
                else if (tok.Value is ")" or "]" or "}") depth--;
            }
            current.Add(tok);
        }
        return [];
    }

    private static IReadOnlyList<DiscardedHunk> CollectDiscardedHunks(
        IReadOnlyList<AcceptanceModInput> mods,
        BuildPlan plan)
    {
        if (plan.HasResolvedHunks || plan.DiscardedHunks.Count > 0 || mods.Count < 2 || mods.Any(m => m.Patch is null))
            return plan.DiscardedHunks;

        // Fallback when callers pass a manually constructed BuildPlan without DiscardedHunks.
        List<DiscardedHunk> discarded = [];
        var plannedCode = plan.Assets
            .Where(a => a.Family == "code")
            .ToDictionary(a => a.Name, a => a, StringComparer.Ordinal);
        string[] modNames = mods.Select(m => m.Name).ToArray();

        var codeEntries = mods
            .SelectMany((m, i) => m.Patch!.Changes.GetValueOrDefault("code")?.Added
                .Concat(m.Patch.Changes.GetValueOrDefault("code")?.Changed ?? [])
                .Select(a => (ModIndex: i, Asset: a)) ?? [])
            .GroupBy(x => x.Asset.Name, StringComparer.Ordinal);

        foreach (var group in codeEntries)
        {
            var versions = group.OrderBy(x => x.ModIndex).ToArray();
            if (versions.Length < 2) continue;
            if (versions.Any(v => v.Asset.ModCode?.Format != "gml")) continue;

            var mergeVersions = versions
                .Select(v => new MergeVersion(modNames[v.ModIndex], v.Asset.ModCode!.Text))
                .ToArray();
            string? ancestor = versions.FirstOrDefault(v => v.Asset.BaseCode?.Format == "gml").Asset.BaseCode?.Text;
            var merge = ancestor is not null ? ThreeWayMerge.Merge(ancestor, mergeVersions) : ThreeWayMerge.Added(mergeVersions);
            if (!merge.HasConflicts) continue;

            plannedCode.TryGetValue(group.Key, out var selected);
            string winner = selected is not null && selected.Source >= 0 && selected.Source < modNames.Length
                ? modNames[selected.Source]
                : modNames[versions[^1].ModIndex];
            string? selectedGml = selected?.Gml;

            foreach (var hunk in merge.Hunks.Where(h => h.Status == "CONFLICT"))
            {
                if (selectedGml is not null && hunk.KeepBoth is not null && selectedGml.Contains(hunk.KeepBoth, StringComparison.Ordinal))
                    continue;
                foreach (var v in hunk.Versions)
                {
                    if (selectedGml is not null && selectedGml.Contains(v.Text.TrimEnd('\n'), StringComparison.Ordinal))
                        continue;
                    discarded.Add(new(v.Name, group.Key, hunk.Start + 1, winner));
                }
            }
        }
        return discarded;
    }

    private static void CheckTableLengths(CodeReader mergedReader, List<RuntimeRisk> risks)
    {
        foreach (var spec in LegacyTables)
        {
            string? gml = mergedReader.Get(spec.CreateCode);
            if (string.IsNullOrWhiteSpace(gml)) continue;

            var tokens = GmlReferences.Tokens(gml);
            var writes = GmlReferences.IndexWrites(gml);
            int maxItemIndex = -1;
            Dictionary<string, int> maxByArray = new(StringComparer.Ordinal);

            foreach (var w in writes)
            {
                if (w.Kind != "array" || !int.TryParse(w.Index, NumberStyles.Integer, CultureInfo.InvariantCulture, out int idx) || idx < 0)
                    continue;
                string field = StripPrefix(w.Container, spec.TableObject);
                maxByArray[field] = Math.Max(maxByArray.GetValueOrDefault(field, -1), idx);

                if (spec.LengthFields.Contains(field, StringComparer.Ordinal))
                {
                    maxItemIndex = Math.Max(maxItemIndex, idx);
                }
                else if (field == "unlocked")
                {
                    var rhs = ExtractWriteRhsTokens(tokens, w);
                    if (rhs is not null && !IsNegativeNumericLiteral(rhs))
                        maxItemIndex = Math.Max(maxItemIndex, idx);
                }
            }

            if (maxItemIndex < 0) continue;
            int actualLength = maxItemIndex + 1;

            var (declaredLength, sourceLabel) = ReadDeclaredTableLength(tokens, spec, maxByArray);
            if (declaredLength is { } declared && declared != actualLength)
            {
                risks.Add(new("TABLE-LENGTH", "WARNING", spec.TableObject,
                    $"{spec.CreateCode} writes item indices up to {maxItemIndex} (length {actualLength}), but {sourceLabel} is {declared}."));
            }

            if (spec.TableObject == "UpgradeData" && actualLength > 18)
                CheckUpgradeExternalLoopBounds(mergedReader, actualLength, risks);
        }
    }

    private static void CheckUpgradeExternalLoopBounds(CodeReader mergedReader, int actualLength, List<RuntimeRisk> risks)
    {
        foreach (var (codeEntry, loopVar) in new[]
        {
            ("gml_Object_UpgradeMenu_Alarm_0", "iCard"),
            ("gml_Object_Girl_Other_10", "i"),
            ("gml_Object_Gear_Other_15", "i")
        })
        {
            string? text = mergedReader.Get(codeEntry);
            if (string.IsNullOrEmpty(text)) continue;
            if (codeEntry != "gml_Object_UpgradeMenu_Alarm_0" && !text.Contains("upgrade_scalar", StringComparison.Ordinal))
                continue;

            int? bound = FindLiteralLoopBound(GmlReferences.Tokens(text), loopVar);
            if (bound is { } b && b < actualLength)
            {
                risks.Add(new("TABLE-LENGTH", "WARNING", $"{specTable("UpgradeData")}/{codeEntry}",
                    $"UpgradeData has {actualLength} entries, but {codeEntry} still loops to a hardcoded bound of {b}."));
            }
        }
        static string specTable(string name) => name;
    }

    private static (int? Length, string Label) ReadDeclaredTableLength(
        List<GmlReferences.Token> tokens,
        LegacyTableSpec spec,
        Dictionary<string, int> maxByArray)
    {
        for (int i = 0; i + 3 < tokens.Count; i++)
        {
            if (tokens[i].Kind != 'i' || tokens[i].Value != spec.CountVar || tokens[i + 1].Value != "=")
                continue;
            if (tokens[i + 2].Kind == 'n' && tokens[i + 3].Value == ";"
                && int.TryParse(tokens[i + 2].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int literal))
            {
                return (literal, spec.CountVar);
            }
            if (tokens[i + 2].Value == "array_length_1d" && i + 5 < tokens.Count
                && tokens[i + 3].Value == "(" && tokens[i + 4].Kind == 'i' && tokens[i + 5].Value == ")")
            {
                string arr = StripPrefix(tokens[i + 4].Value, spec.TableObject);
                if (maxByArray.TryGetValue(arr, out int maxIdx))
                    return (maxIdx + 1, $"{spec.CountVar} (array_length_1d({arr}))");
            }
        }

        if (spec.DefaultLoopBound is not null)
        {
            int? bound = FindLiteralLoopBound(tokens, "i");
            if (bound is not null)
                return (bound, "Create loop bound");
        }

        return (null, "");
    }

    private static int? FindLiteralLoopBound(List<GmlReferences.Token> tokens, string loopVar)
    {
        for (int i = 0; i + 8 < tokens.Count; i++)
        {
            if (tokens[i].Value != "for" || tokens[i + 1].Value != "(") continue;
            int close = FindMatchingParen(tokens, i + 1);
            if (close < 0) continue;
            for (int j = i + 2; j + 2 <= close; j++)
            {
                if (tokens[j].Kind == 'i' && tokens[j].Value == loopVar
                    && tokens[j + 1].Value is "<" or "<="
                    && tokens[j + 2].Kind == 'n'
                    && int.TryParse(tokens[j + 2].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int limit))
                {
                    return tokens[j + 1].Value == "<=" ? limit + 1 : limit;
                }
            }
        }
        return null;
    }

    private static void CheckInstanceDecisions(BuildPlan plan, List<RuntimeRisk> risks)
    {
        foreach (string decision in plan.Decisions.Where(d => d.Contains("[CGF-INSTANCE-002]", StringComparison.Ordinal)))
        {
            string target = "Instance";
            int marker = decision.IndexOf("[CGF-INSTANCE-002]", StringComparison.Ordinal);
            if (marker >= 0)
            {
                string rest = decision[(marker + "[CGF-INSTANCE-002]".Length)..].Trim();
                int comma = rest.IndexOf(',');
                if (comma > 0) target = rest[..comma].Trim();
            }
            risks.Add(new("CGF-INSTANCE-002", "WARNING", target, decision));
        }
    }

    private static void CheckLightCullRisks(UndertaleData mergedData, CodeReader mergedReader, List<RuntimeRisk> risks)
    {
        var heavyPlacements = new Dictionary<UndertaleGameObject, List<(string Room, int Count)>>(ReferenceEqualityComparer.Instance);
        foreach (var room in mergedData.Rooms)
        {
            foreach (var group in room.GameObjects
                .Where(i => i.ObjectDefinition is not null)
                .GroupBy(i => i.ObjectDefinition!, ReferenceEqualityComparer.Instance)
                .Where(g => g.Count() > 5))
            {
                var obj = (UndertaleGameObject)group.Key!;
                if (!heavyPlacements.TryGetValue(obj, out var list))
                    heavyPlacements[obj] = list = [];
                list.Add((room.Name.Content, group.Count()));
            }
        }

        foreach (var (obj, placements) in heavyPlacements.OrderBy(p => p.Key.Name.Content, StringComparer.Ordinal))
        {
            if (!OwnsLightInCreate(obj, mergedReader)) continue;
            foreach (var (roomName, count) in placements.OrderBy(p => p.Room, StringComparer.Ordinal))
            {
                risks.Add(new("LIGHT-CULL-RISK", "WARNING", $"{roomName}/{obj.Name.Content}",
                    $"{obj.Name.Content} creates an owned 'light' instance in Create and has {count} placed instances in '{roomName}' (> 5; FishThinner culling destroys owned lights)."));
            }
        }
    }

    private static bool OwnsLightInCreate(UndertaleGameObject obj, CodeReader reader)
    {
        HashSet<UndertaleGameObject> visited = new(ReferenceEqualityComparer.Instance);
        UndertaleGameObject? current = obj;
        while (current is not null && visited.Add(current))
        {
            var createAction = current.Events[0].FirstOrDefault(e => e.EventSubtype == 0)?.Actions.FirstOrDefault();
            if (createAction?.CodeId is { } code)
            {
                string? gml = reader.Get(code.Name.Content);
                if (gml is null) break;
                if (HasOwnedLightAssignment(GmlReferences.Tokens(gml)))
                    return true;
                if (!gml.Contains("event_inherited", StringComparison.Ordinal) && !gml.Contains("action_inherited", StringComparison.Ordinal))
                    break;
            }
            current = current.ParentId;
        }
        return false;
    }

    private static bool HasOwnedLightAssignment(List<GmlReferences.Token> tokens)
    {
        for (int i = 0; i + 3 < tokens.Count; i++)
        {
            if (tokens[i].Kind == 'i' && tokens[i].Value == "light"
                && (i == 0 || tokens[i - 1].Value != ".")
                && tokens[i + 1].Value == "="
                && tokens[i + 2].Kind == 'i' && tokens[i + 2].Value == "instance_create"
                && tokens[i + 3].Value == "(")
            {
                return true;
            }
        }
        return false;
    }

    private static Dictionary<string, List<(int X, int Y)>> GroupInstancesByObject(UndertaleRoom room)
    {
        Dictionary<string, List<(int X, int Y)>> result = new(StringComparer.Ordinal);
        foreach (var inst in room.GameObjects)
        {
            string? name = inst.ObjectDefinition?.Name.Content;
            if (string.IsNullOrEmpty(name)) continue;
            if (!result.TryGetValue(name, out var list))
                result[name] = list = [];
            list.Add((inst.X, inst.Y));
        }
        return result;
    }

    private static bool AllAddedPlacementsPresent(
        List<(int X, int Y)>? baseCoords,
        List<(int X, int Y)> modCoords,
        List<(int X, int Y)> mergedCoords)
    {
        var added = MultisetCounts(modCoords);
        if (baseCoords is not null)
        {
            foreach (var coord in baseCoords)
            {
                if (added.TryGetValue(coord, out int n) && n > 1) added[coord] = n - 1;
                else added.Remove(coord);
            }
        }
        if (added.Count == 0) return true;
        var merged = MultisetCounts(mergedCoords);
        foreach (var (coord, needed) in added)
            if (merged.GetValueOrDefault(coord) < needed)
                return false;
        return true;

        static Dictionary<(int X, int Y), int> MultisetCounts(List<(int X, int Y)> coords)
        {
            Dictionary<(int X, int Y), int> counts = [];
            foreach (var c in coords) counts[c] = counts.GetValueOrDefault(c) + 1;
            return counts;
        }
    }

    private static Dictionary<string, HashSet<string>> TableNameSets(UndertaleData data) =>
        AddedAssetFamilies.ToDictionary(
            f => f.Family,
            f => TableNames(data, f.Family),
            StringComparer.Ordinal);

    private static HashSet<string> TableNames(UndertaleData data, string family)
    {
        IList table = family switch
        {
            "objects" => (IList)data.GameObjects,
            "sprites" => (IList)data.Sprites,
            "sounds" => (IList)data.Sounds,
            "rooms" => (IList)data.Rooms,
            "scripts" => (IList)data.Scripts,
            _ => throw new ArgumentOutOfRangeException(nameof(family), family, null)
        };
        return table.Cast<UndertaleNamedResource>().Select(r => r.Name.Content).ToHashSet(StringComparer.Ordinal);
    }

    private static List<GmlReferences.Token>? ExtractWriteRhsTokens(List<GmlReferences.Token> tokens, IndexWrite write)
    {
        for (int i = 0; i + 4 < tokens.Count; i++)
        {
            if (tokens[i].Line != write.Line || tokens[i].Kind != 'i') continue;
            if (!tokens[i].Value.Equals(write.Container.Split('.')[0], StringComparison.Ordinal)) continue;
            int eq = i + 1;
            while (eq < tokens.Count && tokens[eq].Line <= write.EndLine && tokens[eq].Value != "=") eq++;
            if (eq >= tokens.Count || tokens[eq].Value != "=") continue;

            int end = eq + 1;
            while (end < tokens.Count && tokens[end].Value != ";" && tokens[end].Line <= write.EndLine) end++;
            if (end <= eq + 1) return null;
            return tokens[(eq + 1)..end];
        }
        return null;
    }

    private static bool TryReadIdentifier(List<GmlReferences.Token> rhs, out string name)
    {
        name = "";
        int start = 0, end = rhs.Count;
        while (end - start >= 3 && rhs[start].Value == "(" && rhs[end - 1].Value == ")") { start++; end--; }
        if (end - start != 1 || rhs[start].Kind != 'i') return false;
        string raw = rhs[start].Value;
        if (raw is "true" or "false" or "undefined" or "noone" or "self" or "other" or "all") return false;
        name = AssetSymbols.Parse(raw, out _, out string unwrapped) ? unwrapped : raw;
        return true;
    }

    private static bool TryReadString(List<GmlReferences.Token> rhs, out string value)
    {
        value = "";
        int start = 0, end = rhs.Count;
        while (end - start >= 3 && rhs[start].Value == "(" && rhs[end - 1].Value == ")") { start++; end--; }
        if (end - start != 1 || rhs[start].Kind != 's') return false;
        value = rhs[start].Value;
        return !string.IsNullOrEmpty(value);
    }

    private static bool IsNegativeNumericLiteral(List<GmlReferences.Token> rhs)
    {
        if (rhs.Count == 2 && rhs[0].Value == "-" && rhs[1].Kind == 'n') return true;
        if (rhs.Count == 1 && rhs[0].Kind == 'n' && decimal.TryParse(rhs[0].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out decimal v))
            return v < 0;
        return false;
    }

    private static string StripPrefix(string container, string tableObject) =>
        container.StartsWith(tableObject + ".", StringComparison.Ordinal)
            ? container[(tableObject.Length + 1)..]
            : container;

    private static int FindMatchingParen(List<GmlReferences.Token> tokens, int openIndex)
    {
        int depth = 0;
        for (int i = openIndex; i < tokens.Count; i++)
        {
            if (tokens[i].Value == "(") depth++;
            else if (tokens[i].Value == ")" && --depth == 0) return i;
        }
        return -1;
    }

    private sealed class CodeReader
    {
        private readonly UndertaleData data;
        private readonly Dictionary<string, string> cache;
        private readonly HashSet<string> normalized;
        private readonly ModPatch? patch;
        private readonly bool normalizeTypedLiterals;
        private Dictionary<string, string[]>? names;
        private GlobalDecompileContext? context;

        internal CodeReader(
            UndertaleData data,
            IReadOnlyDictionary<string, string>? preloaded = null,
            ModPatch? patch = null,
            bool normalizeTypedLiterals = false)
        {
            this.data = data;
            this.patch = patch;
            this.normalizeTypedLiterals = normalizeTypedLiterals;
            cache = preloaded is null
                ? new(StringComparer.Ordinal)
                : new(preloaded, StringComparer.Ordinal);
            normalized = new(StringComparer.Ordinal);
        }

        internal string? Get(string codeName)
        {
            if (cache.TryGetValue(codeName, out string? cached))
            {
                if (!normalizeTypedLiterals || normalized.Contains(codeName))
                    return cached;
                names ??= SemanticReferences.Names(data);
                cached = SemanticReferences.NameTypedLiterals(cached, codeName, names);
                cache[codeName] = cached;
                normalized.Add(codeName);
                return cached;
            }

            if (patch?.Changes.TryGetValue("code", out var codeChanges) == true)
            {
                var patchEntry = codeChanges.Added.Concat(codeChanges.Changed).FirstOrDefault(a => a.Name == codeName);
                if (patchEntry?.ModCode is { Format: "gml", Text: var gml })
                {
                    cache[codeName] = gml;
                    normalized.Add(codeName);
                    return gml;
                }
            }

            var entry = data.Code.ByName(codeName);
            if (entry is null) return null;

            if (data.GameSpecificRegistry is null)
                CgfAssetTypes.Register(data);
            context ??= new GlobalDecompileContext(data, data.GlobalFunctions);
            names ??= SemanticReferences.Names(data);

            try
            {
                string text = new DecompileContext(context, entry).DecompileToString();
                text = SemanticReferences.NameTypedLiterals(text, codeName, names);
                cache[codeName] = text;
                normalized.Add(codeName);
                return text;
            }
            catch
            {
                return null;
            }
        }
    }
}
