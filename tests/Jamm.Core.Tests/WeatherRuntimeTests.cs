using Jamm.Core.Building;
using Jamm.Core.ModKit;
using Jamm.Core.Patching;
using Jamm.ModMaker;
using UndertaleModLib;
using UndertaleModLib.Compiler;
using UndertaleModLib.Models;

namespace Jamm.Core.Tests;

/// <summary>Tests weather validation and the full ModKit-to-profile build without a game archive.</summary>
public sealed class WeatherRuntimeTests
{
    [Theory]
    [InlineData("""{"id":"rain","name":"Rain","duration":[400,200]}""", "duration")]
    [InlineData("""{"id":"rain","name":"Rain","schedule":{"phases":["midnight"]}}""", "phases[0]")]
    [InlineData("""{"id":"rain","name":"Rain","schedule":{"phases":[]}}""", "phases")]
    [InlineData("""{"id":"rain","name":"Rain","water":{"wave":-2}}""", "water.wave")]
    [InlineData("""{"id":"rain","name":"Rain","fish":["absent"]}""", "fish[0]")]
    [InlineData("""{"id":"rain","name":"Rain","background":[{"sprite":"absent"}]}""", "background[0].sprite")]
    [InlineData("""{"id":"rain","name":"Rain","sky":"#green1"}""", "sky")]
    [InlineData("""{"id":"rain","name":"Rain","particles":{"kind":"rain","count":3.5}}""", "particles.count")]
    [InlineData("""{"id":"rain","name":"Rain","music":{"sound":"missing.ogg"}}""", "music.sound")]
    public void ReportsWeatherField(string weather, string field)
    {
        using var temp = new ModKitAreaTests.AreaTestDirectory();
        string dir = temp.Package($$"""{"id":"weather-test","name":"Test","weathers":[{{weather}}]}""");
        Assert.Contains(field, Assert.Throws<InvalidDataException>(() => ModKitPackage.Load(dir)).Message);
    }

    [Fact]
    public void DefaultIsDaytimeWithDayFishReplacement()
    {
        using var temp = new ModKitAreaTests.AreaTestDirectory();
        var package = ModKitPackage.Load(temp.Package("""
            {"id":"weather-test","name":"Test","weathers":[{"id":"rain","name":"Rain"}]}
            """));
        var weather = Assert.Single(package.Weathers);
        Assert.Equal(7, WeatherRuntime.PhaseMask(weather.Phases));
        Assert.True(weather.ReplaceDayFish);
    }

    [Fact]
    public void WeatherAnchorsRejectMissingAndAmbiguousEdits()
    {
        Assert.Equal("before new after", WeatherRuntime.ReplaceAnchor("Test", "before old after", "old", "new"));
        Assert.Throws<InvalidDataException>(() => WeatherRuntime.ReplaceAnchor("Test", "old old", "old", "new"));
        Assert.Throws<InvalidDataException>(() => WeatherRuntime.ReplaceAnchor("Test", "unrelated", "old", "new"));
    }

