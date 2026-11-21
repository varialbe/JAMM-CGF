using Jamm.Core.Building;
using Jamm.Core.Merging;
using Jamm.ModMaker;
using Underanalyzer.Decompiler;
using UndertaleModLib;
using UndertaleModLib.Compiler;
using UndertaleModLib.Decompiler;
using UndertaleModLib.Models;

namespace Jamm.Core.Tests;

/// <summary>An uninitialized fish variable read by a sonar or HUD reader.</summary>
/// <param name="Reader">Code entry name of the sonar/HUD reader.</param>
/// <param name="FishObject">Target fish object name.</param>
/// <param name="Variable">Instance variable name read without assignment in Create or the generator.</param>
public sealed record MissingFishVariable(string Reader, string FishObject, string Variable);

/// <summary>
/// Asserts that every fish instance variable read by sonar and HUD readers is assigned
/// either in the fish object's Create event chain or in the Endless Ocean generator.
/// </summary>
public static class FishParityAssert
{
    private static readonly string[] DefaultSonarAndHudReaders =
    [
        "gml_Object_SonarPanel_Draw_0",
        "gml_Object_SonarPanel_Step_0",
        "gml_Object_CatalogData_Draw_0",
        "gml_Object_Boat_Step_0",
        "gml_Object_Girl_Step_0",
        "gml_Object_BigHook_Step_2",
        "gml_Object_BigHook2_Step_2",
        "gml_Object_Lure_Draw_0"
    ];

    private static readonly string[] DefaultGeneratorEntries =
    [
        "gml_Script_jmk_endless_ocean_load",
        "gml_Object_obj_jmk_endless_ocean_Step_1"
    ];

    private static readonly HashSet<string> Keywords = new(StringComparer.Ordinal)
    {
        "if", "else", "for", "while", "do", "until", "repeat", "switch", "case", "default",
        "break", "continue", "exit", "return", "with", "var", "globalvar", "and", "or",
        "xor", "not", "div", "mod", "begin", "end", "then"
    };

    private static readonly HashSet<string> BuiltinVariables = new(StringComparer.Ordinal)
    {
        "id", "object_index", "x", "y", "xprevious", "yprevious", "xstart", "ystart",
        "hspeed", "vspeed", "direction", "speed", "friction", "gravity", "gravity_direction",
        "sprite_index", "sprite_width", "sprite_height", "sprite_xoffset", "sprite_yoffset",
        "image_alpha", "image_angle", "image_blend", "image_index", "image_number",
        "image_speed", "image_xscale", "image_yscale", "mask_index", "bbox_left",
        "bbox_right", "bbox_top", "bbox_bottom", "solid", "visible", "persistent", "depth",
        "alarm", "path_index", "path_position", "path_positionprevious", "path_speed",
        "path_scale", "path_orientation", "path_endaction", "timeline_index",
        "timeline_position", "timeline_speed", "timeline_running", "timeline_loop",
        "self", "other", "all", "noone", "global", "true", "false", "pi", "undefined",
        "room", "room_width", "room_height", "room_speed", "room_persistent",
        "view_current", "view_xview", "view_yview", "view_wview", "view_hview",
        "view_visible", "view_xport", "view_yport", "view_wport", "view_hport",
        "current_time", "delta_time", "fps", "fps_real", "argument_count", "argument"
    };

    private static readonly HashSet<string> FishWithAliases = new(StringComparer.Ordinal)
    {
        "Lure.fish", "fish", "fish_object_1", "fish_object_2"
    };

    /// <summary>
    /// Verifies that every variable read on fish instances by sonar and HUD code entries
    /// is assigned in each target fish's Create inheritance chain or in the generator.
    /// </summary>
    public static void AssertSonarAndHudVariablesInitialized(
        UndertaleData data,
        params string[] fishObjectNames)
    {
        var missing = FindMissingSonarAndHudVariables(data, fishObjectNames);
        if (missing.Count > 0)
        {
            string summary = string.Join(", ", missing.Select(m => $"{m.Reader} reads {m.FishObject}.{m.Variable}"));
            Assert.Fail($"Uninitialized fish variable(s) used by sonar/HUD readers: {summary}");
        }
    }

