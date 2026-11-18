using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using Jamm.Core.Browsing;
using Jamm.Core.Building;
using Jamm.Core.Management;
using Jamm.Core.ModKit;
using Jamm.Core.Patching;
using Jamm.ModMaker;
using UndertaleModLib;
using UndertaleModLib.Compiler;
using UndertaleModLib.Models;

namespace Jamm.Core.Tests;

/// <summary>Verifies SiloMod, pure-source recipe/ModKit ZIP import, and the staged static downloads site.</summary>
public sealed class SiloAndSiteTests
{
    private static string RepoRoot()
        => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));

    [Fact]
    public void SiloModAppliesCleanlyAndVerifiesOnSyntheticLegacyArchive()
    {
        using var temp = new TempDir();
        string basePath = temp.PathFor("base.win");
        string siloPath = temp.PathFor("silo.win");
        WriteSyntheticSiloBase(basePath);

        using (var loaded = GameFileLoader.Load(basePath))
        {
            SiloMod.Apply(loaded.Data);
            using var outStream = File.Create(siloPath);
            UndertaleIO.Write(outStream, loaded.Data);
        }

        ArchiveVerifier.Verify(siloPath);

        using var orig = GameFileLoader.Load(basePath);
        using var mod = GameFileLoader.Load(siloPath);

        Assert.NotNull(mod.Data.Rooms.ByName(SiloMod.SanctuaryRoomName));
        Assert.NotNull(mod.Data.GameObjects.ByName(SiloMod.ControllerObjectName));
        Assert.NotNull(mod.Data.GameObjects.ByName(SiloMod.BeaconObjectName));
        Assert.NotNull(mod.Data.GameObjects.ByName(SiloMod.AreaObjectName));
        Assert.NotNull(mod.Data.Sprites.ByName("spr_jamm_silo_glow"));
        Assert.NotNull(mod.Data.Sprites.ByName("spr_jamm_silo_diver"));
        Assert.NotNull(mod.Data.Sprites.ByName("spr_jamm_silo_fish"));
        Assert.NotNull(mod.Data.Code.ByName("gml_Object_MissileSilo_Create_0"));
        Assert.NotNull(mod.Data.Code.ByName("gml_Object_MissileSilo_Step_0"));
        Assert.NotNull(mod.Data.Code.ByName("gml_Object_MissileSilo_Draw_0"));

        string areaGui = PatchBuilder.ReadCode(mod, $"gml_Object_{SiloMod.AreaObjectName}_Draw_64").Text;
        Assert.Contains("display_get_gui_width()", areaGui);
        Assert.Contains("string_width_ext", areaGui);
        Assert.Contains("string_height_ext", areaGui);
        Assert.Contains("draw_text_ext", areaGui);
        Assert.DoesNotContain("draw_rectangle(14, 12, 786, 40", areaGui);

        string ctrlGui = PatchBuilder.ReadCode(mod, $"gml_Object_{SiloMod.ControllerObjectName}_Draw_64").Text;
        Assert.Contains("display_get_gui_width()", ctrlGui);
        Assert.Contains("draw_text_ext", ctrlGui);

        string siloDraw = PatchBuilder.ReadCode(mod, "gml_Object_MissileSilo_Draw_0").Text;
        Assert.Contains("string_width(label)", siloDraw);

        var patch = PatchBuilder.Create(orig, mod, "The Silo");
        Assert.True(ModSanitizer.IsJammMod(patch));
        var sanitized = ModSanitizer.Sanitize(patch);
        Assert.Empty(sanitized.Report.Removed);
    }

    [Fact]
    public void SiloModRejectsArchiveMissingMissileSilo()
    {
        using var data = UndertaleData.CreateNew();
        data.Rooms.Add(new UndertaleRoom { Name = data.Strings.MakeString("NewWorld") });
        var ex = Assert.Throws<InvalidDataException>(() => SiloMod.Apply(data));
        Assert.Contains("MissileSilo", ex.Message);
    }

    [Fact]
    public void RecipePackageValidationRejectsBadManifests()
    {
        using var temp = new TempDir();
        string badGen = temp.PathFor("bad-gen");
        Directory.CreateDirectory(badGen);
        File.WriteAllText(Path.Combine(badGen, RecipePackage.ManifestFileName),
            """{"schemaVersion":1,"id":"x","generator":"unknown","name":"Bad"}""");
        Assert.Throws<InvalidDataException>(() => RecipePackage.Load(badGen));

        string badVer = temp.PathFor("bad-ver");
        Directory.CreateDirectory(badVer);
        File.WriteAllText(Path.Combine(badVer, RecipePackage.ManifestFileName),
            """{"schemaVersion":2,"id":"x","generator":"silo","name":"Bad"}""");
        Assert.Throws<InvalidDataException>(() => RecipePackage.Load(badVer));
    }

    [Fact]
    public void DownloadServiceAndLibrarySupportRecipeAndModKitZips()
    {
        using var temp = new TempDir();
        string basePath = temp.PathFor("base.win");
        WriteSyntheticSiloBase(basePath);

        string dataDir = temp.PathFor("store");
        var store = new ManagerStore(dataDir);
        store.Set("base", basePath);

        // Recipe ZIP (silo)
        string recipeZip = temp.PathFor("jamm-silo.zip");
        using (var zip = ZipFile.Open(recipeZip, ZipArchiveMode.Create))
        {
            using var entry = zip.CreateEntry(RecipePackage.ManifestFileName).Open();
            entry.Write("""{"schemaVersion":1,"id":"jamm-silo","generator":"silo","stableId":"jamm-silo","name":"The Silo","version":"1.0.0","author":"JAMM","description":"Silo","default":false}"""u8);
        }
        Assert.Equal(".zip", ModDownloadService.Validate(recipeZip, 1024 * 1024));

        var library = new ModLibrary(store);
        var siloAdded = library.Add(recipeZip);
        Assert.Equal("jamm-silo", siloAdded.Mod.Id);
        Assert.Equal("The Silo", siloAdded.Mod.Name);

        // Reject source ZIP smuggling a .win or .exe
        string badZip = temp.PathFor("bad-source.zip");
        using (var zip = ZipFile.Open(badZip, ZipArchiveMode.Create))
        {
            using (var e1 = zip.CreateEntry(RecipePackage.ManifestFileName).Open())
                e1.Write("""{"schemaVersion":1,"id":"x","generator":"silo","name":"Bad"}"""u8);
            using (var e2 = zip.CreateEntry("payload.exe").Open())
                e2.Write("MZ"u8);
        }
        Assert.Throws<InvalidDataException>(() => ModDownloadService.Validate(badZip, 1024 * 1024));

        // ModKit ZIP (example-fish)
        string modkitZip = Path.Combine(RepoRoot(), "site", "cgf-mods", "packages", "example-fish.zip");
        Assert.Equal(".zip", ModDownloadService.Validate(modkitZip, 1024 * 1024));
        var fishAdded = library.Add(modkitZip);
        Assert.True(fishAdded.Mod.IsModKit);
        Assert.Equal("Example Fish", fishAdded.Mod.Name);
    }

    [Fact]
    public void StagedSiteCatalogAndPackagesArePureSourceValidAndMatchChecksums()
    {
        string siteDir = Path.Combine(RepoRoot(), "site", "cgf-mods");
        string indexPath = Path.Combine(siteDir, "index.html");
        string catalogPath = Path.Combine(siteDir, "catalog.json");
        Assert.True(File.Exists(indexPath));
        Assert.True(File.Exists(catalogPath));

        string html = File.ReadAllText(indexPath);
        Assert.Contains("catalog.json", html);

        using var doc = JsonDocument.Parse(File.ReadAllText(catalogPath));
        Assert.Equal(JsonValueKind.Array, doc.RootElement.ValueKind);
        var entries = doc.RootElement.EnumerateArray().ToArray();
        Assert.Equal(6, entries.Length);

        var ids = entries.Select(e => e.GetProperty("id").GetString()).ToHashSet(StringComparer.Ordinal);
        Assert.Contains("builtin-perf", ids);
        Assert.Contains("builtin-smooth", ids);
        Assert.Contains("builtin-hd-lite", ids);
        Assert.Contains("builtin-console", ids);
        Assert.Contains("example-fish", ids);
        Assert.Contains("jamm-silo", ids);

        foreach (var entry in entries)
        {
            string id = entry.GetProperty("id").GetString()!;
            string fileUrl = entry.GetProperty("fileUrl").GetString()!;
            Assert.StartsWith("https://varialbe.xyz/cgf-mods/packages/", fileUrl, StringComparison.Ordinal);
            Assert.False(entry.GetProperty("isPreview").GetBoolean());

            bool expectedDefault = id.StartsWith("builtin-", StringComparison.Ordinal);
            Assert.Equal(expectedDefault, entry.GetProperty("isDefault").GetBoolean());

            string fileName = Path.GetFileName(new Uri(fileUrl).AbsolutePath);
            string zipPath = Path.Combine(siteDir, "packages", fileName);
            Assert.True(File.Exists(zipPath), $"Missing staged package {zipPath}");
            Assert.Contains(fileName, html);

            byte[] bytes = File.ReadAllBytes(zipPath);
            Assert.Equal(entry.GetProperty("sizeBytes").GetInt64(), bytes.LongLength);
            Assert.Equal(entry.GetProperty("sha256").GetString(), Convert.ToHexStringLower(SHA256.HashData(bytes)));

            Assert.Equal(".zip", ModDownloadService.Validate(zipPath, 1024 * 1024));
            using var zip = ZipFile.OpenRead(zipPath);
            foreach (var zipEntry in zip.Entries)
            {
                string ext = Path.GetExtension(zipEntry.Name).ToLowerInvariant();
                Assert.DoesNotContain(ext, new[] { ".win", ".exe", ".dll", ".so", ".dylib" });
            }
        }
    }

    [RealFilesFact]
    public void RealBaseImportsAllStagedSitePackagesAndBuildsCombinedVerifiedArchive()
    {
        string basePath = Environment.GetEnvironmentVariable("JAMM_ORIGINAL")!;
        string packagesDir = Path.Combine(RepoRoot(), "site", "cgf-mods", "packages");

        using var temp = new TempDir();
        var store = new ManagerStore(temp.PathFor("store"));
        store.Set("base", basePath);

        var library = new ModLibrary(store);
        string[] packageFiles =
        [
            "jamm-perf.zip",
            "jamm-smooth.zip",
            "jamm-hd-lite.zip",
            "jamm-console.zip",
            "example-fish.zip",
            "jamm-silo.zip"
        ];

        var modIds = new List<string>();
        foreach (string file in packageFiles)
        {
            var result = library.Add(Path.Combine(packagesDir, file));
            modIds.Add(result.Mod.Id);
        }

        var profiles = new ProfileLibrary(store);
        profiles.Create("all-site", modIds);

        string mergedOut = temp.PathFor("merged-site.win");
        var check = new ManagerOperations(store).Check("all-site");
        Assert.DoesNotContain(check.Findings, f => f.Severity == "CONFLICT");

        var inputs = modIds.Select(id =>
        {
            var mod = library.Find(id);
            return new BuildInput(library.Patch(mod), store.Path("mods", mod.Id, "data.win"));
        }).ToArray();

        var buildResult = ArchiveBuilder.Build(basePath, inputs, mergedOut);
        Assert.NotNull(buildResult.Verification);

        using var merged = GameFileLoader.Load(mergedOut);
        Assert.NotNull(merged.Data.Rooms.ByName(SiloMod.SanctuaryRoomName));
        Assert.NotNull(merged.Data.GameObjects.ByName("obj_jamm_silo_ctrl"));
        Assert.NotNull(merged.Data.GameObjects.ByName("obj_jamm_silo_area"));
        Assert.NotNull(merged.Data.GameObjects.ByName("obj_jamm_console"));
        Assert.NotNull(merged.Data.GameObjects.ByName("obj_jamm_smooth"));
        Assert.NotNull(merged.Data.GameObjects.ByName("obj_jamm_hd"));
        Assert.NotNull(merged.Data.GameObjects.ByName("jmk_example_fish_goldie"));
    }

    private static void WriteSyntheticSiloBase(string path)
    {
        using var data = UndertaleData.CreateNew();
        data.GeneralInfo.Major = 1;
        data.GeneralInfo.Minor = 0;
        data.GeneralInfo.Release = 0;
        data.GeneralInfo.Build = 1773;
        data.GeneralInfo.BytecodeVersion = 16;
        data.GeneralInfo.DefaultWindowWidth = 1200;
        data.GeneralInfo.DefaultWindowHeight = 720;
        data.GeneralInfo.Name = data.Strings.MakeString("SyntheticCGF");
        data.GeneralInfo.DisplayName = data.Strings.MakeString("Synthetic CGF");

        var sprSilo = MakeSprite(data, "spr_silo", 64, 64, 2);
        var sprSiloMask = MakeSprite(data, "spr_silomask", 64, 64, 5);
        var sprFish = MakeSprite(data, "spr_basefish", 32, 32, 1);

        var abstractLight = AddObj(data, "AbstractLight", null, null, persistent: false);
        var missileSilo = AddObj(data, "MissileSilo", null, sprSilo, persistent: false);
        missileSilo.TextureMaskId = sprSiloMask;
        AddObj(data, "Girl", null, null, persistent: false);
        AddObj(data, "Lure", null, null, persistent: false);
        AddObj(data, "CatSwim", null, null, persistent: false);
        AddObj(data, "Camera", null, null, persistent: false);
        AddObj(data, "SaveManager", null, null, persistent: true);
        AddObj(data, "NavBrain", null, null, persistent: false);
        AddObj(data, "BreathBar", null, null, persistent: false);

        // Minimal ModKit/Catalog hooks so example-fish.zip also imports cleanly against synthetic base
        var fishParent = AddObj(data, "Fish", null, sprFish, persistent: false);
        var fishSmall = AddObj(data, "FishSmall", fishParent, sprFish, persistent: false);
        var fishMed = AddObj(data, "FishMedium", fishParent, sprFish, persistent: false);
        AddObj(data, "FishLarge", fishParent, sprFish, persistent: false);
        AddObj(data, "FishHuge", fishParent, sprFish, persistent: false);
        var baseFish = AddObj(data, "BaseFish", fishSmall, sprFish, persistent: false);
        var catalogData = AddObj(data, "CatalogData", null, null, persistent: false);

        var newWorld = new UndertaleRoom
        {
            Name = data.Strings.MakeString("NewWorld"),
            Width = 40000,
            Height = 13000,
            Speed = 30
        };
        data.Rooms.Add(newWorld);
        data.GeneralInfo.RoomOrder.Add(new(newWorld, 0));
        newWorld.GameObjects.Add(new UndertaleRoom.GameObject
        {
            InstanceID = ++data.GeneralInfo.LastObj,
            ObjectDefinition = missileSilo,
            X = 33984,
            Y = 11570
        });

        var compiler = new CompileGroup(data);
        compiler.QueueCodeReplace(missileSilo.EventHandlerFor(EventType.Create, 0u, data), "image_speed = 0;\ncanOpen = 0;\n");
        compiler.QueueCodeReplace(missileSilo.EventHandlerFor(EventType.Step, (uint)EventSubtypeStep.Step, data), "");
        compiler.QueueCodeReplace(abstractLight.EventHandlerFor(EventType.Create, 0u, data), "add_blend = 0;\nsub_blend = 0;\nimage_scale = 2;\n");
        compiler.QueueCodeReplace(data.GameObjects.ByName("SaveManager")!.EventHandlerFor(EventType.Step, (uint)EventSubtypeStep.Step, data), "if (!loaded) { loaded = 1; }\n");
        compiler.QueueCodeReplace(catalogData.EventHandlerFor(EventType.Create, 0u, data), "object[0, 0] = BaseFish;\nname[0, 0] = \"Base\";\nmap[0, 0] = 0;\ndesc[0, 0] = \"Base\";\n");
        compiler.QueueCodeReplace(fishSmall.EventHandlerFor(EventType.Create, 0u, data), "size = 0;\n");
        compiler.QueueCodeReplace(fishMed.EventHandlerFor(EventType.Create, 0u, data), "size = 1;\n");
        compiler.QueueCodeReplace(baseFish.EventHandlerFor(EventType.Create, 0u, data), "event_inherited();\n");

        var toName = UndertaleCode.CreateEmptyEntry(data, "gml_Script_fish_object_to_name");
        data.Scripts.Add(new() { Name = data.Strings.MakeString("fish_object_to_name"), Code = toName });
        compiler.QueueCodeReplace(toName, "return object_get_name(argument0);\n");

        var lunge = UndertaleCode.CreateEmptyEntry(data, "gml_Script_fish_lunge");
        data.Scripts.Add(new() { Name = data.Strings.MakeString("fish_lunge"), Code = lunge });
        compiler.QueueCodeReplace(lunge, "return 0;\n");

        var isNight = UndertaleCode.CreateEmptyEntry(data, "gml_Script_is_it_night");
        data.Scripts.Add(new() { Name = data.Strings.MakeString("is_it_night"), Code = isNight });
        compiler.QueueCodeReplace(isNight, "return 0;\n");

        Assert.True(compiler.Compile().Successful);
        using var stream = File.Create(path);
        UndertaleIO.Write(stream, data);

        static UndertaleGameObject AddObj(UndertaleData d, string name, UndertaleGameObject? parent, UndertaleSprite? sprite, bool persistent)
        {
            var o = new UndertaleGameObject
            {
                Name = d.Strings.MakeString(name),
                ParentId = parent,
                Sprite = sprite,
                Visible = true,
                Persistent = persistent
            };
            d.GameObjects.Add(o);
            return o;
        }

        static UndertaleSprite MakeSprite(UndertaleData d, string name, uint w, uint h, int frames)
        {
            var s = new UndertaleSprite
            {
                Name = d.Strings.MakeString(name),
                Width = w,
                Height = h
            };
            for (int i = 0; i < frames; i++)
                s.Textures.Add(new UndertaleSprite.TextureEntry());
            d.Sprites.Add(s);
            return s;
        }
    }

    private sealed class TempDir : IDisposable
    {
        private readonly string root = Path.Combine(Path.GetTempPath(), "cgfmm-silo-tests-" + Guid.NewGuid().ToString("N"));
        internal TempDir() => Directory.CreateDirectory(root);
        internal string PathFor(string name) => Path.Combine(root, name);
        public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
}
