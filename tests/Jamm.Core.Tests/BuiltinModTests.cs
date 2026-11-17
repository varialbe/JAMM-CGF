using System.Diagnostics;
using Jamm.Core.Building;
using Jamm.Core.Management;
using Jamm.ModMaker;
using UndertaleModLib;
using UndertaleModLib.Compiler;
using UndertaleModLib.Models;

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

    private static void WriteBase(string path, uint build = 1773, bool perf = true)
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
                ["gml_Object_SceneItem_Step_2"] = "scene_depth = depth + 18; x = home_x;"
            }) compiler.QueueCodeReplace(UndertaleCode.CreateEmptyEntry(data, name), source);
        }
        ModObjects.Compile(compiler);
        using var stream = File.Create(path); UndertaleIO.Write(stream, data);
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
        Assert.True(library.SanitizerReport(first.Mod).IsJammMod);
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
        foreach (string name in new[] { "obj_jamm_perf", "obj_jamm_smooth", "obj_jamm_hd", "obj_jamm_console", "third_party_controller" })
            Assert.NotNull(loaded.Data.GameObjects.ByName(name));
    }

    public void Dispose() => Directory.Delete(root, true);
}