    /// <summary>
    /// Finds any fish instance variables read by sonar/HUD readers that are not assigned
    /// in the target fish's Create event chain or in the generator.
    /// </summary>
    public static IReadOnlyList<MissingFishVariable> FindMissingSonarAndHudVariables(
        UndertaleData data,
        params string[] fishObjectNames)
    {
        ArgumentNullException.ThrowIfNull(data);
        var fishRoot = data.GameObjects.ByName("Fish")
            ?? throw new InvalidDataException("Archive must define a 'Fish' parent object.");

        var fishHierarchy = data.GameObjects
            .Where(o => o is not null && InheritsFrom(o, fishRoot))
            .Select(o => o.Name.Content)
            .ToHashSet(StringComparer.Ordinal);

        string[] targets = fishObjectNames.Length > 0
            ? fishObjectNames
            : data.GameObjects
                .Where(o => o is not null && o != fishRoot && InheritsFrom(o, fishRoot) && data.GameObjects.All(child => child.ParentId != o))
                .Select(o => o.Name.Content)
                .Order(StringComparer.Ordinal)
                .ToArray();

        if (targets.Length == 0)
            throw new InvalidDataException("No target fish objects found in archive.");

        CgfAssetTypes.Register(data);
        GlobalDecompileContext.BuildGlobalFunctionCache(data);
        var decompileContext = new AssetSymbols.Context(data, new HashSet<string>(StringComparer.Ordinal));

        var assetNames = CollectAssetNames(data);
        var readerEntries = DefaultSonarAndHudReaders
            .Select(name => data.Code.FirstOrDefault(c => c.Name?.Content == name && c.ParentEntry is null))
            .Where(c => c is not null)
            .Cast<UndertaleCode>()
            .ToArray();
        if (readerEntries.Length == 0)
            throw new InvalidDataException("Archive contains none of the expected sonar/HUD reader code entries.");

        var reads = new List<(string Reader, string TargetScope, string Variable)>();
        foreach (var entry in readerEntries)
        {
            string gml = Decompile(decompileContext, entry, data);
            foreach (var (scope, variable) in ExtractFishVariableReads(gml, data, fishHierarchy, assetNames))
                reads.Add((entry.Name.Content, scope, variable));
        }

        var generatorAssigned = CollectGeneratorAssignments(data, decompileContext, fishHierarchy);
        var missing = new List<MissingFishVariable>();

        foreach (string fishName in targets)
        {
            var fishObj = data.GameObjects.ByName(fishName)
                ?? throw new InvalidDataException($"Target fish object '{fishName}' does not exist in archive.");
            if (!InheritsFrom(fishObj, fishRoot))
                throw new InvalidDataException($"Target object '{fishName}' does not inherit from 'Fish'.");

            var assigned = CollectCreateAssignments(data, decompileContext, fishObj);
            assigned.UnionWith(generatorAssigned);

            foreach (var (reader, scope, variable) in reads)
            {
                if (!AppliesToFish(scope, fishObj, data))
                    continue;
                if (!assigned.Contains(variable))
                    missing.Add(new(reader, fishName, variable));
            }
        }

        return missing
            .Distinct()
            .OrderBy(m => m.FishObject, StringComparer.Ordinal)
            .ThenBy(m => m.Reader, StringComparer.Ordinal)
            .ThenBy(m => m.Variable, StringComparer.Ordinal)
            .ToArray();
    }

    private static bool AppliesToFish(string scope, UndertaleGameObject fishObj, UndertaleData data)
    {
        if (scope == "*" || scope == fishObj.Name.Content)
            return true;
        var scopeObj = data.GameObjects.ByName(scope);
        return scopeObj is not null && InheritsFrom(fishObj, scopeObj);
    }

