using System.Buffers.Binary;
using System.Diagnostics;
using System.IO.Compression;
using Jamm.Core.Building;
using Jamm.Core.Management;
using Jamm.Core.Merging;
using Jamm.Core.ModKit;
using Jamm.Core.Patching;
using UndertaleModLib;
using UndertaleModLib.Compiler;
using UndertaleModLib.Models;

namespace Jamm.Core.Tests;

/// <summary>Verifies ModKit package validation, sprite/object/code generation, merging, CLI, and example packages.</summary>
public sealed class ModKitTests
{
    [Theory]
    [InlineData("fish")]
    [InlineData("upgrade")]
    [InlineData("boat")]
    [InlineData("hat")]
    public void ScaffoldCreatesValidCheckablePackagesForAllTemplates(string template)
    {
        using var temp = new TemporaryDirectory();
        string dir = temp.PathFor("pkg-" + template);
        ModKitPackage.Scaffold(dir, template);

        var manifest = ModKitPackage.Load(dir);
        Assert.Equal("pkg-" + template, manifest.Id);
        Assert.Throws<InvalidOperationException>(() => ModKitPackage.Scaffold(dir, template));
    }

    [Fact]
    public void PackageValidationRejectsInvalidInputsWithClearContext()
    {
        using var temp = new TemporaryDirectory();

        Assert.Throws<DirectoryNotFoundException>(() => ModKitPackage.Load(temp.PathFor("missing-dir")));

        string emptyDir = temp.PathFor("empty");
        Directory.CreateDirectory(emptyDir);
        Assert.Throws<FileNotFoundException>(() => ModKitPackage.Load(emptyDir));

        string badJson = temp.PathFor("bad-json");
        Directory.CreateDirectory(badJson);
        File.WriteAllText(Path.Combine(badJson, "jamm-mod.json"), "{ invalid");
        var badJsonEx = Assert.Throws<InvalidDataException>(() => ModKitPackage.Load(badJson));
        Assert.Contains("jamm-mod.json", badJsonEx.Message);

        // Bad id
        string badId = CreateMinimalPackage(temp, "bad-id", """{ "id": "Bad ID!", "name": "Bad" }""");
        Assert.Contains("$.id", Assert.Throws<InvalidDataException>(() => ModKitPackage.Load(badId)).Message);

        // Missing sprite + path traversal + duplicate keys + invalid size + bad PNG + bad frames
        string dir = temp.PathFor("errors");
        Directory.CreateDirectory(Path.Combine(dir, "art"));
        File.WriteAllBytes(Path.Combine(dir, "art", "not_a_png.png"), "notpng"u8.ToArray());
        File.WriteAllBytes(Path.Combine(dir, "art", "odd.png"), SolidPng(15, 10));

        string CheckError(string json)
        {
            File.WriteAllText(Path.Combine(dir, "jamm-mod.json"), json);
            return Assert.Throws<InvalidDataException>(() => ModKitPackage.Load(dir)).Message;
        }

        Assert.Contains("$.fish[0].size", CheckError("""
            { "id": "ok", "name": "Ok", "fish": [{ "key": "f", "name": "F", "size": "colossal", "sprite": "art/odd.png", "spawn": { "x": [0, 10], "y": [0, 10], "count": 1 } }] }
            """));

        Assert.Contains("$.fish[0].sprite", CheckError("""
            { "id": "ok", "name": "Ok", "fish": [{ "key": "f", "name": "F", "size": "small", "sprite": "../secret.png", "spawn": { "x": [0, 10], "y": [0, 10], "count": 1 } }] }
            """));

        Assert.Contains("$.fish[0].sprite", CheckError("""
            { "id": "ok", "name": "Ok", "fish": [{ "key": "f", "name": "F", "size": "small", "sprite": "art/missing.png", "spawn": { "x": [0, 10], "y": [0, 10], "count": 1 } }] }
            """));

        Assert.Contains("$.fish[0].sprite", CheckError("""
            { "id": "ok", "name": "Ok", "fish": [{ "key": "f", "name": "F", "size": "small", "sprite": "art/not_a_png.png", "spawn": { "x": [0, 10], "y": [0, 10], "count": 1 } }] }
            """));

        Assert.Contains("frames=2", CheckError("""
            { "id": "ok", "name": "Ok", "fish": [{ "key": "f", "name": "F", "size": "small", "sprite": "art/odd.png", "frames": 2, "spawn": { "x": [0, 10], "y": [0, 10], "count": 1 } }] }
            """));

        Assert.Contains("Duplicate key 'dup'", CheckError("""
            {
              "id": "ok", "name": "Ok",
              "fish": [
                { "key": "dup", "name": "A", "size": "small", "sprite": "art/odd.png", "spawn": { "x": [0, 10], "y": [0, 10], "count": 1 } },
                { "key": "dup", "name": "B", "size": "small", "sprite": "art/odd.png", "spawn": { "x": [0, 10], "y": [0, 10], "count": 1 } }
              ]
            }
            """));

        Assert.Contains("$.fish[0].hooks.step", CheckError("""
            { "id": "ok", "name": "Ok", "fish": [{ "key": "f", "name": "F", "size": "small", "sprite": "art/odd.png", "spawn": { "x": [0, 10], "y": [0, 10], "count": 1 }, "hooks": { "step": "hooks/nope.gml" } }] }
            """));
    }

