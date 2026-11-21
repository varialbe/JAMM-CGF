using System.Diagnostics;
using Jamm.Core.Building;
using Jamm.Core.Management;
using Jamm.Core.Patching;
using UndertaleModLib;
using UndertaleModLib.Compiler;
using UndertaleModLib.Models;

namespace Jamm.Core.Tests;

public sealed class BaseModTests : IDisposable
{
    private readonly string root = Directory.CreateTempSubdirectory("jamm-base-tests-").FullName;
    private const string OverhaulObject = "OverhaulObject";
    private const string OverhaulCreate = "gml_Object_OverhaulObject_Create_0";
    private const string CatalogCreate = "gml_Object_CatalogData_Create_0";
    private const string Catalog = """
        object[0, 0] = BaseFish;
        name[0, 0] = "Base Fish";
        map[0, 0] = 0;
        desc[0, 0] = "Base Fish";
        """;

    [Fact]
    public void AddonsUseSmallPatchesAndBuildTheSameChangesAsManualEdits()
    {
        var (store, original, overhaul) = MakeOverhaul();
        string edit = Write("Edit.win", overhaul, EditObject);
        string fish = Write("Fish.win", overhaul, AddFish);
        string expected = Write("Expected.win", overhaul, data => { EditObject(data); AddFish(data); });
        var library = new ModLibrary(store);
        var foundation = library.Add(overhaul).Mod;
        var editMod = library.Add(edit).Mod;
        var fishMod = library.Add(fish).Mod;
        Assert.True(foundation.SuggestBase);
        Assert.Null(foundation.Role);
        var profiles = new ProfileLibrary(store);
        profiles.Create("pack", [editMod.Id, fishMod.Id, foundation.Id]);
        Assert.Equal("none", profiles.Group(profiles.Get("pack"))[2].Role.Role);
        profiles.SetRole("pack", foundation.Id, "base");
        profiles.SetRole("pack", editMod.Id, "addon", foundation.Id);
        profiles.SetRole("pack", fishMod.Id, "addon", foundation.Id);

        var persisted = new ProfileLibrary(new ManagerStore(store.Root)).Get("pack");
        Assert.Equal(new[] { foundation.Id, editMod.Id, fishMod.Id }, persisted.Mods);
        Assert.Equal(foundation.Id, persisted.Roles[editMod.Id].Base);
        var selection = ProfileSelection.Load(store, persisted, false, null, default);
        Assert.All(selection.Addons, input => Assert.Equal(foundation.Sha256, input.Patch.Base.Sha256));
        var editPatch = selection.Addons[0].Patch;
        Assert.Equal(OverhaulObject, Assert.Single(editPatch.Changes["objects"].Changed).Name);
        Assert.Equal(OverhaulCreate, Assert.Single(editPatch.Changes["code"].Changed).Name);
        Assert.Empty(editPatch.Changes["code"].Added);
        Assert.Empty(editPatch.Changes["rooms"].Changed);
        Assert.Empty(editPatch.Changes["scripts"].Changed);
        var fishPatch = selection.Addons[1].Patch;
        Assert.Equal("AddonFish", Assert.Single(fishPatch.Changes["objects"].Added).Name);
        Assert.Equal(CatalogCreate, Assert.Single(fishPatch.Changes["code"].Changed).Name);
        Assert.DoesNotContain(new ManagerOperations(store).Check("pack").Findings, f => f.Severity == "CONFLICT");

        string game = Directory.CreateDirectory(Path.Combine(root, "game")).FullName;
        string installed = Path.Combine(game, "data.win");
        File.Copy(original, installed);
        store.Set("gameDir", game);
        profiles.SetRepack("pack", false);
        var installer = new ProfileInstaller(store);
        var prepared = installer.Prepare("pack");
        var result = prepared.Build;
        string manualOutput = Path.Combine(root, "manual.win");
        using (var vanilla = GameFileLoader.Load(original))
        using (var manualSource = GameFileLoader.Load(expected))
        {
            var manualPatch = PatchBuilder.Create(vanilla, manualSource);
            ArchiveBuilder.Build(original, [new(manualPatch, expected)], manualOutput, noRepack: true);
        }
        using var manual = GameFileLoader.Load(manualOutput);
        using (var actual = GameFileLoader.Load(prepared.OutputPath))
        {
            Assert.Equal(manual.Data.GameObjects.Select(o => o.Name.Content), actual.Data.GameObjects.Select(o => o.Name.Content));
            var difference = PatchBuilder.Create(manual, actual);
            foreach (string family in new[] { "code", "objects", "rooms", "scripts", "sprites" })
            {
                var changes = difference.Changes[family];
                Assert.Empty(changes.Added);
                Assert.Empty(changes.Changed);
                Assert.Empty(changes.Removed);
            }
        }
        string vanillaHash = BuildFiles.Hash(original);
        Assert.Equal(vanillaHash, result.Base.Sha256);
        Assert.Equal(BuildFiles.Hash(overhaul), foundation.Sha256);
        installer.Install(prepared);
        Assert.True(installer.IsInstalled("pack"));
        Assert.Equal(vanillaHash, BuildFiles.Hash(installed + ".cgfmm-original"));
        Assert.Equal(vanillaHash, installer.Restore());
        Assert.Equal(vanillaHash, BuildFiles.Hash(installed));
        Assert.Empty(Directory.EnumerateFiles(Path.Combine(store.Root, "builds")));
    }

