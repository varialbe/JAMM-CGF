using Jamm.Core.Building;
using Jamm.Core.Merging;
using Jamm.Core.ModKit;
using Jamm.Core.Patching;
using UndertaleModLib;
using UndertaleModLib.Util;

namespace Jamm.Core.Tests;

/// <summary>Opt-in integration checks against the original archive and the seven local pack inputs.</summary>
public sealed class SiloAreaRealTests
{
    [SiloAreaFilesFact]
    public void BuildsTheSiloAloneAndAcrossThePackMatrix()
    {
        string basePath = Environment.GetEnvironmentVariable("JAMM_ORIGINAL")!;
        string modsRoot = Environment.GetEnvironmentVariable("JAMM_SILO_INPUTS")!;
        string package = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..",
            "examples", "modkit", "silo"));
        const string hatchObject = "jmk_jamm_silo_object_hatch";
        var hatchPiece = Assert.Single(Assert.Single(ModKitPackage.Load(package).Areas).Pieces, p => p.Object == hatchObject);
        Assert.InRange(hatchPiece.Set["depth"], -19.49, -19.01);
        using var directory = new ModKitAreaTests.AreaTestDirectory();
        using var original = GameFileLoader.Load(basePath);
        string siloPath = directory.PathFor("silo-source.win");
        ModKitBuilder.Build(package, basePath, siloPath);
        using var source = GameFileLoader.Load(siloPath);
        const string name = "The Silo";
        var silo = new BuildInput(PatchBuilder.Create(original, source, name), siloPath);
        Assert.Empty(silo.Patch.Changes["code"].Changed);
        Assert.Empty(silo.Patch.Changes["code"].Removed);
        Assert.Empty(silo.Patch.Changes["objects"].Changed);
        using (var textures = new TextureWorker())
        {
            var body = source.Data.Sprites.ByName("jmk_jamm_silo_recipe_silo_body")!;
            var rim = source.Data.Sprites.ByName("jmk_jamm_silo_recipe_silo_rim")!;
            int offsetX = body.OriginX - rim.OriginX;
            int offsetY = body.OriginY - rim.OriginY;
            using var bodyImage = textures.GetTextureFor(body.Textures[0].Texture, body.Name.Content, includePadding: true);
            using var rimImage = textures.GetTextureFor(rim.Textures[0].Texture, rim.Name.Content, includePadding: true);
            using var bodyPixels = bodyImage.GetPixels();
            using var rimPixels = rimImage.GetPixels();
            byte[] back = bodyPixels.GetArea(offsetX, offsetY, rim.Width, rim.Height)!;
            byte[] front = rimPixels.GetArea(0, 0, rim.Width, rim.Height)!;
            for (int i = 0; i < front.Length; i += 4)
            {
                if (front[i + 3] == 0) continue;
                Assert.Equal(front.AsSpan(i, 3).ToArray(), back.AsSpan(i, 3).ToArray());
                Assert.Equal(255, back[i + 3]);
            }
        }

        string[] relativePaths =
        [
            "private-vel/TGE.win", "mods/pack-inputs/Antiquity.win", "private-vel/PWB.win",
            "mods/pack-inputs/Abyss.win", "mods/pack-inputs/Colour.win", "mods/pack-inputs/Verdant.win", "mods/cave-expansion.win"
        ];
        var packs = relativePaths.Select(path =>
        {
            string archive = Path.Combine(modsRoot, path);
            Assert.True(File.Exists(archive), $"Missing local Silo input '{archive}'.");
            using var file = GameFileLoader.Load(archive);
            return new BuildInput(ModSanitizer.Sanitize(PatchBuilder.Create(original, file),
                sonarCondition: VanillaSonarCondition.FromData(original.Data)).Patch, archive);
        }).ToArray();

        CheckBuild("solo", []);
        foreach (var pack in packs)
            CheckBuild(Path.GetFileNameWithoutExtension(pack.ArchivePath), [pack]);
        CheckBuild("public5", packs[1..6]);
        CheckBuild("all7", packs);

        void CheckBuild(string label, BuildInput[] selection)
        {
            var inputs = selection.Append(silo).ToArray();
            var expectedAnchor = selection.Contains(packs[^1]) ? (35472, 11984)
                : selection.Contains(packs[5]) ? (33984, 11568)
                : selection.Contains(packs[1]) ? (33776, 11440) : (33984, 11570);
            bool moved = expectedAnchor != (33984, 11570);
            var report = ConflictAnalyzer.Analyze(inputs.Select(x => x.Patch).ToArray(), allowSingle: true);
            Assert.DoesNotContain(report.Findings, f => f.Severity == "CONFLICT" && f.Mods.Contains(name));
            Assert.Equal(moved, report.Findings.Any(f => f.Kind == "area" && f.Message.Contains("follows MissileSilo")));
            string output = directory.PathFor(label + ".win");
            var result = ArchiveBuilder.Build(basePath, inputs, output);
            Assert.NotNull(result.Verification);
            var acceptance = Assert.Single(result.Acceptance!.Mods, mod => mod.Mod == name);
            Assert.Equal(acceptance.TotalCount, acceptance.ArrivedCount);
            Assert.Equal(moved, result.Plan.Decisions.Any(d => d.Contains("[CGF-AREA-001]") && d.Contains("follows MissileSilo")));
            using (var merged = GameFileLoader.Load(output))
            {
                var room = merged.Data.Rooms.ByName("NewWorld")!;
                var hatch = Assert.Single(room.GameObjects, i => i.ObjectDefinition.Name.Content == "MissileSilo");
                Assert.Equal(expectedAnchor, (hatch.X, hatch.Y));
                Assert.NotNull(hatch.CreationCode);
                var overlay = Assert.Single(room.GameObjects, i => i.ObjectDefinition.Name.Content == hatchObject);
                Assert.Equal(expectedAnchor, (overlay.X, overlay.Y));
                Assert.NotNull(overlay.CreationCode);
                Assert.Equal(9, room.GameObjects.Count(i => i.ObjectDefinition.Name.Content.StartsWith("jmk_jamm_silo_", StringComparison.Ordinal)
                    && i != overlay));
                Assert.Single(merged.Data.Sprites.ByName("jmk_jamm_silo_recipe_silo_door")!.Textures);
            }
            File.Delete(output);
        }
    }
}

/// <summary>Requires the local legacy base and the root containing all seven pack inputs.</summary>
public sealed class SiloAreaFilesFactAttribute : FactAttribute
{
    public SiloAreaFilesFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("JAMM_ORIGINAL")) ||
            string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("JAMM_SILO_INPUTS")))
            Skip = "Set JAMM_ORIGINAL and JAMM_SILO_INPUTS to opt into local Silo archive builds.";
    }
}