    private static HashSet<string> CollectCreateAssignments(
        UndertaleData data,
        AssetSymbols.Context context,
        UndertaleGameObject fishObj)
    {
        var assigned = new HashSet<string>(StringComparer.Ordinal);
        for (var cur = fishObj; cur is not null; cur = cur.ParentId)
        {
            var createEvent = cur.Events[(int)EventType.Create].FirstOrDefault();
            var code = createEvent?.Actions.FirstOrDefault()?.CodeId;
            if (code is null)
                continue;
            string gml = Decompile(context, code, data);
            var tokens = GmlReferences.Tokens(gml);
            var locals = CollectLocals(tokens);
            for (int i = 0; i < tokens.Count; i++)
            {
                if (IsSimpleSelfWrite(tokens, i, locals, out string written, out _))
                    assigned.Add(written);
            }
        }
        return assigned;
    }

    private static HashSet<string> CollectGeneratorAssignments(
        UndertaleData data,
        AssetSymbols.Context context,
        HashSet<string> fishHierarchy)
    {
        var assigned = new HashSet<string>(StringComparer.Ordinal);
        foreach (string entryName in DefaultGeneratorEntries)
        {
            var code = data.Code.FirstOrDefault(c => c.Name?.Content == entryName && c.ParentEntry is null);
            if (code is null)
                continue;
            string gml = Decompile(context, code, data);
            var tokens = GmlReferences.Tokens(gml);
            var spawnedVars = new HashSet<string>(StringComparer.Ordinal);

            for (int i = 0; i + 3 < tokens.Count; i++)
            {
                if (tokens[i].Kind == 'i' && tokens[i + 1].Value == "=" && tokens[i + 2].Value == "instance_create")
                    spawnedVars.Add(tokens[i].Value);
            }

            for (int i = 0; i + 3 < tokens.Count; i++)
            {
                if (tokens[i].Kind == 'i'
                    && spawnedVars.Contains(tokens[i].Value)
                    && tokens[i + 1].Value == "."
                    && tokens[i + 2].Kind == 'i'
                    && IsAssignmentOperator(tokens[i + 3].Value))
                {
                    assigned.Add(tokens[i + 2].Value);
                }
            }

            foreach (var (scope, writes) in ExtractWithWrites(tokens, data, fishHierarchy, spawnedVars))
            {
                if (scope is not null)
                    assigned.UnionWith(writes);
            }
        }
        return assigned;
    }

    private static IReadOnlyList<(string Scope, string Variable)> ExtractFishVariableReads(
        string gml,
        UndertaleData data,
        HashSet<string> fishHierarchy,
        HashSet<string> assetNames)
    {
        var tokens = GmlReferences.Tokens(gml);
        var locals = CollectLocals(tokens);
        var reads = new List<(string Scope, string Variable)>();

        // 1. Dotted reads like Lure.fish.<var> or fish.<var> outside or inside blocks.
        for (int i = 0; i + 2 < tokens.Count; i++)
        {
            if (tokens[i].Value == "Lure"
                && i + 4 < tokens.Count
                && tokens[i + 1].Value == "."
                && tokens[i + 2].Value == "fish"
                && tokens[i + 3].Value == "."
                && tokens[i + 4].Kind == 'i')
            {
                string field = tokens[i + 4].Value;
                if (!IsIgnoredField(field, assetNames))
                    reads.Add(("*", field));
            }
            else if (tokens[i].Value == "fish"
                && (i == 0 || tokens[i - 1].Value != ".")
                && !locals.Contains("fish")
                && tokens[i + 1].Value == "."
                && tokens[i + 2].Kind == 'i')
            {
                string field = tokens[i + 2].Value;
                if (!IsIgnoredField(field, assetNames))
                    reads.Add(("*", field));
            }
        }

        // 2. Scan `with (<target>) { ... }` scopes targeting Fish or fish instances.
        var assignedByScope = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        ScanWithScopes(tokens, 0, tokens.Count, null, 0, data, fishHierarchy, assetNames, locals, assignedByScope, new HashSet<string>(StringComparer.Ordinal), reads);
        return reads;
    }