    [Fact]
    public void AddonConflictsAreScopedAndMixedVanillaModsWarn()
    {
        var (store, original, overhaul) = MakeOverhaul();
        string edit = Write("Edit.win", overhaul, EditObject);
        string fish = Write("Fish.win", overhaul, data =>
        {
            AddFish(data);
            Compile(data, OverhaulCreate, "counter = 99;\n");
        });
        string vanilla = Write("Vanilla.win", original, data => data.Rooms[0].Height = 950);
        var library = new ModLibrary(store);
        var foundation = library.Add(overhaul).Mod;
        var editMod = library.Add(edit).Mod;
        var fishMod = library.Add(fish).Mod;
        var vanillaMod = library.Add(vanilla).Mod;
        var profiles = new ProfileLibrary(store);
        profiles.Create("mixed", [vanillaMod.Id, editMod.Id, fishMod.Id, foundation.Id]);
        SetRoleCli("base", "mixed", foundation.Id, "--data-dir", store.Root);
        SetRoleCli("addon", "mixed", editMod.Id, foundation.Id, "--data-dir", store.Root);
        SetRoleCli("addon", "mixed", fishMod.Id, foundation.Id, "--data-dir", store.Root);
        var operation = new ManagerOperations(store);
        var report = operation.Check("mixed");
        var conflict = Assert.Single(report.Findings, f => f.Asset == OverhaulCreate && f.Severity == "CONFLICT");
        Assert.Equal(new[] { "Edit", "Fish" }, conflict.Mods);
        Assert.Equal("addons/code/" + OverhaulCreate, conflict.ResolutionKey);
        var merge = report.CodeMerges[conflict.CodeMergeKey];
        var hunk = Assert.Single(merge.Hunks, h => h.Status == "CONFLICT");
        Assert.Contains(report.Findings, f => f.Message.StartsWith("[CGF-BASE-001] Vanilla targets vanilla but profile uses base Overhaul; expect conflicts", StringComparison.Ordinal));
        var saved = profiles.Resolve("mixed", $"{conflict.ResolutionKey}#{hunk.Start}", editMod.Id);
        Assert.Equal("Edit", Assert.Single(saved.Resolutions).Value);
        string output = Path.Combine(root, "mixed.win");
        var build = RunCli("build", "--profile", "mixed", "--out", output, "--data-dir", store.Root);
        Assert.True(build.Exit == 0, build.Error);
        Assert.Contains("[Base]", build.Output);
        Assert.Contains("    Fish", build.Output);
        var checkedProfile = RunCli("check", "--profile", "mixed", "--details", conflict.CodeMergeKey, "--data-dir", store.Root);
        Assert.Equal(3, checkedProfile.Exit);
        Assert.Contains("choices:", checkedProfile.Output);
        using var actual = GameFileLoader.Load(output);
        Assert.Equal((uint)950, actual.Data.Rooms[0].Height);
        Assert.Contains("counter = 21;", PatchBuilder.ReadCode(actual, OverhaulCreate).Text);
        Assert.NotNull(actual.Data.GameObjects.ByName("AddonFish"));
    }