    [Fact]
    public void BuilderCompilesAllAssetTypesAndTwoModKitModsMergeWithoutConflicts()
    {
        using var temp = new TemporaryDirectory();
        string basePath = temp.PathFor("base.win");
        WriteSyntheticBase(basePath);
        string baseHash = BuildFiles.Hash(basePath);

        string pkgA = temp.PathFor("pkg-alpha");
        Directory.CreateDirectory(Path.Combine(pkgA, "art"));
        Directory.CreateDirectory(Path.Combine(pkgA, "hooks"));
        File.WriteAllBytes(Path.Combine(pkgA, "art", "f1.png"), SolidPng(48, 24)); // 2 frames of 24x24
        File.WriteAllText(Path.Combine(pkgA, "hooks", "f1_step.gml"), "image_blend = c_aqua;\n");
        File.WriteAllText(Path.Combine(pkgA, "jamm-mod.json"), """
            {
              "id": "alpha-pack",
              "name": "Alpha Pack",
              "version": "1.0.0",
              "fish": [
                {
                  "key": "aqua-fin",
                  "name": "Aqua Fin",
                  "size": "small",
                  "sprite": "art/f1.png",
                  "frames": 2,
                  "value": 250,
                  "xp": 50,
                  "spawn": { "x": [1100, 1800], "y": [1400, 1800], "count": 3 },
                  "hooks": { "step": "hooks/f1_step.gml" }
                }
              ]
            }
            """);

        string pkgB = temp.PathFor("pkg-beta");
        Directory.CreateDirectory(Path.Combine(pkgB, "art"));
        Directory.CreateDirectory(Path.Combine(pkgB, "hooks"));
        File.WriteAllBytes(Path.Combine(pkgB, "art", "f2.png"), SolidPng(32, 24));
        File.WriteAllBytes(Path.Combine(pkgB, "art", "boat.png"), SolidPng(120, 40));
        File.WriteAllBytes(Path.Combine(pkgB, "art", "boat_card.png"), SolidPng(64, 32));
        File.WriteAllBytes(Path.Combine(pkgB, "art", "hat.png"), SolidPng(40, 32));
        File.WriteAllBytes(Path.Combine(pkgB, "art", "upg.png"), SolidPng(50, 50));
        File.WriteAllText(Path.Combine(pkgB, "hooks", "hat_effect.gml"), "value_scalar[0] = 1.5;\n");
        File.WriteAllText(Path.Combine(pkgB, "hooks", "upg_apply.gml"), "Rod.upgrades[1] += 2 * argument0;\n");
        File.WriteAllText(Path.Combine(pkgB, "jamm-mod.json"), """
            {
              "id": "beta-pack",
              "name": "Beta Pack",
              "fish": [
                {
                  "key": "beta-bass",
                  "name": "Beta Bass",
                  "size": "medium",
                  "sprite": "art/f2.png",
                  "value": 500,
                  "spawn": { "x": [1500, 2200], "y": [1600, 2100], "count": 2 }
                }
              ],
              "boats": [
                {
                  "key": "skiff",
                  "name": "Skiff",
                  "cost": 12000,
                  "card": "art/boat_card.png",
                  "sprite": "art/boat.png",
                  "stats": { "maxSpeed": 5, "capacity": 5 }
                }
              ],
              "hats": [
                {
                  "key": "visor",
                  "name": "Visor",
                  "sprite": "art/hat.png",
                  "value": 2000,
                  "hooks": { "effect": "hooks/hat_effect.gml" }
                }
              ],
              "upgrades": [
                {
                  "key": "turbo",
                  "name": "Turbo",
                  "description": "Fast reel",
                  "cost": 0.3,
                  "icon": "art/upg.png",
                  "maxStacks": 3,
                  "hooks": { "apply": "hooks/upg_apply.gml" }
                }
              ]
            }
            """);

        string modAPath = temp.PathFor("alpha.win");
        string modBPath = temp.PathFor("beta.win");
        var resA = ModKitBuilder.Build(pkgA, basePath, modAPath);
        var resB = ModKitBuilder.Build(pkgB, basePath, modBPath);

        Assert.Equal(1, resA.SpritesAdded);
        Assert.Equal(1, resA.ObjectsAdded);
        Assert.Equal(3, resA.ScriptsAdded); // jamm_fish_register, jmk_alpha_pack_aqua_fin_step, jamm_init_alpha_pack
        Assert.Equal(5, resB.SpritesAdded); // f2, boat, boat_card, hat, upg
        Assert.Equal(3, resB.ObjectsAdded);
        Assert.Equal(baseHash, BuildFiles.Hash(basePath)); // Input untouched!

        using (var loadedA = GameFileLoader.Load(modAPath))
        {
            var spr = loadedA.Data.Sprites.ByName("jmk_alpha_pack_aqua_fin_spr");
            Assert.NotNull(spr);
            Assert.Equal(2, spr.Textures.Count);
            Assert.Equal(24u, spr.Width);
            Assert.Equal(24u, spr.Height);
            Assert.Equal(12, spr.OriginX);
            Assert.Equal(12, spr.OriginY);
            Assert.Contains("return -1;", PatchBuilder.ReadCode(loadedA, "gml_Script_jamm_fish_register").Text);
            Assert.Contains("jamm_fish_register(\"alpha-pack:aqua-fin\"", PatchBuilder.ReadCode(loadedA, "gml_Script_jamm_init_alpha_pack").Text);
        }

        // Diff, check, and merge both ModKit mods together via ArchiveBuilder
        using var orig = GameFileLoader.Load(basePath);
        using var modA = GameFileLoader.Load(modAPath);
        using var modB = GameFileLoader.Load(modBPath);
        var patchA = PatchBuilder.Create(orig, modA, "Alpha Pack");
        var patchB = PatchBuilder.Create(orig, modB, "Beta Pack");

        var report = ConflictAnalyzer.Analyze([patchA, patchB]);
        Assert.DoesNotContain(report.Findings, f => f.Severity == "CONFLICT");

        string mergedPath = temp.PathFor("merged.win");
        var mergedBuild = ArchiveBuilder.Build(basePath, [new(patchA, modAPath), new(patchB, modBPath)], mergedPath);
        Assert.NotNull(mergedBuild.Verification);

        using var merged = GameFileLoader.Load(mergedPath);
        Assert.NotNull(merged.Data.GameObjects.ByName("jmk_alpha_pack_aqua_fin"));
        Assert.NotNull(merged.Data.GameObjects.ByName("jmk_beta_pack_beta_bass"));
        Assert.NotNull(merged.Data.GameObjects.ByName("jmk_beta_pack_skiff"));
        Assert.NotNull(merged.Data.GameObjects.ByName("jmk_beta_pack_visor"));
        Assert.NotNull(merged.Data.Sprites.ByName("jmk_beta_pack_turbo_icon"));
        Assert.Equal(0, merged.Data.Sprites.ByName("jmk_beta_pack_turbo_icon")!.OriginX);
        Assert.Equal(0, merged.Data.Sprites.ByName("jmk_beta_pack_turbo_icon")!.OriginY);

        string apiInit = PatchBuilder.ReadCode(merged, FishRuntime.ApiInitCode).Text;
        Assert.Contains("jamm_init_alpha_pack()", apiInit);
        Assert.Contains("jamm_init_beta_pack()", apiInit);
        string betaInit = PatchBuilder.ReadCode(merged, "gml_Script_jamm_init_beta_pack").Text;
        Assert.Contains("jmk_beta_pack_beta_bass", betaInit);
        Assert.Contains("instance_create(", PatchBuilder.ReadCode(merged, FishRuntime.RegisterCode).Text);
    }

