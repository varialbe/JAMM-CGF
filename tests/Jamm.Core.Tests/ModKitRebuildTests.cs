using Jamm.Core.Building;
using Jamm.Core.Management;
using Jamm.Core.ModKit;
using UndertaleModLib;
using UndertaleModLib.Models;

namespace Jamm.Core.Tests;

/// <summary>Unit tests verifying ModKit folder metadata persistence and atomic Rebuild operations.</summary>
public sealed class ModKitRebuildTests
{
    private static void WriteSyntheticBase(string path)
    {
        using var data = UndertaleData.CreateNew();
        data.GeneralInfo.Major = 1;
        data.GeneralInfo.BytecodeVersion = 16;
        data.GeneralInfo.Build = 1773;

        foreach (string name in new[]
        {
            "Fish", "FishSmall", "FishMedium", "FishLarge", "FishHuge",
            "Boat", "Gear", "Rod", "BaseFish", "CatalogData", "SaveManager"
        })
        {
            data.GameObjects.Add(new() { Name = data.Strings.MakeString(name), Visible = true });
        }
        data.GameObjects.ByName("FishSmall")!.ParentId = data.GameObjects.ByName("Fish");
        data.GameObjects.ByName("FishMedium")!.ParentId = data.GameObjects.ByName("Fish");
        data.GameObjects.ByName("FishLarge")!.ParentId = data.GameObjects.ByName("Fish");
        data.GameObjects.ByName("FishHuge")!.ParentId = data.GameObjects.ByName("Fish");
        data.GameObjects.ByName("BaseFish")!.ParentId = data.GameObjects.ByName("FishSmall");

        data.GameObjects.ByName("CatalogData")!.EventHandlerFor(EventType.Create, 0u, data);
        data.GameObjects.ByName("SaveManager")!.EventHandlerFor(EventType.Step, 0u, data);

        using var stream = File.Create(path);
        UndertaleIO.Write(stream, data);
    }

    [Fact]
    public void AddFolderStoresSourceFolderAndManifestMetadata()
    {
        using var temp = new TemporaryDirectory();
        string dataDir = temp.PathFor("store");
        string basePath = temp.PathFor("base.win");
        string pkgDir = temp.PathFor("reef-fish");

        WriteSyntheticBase(basePath);
        ModKitPackage.Scaffold(pkgDir, "fish");

        var store = new ManagerStore(dataDir);
        store.Set("base", basePath);
        var library = new ModLibrary(store);

        var result = library.Add(pkgDir);
        Assert.Equal("Reef Fish", result.Mod.Name);
        Assert.True(result.Mod.IsModKit);
        Assert.Equal(Path.GetFullPath(pkgDir), result.Mod.SourceFolder);
        Assert.Equal("Mod Author", result.Mod.Author);
        Assert.Equal("1.0.0", result.Mod.Version);
        Assert.Contains("Adds a custom fish", result.Mod.Description);

        // Verify stored mod.json retains the metadata
        var stored = library.Find(result.Mod.Id);
        Assert.True(stored.IsModKit);
        Assert.Equal(Path.GetFullPath(pkgDir), stored.SourceFolder);
        Assert.Equal("Mod Author", stored.Author);
        Assert.Equal("1.0.0", stored.Version);
    }