    [Fact]
    public void TwoPackagesWithTheSameLocalWeatherIdShareOneController()
    {
        using var temp = new ModKitAreaTests.AreaTestDirectory();
        string basePath = temp.PathFor("base.win");
        using (var data = CreateWorld()) Save(data, basePath);
        using var baseline = GameFileLoader.Load(basePath);

        string consolePath = temp.PathFor("console.win");
        using (var console = GameFileLoader.Load(basePath))
        {
            ConsoleMod.Apply(console.Data);
            Save(console.Data, consolePath);
        }
        using var consoleFile = GameFileLoader.Load(consolePath);
        var consoleInput = new BuildInput(PatchBuilder.Create(baseline, consoleFile, "Console"), consolePath);
        var first = BuildInput("first-weather", "First", "day");
        var second = BuildInput("second-weather", "Second", "night");
        string output = temp.PathFor("combined.win");
        var result = ArchiveBuilder.Build(basePath, [second, first, consoleInput], output);
        Assert.Contains(result.Plan.Decisions, d => d.Contains("one controller for 2 weather(s)", StringComparison.Ordinal));
        using var merged = GameFileLoader.Load(output);
        Assert.Single(merged.Data.GameObjects, o => o.Name.Content == ConsoleMod.ObjectName);
        var control = merged.Data.GameObjects.ByName(WeatherRuntime.Controller)!;
        Assert.True(control.Persistent);
        Assert.Single(merged.Data.Rooms.ByName("NewWorld")!.GameObjects, i => i.ObjectDefinition == control);
        var text = ModObjects.Decompile(merged.Data, merged.Data.Code.ByName($"gml_Object_{WeatherRuntime.Controller}_Create_0")!);
        Assert.Contains("\"first-weather:rain\"", text);
        Assert.Contains("\"second-weather:rain\"", text);
        Assert.Contains("weather_rule[0, 1] = 2;", text);
        Assert.Contains("weather_rule[1, 1] = 8;", text);

        string population = PatchBuilder.ReadCode(merged, "gml_Script_jamm_weather_population").Text;
        Assert.Contains("jamm_fish_sonar_init(fish)", population);
        Assert.Contains("if (instance_exists(fish))", population);
        Assert.True(population.IndexOf("jamm_fish_sonar_init(fish)", StringComparison.Ordinal)
            < population.IndexOf("fish.base_lunge_mod = 1", StringComparison.Ordinal));
        Assert.Contains("s5 = (size + 0.5) / 3.5", PatchBuilder.ReadCode(merged, FishSonarRuntime.Code).Text);
        Assert.Single(merged.Data.Scripts, s => s.Name.Content == FishSonarRuntime.Script);

        BuildInput BuildInput(string id, string displayName, string phase)
        {
            string dir = temp.Package($$"""
                {"id":"{{id}}","name":"{{displayName}}","weathers":[
                  {"id":"rain","name":"{{displayName}} Rain","schedule":{"phases":["{{phase}}"]} } ] }
                """);
            string path = temp.PathFor(id + ".win");
            ModKitBuilder.Build(dir, basePath, path);
            using var source = GameFileLoader.Load(path);
            var patch = PatchJson.Deserialize(PatchJson.Serialize(PatchBuilder.Create(baseline, source, displayName)));
            Assert.Empty(patch.Changes["code"].Changed);
            Assert.Empty(patch.Changes["rooms"].Changed);
            return new(patch, path);
        }
    }

    [Fact]
    public void WeatherSceneWorksWithoutWeatherPack()
    {
        using var temp = new ModKitAreaTests.AreaTestDirectory();
        string inputPath = temp.PathFor("vanilla.win");
        string outputPath = temp.PathFor("weather-bench.win");
        using (var data = CreateWeatherBenchWorld()) Save(data, inputPath);

        int rc = Jamm.ModMaker.Program.Main(["bench", inputPath, outputPath, "--scene", "weather"]);
        Assert.Equal(0, rc);
        Assert.True(File.Exists(outputPath));

        using var reloaded = GameFileLoader.Load(outputPath);
        Assert.NotNull(reloaded.Data.GameObjects.ByName("obj_jamm_bench_weather"));
        string create = ModObjects.Decompile(reloaded.Data, reloaded.Data.Code.ByName("gml_Object_obj_jamm_bench_weather_Create_0")!);
        string beginStep = ModObjects.Decompile(reloaded.Data, reloaded.Data.Code.ByName("gml_Object_obj_jamm_bench_weather_Step_1")!);
        string endStep = ModObjects.Decompile(reloaded.Data, reloaded.Data.Code.ByName("gml_Object_obj_jamm_bench_weather_Step_2")!);
        string user0 = ModObjects.Decompile(reloaded.Data, reloaded.Data.Code.ByName("gml_Object_obj_jamm_bench_weather_Other_10")!);

        Assert.Contains("weather_id,active", create);
        Assert.Contains("fish_count,sonar_missing", create);
        Assert.Contains("SonarPanel.active = 1", beginStep);
        Assert.Contains("SonarPanel.shouldDraw = 1", beginStep);
        Assert.Contains("view_visible[3] = true", beginStep);
        Assert.Contains("SonarPanel.active = 1", endStep);
        Assert.Contains("view_visible[3] = true", endStep);
        Assert.Contains("variable_instance_exists(id, \"s5\")", user0);
        Assert.Contains("sonar_missing", user0);
        Assert.Contains("fish_count", user0);
        Assert.DoesNotContain("jamm_weather_command", beginStep);
        Assert.DoesNotContain("obj_jamm_weather", user0);
    }