    [Fact]
    public void CliModKitNewCheckBuildAndAddFolderWorkEndToEnd()
    {
        using var temp = new TemporaryDirectory();
        string dataDir = temp.PathFor("data-dir");
        string basePath = temp.PathFor("base.win");
        string pkgDir = temp.PathFor("my-fish");
        string outPath = temp.PathFor("my-fish.win");
        WriteSyntheticBase(basePath);

        var (newCode, newOut, _) = RunCli(["--data-dir", dataDir, "modkit", "new", pkgDir, "--template", "fish"]);
        Assert.Equal(0, newCode);
        Assert.Contains("Created ModKit 'fish'", newOut);

        var (chkCode, chkOut, _) = RunCli(["--data-dir", dataDir, "modkit", "check", pkgDir]);
        Assert.Equal(0, chkCode);
        Assert.Contains("is valid:", chkOut);

        var (cfgCode, _, _) = RunCli(["--data-dir", dataDir, "config", "set", "base", basePath]);
        Assert.Equal(0, cfgCode);

        var (bldCode, bldOut, _) = RunCli(["--data-dir", dataDir, "modkit", "build", pkgDir, "--out", outPath]);
        Assert.Equal(0, bldCode);
        Assert.True(File.Exists(outPath));
        Assert.Contains("Built and verified", bldOut);

        var (addCode, addOut, _) = RunCli(["--data-dir", dataDir, "add", pkgDir]);
        Assert.Equal(0, addCode);
        Assert.Contains("Added My Fish", addOut);

        var list = new ModLibrary(new ManagerStore(dataDir)).List();
        Assert.Single(list);
        Assert.Equal("My Fish", list[0].Mod.Name);
    }