    private static void ScanWithScopes(
        IReadOnlyList<GmlReferences.Token> tokens,
        int start,
        int end,
        string? fishScope,
        int conditionalDepth,
        UndertaleData data,
        HashSet<string> fishHierarchy,
        HashSet<string> assetNames,
        HashSet<string> locals,
        Dictionary<string, HashSet<string>> assignedByScope,
        HashSet<string> assignedInScope,
        List<(string Scope, string Variable)> reads)
    {
        for (int i = start; i < end; i++)
        {
            string val = tokens[i].Value;

            if (val == "if" && i + 1 < end && tokens[i + 1].Value == "(")
            {
                int condEnd = FindMatchingParen(tokens, i + 1);
                if (fishScope is not null)
                    ScanExpressionReads(tokens, i + 2, condEnd, fishScope, assetNames, locals, assignedInScope, reads);
                int bodyStart = condEnd + 1;
                int bodyEnd = FindStatementEnd(tokens, bodyStart, end);
                var branchByScope = CloneScopeMap(assignedByScope);
                var branchAssigned = new HashSet<string>(assignedInScope, StringComparer.Ordinal);
                ScanWithScopes(tokens, bodyStart, bodyEnd, fishScope, conditionalDepth + 1, data, fishHierarchy, assetNames, locals, branchByScope, branchAssigned, reads);
                i = bodyEnd - 1;
                continue;
            }

            if (val == "with" && i + 1 < end && tokens[i + 1].Value == "(")
            {
                int parenClose = FindMatchingParen(tokens, i + 1);
                string? targetScope = ResolveFishWithTarget(tokens, i + 2, parenClose, data, fishHierarchy);
                int bodyStart = parenClose + 1;
                int bodyEnd = FindStatementEnd(tokens, bodyStart, end);
                if (targetScope is not null)
                {
                    var seeded = SeedAssignedForScope(targetScope, assignedByScope, data);
                    ScanWithScopes(tokens, bodyStart, bodyEnd, targetScope, 0, data, fishHierarchy, assetNames, locals, assignedByScope, seeded, reads);
                    if (!assignedByScope.TryGetValue(targetScope, out var scopeSet))
                        assignedByScope[targetScope] = scopeSet = new HashSet<string>(StringComparer.Ordinal);
                    scopeSet.UnionWith(seeded);
                }
                else
                {
                    // Nested non-fish `with` clears implicit fish `self` inside its body.
                    ScanWithScopes(tokens, bodyStart, bodyEnd, null, 0, data, fishHierarchy, assetNames, locals, assignedByScope, new HashSet<string>(StringComparer.Ordinal), reads);
                }
                i = bodyEnd - 1;
                continue;
            }

            if (fishScope is not null)
            {
                if (IsSimpleSelfWrite(tokens, i, locals, out string written, out int rhsStart))
                {
                    int stmtEnd = FindStatementEnd(tokens, rhsStart, end);
                    ScanExpressionReads(tokens, rhsStart, stmtEnd, fishScope, assetNames, locals, assignedInScope, reads);
                    if (conditionalDepth == 0)
                        assignedInScope.Add(written);
                    i = stmtEnd - 1;
                    continue;
                }

                if (IsReadIdentifier(tokens, i, assetNames, locals, assignedInScope))
                    reads.Add((fishScope, val));
            }
        }
    }

    private static Dictionary<string, HashSet<string>> CloneScopeMap(Dictionary<string, HashSet<string>> source) =>
        source.ToDictionary(p => p.Key, p => new HashSet<string>(p.Value, StringComparer.Ordinal), StringComparer.Ordinal);

    private static HashSet<string> SeedAssignedForScope(
        string targetScope,
        Dictionary<string, HashSet<string>> assignedByScope,
        UndertaleData data)
    {
        var result = new HashSet<string>(StringComparer.Ordinal);
        if (assignedByScope.TryGetValue(targetScope, out var exact))
            result.UnionWith(exact);
        var targetObj = data.GameObjects.ByName(targetScope);
        if (targetObj is not null)
        {
            for (var cur = targetObj.ParentId; cur is not null; cur = cur.ParentId)
            {
                if (assignedByScope.TryGetValue(cur.Name.Content, out var parentAssigned))
                    result.UnionWith(parentAssigned);
            }
        }
        else if (targetScope == "*" && assignedByScope.TryGetValue("Fish", out var fishAssigned))
        {
            result.UnionWith(fishAssigned);
        }
        return result;
    }

