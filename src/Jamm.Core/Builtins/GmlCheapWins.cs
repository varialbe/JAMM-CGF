using UndertaleModLib;
using UndertaleModLib.Compiler;
using UndertaleModLib.Models;
using System.Text.RegularExpressions;

namespace Jamm.ModMaker;

/// <summary>Independent, experimental build-time GML transformations for the cheap-win experiments.</summary>
public static class GmlCheapWins
{
    private static readonly HashSet<string> VisualOnlyDecor = new(StringComparer.Ordinal)
    {
        "Bubble", "Bubble_red", "Bubble_cyan", "Bubble_orange", "Bubble_jamm_cave_expansion"
    };

    /// <summary>Applies only the requested experimental transformations and returns a decision for each enabled flag.</summary>
    public static IReadOnlyList<string> Apply(UndertaleData data, List<UndertaleCode> edits,
        bool masksRect = false, bool fishLod = false, bool decorAnimNative = false,
        bool cacheHot = false, bool particlesFx = false)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(edits);
        var decisions = new List<string>();
        var compiler = new CompileGroup(data);
        var queued = new HashSet<UndertaleCode>();
        var pendingSources = new Dictionary<UndertaleCode, string>();

        if (masksRect) ApplyMasks();
        if (fishLod) ApplyFishLod();
        if (decorAnimNative) ApplyDecorAnimation();
        if (cacheHot) ApplyHotCache();
        if (particlesFx) ApplyParticles();
        if (queued.Count > 0) ModObjects.Compile(compiler);
        return decisions;

        void ApplyMasks()
        {
            var fish = data.GameObjects.ByName("Fish");
            var changed = new HashSet<UndertaleSprite>();
            foreach (var obj in data.GameObjects)
            {
                bool isFish = fish is not null && InheritsFrom(obj, fish);
                if (!isFish && !VisualOnlyDecor.Contains(obj.Name.Content)) continue;
                var sprite = obj.Sprite;
                if (sprite is null || sprite.SepMasks != UndertaleSprite.SepMaskType.Precise) continue;
                changed.Add(sprite);
            }
            foreach (var sprite in changed) sprite.SepMasks = UndertaleSprite.SepMaskType.AxisAlignedRect;
            string names = changed.Count == 0 ? "none"
                : string.Join(", ", changed.Select(s => s.Name.Content).Order(StringComparer.Ordinal));
            decisions.Add($"[CGF-CHEAP-MASKS] {(changed.Count == 0 ? "no precise eligible masks" : $"changed {changed.Count} sprite mask(s)")}; sprites={names}");
        }

        void ApplyFishLod()
        {
            var code = data.Code.ByName("gml_Object_Fish_Step_2");
            if (code is null)
            {
                decisions.Add("[CGF-CHEAP-FISH-LOD] skipped (Fish_Step_2 is missing)");
                return;
            }
            string source = GetSource(code);
            const string marker = "switch (state)";
            if (source.Split(marker, StringSplitOptions.None).Length != 2)
            {
                decisions.Add("[CGF-CHEAP-FISH-LOD] skipped (Fish_Step_2 shape differs)");
                return;
            }
            string replacement = """
                if (!variable_instance_exists(id, "jamm_lod_tick")) jamm_lod_tick = 0;
                var jamm_lod_far = point_distance(x, y, Camera.x, Camera.y) > max(view_wview[view_current], view_hview[view_current]) * 1.5;
                jamm_lod_tick += 1;
                if (!jamm_lod_far || (jamm_lod_tick mod 2) == 0)
                {
                    if (jamm_lod_far)
                    {
                        for (var jamm_lod_alarm = 0; jamm_lod_alarm < 12; jamm_lod_alarm++)
                            if (alarm[jamm_lod_alarm] > 0) alarm[jamm_lod_alarm] -= 1;
                    }
                switch (state)
                """;
            source = source.Replace(marker, replacement, StringComparison.Ordinal);
            int switchStart = source.IndexOf("switch (state)", StringComparison.Ordinal);
            int switchEnd = FindMatchingBrace(source, source.IndexOf('{', switchStart));
            if (switchEnd < 0)
            {
                decisions.Add("[CGF-CHEAP-FISH-LOD] skipped (Fish_Step_2 switch is unbalanced)");
                return;
            }
            source = source.Insert(switchEnd + 1, "\n}\n");
            Queue(code, source);
            decisions.Add("[CGF-CHEAP-FISH-LOD] enabled (far fish state AI every second End Step; engine motion stays per-frame and alarms catch up on AI frames; custom state counters/interactions can differ)");
        }