    [RealFilesFact]
    public void RealBaseBuildsAndMergesAllExamplePackages()
    {
        string basePath = Environment.GetEnvironmentVariable("JAMM_ORIGINAL")!;
        string repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        string fishPkg = Path.Combine(repoRoot, "examples", "modkit", "example-fish");
        string upgPkg = Path.Combine(repoRoot, "examples", "modkit", "showcase-upgrades");
        string harborPkg = Path.Combine(repoRoot, "examples", "modkit", "harbor-pack");

        using var temp = new TemporaryDirectory();
        string fishOut = temp.PathFor("example-fish.win");
        string upgOut = temp.PathFor("showcase-upgrades.win");
        string harborOut = temp.PathFor("harbor-pack.win");
        string mergedOut = temp.PathFor("merged.win");

        var fishBuild = ModKitBuilder.Build(fishPkg, basePath, fishOut);
        var upgBuild = ModKitBuilder.Build(upgPkg, basePath, upgOut);
        var harborBuild = ModKitBuilder.Build(harborPkg, basePath, harborOut);
        Assert.Equal(2, fishBuild.ObjectsAdded);
        Assert.Equal(5, upgBuild.SpritesAdded);
        Assert.Equal(6, harborBuild.SpritesAdded);
        Assert.Equal(4, harborBuild.ObjectsAdded);
        Assert.Equal(4, harborBuild.ScriptsAdded);

        using var orig = GameFileLoader.Load(basePath);
        using var fishMod = GameFileLoader.Load(fishOut);
        using var upgMod = GameFileLoader.Load(upgOut);
        using var harborMod = GameFileLoader.Load(harborOut);
        var patchFish = PatchBuilder.Create(orig, fishMod, "Example Fish");
        var patchUpg = PatchBuilder.Create(orig, upgMod, "Showcase Upgrades");
        var patchHarbor = PatchBuilder.Create(orig, harborMod, "Harbor Pack");

        var report = ConflictAnalyzer.Analyze([patchHarbor, patchFish, patchUpg]);
        Assert.DoesNotContain(report.Findings, f => f.Severity == "CONFLICT");

        var merged = ArchiveBuilder.Build(
            basePath,
            [new(patchHarbor, harborOut), new(patchFish, fishOut), new(patchUpg, upgOut)],
            mergedOut);
        Assert.NotNull(merged.Verification);

        using var mergedFile = GameFileLoader.Load(mergedOut);
        Assert.NotNull(mergedFile.Data.GameObjects.ByName("jmk_example_fish_goldie"));
        Assert.NotNull(mergedFile.Data.GameObjects.ByName("jmk_example_fish_glowfin"));
        Assert.NotNull(mergedFile.Data.GameObjects.ByName("jmk_harbor_pack_paddle_raft"));
        Assert.NotNull(mergedFile.Data.GameObjects.ByName("jmk_harbor_pack_comet_cruiser"));
        Assert.NotNull(mergedFile.Data.GameObjects.ByName("jmk_harbor_pack_fishers_beanie"));
        Assert.NotNull(mergedFile.Data.GameObjects.ByName("jmk_harbor_pack_lucky_captain_hat"));

        string apiInit = PatchBuilder.ReadCode(mergedFile, FishRuntime.ApiInitCode).Text;
        Assert.Contains("jamm_init_harbor_pack()", apiInit);
        Assert.Contains("jamm_init_example_fish()", apiInit);
        Assert.Contains("jamm_init_showcase_upgrades()", apiInit);
        Assert.Contains("BoatData.nItems", PatchBuilder.ReadCode(mergedFile, "gml_Script_jamm_boat_register").Text);
        Assert.Contains("GearData.n_gear", PatchBuilder.ReadCode(mergedFile, "gml_Script_jamm_hat_register").Text);
        Assert.Contains("scroll_row", PatchBuilder.ReadCode(mergedFile, "gml_Object_CardHandler_Create_0").Text);
    }