    private static void ScanExpressionReads(
        IReadOnlyList<GmlReferences.Token> tokens,
        int start,
        int end,
        string fishScope,
        HashSet<string> assetNames,
        HashSet<string> locals,
        HashSet<string> assignedInScope,
        List<(string Scope, string Variable)> reads)
    {
        for (int i = start; i < end; i++)
        {
            if (IsReadIdentifier(tokens, i, assetNames, locals, assignedInScope))
                reads.Add((fishScope, tokens[i].Value));
        }
    }

    private static IEnumerable<(string? Scope, HashSet<string> Writes)> ExtractWithWrites(
        IReadOnlyList<GmlReferences.Token> tokens,
        UndertaleData data,
        HashSet<string> fishHierarchy,
        HashSet<string> spawnedVars)
    {
        for (int i = 0; i + 2 < tokens.Count; i++)
        {
            if (tokens[i].Value != "with" || tokens[i + 1].Value != "(")
                continue;
            int close = FindMatchingParen(tokens, i + 1);
            string target = string.Join("", tokens.Skip(i + 2).Take(close - (i + 2)).Select(t => t.Value));
            bool isFishTarget = spawnedVars.Contains(target)
                || ResolveFishWithTarget(tokens, i + 2, close, data, fishHierarchy) is not null;
            if (!isFishTarget)
                continue;
            int bodyEnd = FindStatementEnd(tokens, close + 1, tokens.Count);
            var writes = new HashSet<string>(StringComparer.Ordinal);
            var locals = CollectLocals(tokens);
            for (int j = close + 1; j < bodyEnd; j++)
            {
                if (IsSimpleSelfWrite(tokens, j, locals, out string written, out _))
                    writes.Add(written);
            }
            yield return (target, writes);
        }
    }

    private static bool IsReadIdentifier(
        IReadOnlyList<GmlReferences.Token> tokens,
        int index,
        HashSet<string> assetNames,
        HashSet<string> locals,
        HashSet<string> assignedInScope)
    {
        var token = tokens[index];
        if (token.Kind != 'i')
            return false;
        string val = token.Value;
        if (Keywords.Contains(val) || BuiltinVariables.Contains(val) || assetNames.Contains(val) || locals.Contains(val))
            return false;
        if (assignedInScope.Contains(val))
            return false;
        if (index > 0 && tokens[index - 1].Value == ".")
            return false;
        if (index + 1 < tokens.Count && tokens[index + 1].Value is "(" or ".")
            return false;
        return true;
    }

    private static bool IsSimpleSelfWrite(
        IReadOnlyList<GmlReferences.Token> tokens,
        int index,
        HashSet<string> locals,
        out string variable,
        out int rhsStart)
    {
        variable = "";
        rhsStart = index;
        if (index + 1 >= tokens.Count || tokens[index].Kind != 'i')
            return false;
        if (index > 0 && tokens[index - 1].Value == ".")
            return false;
        string name = tokens[index].Value;
        if (Keywords.Contains(name) || locals.Contains(name))
            return false;
        if (tokens[index + 1].Value == "=" && (index == 0 || tokens[index - 1].Value is not ("var" or "globalvar")))
        {
            variable = name;
            rhsStart = index + 2;
            return true;
        }
        if (IsAssignmentOperator(tokens[index + 1].Value) && tokens[index + 1].Value != "=")
        {
            variable = name;
            rhsStart = index + 2;
            return true;
        }
        return false;
    }

    private static bool IsAssignmentOperator(string op) =>
        op is "=" or "+=" or "-=" or "*=" or "/=" or "|=" or "&=" or "^=" or "%=";

    private static string? ResolveFishWithTarget(
        IReadOnlyList<GmlReferences.Token> tokens,
        int start,
        int end,
        UndertaleData data,
        HashSet<string> fishHierarchy)
    {
        if (end <= start)
            return null;
        string expr = string.Join("", tokens.Skip(start).Take(end - start).Select(t => t.Value));
        if (fishHierarchy.Contains(expr))
            return expr;
        if (FishWithAliases.Contains(expr))
            return "*";
        if (int.TryParse(expr, out int objIndex) && objIndex >= 0 && objIndex < data.GameObjects.Count)
        {
            string name = data.GameObjects[objIndex].Name.Content;
            if (fishHierarchy.Contains(name))
                return name;
        }
        return null;
    }