        void ApplyDecorAnimation()
        {
            var code = data.Code.ByName("gml_Object_Weed_Step_0");
            if (code is null)
            {
                decisions.Add("[CGF-CHEAP-DECOR-ANIM] skipped (Weed_Step_0 is missing)");
                return;
            }
            string source = GetSource(code);
            const string before = "point_distance(x, y, Camera.x, Camera.y) > 5000";
            const string after = "((x - Camera.x) * (x - Camera.x) + (y - Camera.y) * (y - Camera.y)) > 25000000";
            if (source.Split(before, StringSplitOptions.None).Length != 2)
            {
                decisions.Add("[CGF-CHEAP-DECOR-ANIM] skipped (Weed offscreen guard differs)");
                return;
            }
            Queue(code, source.Replace(before, after, StringComparison.Ordinal));
            decisions.Add("[CGF-CHEAP-DECOR-ANIM] enabled (Weed offscreen distance uses squared coordinates; retains existing offscreen skip)");
        }

        void ApplyHotCache()
        {
            int changed = 0;
            var fishStep = data.Code.ByName("gml_Object_Fish_Step_2");
            if (fishStep is not null)
            {
                string source = GetSource(fishStep);
                const string lureDistance = "((x - Lure.x) * (x - Lure.x)) + ((y - Lure.y) * (y - Lure.y))";
                if (source.Split(lureDistance, StringSplitOptions.None).Length == 2)
                {
                    source = source.Replace(lureDistance,
                        "((x - jamm_lure_x) * (x - jamm_lure_x)) + ((y - jamm_lure_y) * (y - jamm_lure_y))",
                        StringComparison.Ordinal);
                    const string insertAt = "lure_distance_squared =";
                    source = source.Replace(insertAt,
                        "var jamm_lure_x = Lure.x;\nvar jamm_lure_y = Lure.y;\n" + insertAt,
                        StringComparison.Ordinal);
                    const string cameraDistance = "point_distance(x, y, Camera.x, Camera.y)";
                    const string timerBlock = "if (check_timer >= 6)\n{";
                    if (source.Contains(cameraDistance, StringComparison.Ordinal)
                        && source.Split(timerBlock, StringSplitOptions.None).Length == 2)
                    {
                        source = source.Replace(timerBlock,
                            timerBlock + "\n    var jamm_camera_x = Camera.x;\n    var jamm_camera_y = Camera.y;",
                            StringComparison.Ordinal)
                            .Replace(cameraDistance, "point_distance(x, y, jamm_camera_x, jamm_camera_y)", StringComparison.Ordinal);
                    }
                    Queue(fishStep, source);
                    changed++;
                }
            }

            var sonar = data.Code.ByName("gml_Object_SonarPanel_Step_1");
            if (sonar is not null)
            {
                string source = GetSource(sonar);
                const string withFish = "with (Fish)";
                const string advancedCheck = "SonarPanelAdvanced.advancedDrawEnabled || other.cave_sonar";
                if (source.Split(withFish, StringSplitOptions.None).Length == 2
                    && source.Split(advancedCheck, StringSplitOptions.None).Length == 2)
                {
                    source = source.Replace(withFish,
                        "jamm_sonar_advanced = SonarPanelAdvanced.advancedDrawEnabled;\njamm_cave_sonar = cave_sonar;\n" + withFish,
                        StringComparison.Ordinal)
                        .Replace(advancedCheck, "other.jamm_sonar_advanced || other.jamm_cave_sonar", StringComparison.Ordinal);
                    Queue(sonar, source);
                    changed++;
                }
            }
            decisions.Add($"[CGF-CHEAP-CACHE] {(changed == 0 ? "no supported hot expression found" : $"cached repeated values in {changed} hot event(s)")}; cache is event-local to preserve update order");
        }