    [Fact]
    public void WeatherSceneEnablesSonarAndChecksWeatherFish()
    {
        using var temp = new ModKitAreaTests.AreaTestDirectory();
        string basePath = temp.PathFor("base.win");
        using (var data = CreateWeatherBenchWorld()) Save(data, basePath);
        using var baseline = GameFileLoader.Load(basePath);

        string consolePath = temp.PathFor("console.win");
        using (var console = GameFileLoader.Load(basePath))
        {
            ConsoleMod.Apply(console.Data);
            Save(console.Data, consolePath);
        }
        using var consoleFile = GameFileLoader.Load(consolePath);
        var consoleInput = new BuildInput(PatchBuilder.Create(baseline, consoleFile, "Console"), consolePath);

        string weatherDir = temp.Package("""
            {
              "id": "weather-test",
              "name": "Weather Test",
              "weathers": [
                { "id": "rainy", "name": "Rainy" },
                { "id": "super-sunny", "name": "Super Sunny" },
                { "id": "windy", "name": "Windy" },
                { "id": "fog", "name": "Fog" }
              ]
            }
            """);
        string weatherPath = temp.PathFor("weather-mod.win");
        ModKitBuilder.Build(weatherDir, basePath, weatherPath);
        using var weatherFile = GameFileLoader.Load(weatherPath);
        var weatherInput = new BuildInput(PatchBuilder.Create(baseline, weatherFile, "Weather"), weatherPath);

        string mergedPath = temp.PathFor("merged.win");
        ArchiveBuilder.Build(basePath, [weatherInput, consoleInput], mergedPath);

        string benchPath = temp.PathFor("merged-bench.win");
        int rc = Jamm.ModMaker.Program.Main(["bench", mergedPath, benchPath, "--scene", "weather"]);
        Assert.Equal(0, rc);

        using var reloaded = GameFileLoader.Load(benchPath);
        string beginStep = ModObjects.Decompile(reloaded.Data, reloaded.Data.Code.ByName("gml_Object_obj_jamm_bench_weather_Step_1")!);
        string user0 = ModObjects.Decompile(reloaded.Data, reloaded.Data.Code.ByName("gml_Object_obj_jamm_bench_weather_Other_10")!);

        Assert.Contains("SonarPanel.active = 1", beginStep);
        Assert.Contains("view_visible[3] = true", beginStep);
        Assert.Contains("\"rainy\"", beginStep);
        Assert.Contains("\"super-sunny\"", beginStep);
        Assert.Contains("\"windy\"", beginStep);
        Assert.Contains("\"fog\"", beginStep);
        Assert.Contains("jamm_weather_command", beginStep);
        Assert.Contains("jamm_weather_find", beginStep);
        Assert.Contains("with (obj_jamm_weather)", beginStep);
        Assert.Contains("bench_controller_ready", beginStep);
        Assert.Contains("jamm_weather_owner", beginStep);
        Assert.Contains("weather_species", beginStep);
        Assert.Contains("variable_instance_exists(id, \"s5\")", user0);
        Assert.Contains("sonar_missing", user0);
        Assert.Contains("fish_count", user0);
        Assert.Contains("jamm_weather_current()", user0);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void StressSceneRunsWithPerfHooksOptional(bool includePerf)
    {
        using var temp = new ModKitAreaTests.AreaTestDirectory();
        string inputPath = temp.PathFor("stress-input.win");
        string outputPath = temp.PathFor("stress-output.win");
        using (var data = CreateStressWorld(includePerf)) Save(data, inputPath);

        var error = new StringWriter();
        TextWriter originalError = Console.Error;
        int rc;
        try
        {
            Console.SetError(error);
            rc = Jamm.ModMaker.Program.Main(["bench", inputPath, outputPath, "--scene", "stress", "--duration", "180"]);
        }
        finally { Console.SetError(originalError); }
        Assert.True(rc == 0, error.ToString());

        using var reloaded = GameFileLoader.Load(outputPath);
        string beginStep = ModObjects.Decompile(reloaded.Data,
            reloaded.Data.Code.ByName("gml_Object_obj_jamm_bench_stress_Step_1")!);
        Assert.Contains("perf_hooks,perf,perf_skipped", beginStep);
        Assert.Equal(includePerf, beginStep.Contains("global.jamm_perf_mode", StringComparison.Ordinal));
        Assert.Equal(includePerf, beginStep.Contains("instance_exists(obj_jamm_perf)", StringComparison.Ordinal));
    }

    private static UndertaleData CreateStressWorld(bool includePerf)
    {
        var data = CreateWorld();
        foreach (string name in new[] { "obj_jamm_weather", "Bo", "Gallina", "Skrimp", "Cowfish", "Grumper" })
            data.GameObjects.Add(new() { Name = data.Strings.MakeString(name) });
        if (includePerf) data.GameObjects.Add(new() { Name = data.Strings.MakeString("obj_jamm_perf") });
        var compiler = new CompileGroup(data);
        foreach ((string name, string body) in new[]
        {
            ("jamm_weather_command", "return 0;"),
            ("jamm_weather_current", "return \"clear\";"),
            ("fish_default_lunge", "return 0;")
        })
        {
            var code = UndertaleCode.CreateEmptyEntry(data, "gml_Script_" + name);
            data.Scripts.Add(new() { Name = data.Strings.MakeString(name), Code = code });
            compiler.QueueCodeReplace(code, body);
        }
        ArchiveVerifier.Compile(compiler, "synthetic stress scene world");
        return data;
    }

    private static UndertaleData CreateWeatherBenchWorld()
    {
        var data = CreateWorld();
        foreach (string name in new[] { "SonarPanel", "Camera", "Boat", "LightingSystem" })
            data.GameObjects.Add(new() { Name = data.Strings.MakeString(name) });

        var compiler = new CompileGroup(data);
        var stormCode = UndertaleCode.CreateEmptyEntry(data, "gml_Script_is_it_storming");
        data.Scripts.Add(new() { Name = data.Strings.MakeString("is_it_storming"), Code = stormCode });
        compiler.QueueCodeReplace(stormCode, "return false;");
        var ambianceStep = data.GameObjects.ByName("AmbianceController")!.EventHandlerFor(EventType.Step, 0u, data);
        compiler.QueueCodeReplace(ambianceStep, "isStorming = is_it_storming();\nminimumOvercast = 0;");
        ArchiveVerifier.Compile(compiler, "synthetic weather bench world");
        return data;
    }

    private static UndertaleData CreateWorld()
    {
        var data = UndertaleData.CreateNew();
        data.GeneralInfo.BytecodeVersion = 16;
        data.Rooms[0].Name = data.Strings.MakeString("NewWorld");
        data.Rooms.Add(new() { Name = data.Strings.MakeString("room11") });
        data.Sprites.Add(new() { Name = data.Strings.MakeString("spr"), Width = 1, Height = 1 });
        foreach (string name in new[]
        {
            "Girl", "Fish", "Sea", "ParallaxBackground", "AmbianceController", "SaveManager", "Collidable", "CatalogData",
            "Rod", "Lure", "RodData", "LureData", "BoatData", "ToolData", "UpgradeData", "GearData", "Icons"
        })
            data.GameObjects.Add(new() { Name = data.Strings.MakeString(name) });

        var compiler = new CompileGroup(data);
        Event("Girl", EventType.Step, 0, "audio_sound_gain(background_music, gain * power(background_music_volume, 2), 0);");
        Event("Fish", EventType.Step, 2, "speed = spd;");
        Event("Sea", EventType.Step, 0, "x = origin + (sin(phase) * SCALE);");
        Event("ParallaxBackground", EventType.Draw, 0, """
            draw_rectangle_colour(0, 0, 100, 100, sky_color, sky_color, sky_color, sky_color, 0);
            alpha = 1 - power(1 - value, 2);
            """);
        Script("is_it_night", "return false;");
        Script("x_pos_to_y_off", "var ty = argument0; return ty;");
        Script("color_from_depth", "return make_color_rgb(val * 255, val * 255, val * val * 240);");
        ArchiveVerifier.Compile(compiler, "synthetic weather world");
        return data;

        void Event(string name, EventType type, uint subtype, string gml) =>
            compiler.QueueCodeReplace(data.GameObjects.ByName(name)!.EventHandlerFor(type, subtype, data), gml);
        void Script(string name, string gml)
        {
            var code = UndertaleCode.CreateEmptyEntry(data, "gml_Script_" + name);
            data.Scripts.Add(new() { Name = data.Strings.MakeString(name), Code = code });
            compiler.QueueCodeReplace(code, gml);
        }
    }

    private static void Save(UndertaleData data, string path)
    {
        using var stream = File.Open(path, FileMode.CreateNew, FileAccess.Write);
        UndertaleIO.Write(stream, data);
    }
}