    private static HashSet<string> CollectLocals(IReadOnlyList<GmlReferences.Token> tokens)
    {
        var locals = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < tokens.Count; i++)
        {
            if (tokens[i].Value != "var")
                continue;
            for (int j = i + 1; j < tokens.Count; j++)
            {
                string val = tokens[j].Value;
                if (val == ";")
                    break;
                if (tokens[j].Kind == 'i' && (j == i + 1 || tokens[j - 1].Value == ","))
                    locals.Add(val);
            }
        }
        return locals;
    }

    private static int FindMatchingParen(IReadOnlyList<GmlReferences.Token> tokens, int openIndex)
    {
        int depth = 0;
        for (int i = openIndex; i < tokens.Count; i++)
        {
            if (tokens[i].Value == "(") depth++;
            else if (tokens[i].Value == ")" && --depth == 0) return i;
        }
        return tokens.Count - 1;
    }

    private static int FindStatementEnd(IReadOnlyList<GmlReferences.Token> tokens, int start, int limit)
    {
        if (start >= limit)
            return limit;
        if (tokens[start].Value == "{")
        {
            int depth = 0;
            for (int i = start; i < limit; i++)
            {
                if (tokens[i].Value == "{") depth++;
                else if (tokens[i].Value == "}" && --depth == 0) return i + 1;
            }
            return limit;
        }
        int parenDepth = 0;
        for (int i = start; i < limit; i++)
        {
            string v = tokens[i].Value;
            if (v is "(" or "[") parenDepth++;
            else if (v is ")" or "]") parenDepth = Math.Max(0, parenDepth - 1);
            else if (parenDepth == 0 && v == ";") return i + 1;
        }
        return limit;
    }

    private static bool InheritsFrom(UndertaleGameObject obj, UndertaleGameObject ancestor)
    {
        for (var cur = obj; cur is not null; cur = cur.ParentId)
        {
            if (cur == ancestor)
                return true;
        }
        return false;
    }

    private static bool IsIgnoredField(string field, HashSet<string> assetNames) =>
        BuiltinVariables.Contains(field) || Keywords.Contains(field) || assetNames.Contains(field);

    private static HashSet<string> CollectAssetNames(UndertaleData data)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var o in data.GameObjects) if (o?.Name?.Content is { } n) names.Add(n);
        foreach (var s in data.Sprites) if (s?.Name?.Content is { } n) names.Add(n);
        foreach (var s in data.Scripts) if (s?.Name?.Content is { } n) names.Add(n);
        foreach (var r in data.Rooms) if (r?.Name?.Content is { } n) names.Add(n);
        foreach (var f in data.Fonts) if (f?.Name?.Content is { } n) names.Add(n);
        foreach (var p in data.Paths) if (p?.Name?.Content is { } n) names.Add(n);
        foreach (var s in data.Sounds) if (s?.Name?.Content is { } n) names.Add(n);
        foreach (var b in data.Backgrounds) if (b?.Name?.Content is { } n) names.Add(n);
        return names;
    }

    private static string Decompile(AssetSymbols.Context context, UndertaleCode code, UndertaleData data) =>
        new DecompileContext(context, code.ParentEntry ?? code, data.ToolInfo.DecompilerSettings)
            .DecompileToString()
            .Replace("\r\n", "\n", StringComparison.Ordinal);
}

/// <summary>Verifies fish variable parity assertions on synthetic Endless Ocean archives.</summary>
public sealed class FishVariableParityTests
{
    [Fact]
    public void GeneratedFishPassesWhenSonarAndHudVariablesAreInitializedInCreateOrGenerator()
    {
        using var data = BuildSyntheticArchive(initializeSonarVarsInGenerator: true, initializeHudVarsInCreate: true);

        FishParityAssert.AssertSonarAndHudVariablesInitialized(data, "Dijon");
    }

