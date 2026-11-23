using System.IO.Compression;
using System.Diagnostics;
using System.Text.Json;
using Jamm.Core.Building;
using Jamm.Core.Management;
using Jamm.ModMaker;
using UndertaleModLib;
using UndertaleModLib.Models;
using UndertaleModLib.Compiler;

namespace Jamm.Core.Tests;

/// <summary>Temporary-directory management, archive import, profile safety and runner command regressions.</summary>
public sealed class ManagementTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "cgfmm-management-" + Guid.NewGuid().ToString("N"));
    private readonly ManagerStore store;
    private readonly string original;
    private readonly string source;

    public ManagementTests()
    {
        Directory.CreateDirectory(directory);
        store = new ManagerStore(Path.Combine(directory, "manager"));
        original = Path.Combine(directory, "original.win");
        source = Path.Combine(directory, "mod.win");
        WriteArchive(original, 0);
        WriteArchive(source, 5);
        store.Set("base", original);
    }

    [Fact]
    public void WeakPcPresetPersistsAndRestoresPreviousSettings()
    {
        var library = new ModLibrary(store);
        var perf = library.Add(source, "JAMM Perf", stableId: "builtin-perf").Mod;
        string hdPath = Path.Combine(directory, "hd.win");
        WriteArchive(hdPath, 9);
        var hd = library.Add(hdPath, "JAMM HD Lite", stableId: "builtin-hd-lite").Mod;
        var profiles = new ProfileLibrary(store);
        profiles.Create("laptop", [hd.Id, perf.Id]);
        profiles.SetBuiltin("laptop", "perf", false);
        profiles.SetRepack("laptop", false);
        profiles.SetPerf("laptop", fishGrid: false, interval: 6);
        var before = profiles.Get("laptop");

        Assert.Equal(0, Jamm.Cli.ManagementCommand.Run("profile", ["preset", "laptop", "weak-pc"], store.Root));
        var active = new ProfileLibrary(new ManagerStore(store.Root)).Get("laptop");
        Assert.Equal("weak-pc", active.Preset);
        Assert.Equal([perf.Id], active.EnabledMods);
        Assert.False(active.NoRepack);
        Assert.True(active.Perf.StartEnabled);
        Assert.False(active.Perf.FishGrid);
        Assert.True(active.Perf.Interval > 4);
        Assert.Equal(active.BeforeWeakPc, profiles.SetPreset("laptop", "weak-pc").BeforeWeakPc);

        Assert.Equal(0, Jamm.Cli.ManagementCommand.Run("profile", ["preset", "laptop", "default"], store.Root));
        var reverted = profiles.Get("laptop");
        Assert.Equal(before.Preset, reverted.Preset);
        Assert.Equal(before.Mods, reverted.Mods);
        Assert.Equal(before.EnabledMods, reverted.EnabledMods);
        Assert.Equal(before.NoRepack, reverted.NoRepack);
        Assert.Equal(before.Perf, reverted.Perf);
        Assert.Null(reverted.BeforeWeakPc);
    }

    [Fact]
    public void ExperimentalPerfFlagsPersistAndCanBeChangedByTheCli()
    {
        var mod = new ModLibrary(store).Add(source, "Content").Mod;
        var profiles = new ProfileLibrary(store);
        profiles.Create("cheap-wins", [mod.Id]);
        profiles.SetPerf("cheap-wins", masksRect: true, fishLod: true,
            decorAnimNative: true, cacheHot: true, particlesFx: true);

        var enabled = profiles.Get("cheap-wins").Perf;
        Assert.True(enabled.MasksRect);
        Assert.True(enabled.FishLod);
        Assert.True(enabled.DecorAnimNative);
        Assert.True(enabled.CacheHot);
        Assert.True(enabled.ParticlesFx);

        Assert.Equal(0, Jamm.Cli.ManagementCommand.Run("profile",
            ["perf", "cheap-wins", "fish-lod", "off"], store.Root));
        Assert.False(new ProfileLibrary(new ManagerStore(store.Root)).Get("cheap-wins").Perf.FishLod);
        Assert.False(new PerfSettings().MasksRect);
        Assert.False(new PerfSettings().FishLod);
        Assert.False(new PerfSettings().DecorAnimNative);
        Assert.False(new PerfSettings().CacheHot);
        Assert.False(new PerfSettings().ParticlesFx);
    }

    [Fact]
    public void WeakPcIndividualOverridesWinAndPresetAloneInvalidatesInstall()
    {
        var library = new ModLibrary(store);
        var content = library.Add(source, "Content").Mod;
        string perfPath = Path.Combine(directory, "perf.win");
        WriteArchive(perfPath, 9);
        var perf = library.Add(perfPath, "JAMM Perf", stableId: "builtin-perf").Mod;
        var profiles = new ProfileLibrary(store);
        profiles.Create("laptop", [content.Id, perf.Id]);
        profiles.SetBuiltin("laptop", "perf", false);
        string game = Path.Combine(directory, "game");
        Directory.CreateDirectory(game);
        File.Copy(original, Path.Combine(game, "data.win"));
        store.Set("gameDir", game);
        var installer = new ProfileInstaller(store);
        installer.Install(installer.Prepare("laptop"));
        Assert.True(installer.IsInstalled("laptop"));

        profiles.SetPreset("laptop", "weak-pc");
        profiles.SetBuiltin("laptop", "perf", false);
        profiles.SetPerf("laptop", startEnabled: false, fishGrid: true, interval: 4);
        Assert.False(installer.IsInstalled("laptop"));
        profiles.SetRepack("laptop", false);
        profiles.SetPerf("laptop", fishGrid: false, interval: 13);
        Assert.Equal(13, profiles.Get("laptop").Perf.Interval);
        var reverted = profiles.SetPreset("laptop", "default");
        Assert.Equal([content.Id], reverted.EnabledMods);
        Assert.False(reverted.Perf.FishGrid);
        Assert.Equal(13, reverted.Perf.Interval);
        Assert.True(reverted.NoRepack);
    }

    [Theory]
    [InlineData(null, "[Perf]\r\nMode=1\r\n")]
    [InlineData("[Perf]\r\nmode = 0\r\nInterval=27\r\n", "[Perf]\r\nmode = 0\r\nInterval=27\r\n")]
    [InlineData("[perf]\n; Keep this value\nInterval=27\n", "[perf]\nMode=1\n; Keep this value\nInterval=27\n")]
    [InlineData("[Perf]", "[Perf]\r\nMode=1\r\n")]
    [InlineData("[Other]\r\nMode=0\r\n", "[Other]\r\nMode=0\r\n[Perf]\r\nMode=1\r\n")]
    public void PerfIniSeedsOnlyMissingMode(string? originalIni, string expected)
    {
        string save = Path.Combine(directory, "save");
        string path = Path.Combine(save, "jamm_perf.ini");
        if (originalIni is not null)
        {
            Directory.CreateDirectory(save);
            File.WriteAllText(path, originalIni);
        }
        PerfIni.SeedDirectory(save);
        PerfIni.SeedDirectory(save);
        Assert.Equal(expected, File.ReadAllText(path));
        Assert.Single(Directory.EnumerateFiles(save));
    }

    [Fact]
    public void ProfileEditsRetainChoicesAndUnknownFieldsUntilWinnerIsRemoved()
    {
        var library = new ModLibrary(store);
        var a = library.Add(source, "Abyss").Mod;
        string second = Path.Combine(directory, "other.win"); WriteArchive(second, 9);
        var b = library.Add(second, "Other").Mod;
        var profiles = new ProfileLibrary(store); profiles.Create("everything", [a.Id, b.Id]);
        profiles.Resolve("everything", "code/synthetic", a.Id);
        string path = Path.Combine(store.Root, "profiles", "everything.json");
        var json = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(path))!;
        json["future"] = System.Text.Json.Nodes.JsonNode.Parse("{\"nested\":[1,true,\"value\"]}");
        json.AsObject().Remove("noRepack"); // Older profiles lack the setting.
        File.WriteAllText(path, json.ToJsonString());
        Assert.False(profiles.Get("everything").NoRepack);
        Assert.Equal(0, Jamm.Cli.ManagementCommand.Run("profile", ["repack", "everything", "off"], store.Root));
        List<string> notices = [];
        profiles.Update("everything", [b.Id, a.Id], [a.Id], notices.Add);
        Assert.Equal("Abyss", profiles.Get("everything").Resolutions["code/synthetic"]);
        profiles.Update("everything", [a.Id, b.Id], [], notices.Add);
        profiles.Rename("everything", "renamed");
        Assert.True(profiles.Get("renamed").NoRepack);
        Assert.Equal(0, Jamm.Cli.ManagementCommand.Run("profile", ["repack", "renamed", "on"], store.Root));
        Assert.False(profiles.Get("renamed").NoRepack);
        Assert.Empty(notices);
        Assert.Equal(json["future"]!.ToJsonString(), System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(Path.Combine(store.Root, "profiles", "renamed.json")))!["future"]!.ToJsonString());
        profiles.Update("renamed", [b.Id], [], notices.Add);
        Assert.Empty(profiles.Get("renamed").Resolutions);
        Assert.Contains("code/synthetic", Assert.Single(notices));
    }

    [Fact]
    public void LegacyUnlockRepairIsOffByDefaultAndPersistsPerProfileAndMod()
    {
        var mod = new ModLibrary(store).Add(source, "Legacy").Mod;
        var profiles = new ProfileLibrary(store);
        profiles.Create("repair", [mod.Id]);
        profiles.Create("original", [mod.Id]);
        Assert.Null(profiles.Get("repair").UnlockFixMods);

        Assert.Equal(0, Jamm.Cli.ManagementCommand.Run("profile", ["unlockfix", "repair", "Legacy", "on"], store.Root));
        Assert.Equal(mod.Id, Assert.Single(new ProfileLibrary(store).Get("repair").UnlockFixMods!));
        Assert.Null(profiles.Get("original").UnlockFixMods);
        profiles.Update("repair", [mod.Id], [mod.Id]);
        Assert.Equal(mod.Id, Assert.Single(profiles.Get("repair").UnlockFixMods!));
        Assert.Equal(0, Jamm.Cli.ManagementCommand.Run("profile", ["unlockfix", "repair", mod.Id, "off"], store.Root));
        Assert.Null(profiles.Get("repair").UnlockFixMods);

        profiles.SetUnlockFix("repair", mod.Id, true);
        profiles.Update("repair", [], []);
        Assert.Null(profiles.Get("repair").UnlockFixMods);
    }

    [Theory]
    [InlineData("check")]
    [InlineData("resolve")]
    [InlineData("apply")]
    public void OutdatedPatchIsLazilyRefreshedFromStoredCopy(string operation)
    {
        var library = new ModLibrary(store); var a = library.Add(source, "Abyss").Mod;
        string second = Path.Combine(directory, "other.win"); WriteArchive(second, 9);
        var b = library.Add(second, "Other").Mod;
        var profiles = new ProfileLibrary(store); profiles.Create("everything", [a.Id, b.Id]);
        string path = Path.Combine(store.Root, "patches", a.Id + ".cgfmm-patch.json");
        var patch = library.Patch(a);
        File.WriteAllText(path, Jamm.Core.Patching.PatchJson.Serialize(patch with { AssetTypesVersion = 0 }));
        string stale = File.ReadAllText(path);
        File.Delete(source); // Only the library copy remains available.
        List<string> progress = [];
        if (operation == "check") new ManagerOperations(store).Check("everything", progress: progress.Add);
        else if (operation == "resolve") profiles.Resolve("everything", "code/synthetic", a.Id, progress.Add);
        else
        {
            string game = Path.Combine(directory, "game"); Directory.CreateDirectory(game);
            File.Copy(original, Path.Combine(game, "data.win")); store.Set("gameDir", game);
            var installer = new ProfileInstaller(store);
            var prepared = installer.Prepare("everything", progress.Add); installer.Discard(prepared);
        }
        Assert.Contains(progress, p => p.Contains("Refreshing outdated"));
        Assert.NotEqual(stale, File.ReadAllText(path));
        Assert.Equal(Jamm.Core.Patching.PatchJson.AssetTypesVersion, library.Patch(a).AssetTypesVersion);
        string refreshed = File.ReadAllText(path); library.Patch(a);
        Assert.Equal(refreshed, File.ReadAllText(path));
        Assert.Equal(patch.Mod.Sha256, BuildFiles.Hash(Path.Combine(store.Root, "mods", a.Id, "data.win")));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FailedOrCancelledRefreshKeepsOldCache(bool missingCopy)
    {
        var library = new ModLibrary(store); var mod = library.Add(source, "Abyss").Mod;
        string path = Path.Combine(store.Root, "patches", mod.Id + ".cgfmm-patch.json");
        File.WriteAllText(path, Jamm.Core.Patching.PatchJson.Serialize(library.Patch(mod) with { AssetTypesVersion = 0 }));
        string stale = File.ReadAllText(path);
        if (missingCopy)
        {
            File.Delete(Path.Combine(store.Root, "mods", mod.Id, "data.win"));
            Assert.Contains("Add the original mod again", Assert.Throws<InvalidDataException>(() => library.Patch(mod)).Message);
        }
        else
        {
            using var cancel = new CancellationTokenSource();
            Assert.Throws<OperationCanceledException>(() => library.Patch(mod, _ => cancel.Cancel(), cancel.Token));
        }
        Assert.Equal(stale, File.ReadAllText(path));
    }

    [Fact]
    public void ConfigPersistsValidatesAndHonorsExplicitDataDirectory()
    {
        store.Set("wine.mode", "proton");
        store.Set("steam.appId", "343780");
        store.Set("proton.compatDataPath", Path.Combine(directory, "compat space"));
        var fresh = new ManagerStore(store.Root).Config();
        Assert.Equal("proton", fresh.Wine.Mode);
        Assert.Equal(original, fresh.Base);
        Assert.Throws<ArgumentException>(() => store.Set("wine.mode", "shell"));
        Assert.Throws<ArgumentException>(() => store.Set("steam.appId", "0"));
        Assert.Throws<ArgumentException>(() => store.Set("unknown", "value"));
        Assert.Equal(store.Root, new ManagerStore(store.Root).Root);
    }

    [Fact]
    public void WinImportCachesPatchListsAndRemovesWithoutChangingInputs()
    {
        string before = BuildFiles.Hash(source), baseHash = BuildFiles.Hash(original);
        var library = new ModLibrary(store);
        Assert.Empty(library.List());
        var result = library.Add(source, "Test Mod");
        Assert.Equal("mod.win", result.Mod.SourceFileName);
        Assert.Equal(before, result.Mod.Sha256);
        var listed = Assert.Single(library.List());
        Assert.True(listed.PatchCached);
        Assert.True(listed.BaseHashMatches);
        Assert.True(listed.TimestampMatches);
        Assert.True(listed.VersionMatches);
        Assert.True(library.Add(source).AlreadyAdded);
        Assert.Equal("Test Mod", library.Find("mod").Name);
        Assert.Equal(before, BuildFiles.Hash(source));
        Assert.Equal(baseHash, BuildFiles.Hash(original));
        library.Remove("Test Mod");
        Assert.Empty(library.List());
    }

    [Theory]
    [InlineData("mod,v2")]
    [InlineData("mod\nname")]
    [InlineData("mod\tname")]
    public void ImportRejectsUnreferenceableDisplayNamesBeforeCopying(string name)
    {
        var library = new ModLibrary(store);
        Assert.Contains("--name", Assert.Throws<ArgumentException>(() => library.Add(source, name)).Message);
        // Default names receive the same validation, before opening the source.
        Assert.Throws<ArgumentException>(() => library.Add(Path.Combine(directory, name + ".win")));
        Assert.Empty(Directory.GetFileSystemEntries(Path.Combine(store.Root, "mods")));
    }

    [Fact]
    public void DefaultModNamesWithSpacesCanBeUsedInProfiles()
    {
        string spaced = Path.Combine(directory, "Test Mod.win");
        File.Copy(source, spaced);
        var mod = new ModLibrary(store).Add(spaced).Mod;
        Assert.Equal(mod.Id, Assert.Single(new ProfileLibrary(store).Create("spaces", ["Test Mod"]).Mods));
    }

    [Fact]
    public void NestedZipImportsOnlyDataWinAndRejectsTraversalEvenInUnusedEntries()
    {
        string zip = Path.Combine(directory, "mod.zip");
        using (var archive = ZipFile.Open(zip, ZipArchiveMode.Create))
        {
            archive.CreateEntryFromFile(source, "nested/deeper/data.win");
            using var text = new StreamWriter(archive.CreateEntry("installer.exe").Open());
            text.Write("never execute or extract");
        }
        var library = new ModLibrary(store);
        var added = library.Add(zip);
        Assert.Equal(BuildFiles.Hash(source), added.Mod.Sha256);
        Assert.Equal("mod.zip", added.Mod.SourceFileName);
        Assert.Equal("data.win", library.Patch(added.Mod).Mod.FileName);
        Assert.False(File.Exists(Path.Combine(store.Root, "installer.exe")));
        library.Remove(added.Mod.Id);
        foreach (string unsafeName in new[] { "../escaped", "nested/../../escaped", "/absolute", "C:\\escape", "..\\escaped" })
        {
            string unsafeZip = Path.Combine(directory, "unsafe.zip");
            File.Delete(unsafeZip);
            using (var archive = ZipFile.Open(unsafeZip, ZipArchiveMode.Create))
            {
                archive.CreateEntryFromFile(source, "nested/data.win");
                archive.CreateEntry(unsafeName);
            }
            Assert.Throws<InvalidDataException>(() => library.Add(unsafeZip));
            Assert.Empty(library.List());
        }
        Assert.Empty(Directory.GetDirectories(Path.Combine(store.Root, "mods")));
    }

    [Fact]
    public void ZipRejectsMissingAndAmbiguousArchivesAndInvalidModsLeaveNoPartialImport()
    {
        string zip = Path.Combine(directory, "empty.zip");
        using (var archive = ZipFile.Open(zip, ZipArchiveMode.Create)) archive.CreateEntry("README");
        var library = new ModLibrary(store);
        Assert.Throws<InvalidDataException>(() => library.Add(zip));
        File.Delete(zip);
        using (var archive = ZipFile.Open(zip, ZipArchiveMode.Create))
        {
            archive.CreateEntry("one/data.win"); archive.CreateEntry("two/data.win");
        }
        Assert.Throws<InvalidDataException>(() => library.Add(zip));
        string broken = Path.Combine(directory, "bad.win"); File.WriteAllText(broken, "broken");
        Assert.ThrowsAny<Exception>(() => library.Add(broken));
        Assert.Empty(library.List());
        Assert.Empty(Directory.GetDirectories(Path.Combine(store.Root, "mods")));
    }

    [Fact]
    public void ProfilesRetainOrderChoicesAndBlockRemovingDependencies()
    {
        var library = new ModLibrary(store);
        var one = library.Add(source, "One").Mod;
        string second = Path.Combine(directory, "second.win"); WriteArchive(second, 9);
        var two = library.Add(second, "Two").Mod;
        var profiles = new ProfileLibrary(store);
        var profile = profiles.Create("test", ["Two", "One"]);
        Assert.Equal(new[] { two.Id, one.Id }, profile.Mods);
        Assert.Throws<IOException>(() => profiles.Create("test", ["One"]));
        Assert.Throws<ArgumentException>(() => profiles.Create("../escape", ["One"]));
        Assert.Throws<ArgumentException>(() => profiles.Create("duplicate", ["One", one.Id]));
        profiles.Use("test");
        Assert.Equal("test", store.Config().ActiveProfile);
        Assert.Equal("One", profiles.Resolve("test", "code/synthetic", one.Id).Resolutions["code/synthetic"]);
        Assert.Throws<ArgumentException>(() => profiles.Resolve("test", "unknown", "One"));
        Assert.Throws<IOException>(() => library.Remove("One"));
        Assert.Single(profiles.List());
        profiles.Delete("test");
        Assert.Null(store.Config().ActiveProfile);
        library.Remove("One"); library.Remove("Two");
        Assert.Empty(library.List());
    }

    [Fact]
    public void DormantUpgradeGroupChoiceIsIgnoredWhenTheLosingModIsDisabledOrRemoved()
    {
        const string ancestor = "// appended upgrade rows\n";
        WriteUpgradeArchive(original, ancestor);
        store.Set("base", original);
        WriteUpgradeArchive(source, "name[18] = \"Alpha\";\ncost[18] = 100;\n// appended upgrade rows\n");
        string second = Path.Combine(directory, "beta.win");
        WriteUpgradeArchive(second, "name[18] = \"Beta\";\ncost[18] = 200;\n// appended upgrade rows\n");
        var library = new ModLibrary(store);
        var alpha = library.Add(source, "Alpha").Mod;
        var beta = library.Add(second, "Beta").Mod;
        var profiles = new ProfileLibrary(store);
        profiles.Create("upgrade", [alpha.Id, beta.Id]);
        const string groupKey = "code/upgrade-slot[18]";
        profiles.Resolve("upgrade", groupKey, alpha.Id);

        var profile = profiles.Update("upgrade", [alpha.Id, beta.Id], [beta.Id]);
        Assert.Equal("Alpha", profile.Resolutions[groupKey]);
        Assert.DoesNotContain(groupKey, profiles.ActiveResolutions(profile));
        profile = profiles.Update("upgrade", [alpha.Id], []);
        Assert.Equal("Alpha", profile.Resolutions[groupKey]);
        Assert.DoesNotContain(groupKey, profiles.ActiveResolutions(profile));
        var plan = BuildPlan.Create([library.Patch(alpha)], profiles.ActiveResolutions(profile));
        Assert.Contains(plan.Assets, a => a.Name == "gml_Object_UpgradeData_Create_0");
    }

    [Fact]
    public void ChangedBaseOrDamagedPayloadMakesCacheUnusable()
    {
        var library = new ModLibrary(store);
        var mod = library.Add(source).Mod;
        store.Set("base", source);
        Assert.False(Assert.Single(library.List()).BaseHashMatches);
        Assert.False(Assert.Single(library.List()).PatchCached);
        store.Set("base", original);
        File.WriteAllText(Path.Combine(store.Root, "mods", mod.Id, "data.win"), "changed");
        Assert.False(Assert.Single(library.List()).PatchCached);
        Assert.Throws<InvalidDataException>(() => library.Patch(mod));
    }

    [Fact]
    public void LinkedManagedPathsAreRefusedWithoutWritingOutsideRoot()
    {
        if (OperatingSystem.IsWindows()) return;
        string outside = Path.Combine(directory, "outside"); Directory.CreateDirectory(outside);
        Directory.Delete(Path.Combine(store.Root, "patches"));
        Directory.CreateSymbolicLink(Path.Combine(store.Root, "patches"), outside);
        Assert.Throws<IOException>(() => new ManagerStore(store.Root));
        Assert.Empty(Directory.GetFiles(outside));
        File.Delete(Path.Combine(store.Root, "patches"));
        File.Delete(Path.Combine(store.Root, "config.json"));
        File.CreateSymbolicLink(Path.Combine(store.Root, "config.json"), source);
        string hash = BuildFiles.Hash(source);
        Assert.Throws<IOException>(() => store.Set("gameDir", outside));
        Assert.Equal(hash, BuildFiles.Hash(source));
        File.Delete(Path.Combine(store.Root, "config.json"));
    }

    [Theory]
    [InlineData("wine")]
    [InlineData("proton")]
    public void LaunchPreservesArgumentBoundariesAndEnvironment(string mode)
    {
        var config = new ManagerConfig
        {
            GameDir = Path.Combine(directory, "game space;literal"),
            Wine = new() { Mode = mode, Binary = "/runner path/wine", Prefix = "/prefix space" },
            Proton = new() { Path = "/proton space", ClientInstallPath = "/steam space", CompatDataPath = "/compat space" }
        };
        var command = GameLauncher.Create(config, store.Root);
        var info = command.ToStartInfo();
        Assert.False(info.UseShellExecute);
        Assert.Equal(config.GameDir, info.WorkingDirectory);
        Assert.Equal(Path.Combine(config.GameDir, "Cat Goes Fishing.exe"), info.ArgumentList[^1]);
        Assert.Equal("343780", info.Environment["SteamAppId"]);
        if (mode == "wine")
        {
            Assert.Single(info.ArgumentList);
            Assert.Equal("/runner path/wine", info.FileName);
            Assert.Equal("/prefix space", info.Environment["WINEPREFIX"]);
        }
        else
        {
            Assert.Equal("run", info.ArgumentList[0]);
            Assert.Equal("/proton space/proton", info.FileName);
            Assert.Equal("/compat space", info.Environment["STEAM_COMPAT_DATA_PATH"]);
            Assert.Equal("/steam space", info.Environment["STEAM_COMPAT_CLIENT_INSTALL_PATH"]);
            var defaultCommand = GameLauncher.Create(config with { Proton = config.Proton with { CompatDataPath = null } }, store.Root);
            Assert.Equal(Path.Combine(store.Root, "proton", "343780"), defaultCommand.Environment["STEAM_COMPAT_DATA_PATH"]);
        }
        Assert.Contains("Cat Goes Fishing.exe", command.Display());
        Assert.Throws<FileNotFoundException>(() => GameLauncher.Validate(command));
    }

    [Fact]
    public void MissingRunnerAndProtonConfigurationHaveActionableErrors()
    {
        string game = Path.Combine(directory, "game"); Directory.CreateDirectory(game);
        File.WriteAllText(Path.Combine(game, "Cat Goes Fishing.exe"), "placeholder");
        var config = new ManagerConfig { GameDir = game, Wine = new() { Binary = "cgfmm-no-such-runner" } };
        var command = GameLauncher.Create(config, store.Root);
        Assert.Contains("wine.binary", Assert.Throws<FileNotFoundException>(() => GameLauncher.Validate(command)).Message);
        Assert.Throws<ArgumentException>(() => GameLauncher.Create(config with { Wine = new() { Mode = "proton" } }, store.Root));
    }

    [Fact]
    public void ExplicitRelativeRunnerPathsAreValidatedWithoutSearchingPath()
    {
        string game = Path.Combine(directory, "game"); Directory.CreateDirectory(game);
        File.WriteAllText(Path.Combine(game, "Cat Goes Fishing.exe"), "placeholder");
        string runner = Path.Combine(directory, "runner.exe"); File.WriteAllText(runner, "placeholder");
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(runner, UnixFileMode.UserRead | UnixFileMode.UserExecute);
        string relative = Path.GetRelativePath(Environment.CurrentDirectory, runner).Replace(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        store.Set("wine.binary", relative);
        Assert.Equal(runner, store.Config().Wine.Binary);
        var command = GameLauncher.Create(new ManagerConfig { GameDir = game, Wine = new() { Binary = relative } }, store.Root);
        GameLauncher.Validate(command);
        File.Delete(runner);
        Assert.Throws<FileNotFoundException>(() => GameLauncher.Validate(command));
    }

    [Theory]
    [InlineData("environment")]
    [InlineData(".steam/steam")]
    [InlineData(".local/share/Steam")]
    [InlineData("explicit")]
    public void ProtonFindsSteamBeforeCheckingTheSelectedProfile(string location)
    {
        if (OperatingSystem.IsWindows()) return; // Standard home fallbacks are Linux Steam locations.
        string game = Path.Combine(directory, "game"); Directory.CreateDirectory(game);
        File.WriteAllText(Path.Combine(game, "Cat Goes Fishing.exe"), "placeholder");
        string proton = Path.Combine(directory, "runner"); Directory.CreateDirectory(proton);
        string runner = Path.Combine(proton, "proton"); File.WriteAllText(runner, "placeholder");
        File.SetUnixFileMode(runner, UnixFileMode.UserRead | UnixFileMode.UserExecute);
        string home = Path.Combine(directory, "home"); Directory.CreateDirectory(home);
        string steam = Path.Combine(home, location); Directory.CreateDirectory(steam);
        store.Set("gameDir", game); store.Set("wine.mode", "proton"); store.Set("proton.path", proton);
        if (location == "explicit") store.Set("proton.clientInstallPath", steam);
        var result = Cli(["play", "--data-dir", store.Root], environment: new()
        {
            ["HOME"] = home,
            ["STEAM_COMPAT_CLIENT_INSTALL_PATH"] = location == "environment" ? steam : location == "explicit" ? "/missing-steam" : null
        });
        Assert.Equal(1, result.Exit);
        Assert.Contains("No active profile", result.Error); // Runner and Steam checks passed; no process is launched.
    }

    [Theory]
    [InlineData("--leading-name")]
    [InlineData("--verbose")]
    [InlineData("--data-dir")]
    public void CliAcceptsOptionValuesThatLookLikeOptions(string name)
    {
        var result = Cli(["add", source, "--name", name, "--data-dir", store.Root]);
        Assert.Equal(0, result.Exit);
        Assert.Equal(name, new ModLibrary(store).Find(name).Name);
        result = Cli(["profile", "create", "leading", "--mods", name, "--data-dir", store.Root]);
        Assert.Equal(0, result.Exit);
        // Relative data directories starting with '--' are resolved within an isolated working directory.
        result = Cli(["--data-dir", "--storage", "config", "show", "--json"], workingDirectory: directory);
        Assert.Equal(0, result.Exit);
        Assert.True(Directory.Exists(Path.Combine(directory, "--storage", "mods")));
    }

    [Fact]
    public void CliConfigTextUsesSetKeysAndJsonRemainsStructured()
    {
        var result = Cli(["config", "show", "--data-dir", store.Root]);
        Assert.Equal(0, result.Exit);
        Assert.Contains("base = " + original, result.Output);
        Assert.Contains("wine.mode = wine", result.Output);
        Assert.Contains("proton.path = (unset)", result.Output);
        Assert.Contains("steam.appId = 343780", result.Output);
        Assert.DoesNotContain("{", result.Output);
        result = Cli(["profile", "list", "--json", "--data-dir", store.Root]);
        Assert.Equal(0, result.Exit);
        Assert.Equal(JsonValueKind.Array, JsonDocument.Parse(result.Output).RootElement.ValueKind);
    }

    [Fact]
    public void CliJsonAndDataDirectoryPrecedenceWorkAcrossProcesses()
    {
        var library = new ModLibrary(store);
        var mod = library.Add(source).Mod;
        new ProfileLibrary(store).Create("json-profile", [mod.Id]);
        var result = Cli(["--data-dir", store.Root, "config", "show", "--json"], "", new() { ["CGFMM_DATA_DIR"] = Path.Combine(directory, "ignored") });
        Assert.Equal(0, result.Exit);
        Assert.Equal(original, JsonDocument.Parse(result.Output).RootElement.GetProperty("base").GetString());
        Assert.False(Directory.Exists(Path.Combine(directory, "ignored")));
        result = Cli(["list", "--json"], "", new() { ["CGFMM_DATA_DIR"] = store.Root });
        Assert.Equal(0, result.Exit);
        Assert.True(JsonDocument.Parse(result.Output).RootElement[0].GetProperty("patchCached").GetBoolean());
        Assert.Equal("", result.Error);
        result = Cli(["profile", "show", "json-profile", "--json", "--data-dir", store.Root]);
        Assert.Equal(mod.Id, JsonDocument.Parse(result.Output).RootElement.GetProperty("mods")[0].GetString());
        string xdg = Path.Combine(directory, "xdg");
        result = Cli(["config", "show", "--json"], "", new() { ["CGFMM_DATA_DIR"] = null, ["XDG_DATA_HOME"] = xdg });
        Assert.Equal(0, result.Exit);
        Assert.True(Directory.Exists(Path.Combine(xdg, "cgfmm", "mods")));
        result = Cli(["--data-dir", store.Root, "--help"]);
        Assert.Equal(0, result.Exit);
        Assert.Contains("profile resolve", result.Output);
    }

    [Fact]
    public void CliDecliningApplyLeavesGameUntouchedAndYesInstalls()
    {
        var mod = new ModLibrary(store).Add(source).Mod;
        var profiles = new ProfileLibrary(store);
        profiles.Create("confirm", [mod.Id]); profiles.Use("confirm");
        string game = Path.Combine(directory, "game"); Directory.CreateDirectory(game);
        File.Copy(original, Path.Combine(game, "data.win"));
        store.Set("gameDir", game);
        var declined = Cli(["apply", "--data-dir", store.Root], "no\n");
        Assert.Equal(1, declined.Exit);
        Assert.Contains("Installation cancelled", declined.Output);
        Assert.Equal(BuildFiles.Hash(original), BuildFiles.Hash(Path.Combine(game, "data.win")));
        Assert.False(File.Exists(Path.Combine(game, "data.win.cgfmm-original")));
        Assert.Empty(Directory.GetFiles(Path.Combine(store.Root, "builds")));
        var accepted = Cli(["apply", "--yes", "--data-dir", store.Root]);
        Assert.Equal(0, accepted.Exit);
        Assert.True(new ProfileInstaller(store).IsInstalled());
        Assert.Empty(Directory.GetFiles(Path.Combine(store.Root, "builds")));
        Assert.Equal(BuildFiles.Hash(original), BuildFiles.Hash(Path.Combine(game, "data.win.cgfmm-original")));
        var repeated = Cli(["apply", "--data-dir", store.Root]);
        Assert.Equal(0, repeated.Exit);
        Assert.Contains("already installed", repeated.Output);
        Assert.Equal(0, Cli(["restore", "--data-dir", store.Root]).Exit);
        Assert.Equal(BuildFiles.Hash(original), BuildFiles.Hash(Path.Combine(game, "data.win")));
        Assert.Empty(Directory.GetFiles(Path.Combine(store.Root, "builds")));
    }

    [Theory]
    [InlineData("marker")]
    [InlineData("backup")]
    [InlineData("installed-state")]
    public void StaleInstallationStateReturnsFalseAndPrepareStillRefusesIt(string damage)
    {
        var mod = new ModLibrary(store).Add(source).Mod;
        var profiles = new ProfileLibrary(store);
        profiles.Create("state", [mod.Id]); profiles.Use("state");
        string game = Path.Combine(directory, "game"); Directory.CreateDirectory(game);
        File.Copy(original, Path.Combine(game, "data.win")); store.Set("gameDir", game);
        var installer = new ProfileInstaller(store);
        var prepared = installer.Prepare();
        installer.Install(prepared);
        Assert.False(File.Exists(prepared.OutputPath));
        if (damage == "installed-state")
        {
            File.AppendAllText(Path.Combine(game, "data.win"), "changed");
            string path = Path.Combine(store.Root, "installed.json");
            var state = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path))!;
            state["buildSha256"] = BuildFiles.Hash(Path.Combine(game, "data.win"));
            File.WriteAllText(path, JsonSerializer.Serialize(state));
        }
        else File.WriteAllText(Path.Combine(game, damage == "marker" ? "data.win.cgfmm-build.json" : "data.win.cgfmm-original"), "changed");
        string currentHash = BuildFiles.Hash(Path.Combine(game, "data.win"));
        Assert.False(installer.IsInstalled());
        Assert.Throws<IOException>(() => installer.Prepare());
        Assert.Equal(currentHash, BuildFiles.Hash(Path.Combine(game, "data.win")));
        Assert.Empty(Directory.GetFiles(Path.Combine(store.Root, "builds")));
    }

    [Fact]
    public void DiscardRemovesOnlyThePreparedBuildAndLeavesSourcesAndBackups()
    {
        var mod = new ModLibrary(store).Add(source).Mod;
        var profiles = new ProfileLibrary(store);
        profiles.Create("discard", [mod.Id]); profiles.Use("discard");
        string game = Path.Combine(directory, "game"); Directory.CreateDirectory(game);
        File.Copy(original, Path.Combine(game, "data.win")); store.Set("gameDir", game);
        var installer = new ProfileInstaller(store);
        var prepared = installer.Prepare();
        Assert.Throws<IOException>(() => installer.Discard(prepared with { OutputPath = original }));
        Assert.True(File.Exists(original));
        installer.Discard(prepared);
        installer.Discard(prepared);
        Assert.Empty(Directory.GetFiles(Path.Combine(store.Root, "builds")));
        Assert.Equal(BuildFiles.Hash(original), BuildFiles.Hash(Path.Combine(game, "data.win")));
    }

    private static (int Exit, string Output, string Error) Cli(string[] arguments, string input = "", Dictionary<string, string?>? environment = null, string? workingDirectory = null)
    {
        string dotnet = Environment.GetEnvironmentVariable("DOTNET_ROOT") is { } root ? Path.Combine(root, "dotnet") : "dotnet";
        var info = new ProcessStartInfo(dotnet) { UseShellExecute = false, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true };
        if (workingDirectory is not null) info.WorkingDirectory = workingDirectory;
        info.ArgumentList.Add(typeof(Jamm.Cli.OutputFile).Assembly.Location);
        foreach (string argument in arguments) info.ArgumentList.Add(argument);
        foreach (var pair in environment ?? [])
        {
            if (pair.Value is null) info.Environment.Remove(pair.Key);
            else info.Environment[pair.Key] = pair.Value;
        }
        using var process = Process.Start(info)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        process.StandardInput.Write(input); process.StandardInput.Close();
        Assert.True(process.WaitForExit(30000), "Synthetic CLI operation exceeded 30 seconds.");
        return (process.ExitCode, output.GetAwaiter().GetResult(), error.GetAwaiter().GetResult());
    }

    [Fact]
    public void CliPersistsMixedHunkChoicesAndRejectsReplacementKeepBoth()
    {
        static void WriteCode(string path, string text)
        {
            using var data = UndertaleData.CreateNew();
            data.GeneralInfo.Major = 1; data.GeneralInfo.Minor = 0; data.GeneralInfo.Release = 0; data.GeneralInfo.Build = 1773;
            var code = UndertaleCode.CreateEmptyEntry(data, "synthetic");
            var compiler = new CompileGroup(data); compiler.QueueCodeReplace(code, text);
            Assert.True(compiler.Compile().Successful);
            using var stream = File.Create(path); UndertaleIO.Write(stream, data);
        }
        WriteCode(original, "x = 0;\nz = 0;\ny = 0;\n");
        WriteCode(source, "x = 0;\na = 1;\nz = 0;\ny = 1;\n");
        string second = Path.Combine(directory, "second.win");
        WriteCode(second, "x = 0;\nb = 2;\nz = 0;\ny = 2;\n");
        var library = new ModLibrary(store);
        var a = library.Add(source, "A").Mod; var b = library.Add(second, "B").Mod;
        var profiles = new ProfileLibrary(store); profiles.Create("hunks", [a.Id, b.Id]);
        var merge = Jamm.Core.Merging.ConflictAnalyzer.Analyze([library.Patch(a), library.Patch(b)]).CodeMerges["synthetic"];
        var list = Assert.Single(merge.Hunks, h => h.Status == "CONFLICT" && h.KeepBoth is not null);
        var replacement = Assert.Single(merge.Hunks, h => h.Status == "CONFLICT" && h.KeepBoth is null);
        string key = $"code/synthetic#{list.Start}", replaceKey = $"code/synthetic#{replacement.Start}";
        Assert.Equal(0, Cli(["profile", "resolve", "hunks", key, "keep-both", "--data-dir", store.Root]).Exit);
        Assert.Equal(0, Cli(["profile", "resolve", "hunks", replaceKey, "A", "--data-dir", store.Root]).Exit);
        Assert.Equal("keep-both", profiles.Get("hunks").Resolutions[key]);
        Assert.Equal("A", profiles.Get("hunks").Resolutions[replaceKey]);
        Assert.Equal(1, Cli(["profile", "resolve", "hunks", replaceKey, "keep-both", "--data-dir", store.Root]).Exit);
        Assert.Equal("A", profiles.Get("hunks").Resolutions[replaceKey]);
    }

    [Theory]
    [InlineData("/path/to/compatdata/343780")]
    [InlineData("/path/to/compatdata/480")]
    [InlineData("/path/to/...")]
    [InlineData("path/to/...")]
    [InlineData("<data-dir>/proton/<app-id>")]
    [InlineData("<Flatpak Steam>/steamapps/compatdata/343780")]
    public void StockCompatPlaceholdersInCliAndExistingConfigsUseNativeProtonDefaultsAndKeepRealOverrides(string placeholder)
    {
        string game = Path.Combine(directory, "game"); Directory.CreateDirectory(game);
        File.Copy(original, Path.Combine(game, "data.win"));
        File.WriteAllText(Path.Combine(game, "Cat Goes Fishing.exe"), "placeholder");
        string proton = Path.Combine(directory, "proton-bin"); Directory.CreateDirectory(proton);
        string runner = Path.Combine(proton, "proton");
        File.WriteAllText(runner, "#!/bin/sh\nexit 0\n");
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(runner, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        string home = Path.Combine(directory, "home");
        string steam = Path.Combine(home, ".steam", "steam");
        Directory.CreateDirectory(steam);

        File.WriteAllText(Path.Combine(store.Root, "config.json"), JsonSerializer.Serialize(new
        {
            @base = original,
            gameDir = game,
            wine = new { mode = "proton", binary = "wine" },
            proton = new { path = proton, compatDataPath = placeholder, clientInstallPath = steam }
        }));
        var loaded = new ManagerStore(store.Root).Config();
        Assert.Null(loaded.Proton.CompatDataPath);
        Assert.Equal(steam, loaded.Proton.ClientInstallPath);
        var defaultCmd = GameLauncher.Create(loaded, store.Root);
        string expectedDefaultCompat = Path.Combine(store.Root, "proton", "343780");
        Assert.Equal(expectedDefaultCompat, defaultCmd.Environment["STEAM_COMPAT_DATA_PATH"]);
        Assert.Equal(steam, defaultCmd.Environment["STEAM_COMPAT_CLIENT_INSTALL_PATH"]);

        var setResult = Cli(["config", "set", "proton.compatDataPath", placeholder, "--data-dir", store.Root]);
        Assert.Equal(0, setResult.Exit);
        Assert.Contains("Cleared proton.compatDataPath", setResult.Output);
        var showResult = Cli(["config", "show", "--data-dir", store.Root]);
        Assert.Equal(0, showResult.Exit);
        Assert.Contains("proton.compatDataPath = (unset)", showResult.Output);

        var directCmd = GameLauncher.Create(loaded with { Proton = loaded.Proton with { CompatDataPath = placeholder } }, store.Root);
        Assert.Equal(expectedDefaultCompat, directCmd.Environment["STEAM_COMPAT_DATA_PATH"]);

        // Non-placeholder overrides (including /path/to/custom) and existing other optional-path rules stay intact.
        store.Set("proton.compatDataPath", "/path/to/custom");
        Assert.Equal(Path.GetFullPath("/path/to/custom"), new ManagerStore(store.Root).Config().Proton.CompatDataPath);
        Assert.Throws<ArgumentException>(() => store.Set("wine.prefix", ""));
        Assert.Throws<ArgumentException>(() => store.Set("proton.path", ""));
        Assert.Throws<ArgumentException>(() => store.Set("proton.clientInstallPath", ""));

        string realOverride = Path.Combine(directory, "real-compat", "343780");
        Assert.Equal(0, Cli(["config", "set", "proton.compatDataPath", realOverride, "--data-dir", store.Root]).Exit);
        Assert.Equal(realOverride, new ManagerStore(store.Root).Config().Proton.CompatDataPath);
        Assert.Equal(realOverride, GameLauncher.Create(new ManagerStore(store.Root).Config(), store.Root).Environment["STEAM_COMPAT_DATA_PATH"]);

        Assert.Equal(0, Cli(["config", "set", "proton.compatDataPath", "", "--data-dir", store.Root]).Exit);
        Assert.Null(new ManagerStore(store.Root).Config().Proton.CompatDataPath);

        if (OperatingSystem.IsWindows()) return;
        var mod = new ModLibrary(store).Add(source).Mod;
        var profiles = new ProfileLibrary(store);
        profiles.Create("proton-default", [mod.Id]);
        profiles.Use("proton-default");
        var play = Cli(["play", "--yes", "--data-dir", store.Root], environment: new() { ["HOME"] = home });
        Assert.Equal(0, play.Exit);
        Assert.True(Directory.Exists(expectedDefaultCompat));
    }

    private static void WriteArchive(string path, short value)
    {
        using var data = UndertaleData.CreateNew();
        data.GeneralInfo.Timestamp = 1692207312; // Matching fixtures must not depend on the current second.
        data.GeneralInfo.Major = 1; data.GeneralInfo.Minor = 0; data.GeneralInfo.Release = 0; data.GeneralInfo.Build = 1773;
        var code = UndertaleCode.CreateEmptyEntry(data, "synthetic");
        var compiler = new CompileGroup(data);
        compiler.QueueCodeReplace(code, $"x = {value};\n");
        Assert.True(compiler.Compile().Successful);
        using var stream = File.Create(path); UndertaleIO.Write(stream, data);
    }

    private static void WriteUpgradeArchive(string path, string text)
    {
        using var data = UndertaleData.CreateNew();
        data.GeneralInfo.Timestamp = 1692207312;
        data.GeneralInfo.Major = 1; data.GeneralInfo.Minor = 0; data.GeneralInfo.Release = 0; data.GeneralInfo.Build = 1773;
        var code = UndertaleCode.CreateEmptyEntry(data, "gml_Object_UpgradeData_Create_0");
        var compiler = new CompileGroup(data); compiler.QueueCodeReplace(code, text);
        Assert.True(compiler.Compile().Successful);
        using var stream = File.Create(path); UndertaleIO.Write(stream, data);
    }

    public void Dispose() => Directory.Delete(directory, true);
}