    [Fact]
    public void ManifestRolesValidateMissingDisabledAndMultipleBasesAndWarnOnMismatch()
    {
        var (store, original, overhaul) = MakeOverhaul();
        File.WriteAllText(Path.ChangeExtension(overhaul, ".jamm.json"), """{"id":"overhaul","version":"2","role":"base"}""");
        string addon = Write("Edit.win", overhaul, EditObject);
        File.WriteAllText(Path.ChangeExtension(addon, ".jamm.json"), $$"""{"base":"overhaul","baseVersion":"1","baseSha256":"{{new string('0', 64)}}"}""");
        var library = new ModLibrary(store);
        var edit = library.Add(addon).Mod;
        Assert.False(File.Exists(library.PatchPath(edit)));
        var profiles = new ProfileLibrary(store);
        profiles.Create("roles", [edit.Id]);
        var operation = new ManagerOperations(store);
        Assert.Contains("missing or disabled", Assert.Throws<InvalidDataException>(() => operation.Check("roles")).Message);
        var foundation = library.Add(overhaul, stableId: "overhaul-library").Mod;
        var adopted = profiles.Update("roles", [edit.Id, foundation.Id], [foundation.Id]);
        Assert.Equal(new[] { foundation.Id, edit.Id }, adopted.Mods);
        Assert.Contains("missing or disabled", Assert.Throws<InvalidDataException>(() => operation.Check("roles")).Message);
        profiles.Update("roles", adopted.Mods, []);
        Assert.Contains(operation.Check("roles").Findings, f => f.Message.StartsWith("[CGF-BASE-002]", StringComparison.Ordinal));
        Assert.Contains("expected SHA-256", Assert.Throws<InvalidDataException>(() => operation.Check("roles", strict: true)).Message);
        string output = Path.Combine(root, "strict.win");
        Assert.Throws<InvalidDataException>(() => operation.Build("roles", output, strict: true));
        Assert.False(File.Exists(output));
        var patch = ProfileSelection.Load(store, profiles.Get("roles"), false, null, default).Addons[0].Patch;
        var reused = ProfileSelection.Load(store, profiles.Get("roles"), false, null, default).Addons[0].Patch;
        Assert.Equal(patch.CreatedUtc, reused.CreatedUtc);
        string updated = Write("Updated.win", overhaul, data => data.GameObjects.ByName(OverhaulObject)!.Visible = false);
        File.Copy(Path.ChangeExtension(overhaul, ".jamm.json"), Path.ChangeExtension(updated, ".jamm.json"));
        var replacement = library.Add(updated, name: foundation.Name, stableId: foundation.Id).Mod;
        var refreshed = ProfileSelection.Load(store, profiles.Get("roles"), false, null, default).Addons[0].Patch;
        Assert.Equal(replacement.Sha256, refreshed.Base.Sha256);
        Assert.NotEqual(patch.Base.Sha256, refreshed.Base.Sha256);
        Assert.Equal(2, Directory.EnumerateFiles(Path.Combine(store.Root, "patches", edit.Id)).Count());

        string other = Write("Other.win", original, data => data.Rooms[0].Width = 1280);
        var extra = library.Add(other).Mod;
        profiles.Update("roles", [edit.Id, extra.Id, foundation.Id], []);
        profiles.SetRole("roles", extra.Id, "base");
        Assert.Contains("two or more", Assert.Throws<InvalidDataException>(() => operation.Check("roles")).Message);
        SetRoleCli("role", "roles", extra.Id, "none", "--data-dir", store.Root);
        SetRoleCli("role", "roles", edit.Id, "none", "--data-dir", store.Root);
        Assert.Contains(operation.Check("roles").Findings, f => f.Message.StartsWith("[CGF-BASE-001] Edit", StringComparison.Ordinal));
    }

    [Fact]
    public void ModKitManifestBuildsAgainstTheDeclaredOverhaul()
    {
        var (store, _, overhaul) = MakeOverhaul();
        File.WriteAllText(Path.ChangeExtension(overhaul, ".jamm.json"), """{"id":"overhaul","role":"base"}""");
        string package = Directory.CreateDirectory(Path.Combine(root, "modkit")).FullName;
        File.WriteAllText(Path.Combine(package, "jamm-mod.json"), """{"id":"modkit-addon","name":"ModKit addon","base":"overhaul","objects":[{"key":"marker","depth":0}]}""");
        var library = new ModLibrary(store);
        Assert.Contains("requires base", Assert.Throws<InvalidDataException>(() => library.Add(package)).Message);
        var foundation = library.Add(overhaul).Mod;
        var addon = library.Add(package).Mod;
        Assert.Equal("modkit-addon", addon.PackageId);
        Assert.Equal("addon", addon.Role?.Role);
        using var data = GameFileLoader.Load(library.Archive(addon));
        Assert.NotNull(data.Data.GameObjects.ByName(OverhaulObject));
        var profiles = new ProfileLibrary(store);
        profiles.Create("package", [addon.Id, foundation.Id]);
        var selection = ProfileSelection.Load(store, profiles.Get("package"), false, null, default);
        Assert.Equal(foundation.Sha256, Assert.Single(selection.Addons).Patch.Base.Sha256);
        // Only the package's own object is new relative to the overhaul; the overhaul's objects are inherited.
        Assert.DoesNotContain(selection.Addons[0].Patch.Changes["objects"].Added, a => a.Name == OverhaulObject);
        Assert.Single(selection.Addons[0].Patch.Changes["objects"].Added);
    }