    [Fact]
    public void GeneratedFishFailsWhenSonarS5OrHudVariablesAreOmittedFromCreateAndGenerator()
    {
        using var data = BuildSyntheticArchive(initializeSonarVarsInGenerator: false, initializeHudVarsInCreate: false);

        var missing = FishParityAssert.FindMissingSonarAndHudVariables(data, "Dijon");

        Assert.Contains(missing, m => m.Reader == "gml_Object_SonarPanel_Draw_0" && m.FishObject == "Dijon" && m.Variable == "s5");
        Assert.Contains(missing, m => m.Reader == "gml_Object_SonarPanel_Draw_0" && m.FishObject == "Dijon" && m.Variable == "s1");
        Assert.Contains(missing, m => m.Reader == "gml_Object_CatalogData_Draw_0" && m.FishObject == "Dijon" && m.Variable == "name");
        Assert.Contains(missing, m => m.Reader == "gml_Object_CatalogData_Draw_0" && m.FishObject == "Dijon" && m.Variable == "value");
        Assert.ThrowsAny<Exception>(() => FishParityAssert.AssertSonarAndHudVariablesInitialized(data, "Dijon"));
    }

    [Fact]
    public void HonorsCreateInheritanceChainWithoutCreditingStepOnlyAssignments()
    {
        using var data = BuildSyntheticArchive(initializeSonarVarsInGenerator: false, initializeHudVarsInCreate: true);

        // Even though SonarPanel_Step_1 assigns s1..s5, it is not a Create event or generator.
        var missing = FishParityAssert.FindMissingSonarAndHudVariables(data, "Dijon");

        Assert.Equal(new[] { "s1", "s2", "s3", "s4", "s5" },
            missing.Where(m => m.Reader == "gml_Object_SonarPanel_Draw_0").Select(m => m.Variable).Order(StringComparer.Ordinal));
        Assert.DoesNotContain(missing, m => m.Variable is "display_size" or "BASE_VALUE" or "state" or "name" or "value");
    }