        void ApplyParticles()
        {
            var bubble = data.GameObjects.ByName("Bubble");
            var systems = data.GameObjects.ByName("ParticleSystems");
            var create = data.Code.ByName("gml_Object_ParticleSystems_Create_0");
            if (bubble?.Sprite is null || systems is null || create is null
                || bubble.Solid || HasEventsInParentChain(bubble)
                || !ModObjects.Decompile(data, create).Contains("water_splash = part_type_create()", StringComparison.Ordinal)
                || data.Code.ByName("gml_Script_jamm_cw_bubble") is not null
                || data.Functions.ByName("jamm_cw_bubble") is not null)
            {
                decisions.Add("[CGF-CHEAP-PARTICLES] skipped (Bubble sprite or reusable particle system is unavailable; PotEffect remains ineligible)");
                return;
            }

            var replacements = new List<(UndertaleCode Code, string Source, int Count)>();
            int bubbleReferences = 0;
            var call = new Regex(@"(?m)^(?<indent>[ \t]*)instance_create\(\s*(?<x>[^,;\n]+)\s*,\s*(?<y>[^,;\n]+)\s*,\s*Bubble\s*\)\s*;",
                RegexOptions.CultureInvariant);
            var bubbleName = new Regex(@"\bBubble\b", RegexOptions.CultureInvariant);
            foreach (var code in data.Code)
            {
                string source = GetSource(code);
                var matches = call.Matches(source);
                bubbleReferences += bubbleName.Matches(source).Count;
                if (matches.Count == 0) continue;
                string changed = call.Replace(source, m =>
                    $"{m.Groups["indent"].Value}jamm_cw_bubble({m.Groups["x"].Value.Trim()}, {m.Groups["y"].Value.Trim()});");
                replacements.Add((code, changed, matches.Count));
            }
            if (replacements.Count == 0)
            {
                decisions.Add("[CGF-CHEAP-PARTICLES] skipped (no standalone Bubble creation calls found; PotEffect remains ineligible)");
                return;
            }
            int directCreations = replacements.Sum(r => r.Count);
            if (bubbleReferences != directCreations)
            {
                decisions.Add("[CGF-CHEAP-PARTICLES] skipped (Bubble also has non-creation code references, so gameplay/collision use is not ruled out)");
                return;
            }

            string spriteName = bubble.Sprite.Name.Content;
            var helper = UndertaleCode.CreateEmptyEntry(data, "gml_Script_jamm_cw_bubble");
            data.Scripts.Add(new UndertaleScript { Name = data.Strings.MakeString("jamm_cw_bubble"), Code = helper });
            Queue(helper, $$"""
                if (instance_exists(ParticleSystems))
                {
                    if (!variable_global_exists("jamm_cw_bubble_type"))
                    {
                        global.jamm_cw_bubble_type = part_type_create();
                        part_type_sprite(global.jamm_cw_bubble_type, {{spriteName}}, true, false, false);
                        part_type_alpha2(global.jamm_cw_bubble_type, 1, 0);
                        part_type_speed(global.jamm_cw_bubble_type, 0, 0, 0, 0);
                        part_type_life(global.jamm_cw_bubble_type, 45, 45);
                    }
                    part_particles_create(ParticleSystems.background, argument0, argument1, global.jamm_cw_bubble_type, 1);
                }
                """);
            foreach (var replacement in replacements) Queue(replacement.Code, replacement.Source);
            int count = replacements.Sum(r => r.Count);
            decisions.Add($"[CGF-CHEAP-PARTICLES] converted {count} standalone Bubble creation(s) in {replacements.Count} code entry/entries; 45-tick visual particle replaces the instance (PotEffect remains ineligible)");
        }

        void Queue(UndertaleCode code, string source)
        {
            if (queued.Add(code)) edits.Add(code);
            pendingSources[code] = source;
            compiler.QueueCodeReplace(code, source);
        }

        string GetSource(UndertaleCode code) => pendingSources.TryGetValue(code, out string? source)
            ? source
            : ModObjects.Decompile(data, code);
    }

    private static bool InheritsFrom(UndertaleGameObject obj, UndertaleGameObject root)
    {
        for (var current = obj; current is not null; current = current.ParentId)
            if (ReferenceEquals(current, root)) return true;
        return false;
    }

    private static bool HasEventsInParentChain(UndertaleGameObject obj)
    {
        for (var current = obj; current is not null; current = current.ParentId)
            if (current.Events.Any(group => group.Count > 0)) return true;
        return false;
    }

    private static int FindMatchingBrace(string source, int open)
    {
        if (open < 0) return -1;
        int depth = 0;
        for (int i = open; i < source.Length; i++)
        {
            if (source[i] == '{') depth++;
            else if (source[i] == '}' && --depth == 0) return i;
        }
        return -1;
    }
}