    private static (int ExitCode, string StdOut, string StdErr) RunCli(string[] args)
    {
        string cliDll = typeof(Jamm.Cli.ModKitCommand).Assembly.Location;
        var psi = new ProcessStartInfo("dotnet", [cliDll, .. args])
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        using var proc = Process.Start(psi)!;
        string stdout = proc.StandardOutput.ReadToEnd();
        string stderr = proc.StandardError.ReadToEnd();
        proc.WaitForExit();
        return (proc.ExitCode, stdout, stderr);
    }

    private static string CreateMinimalPackage(TemporaryDirectory temp, string folder, string json)
    {
        string dir = temp.PathFor(folder);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "jamm-mod.json"), json);
        return dir;
    }

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

        var catalogCreate = data.GameObjects.ByName("CatalogData")!.EventHandlerFor(EventType.Create, 0u, data);
        var saveStep = data.GameObjects.ByName("SaveManager")!.EventHandlerFor(EventType.Step, 0u, data);
        var toName = UndertaleCode.CreateEmptyEntry(data, FishRuntime.FishObjectToName);
        data.Scripts.Add(new() { Name = data.Strings.MakeString("fish_object_to_name"), Code = toName });
        var lunge = UndertaleCode.CreateEmptyEntry(data, "gml_Script_fish_lunge");
        data.Scripts.Add(new() { Name = data.Strings.MakeString("fish_lunge"), Code = lunge });

        var compiler = new CompileGroup(data);
        compiler.QueueCodeReplace(catalogCreate, "object[0, 0] = BaseFish;\nname[0, 0] = \"Base\";\nmap[0, 0] = 0;\ndesc[0, 0] = \"Base\";\n");
        compiler.QueueCodeReplace(saveStep, "if (!loaded) { loaded = 1; }\n");
        compiler.QueueCodeReplace(toName, "return object_get_name(argument0);\n");
        compiler.QueueCodeReplace(lunge, "return 0;\n");
        Assert.True(compiler.Compile().Successful);

        using var stream = File.Create(path);
        UndertaleIO.Write(stream, data);
    }

    private static byte[] SolidPng(int width, int height)
    {
        byte[] sig = [137, 80, 78, 71, 13, 10, 26, 10];
        using var ms = new MemoryStream();
        ms.Write(sig);
        WriteChunk(ms, "IHDR"u8, w =>
        {
            Span<byte> buf = stackalloc byte[13];
            BinaryPrimitives.WriteInt32BigEndian(buf[0..4], width);
            BinaryPrimitives.WriteInt32BigEndian(buf[4..8], height);
            buf[8] = 8;
            buf[9] = 6;
            w.Write(buf);
        });
        byte[] raw = new byte[height * (1 + width * 4)];
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                int i = y * (1 + width * 4) + 1 + x * 4;
                raw[i] = 255; raw[i + 1] = 200; raw[i + 2] = 60; raw[i + 3] = 255;
            }
        byte[] compressed;
        using (var zms = new MemoryStream())
        {
            using (var zlib = new ZLibStream(zms, CompressionLevel.Optimal, leaveOpen: true))
                zlib.Write(raw);
            compressed = zms.ToArray();
        }
        WriteChunk(ms, "IDAT"u8, w => w.Write(compressed));
        WriteChunk(ms, "IEND"u8, _ => { });
        return ms.ToArray();

        static void WriteChunk(Stream output, ReadOnlySpan<byte> type, Action<Stream> writeData)
        {
            using var body = new MemoryStream();
            body.Write(type);
            writeData(body);
            byte[] bytes = body.ToArray();
            Span<byte> len = stackalloc byte[4];
            BinaryPrimitives.WriteInt32BigEndian(len, bytes.Length - 4);
            output.Write(len);
            output.Write(bytes);
            uint crc = 0xFFFFFFFFu;
            foreach (byte b in bytes)
            {
                crc ^= b;
                for (int bit = 0; bit < 8; bit++)
                    crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320u : crc >> 1;
            }
            Span<byte> crcBuf = stackalloc byte[4];
            BinaryPrimitives.WriteUInt32BigEndian(crcBuf, ~crc);
            output.Write(crcBuf);
        }
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        private readonly string directory = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "cgfmm-modkit-tests-" + Guid.NewGuid().ToString("N"));
        internal TemporaryDirectory() => Directory.CreateDirectory(directory);
        internal string PathFor(string name) => System.IO.Path.Combine(directory, name);
        public void Dispose() => Directory.Delete(directory, true);
    }
}
