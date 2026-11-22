using System.Diagnostics;
using Jamm.Core.Building;
using Jamm.Core.Management;
using Jamm.ModMaker;
using UndertaleModLib;
using UndertaleModLib.Compiler;
using UndertaleModLib.Models;
using UndertaleModLib.Util;

namespace Jamm.Core.Tests;

public sealed class BuiltinModTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "jamm-builtins-" + Guid.NewGuid().ToString("N"));
    private readonly ManagerStore store;
    private readonly string original;

    public BuiltinModTests()
    {
        Directory.CreateDirectory(root);
        store = new ManagerStore(Path.Combine(root, "manager"));
        original = Path.Combine(root, "original.win");
        WriteBase(original);
        store.Set("base", original);
    }

    private static void WriteBase(string path, uint build = 1773, bool perf = true, Action<UndertaleData>? edit = null)
    {
        using var data = UndertaleData.CreateNew();
        data.GeneralInfo.Major = 1; data.GeneralInfo.Minor = 0; data.GeneralInfo.Build = build;
        data.GeneralInfo.Timestamp = 1692207312; data.GeneralInfo.BytecodeVersion = 16;
        foreach (string name in new[] { "Girl", "Rod", "Lure", "Boat", "Fish", "QuestEngine", "RodData", "LureData", "BoatData", "ToolData", "UpgradeData", "GearData", "Icons" })
            ModObjects.Create(data, name, false);
        foreach (string name in new[] { "NewWorld", "room11" }) data.Rooms.Add(new() { Name = data.Strings.MakeString(name) });
        data.Paths.Add(new() { Name = data.Strings.MakeString("synthetic_path") });
        var compiler = new CompileGroup(data);
        compiler.QueueCodeReplace(data.GameObjects.ByName("QuestEngine").EventHandlerFor(EventType.Step, 1, data), "quest_counter = 0;");
        compiler.QueueCodeReplace(data.GameObjects.ByName("Girl").EventHandlerFor(EventType.Step, 0u, data), "a = delta_time; b = delta_time; c = delta_time;");
        if (perf)
        {
            foreach (var (name, source) in new Dictionary<string, string>
            {
                ["gml_Object_Magmer_Draw_0"] = "light.image_alpha = 1 - power(1 - heat_level, 2);",
                ["gml_Object_Volteen_Other_13"] = "light.add_blend = 4210752; light.sub_blend = 4210752;",
                ["gml_Object_Volteen_Other_10"] = "if (active) { light.add_blend = 0; light.sub_blend = 0; } else { light.add_blend = 4210752; light.sub_blend = 4210752; }",
                ["gml_Object_Fish_Step_2"] = "if (y > 5000 && !deepfish) { x += 1; y += 1; }",
                ["gml_Script_fish_check_trawler"] = "if (collision_line(Net.top.x, Net.top.y, Net.bot.x, Net.bot.y, id, true, false)) { x += 1; y += 1; }",
                ["gml_Script_fish_avoid_fish"] = "x = (argument1 * argument1) + 1; y = (100000 * argument2) + 1; z = (100000 * argument2) + 2;",
                ["gml_Script_fish_flock"] = "if (pal > 0) { impulse = point_distance(x, y, pal.x, pal.y) - argument0; hspeed += lengthdir_x(impulse * argument1, point_direction(x, y, pal.x, pal.y)); vspeed += lengthdir_y(impulse * argument1, point_direction(x, y, pal.x, pal.y)); return 1; } return 0;",
                ["gml_Script_fish_lunge"] = "distance_to_lure = point_distance(x, y, Lure.x, Lure.y); fish_set_lunge_mod(); if (check_lunge()) { state = 1; }",
                ["gml_Script_check_lunge"] = "if (point_distance(x, y, Lure.x, Lure.y) < 100) { return 1; } return 0;",
                ["gml_Script_fish_set_lunge_mod"] = "lunge_mod = base_lunge_mod;",
                ["gml_Object_SceneItem_Step_2"] = "scene_depth = depth + 18; x = home_x;"
            }) compiler.QueueCodeReplace(UndertaleCode.CreateEmptyEntry(data, name), source);
            foreach (string name in new[] { "fish_lunge", "check_lunge", "fish_set_lunge_mod" })
                data.Scripts.Add(new UndertaleScript { Name = data.Strings.MakeString(name), Code = data.Code.ByName("gml_Script_" + name)! });
        }
        ModObjects.Compile(compiler);
        edit?.Invoke(data);
        using var stream = File.Create(path); UndertaleIO.Write(stream, data);
    }

    [Fact]
    public void PerfHoistsRepeatedFlockDirectionWithoutChangingItsGuard()
    {
        WriteBase(original);
        using var stream = File.OpenRead(original);
        using var data = UndertaleIO.Read(stream, (_, _) => { }, _ => { });
        var flock = data.Code.ByName("gml_Script_fish_flock")!;
        int callsBefore = CountCalls(flock, "point_direction");
        var check = data.Code.ByName("gml_Script_check_lunge")!;
        int lungeCheckInstructionsBefore = data.Code.ByName("gml_Script_fish_lunge")!.Instructions.Count + check.Instructions.Count;
        int flockInstructionsBefore = flock.Instructions.Count;

        PerfMod.Apply(data, applyScheduler: false, applyDecorSleep: false);
        var microCompiler = new CompileGroup(data);
        _ = FishMicroOptimizations.Queue(data, microCompiler);
        ModObjects.Compile(microCompiler);

        flock = data.Code.ByName("gml_Script_fish_flock")!;
        int callsAfter = CountCalls(flock, "point_direction");
        int instructionsAfter = flock.Instructions.Count;
        int lungeCheckInstructionsAfter = data.Code.ByName("gml_Script_fish_lunge")!.Instructions.Count
            + data.Code.ByName("gml_Script_check_lunge")!.Instructions.Count;
        string source = ModObjects.Decompile(data, flock);
        Assert.True(instructionsAfter < flockInstructionsBefore,
            $"Expected fewer fish_flock VM instructions after caching the shared direction ({flockInstructionsBefore} -> {instructionsAfter}).");
        Assert.Equal(2, callsBefore);
        Assert.Equal(1, callsAfter);
        Assert.Contains("if (pal > 0)", source);
        Assert.Contains("var jamm_perf_flock_direction = point_direction(x, y, pal.x, pal.y);", source);
        Assert.True(source.IndexOf("var jamm_perf_flock_direction", StringComparison.Ordinal)
            > source.IndexOf("if (pal > 0)", StringComparison.Ordinal));
        Assert.Equal(0, CountCalls(data.Code.ByName("gml_Script_check_lunge")!, "point_distance"));
        Assert.Contains("check_lunge(distance_to_lure)", ModObjects.Decompile(data, data.Code.ByName("gml_Script_fish_lunge")!));
        Assert.True(lungeCheckInstructionsAfter < lungeCheckInstructionsBefore,
            $"Expected fewer lunge/check VM instructions ({lungeCheckInstructionsBefore} -> {lungeCheckInstructionsAfter}).");
    }

    [Fact]
    public void FishMicroIsOptInAndOnlyChangesTheSelectedPerfProfileBuild()
    {
        var perf = new BuiltinMods(store).Add("perf").Mod;
        var library = new ModLibrary(store);
        var input = new BuildInput(library.Patch(perf), library.Archive(perf));
        using (var plainArchive = File.OpenRead(library.Archive(perf)))
        using (var plainData = UndertaleIO.Read(plainArchive, (_, _) => { }, _ => { }))
            Assert.Equal(2, CountCalls(plainData.Code.ByName("gml_Script_fish_flock")!, "point_direction"));
        string defaultOutput = Path.Combine(root, "perf-default.win");
        string microOutput = Path.Combine(root, "perf-micro.win");

        ArchiveBuilder.Build(original, [input], defaultOutput, noRepack: true,
            perfSettings: new PerfSettings(DecorSleep: false));
        Assert.Equal(BuildFiles.Hash(original), BuildFiles.Hash(defaultOutput));

        var result = ArchiveBuilder.Build(original, [input], microOutput, noRepack: true,
            perfSettings: new PerfSettings(DecorSleep: false, FishMicro: true));
        Assert.Contains(result.Plan.Decisions, decision => decision.Contains("fish_flock: cached", StringComparison.Ordinal));
        using var stream = File.OpenRead(microOutput);
        using var optimized = UndertaleIO.Read(stream, (_, _) => { }, _ => { });
        Assert.Equal(1, CountCalls(optimized.Code.ByName("gml_Script_fish_flock")!, "point_direction"));
        Assert.Contains("check_lunge(distance_to_lure)", ModObjects.Decompile(optimized, optimized.Code.ByName("gml_Script_fish_lunge")!));
    }

    [Fact]
    public void FishMicroSkipsDistanceReuseWhenMergedSourceAddsAnotherCheckLungeCaller()
    {
        WriteBase(original);
        using var stream = File.OpenRead(original);
        using var data = UndertaleIO.Read(stream, (_, _) => { }, _ => { });
        var externalCaller = UndertaleCode.CreateEmptyEntry(data, "gml_Script_mod_check_lunge");
        data.Scripts.Add(new UndertaleScript { Name = data.Strings.MakeString("mod_check_lunge"), Code = externalCaller });
        var callerCompiler = new CompileGroup(data);
        callerCompiler.QueueCodeReplace(externalCaller, "check_lunge();");
        ModObjects.Compile(callerCompiler);

        var microCompiler = new CompileGroup(data);
        var edits = new List<UndertaleCode>();
        string decision = FishMicroOptimizations.Queue(data, microCompiler, edits);
        ModObjects.Compile(microCompiler);

        Assert.Contains("fish_lunge/check_lunge: skipped (another merged caller: gml_Script_mod_check_lunge)", decision);
        Assert.Equal(1, CountCalls(data.Code.ByName("gml_Script_check_lunge")!, "point_distance"));
        Assert.Contains(data.Code.ByName("gml_Script_fish_flock")!, edits);
    }

    [Fact]
    public void FishMicroDoesNotTreatCheckLungeCatswimAsAnotherCheckLungeCaller()
    {
        WriteBase(original);
        using var stream = File.OpenRead(original);
        using var data = UndertaleIO.Read(stream, (_, _) => { }, _ => { });
        var catswim = UndertaleCode.CreateEmptyEntry(data, "gml_Script_check_lunge_catswim");
        var follow = UndertaleCode.CreateEmptyEntry(data, "gml_Script_fish_default_follow");
        var callerCompiler = new CompileGroup(data);
        callerCompiler.QueueCodeReplace(catswim, "return 1;");
        callerCompiler.QueueCodeReplace(follow, "if (!check_lunge_catswim()) return 0;");
        data.Scripts.Add(new UndertaleScript { Name = data.Strings.MakeString("check_lunge_catswim"), Code = catswim });
        data.Scripts.Add(new UndertaleScript { Name = data.Strings.MakeString("fish_default_follow"), Code = follow });
        ModObjects.Compile(callerCompiler);

        var microCompiler = new CompileGroup(data);
        string decision = FishMicroOptimizations.Queue(data, microCompiler);
        ModObjects.Compile(microCompiler);

        Assert.Contains("fish_lunge/check_lunge: reused", decision);
        Assert.Equal(0, CountCalls(data.Code.ByName("gml_Script_check_lunge")!, "point_distance"));
    }

    private static int CountCalls(UndertaleCode code, string name) => code.Instructions.Count(instruction =>
        instruction.Kind == UndertaleInstruction.Opcode.Call
        && instruction.ValueFunction?.Name.Content == name);

    [Fact]
    public void BuiltinOnlyProfileRepacksAndInvalidatesAnUnrepackedInstallation()
    {
        WriteBase(original, edit: data =>
        {
            var page = new UndertaleEmbeddedTexture { TextureData = new() { Image = new GMImage(32, 32).ConvertToPng() } };
            var item = new UndertaleTexturePageItem { TexturePage = page, SourceWidth = 1, SourceHeight = 1,
                TargetWidth = 1, TargetHeight = 1, BoundingWidth = 1, BoundingHeight = 1 };
            data.EmbeddedTextures.Add(page);
            data.TexturePageItems.Add(item);
            data.Backgrounds.Add(new() { Name = data.Strings.MakeString("synthetic_background"), Texture = item });
            data.EmbeddedTextures.Add(new() { TextureData = new() { Image = page.TextureData.Image } });
        });
        string game = Path.Combine(root, "game");
        Directory.CreateDirectory(game);
        File.Copy(original, Path.Combine(game, "data.win"));
        store.Set("gameDir", game);
        var mod = new BuiltinMods(store).Add("perf").Mod;
        var profiles = new ProfileLibrary(store);
        profiles.Create("perf", [mod.Id]);
        profiles.SetRepack("perf", enabled: false);
        var installer = new ProfileInstaller(store);
        var old = installer.Prepare("perf");
        Assert.Equal(2, old.Build.Counts[AssetKind.TexturePages]);
        Assert.DoesNotContain(old.Build.Plan.Decisions, decision => decision.StartsWith("[CGF-TEX-001]", StringComparison.Ordinal));
        installer.Install(old);
        Assert.True(installer.IsInstalled("perf"));

        profiles.SetRepack("perf", enabled: true);
        Assert.False(installer.IsInstalled("perf"));
        var repacked = installer.Prepare("perf");
        Assert.Equal(1, repacked.Build.Counts[AssetKind.TexturePages]);
        Assert.Contains(repacked.Build.Plan.Decisions, decision => decision.StartsWith("[CGF-TEX-001] Texture", StringComparison.Ordinal));
        installer.Install(repacked);
        Assert.True(installer.IsInstalled("perf"));
    }

    [Theory]
    [InlineData("console")]
    [InlineData("hd-lite")]
    [InlineData("smooth")]
    [InlineData("perf")]
    public void GenerationImportsCachesAndSurvivesSanitizing(string id)
    {
        string before = BuildFiles.Hash(original);
        List<string> progress = [];
        var service = new BuiltinMods(store);
        var first = service.Add(id, progress.Add);
        Assert.Equal("builtin-" + id, first.Mod.Id);
        Assert.NotEmpty(first.Mod.Description!);
        var library = new ModLibrary(store);
        Assert.Equal(id != "perf", library.SanitizerReport(first.Mod).IsJammMod);
        Assert.Empty(library.SanitizerReport(first.Mod).Removed);
        progress.Clear();
        Assert.True(service.Add(id, progress.Add).AlreadyAdded);
        Assert.Contains(progress, p => p.Contains("Using cached"));
        Assert.Equal(before, BuildFiles.Hash(original));
        Assert.Single(library.Metadata());
    }

    [Fact]
    public void ChangedBaseAndDamagedCacheRegenerateWithoutChangingProfileIds()
    {
        var service = new BuiltinMods(store);
        var first = service.Add("console");
        var profiles = new ProfileLibrary(store); profiles.Create("test", [first.Mod.Id]);
        string cached = Directory.GetFiles(Path.Combine(store.Root, "builtin"), "*.win").Single();
        File.WriteAllText(cached, "damaged");
        List<string> progress = [];
        service.Add("console", progress.Add);
        Assert.Contains(progress, p => p.StartsWith("Generating"));
        // A previous generator version is never used as a cache hit.
        File.Move(cached, cached.Replace($"-{BuiltinMods.GeneratorVersion}-", "-old-"));
        progress.Clear(); service.Add("console", progress.Add);
        Assert.Contains(progress, p => p.StartsWith("Generating"));
        WriteBase(original, 1774);
        var next = service.Add("console");
        Assert.Equal(first.Mod.Id, next.Mod.Id);
        Assert.NotEqual(first.Mod.BaseSha256, next.Mod.BaseSha256);
        Assert.Equal(next.Mod.Id, Assert.Single(profiles.Get("test").Mods));
        Assert.True(new ModLibrary(store).SanitizerReport(next.Mod).IsJammMod);
    }

    [Fact]
    public void EmptyPerfPatchCopiesBaseWithoutAddingGenericRuntimeCode()
    {
        var mod = new BuiltinMods(store).Add("perf").Mod;
        var library = new ModLibrary(store);
        var patch = library.Patch(mod);
        string output = Path.Combine(root, "perf-only.win");

        var result = ArchiveBuilder.Build(original,
            [new BuildInput(patch, library.Archive(mod))], output, noRepack: true);

        Assert.Equal(BuildFiles.Hash(original), BuildFiles.Hash(output));
        Assert.Equal(BuildFiles.Hash(original), result.Verification!.Inspection.Sha256);
        using var loaded = GameFileLoader.Load(output);
        Assert.Null(loaded.Data.GameObjects.ByName("obj_jamm_perf"));
        Assert.Null(loaded.Data.GameObjects.ByName("obj_jamm_catalog"));
    }

    [Fact]
    public void CancellationAndUnsupportedBaseLeaveLibraryEmpty()
    {
        Assert.Throws<OperationCanceledException>(() => new BuiltinMods(store).Add("console", cancellationToken: new(true)));
        WriteBase(original, perf: false);
        Assert.Throws<InvalidDataException>(() => new BuiltinMods(store).Add("perf"));
        Assert.Empty(new ModLibrary(store).Metadata());
        Assert.Empty(Directory.GetFiles(Path.Combine(store.Root, "builtin"), "*.tmp"));
    }

    [Fact]
    public void CliListsAddsAndRejectsUnknownGenerators()
    {
        (int Exit, string Text) Run(params string[] args)
        {
            var info = new ProcessStartInfo(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet")
            { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
            info.ArgumentList.Add(typeof(Jamm.Cli.OutputFile).Assembly.Location);
            foreach (string arg in args.Concat(new[] { "--data-dir", store.Root })) info.ArgumentList.Add(arg);
            using var process = Process.Start(info)!;
            var stdout = process.StandardOutput.ReadToEndAsync(); var stderr = process.StandardError.ReadToEndAsync();
            process.WaitForExit(); return (process.ExitCode, stdout.Result + stderr.Result);
        }
        var list = Run("builtin", "list"); Assert.Equal(0, list.Exit);
        foreach (var item in BuiltinMods.Catalog) Assert.Contains(item.Name, list.Text);
        Assert.Contains("Hotkeys:", list.Text);
        Assert.Contains("F8", list.Text);
        Assert.Contains("F7", list.Text);
        Assert.Contains("F11", list.Text);
        Assert.Contains("F12 or backtick", list.Text);
        Assert.Contains("Console commands:", list.Text);
        Assert.Contains("unlockall", list.Text);
        Assert.Contains("unlockgear", list.Text);
        Assert.Contains("maxlevel", list.Text);
        Assert.Contains("level <n>", list.Text);
        Assert.Contains("catalog all", list.Text);
        Assert.Contains("money <n>", list.Text);
        Assert.Equal(0, Run("builtin", "add", "console").Exit);
        Assert.Equal(0, Run("builtin", "add", "console").Exit);
        Assert.Equal(1, Run("builtin", "add", "invalid").Exit);
        Assert.Single(new ModLibrary(store).Metadata());
    }

    [Fact]
    public void BuiltinsMergeWithIndependentThirdPartyModThroughNormalBuilder()
    {
        var service = new BuiltinMods(store);
        var library = new ModLibrary(store);
        var mods = BuiltinMods.Catalog.Select(d => service.Add(d.Id).Mod).ToList();
        string third = Path.Combine(root, "third.win");
        using (var input = File.OpenRead(original))
        using (var data = UndertaleIO.Read(input))
        {
            var obj = ModObjects.Create(data, "third_party_controller", true);
            var compiler = new CompileGroup(data);
            compiler.QueueCodeReplace(obj.EventHandlerFor(EventType.Create, 0u, data), "third_party_value = 123;");
            ModObjects.Compile(compiler);
            using var output = File.Create(third); UndertaleIO.Write(output, data);
        }
        mods.Add(library.Add(third).Mod);
        string merged = Path.Combine(root, "merged.win");
        var result = ArchiveBuilder.Build(original, mods.Select(m => new BuildInput(library.Patch(m), Path.Combine(store.Root, "mods", m.Id, "data.win"))).ToArray(), merged);
        Assert.NotNull(result.Verification);
        using var loaded = GameFileLoader.Load(merged);
        Assert.Null(loaded.Data.GameObjects.ByName("obj_jamm_perf"));
        foreach (string name in new[] { "obj_jamm_smooth", "obj_jamm_hd", "obj_jamm_console", "third_party_controller" })
            Assert.NotNull(loaded.Data.GameObjects.ByName(name));
    }

    [Fact]
    public void ConsoleCatalogAllSetsMaxLifeAndStaleBuiltinCacheAutoRefreshesOnCheckAndInstallCheck()
    {
        using (var stream = File.Open(original, FileMode.Open, FileAccess.ReadWrite))
        {
            var data = UndertaleIO.Read(stream);
            ModObjects.Create(data, "CatalogData", false);
            stream.SetLength(0);
            stream.Position = 0;
            UndertaleIO.Write(stream, data);
        }

        var console = new BuiltinMods(store).Add("console").Mod;
        using (var loaded = GameFileLoader.Load(Path.Combine(store.Root, "mods", console.Id, "data.win")))
        {
            string step1 = Jamm.Core.Patching.PatchBuilder.ReadCode(loaded, "gml_Object_obj_jamm_console_Step_1").Text;
            Assert.Contains("CatalogData.life[jamm_i, jamm_j] = max(CatalogData.life[jamm_i, jamm_j], 3);", step1);
            Assert.Contains("CatalogData.hide[jamm_i, jamm_j] = 0;", step1);
            Assert.Contains("CatalogData.all_fish_revealed = 1;", step1);
            Assert.Contains("min(ToolData.nItems, array_length_1d(ToolData.unlocked))", step1);
            Assert.Contains("jamm_i < array_length_1d(ToolData.item)", step1);
            Assert.Contains("instance_exists(ToolData.item[jamm_i])", step1);
            Assert.Contains("ToolData.item[jamm_i].active = 1;", step1);
        }

        // Simulate an older GeneratorVersion cached console (receipt/version mismatch against current GeneratorVersion).
        var profiles = new ProfileLibrary(store);
        profiles.Create("default", ["builtin-console"]);
        profiles.Use("default");
        string sha = BuildFiles.Hash(original);
        string currentCached = store.Path("builtin", $"console-{BuiltinMods.GeneratorVersion}-{sha}.win");
        File.WriteAllText(store.Path("builtin", $"console-{BuiltinMods.GeneratorVersion}-{sha}.win.sha256"), "stale-hash-from-v2");

        var report = new ManagerOperations(store).Check("default");
        Assert.NotNull(report);
        Assert.True(File.Exists(currentCached));
        Assert.Equal(BuildFiles.Hash(currentCached), File.ReadAllText(currentCached + ".sha256"));

        // Also verify ProfileInstaller.IsInstalled refreshes a stale builtin receipt/cache automatically:
        string gameDir = Path.Combine(root, "game");
        Directory.CreateDirectory(gameDir);
        File.Copy(original, Path.Combine(gameDir, "data.win"));
        store.Set("gameDir", gameDir);
        File.WriteAllText(currentCached + ".sha256", "stale-hash-again");
        Assert.False(new ProfileInstaller(store).IsInstalled("default"));
        Assert.Equal(BuildFiles.Hash(currentCached), File.ReadAllText(currentCached + ".sha256"));
    }

    public void Dispose() => Directory.Delete(root, true);
}