    [Fact]
    public void RebuildUpdatesLibraryArchiveAndMetadataInPlace()
    {
        using var temp = new TemporaryDirectory();
        string dataDir = temp.PathFor("store");
        string basePath = temp.PathFor("base.win");
        string pkgDir = temp.PathFor("upgrade-mod");

        WriteSyntheticBase(basePath);
        ModKitPackage.Scaffold(pkgDir, "upgrade");

        var store = new ManagerStore(dataDir);
        store.Set("base", basePath);
        var library = new ModLibrary(store);

        var initial = library.Add(pkgDir).Mod;
        Assert.Equal("Upgrade Mod", initial.Name);
        Assert.Equal("1.0.0", initial.Version);

        // Edit jamm-mod.json (change name, version, author, description)
        string manifestPath = Path.Combine(pkgDir, ModKitPackage.ManifestFileName);
        string json = File.ReadAllText(manifestPath);
        json = json.Replace("\"name\": \"Upgrade Mod\"", "\"name\": \"Swift Crank Pro\"")
                   .Replace("\"version\": \"1.0.0\"", "\"version\": \"1.1.0\"")
                   .Replace("\"author\": \"Mod Author\"", "\"author\": \"Crank Master\"")
                   .Replace("\"description\": \"Adds a custom rod upgrade created with JAMM ModKit.\"",
                            "\"description\": \"Upgraded crank with v1.1 reel tuning.\"");
        File.WriteAllText(manifestPath, json);

        var rebuilt = library.Rebuild(initial.Id).Mod;
        Assert.Equal(initial.Id, rebuilt.Id);
        Assert.Equal("Swift Crank Pro", rebuilt.Name);
        Assert.Equal("1.1.0", rebuilt.Version);
        Assert.Equal("Crank Master", rebuilt.Author);
        Assert.Equal("Upgraded crank with v1.1 reel tuning.", rebuilt.Description);
        Assert.True(rebuilt.IsModKit);
        Assert.Equal(Path.GetFullPath(pkgDir), rebuilt.SourceFolder);

        // Metadata on disk must also match
        var stored = library.Find(initial.Id);
        Assert.Equal("1.1.0", stored.Version);
        Assert.Equal("Crank Master", stored.Author);
    }

    [Fact]
    public void RebuildThrowsClearExceptionsWhenFolderIsMissingOrInvalid()
    {
        using var temp = new TemporaryDirectory();
        string dataDir = temp.PathFor("store");
        string basePath = temp.PathFor("base.win");
        string pkgDir = temp.PathFor("boat-mod");

        WriteSyntheticBase(basePath);
        ModKitPackage.Scaffold(pkgDir, "boat");

        var store = new ManagerStore(dataDir);
        store.Set("base", basePath);
        var library = new ModLibrary(store);

        var mod = library.Add(pkgDir).Mod;

        // Delete the folder
        Directory.Delete(pkgDir, true);

        var missingDirEx = Assert.Throws<DirectoryNotFoundException>(() => library.Rebuild(mod.Id));
        Assert.Contains(pkgDir, missingDirEx.Message);

        // Recreate directory without manifest
        Directory.CreateDirectory(pkgDir);
        var missingManifestEx = Assert.Throws<FileNotFoundException>(() => library.Rebuild(mod.Id));
        Assert.Contains("jamm-mod.json", missingManifestEx.Message);

        // Invalid manifest JSON
        File.WriteAllText(Path.Combine(pkgDir, "jamm-mod.json"), "{ invalid syntax");
        var badJsonEx = Assert.Throws<InvalidDataException>(() => library.Rebuild(mod.Id));
        Assert.Contains("jamm-mod.json", badJsonEx.Message);
    }

    [Fact]
    public void SetSourceFolderUpdatesMetadata()
    {
        using var temp = new TemporaryDirectory();
        string dataDir = temp.PathFor("store");
        string basePath = temp.PathFor("base.win");
        string pkgDir = temp.PathFor("hat-mod");

        WriteSyntheticBase(basePath);
        ModKitPackage.Scaffold(pkgDir, "hat");

        var store = new ManagerStore(dataDir);
        store.Set("base", basePath);
        var library = new ModLibrary(store);

        var mod = library.Add(pkgDir).Mod;
        string newPath = temp.PathFor("moved-hat-mod");
        Directory.CreateDirectory(newPath);

        var updated = library.SetSourceFolder(mod.Id, newPath);
        Assert.Equal(Path.GetFullPath(newPath), updated.SourceFolder);
        Assert.True(updated.IsModKit);

        var stored = library.Find(mod.Id);
        Assert.Equal(Path.GetFullPath(newPath), stored.SourceFolder);
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        private readonly string directory = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "cgfmm-rebuild-tests-" + Guid.NewGuid().ToString("N"));
        internal TemporaryDirectory() => Directory.CreateDirectory(directory);
        internal string PathFor(string name) => System.IO.Path.Combine(directory, name);
        public void Dispose() => Directory.Delete(directory, true);
    }
}