    private (ManagerStore Store, string Original, string Overhaul) MakeOverhaul()
    {
        string original = Write("original.win", null, data =>
        {
            AddObject(data, "BaseFish");
            var catalog = AddObject(data, "CatalogData");
            catalog.EventHandlerFor(EventType.Create, 0u, data);
            Compile(data, CatalogCreate, Catalog);
            AddObject(data, "Props");
            for (int i = 0; i < 5; i++) AddScript(data, "fixture_" + i, "return 10;");
        });
        string overhaul = Write("Overhaul.win", original, data =>
        {
            for (int i = 0; i < 5; i++) Compile(data, "gml_Script_fixture_" + i, $"return {i + 12};");
            var props = data.GameObjects.ByName("Props")!;
            props.Depth = 48;
            data.GameObjects.Remove(props);
            data.GameObjects.Insert(0, props);
            Compile(data, CatalogCreate, Catalog);
            data.Rooms[0].Width = 1100;
            var obj = AddObject(data, OverhaulObject);
            obj.EventHandlerFor(EventType.Create, 0u, data);
            Compile(data, OverhaulCreate, "counter = 11;\n");
            AddScript(data, "overhaul_extra", "return 42;");
        });
        var store = new ManagerStore(Path.Combine(root, "store"));
        store.Set("base", original);
        return (store, original, overhaul);
    }

    private static void EditObject(UndertaleData data)
    {
        data.GameObjects.ByName(OverhaulObject)!.Depth = 120;
        Compile(data, OverhaulCreate, "counter = 21;\n");
    }

    private static void AddFish(UndertaleData data)
    {
        AddObject(data, "AddonFish").ParentId = data.GameObjects.ByName("BaseFish");
        Compile(data, CatalogCreate, Catalog + "\n" + """
            object[0, 1] = AddonFish;
            name[0, 1] = "Addon Fish";
            map[0, 1] = 1;
            desc[0, 1] = "Addon Fish";
            """);
    }

    private static UndertaleGameObject AddObject(UndertaleData data, string name)
    {
        var item = new UndertaleGameObject { Name = data.Strings.MakeString(name), Visible = true };
        data.GameObjects.Add(item);
        return item;
    }

    private static void AddScript(UndertaleData data, string name, string body)
    {
        var code = UndertaleCode.CreateEmptyEntry(data, "gml_Script_" + name);
        data.Scripts.Add(new() { Name = data.Strings.MakeString(name), Code = code });
        Compile(data, code.Name.Content, body);
    }

    private static void Compile(UndertaleData data, string name, string body)
    {
        var compiler = new CompileGroup(data);
        compiler.QueueCodeReplace(data.Code.ByName(name)!, body);
        var result = compiler.Compile();
        if (!result.Successful)
            Assert.Fail(string.Join("\n", result.Errors.Select(e => e.GenerateDetailedMessage())));
    }

    private string Write(string name, string? ancestor, Action<UndertaleData> edit)
    {
        using var source = ancestor is null ? null : GameFileLoader.Load(ancestor);
        using var fresh = source is null ? UndertaleData.CreateNew() : null;
        var data = source?.Data ?? fresh!;
        if (fresh is not null)
        {
            data.GeneralInfo.Timestamp = 1692207312;
            data.GeneralInfo.Major = 1;
            data.GeneralInfo.BytecodeVersion = 16;
            data.GeneralInfo.Build = 1773;
        }
        edit(data);
        string path = Path.Combine(root, name);
        using var stream = File.Create(path);
        UndertaleIO.Write(stream, data);
        return path;
    }

    private static void SetRoleCli(params string[] args)
    {
        var result = RunCli(["profile", .. args]);
        Assert.True(result.Exit == 0, result.Error);
    }

    private static (int Exit, string Output, string Error) RunCli(params string[] args)
    {
        string cli = typeof(Jamm.Cli.CheckCommand).Assembly.Location;
        using var process = Process.Start(new ProcessStartInfo("dotnet", [cli, .. args])
            { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false })!;
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        process.WaitForExit();
        return (process.ExitCode, output.GetAwaiter().GetResult(), error.GetAwaiter().GetResult());
    }

    public void Dispose() => Directory.Delete(root, true);
}
