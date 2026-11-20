using Jamm.Core.Building;
using Jamm.Core.Merging;
using Jamm.Core.ModKit;
using Jamm.Core.Patching;
using UndertaleModLib;

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
                Assert.Equal(9, room.GameObjects.Count(i => i.ObjectDefinition.Name.Content.StartsWith("jmk_jamm_silo_", StringComparison.Ordinal)));
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