    private static UndertaleData BuildSyntheticArchive(bool initializeSonarVarsInGenerator, bool initializeHudVarsInCreate)
    {
        var data = UndertaleData.CreateNew();
        data.GeneralInfo.Major = 1;
        data.GeneralInfo.BytecodeVersion = 16;
        data.GeneralInfo.Build = 1773;

        foreach (string name in new[]
        {
            "Fish", "FishMedium", "Dijon",
            "SonarPanel", "SonarPanelAdvanced", "CatalogData", "Boat", "Girl", "Lure", "Rod", "Camera", "CatchQuestNewNew"
        })
        {
            data.GameObjects.Add(new UndertaleGameObject { Name = data.Strings.MakeString(name), Visible = true });
        }

        var fish = data.GameObjects.ByName("Fish")!;
        var medium = data.GameObjects.ByName("FishMedium")!;
        var dijon = data.GameObjects.ByName("Dijon")!;
        medium.ParentId = fish;
        dijon.ParentId = medium;

        var drawTextShadowed = UndertaleCode.CreateEmptyEntry(data, "gml_Script_draw_text_shadowed");
        data.Scripts.Add(new() { Name = data.Strings.MakeString("draw_text_shadowed"), Code = drawTextShadowed });

        var numToDollar = UndertaleCode.CreateEmptyEntry(data, "gml_Script_number_to_dollar_string");
        data.Scripts.Add(new() { Name = data.Strings.MakeString("number_to_dollar_string"), Code = numToDollar });

        var loadCode = UndertaleCode.CreateEmptyEntry(data, "gml_Script_jmk_endless_ocean_load");
        data.Scripts.Add(new() { Name = data.Strings.MakeString("jmk_endless_ocean_load"), Code = loadCode });

        var compiler = new CompileGroup(data);
        compiler.QueueCodeReplace(drawTextShadowed, "return 0;\n");
        compiler.QueueCodeReplace(numToDollar, "return \"$\";\n");

        string hudCreate = initializeHudVarsInCreate ? "name = \"\";\nvalue = 0;\n" : "";
        compiler.QueueCodeReplace(fish.EventHandlerFor(EventType.Create, 0u, data), $$"""
            state = 0;
            display_size = size;
            catalog_index = -1;
            exotic = 0;
            life = 3;
            hasGivenExperience = 0;
            {{hudCreate}}
            """);
        compiler.QueueCodeReplace(medium.EventHandlerFor(EventType.Create, 0u, data), """
            size = 2;
            action_inherited();
            """);
        compiler.QueueCodeReplace(dijon.EventHandlerFor(EventType.Create, 0u, data), """
            BASE_VALUE = 12000;
            action_inherited();
            """);

        string generatorSonarInit = initializeSonarVarsInGenerator
            ? """
              swimmer.s1 = 0;
              swimmer.s2 = 0;
              swimmer.s3 = 1;
              swimmer.s4 = 1;
              swimmer.s5 = 1;
              """
            : "";
        compiler.QueueCodeReplace(loadCode, $$"""
            var swimmer = instance_create(1000, 2000, Dijon);
            swimmer.jmk_endless_ocean_fish = true;
            swimmer.catalog_index = 0;
            {{generatorSonarInit}}
            """);

        compiler.QueueCodeReplace(data.GameObjects.ByName("SonarPanel")!.EventHandlerFor(EventType.Step, (uint)EventSubtypeStep.BeginStep, data), """
            with (Fish)
            {
                s1 = sprite_index;
                s2 = image_index;
                s3 = image_xscale;
                s4 = image_yscale;
                s5 = (size + 0.5) / 3.5;
            }
            """);
        compiler.QueueCodeReplace(data.GameObjects.ByName("SonarPanel")!.EventHandlerFor(EventType.Step, 0u, data), """
            if (Lure.fish > 0 && Lure.fish.display_size == 4)
            {
                shouldDraw = 1;
            }
            """);
        compiler.QueueCodeReplace(data.GameObjects.ByName("SonarPanel")!.EventHandlerFor(EventType.Draw, 0u, data), """
            if (active && shouldDraw)
            {
                with (Fish)
                {
                    drawn = 0;
                }
                if (Girl.gear_seer)
                {
                    with (CatchQuestNewNew)
                    {
                        with (fish_object_1)
                        {
                            if (visible)
                                draw_sprite_ext(s1, s2, x, y, s3, s4, image_angle, SonarPanel.radar_fgcolor_target, s5);
                            drawn = 1;
                        }
                    }
                }
                with (Fish)
                {
                    if (!drawn && visible)
                        draw_sprite_ext(s1, s2, x, y, s3, s4, image_angle, SonarPanel.radar_fgcolor, s5);
                }
            }
            """);
        compiler.QueueCodeReplace(data.GameObjects.ByName("CatalogData")!.EventHandlerFor(EventType.Draw, 0u, data), """
            if (Lure.fish > 0)
            {
                with (Lure.fish)
                {
                    if (state == 3 && Lure.state == 0 && Rod.state == 0 && !Girl.hide_ui)
                    {
                        if (catalog_index >= 0 && exotic && life >= 3)
                            draw_text_shadowed(Camera.x, Camera.y - 88, "[Exotic]");
                        draw_text_shadowed(Camera.x, Camera.y - 6, name);
                        draw_text_shadowed(Camera.x, Camera.y + 26, "Worth " + number_to_dollar_string(value));
                    }
                }
            }
            """);
        compiler.QueueCodeReplace(data.GameObjects.ByName("Boat")!.EventHandlerFor(EventType.Step, 0u, data), """
            trawler_value = 0;
            with (Fish)
            {
                if (state == 15)
                    other.trawler_value += BASE_VALUE;
            }
            fish_on_hook_value = 0;
            if (Lure.hookedFishSize > 0 && Lure.state == 0 && Lure.fish.hasGivenExperience)
                fish_on_hook_value = Lure.fish.value;
            """);
        compiler.QueueCodeReplace(data.GameObjects.ByName("Girl")!.EventHandlerFor(EventType.Step, 0u, data), """
            if (Lure.hookedFishSize > 0 && Lure.fish.display_size > 3 && Lure.state == 2)
                hide_ui = 1;
            """);

        ModObjects.Compile(compiler);
        return data;
    }
}
