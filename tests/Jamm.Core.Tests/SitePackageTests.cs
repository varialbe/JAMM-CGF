using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using Jamm.Core.Browsing;
using Jamm.Core.Building;
using Jamm.Core.Management;
using Jamm.Core.ModKit;
using UndertaleModLib;
using UndertaleModLib.Compiler;
using UndertaleModLib.Models;

namespace Jamm.Core.Tests;

/// <summary>Verifies pure-source package validation, ZIP import and the staged downloads site.</summary>
public sealed class SitePackageTests
{
    private static string RepoRoot()
        => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));

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
            """{"schemaVersion":2,"id":"x","generator":"perf","name":"Bad"}""");
        Assert.Throws<InvalidDataException>(() => RecipePackage.Load(badVer));
    }

    [Fact]
    public void DownloadServiceValidatesSourceZipsAndLibraryImportsModKit()
    {
        using var temp = new TempDir();
        string basePath = temp.PathFor("base.win");
        WriteSyntheticBase(basePath);

        string dataDir = temp.PathFor("store");
        var store = new ManagerStore(dataDir);
        store.Set("base", basePath);

        var library = new ModLibrary(store);

        // Reject source ZIP smuggling a .win or .exe
        string badZip = temp.PathFor("bad-source.zip");
        using (var zip = ZipFile.Open(badZip, ZipArchiveMode.Create))
        {
            using (var e1 = zip.CreateEntry(RecipePackage.ManifestFileName).Open())
                e1.Write("""{"schemaVersion":1,"id":"x","generator":"perf","name":"Bad"}"""u8);
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
        var world = merged.Data.Rooms.ByName("NewWorld")!;
        Assert.Single(world.GameObjects, i => i.ObjectDefinition.Name.Content == "jmk_jamm_silo_garrusk");
        var silo = Assert.Single(world.GameObjects, i => i.ObjectDefinition.Name.Content == "MissileSilo");
        Assert.NotNull(silo.CreationCode);
        Assert.NotNull(merged.Data.GameObjects.ByName("obj_jamm_console"));
        Assert.NotNull(merged.Data.GameObjects.ByName("obj_jamm_smooth"));
        Assert.NotNull(merged.Data.GameObjects.ByName("obj_jamm_hd"));
        Assert.NotNull(merged.Data.GameObjects.ByName("jmk_example_fish_goldie"));
    }

    private static void WriteSyntheticBase(string path)
    {
        using var data = UndertaleData.CreateNew();
        data.GeneralInfo.Major = 1;
        data.GeneralInfo.Minor = 0;
        data.GeneralInfo.Release = 0;
        data.GeneralInfo.Build = 1773;
        data.GeneralInfo.BytecodeVersion = 16;
        foreach (string name in new[] { "FishSmall", "FishMedium" })
            data.GameObjects.Add(new() { Name = data.Strings.MakeString(name) });
        var create = data.GameObjects.ByName("FishSmall")!.EventHandlerFor(EventType.Create, 0u, data);
        var night = UndertaleCode.CreateEmptyEntry(data, "gml_Script_is_it_night");
        data.Scripts.Add(new() { Name = data.Strings.MakeString("is_it_night"), Code = night });
        var compiler = new CompileGroup(data);
        compiler.QueueCodeReplace(create, "size = 1;");
        compiler.QueueCodeReplace(night, "return 0;");
        ArchiveVerifier.Compile(compiler, "synthetic site package base");
        using var stream = File.Create(path);
        UndertaleIO.Write(stream, data);
    }

    private sealed class TempDir : IDisposable
    {
        private readonly string root = Path.Combine(Path.GetTempPath(), "cgfmm-silo-tests-" + Guid.NewGuid().ToString("N"));
        internal TempDir() => Directory.CreateDirectory(root);
        internal string PathFor(string name) => Path.Combine(root, name);
        public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
}
