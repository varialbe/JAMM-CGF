using Jamm.Core.Building;
using Jamm.Core.Diffing;
using Jamm.Core.Patching;
using Underanalyzer.Decompiler;
using UndertaleModLib.Decompiler;
using System.Text.RegularExpressions;

namespace Jamm.Core.Tests;

/// <summary>Opt-in single-mod equivalence, both three-mod orders and a copied legacy installation round trip.</summary>
public sealed class RealBuildTests
{
    [RealFilesFact]
    public void SingleModsAndBothLoadOrdersBuildVerifyAndRestore()
    {
        string basePath = Environment.GetEnvironmentVariable("JAMM_ORIGINAL")!;
        string mods = Environment.GetEnvironmentVariable("JAMM_MODS_DIR")!;
        string directory = Path.Combine(Path.GetTempPath(), "cgfmm-real-build-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string[] paths = new[] { "the-abyss-beta.win", "cave-expansion.win", "colour-expansion.win" }.Select(n => Path.Combine(mods, n)).ToArray();
        string[] hashes = new[] { basePath }.Concat(paths).Select(BuildFiles.Hash).ToArray();
        try
        {
            using var original = GameFileLoader.Load(basePath);
            List<BuildInput> inputs = [];
            foreach (string path in paths)
            {
                using var mod = GameFileLoader.Load(path);
                var patch = PatchBuilder.Create(original, mod);
                inputs.Add(new(patch, path));
                string output = Path.Combine(directory, Path.GetFileName(path));
                var build = ArchiveBuilder.Build(basePath, [inputs[^1]], output);
                Assert.NotNull(build.Verification);
                using var result = GameFileLoader.Load(output);
                foreach (var kind in mod.Inspection.Counts.Keys.Except(new[] { AssetKind.Strings, AssetKind.Variables, AssetKind.Functions, AssetKind.Objects, AssetKind.Code, AssetKind.Scripts }))
                    Assert.Equal(mod.Inspection.Counts[kind], result.Inspection.Counts[kind]);
                Assert.Equal(original.Data.GameObjects.Select(o => o.Name.Content).Union(mod.Data.GameObjects.Select(o => o.Name.Content)).Count(), result.Data.GameObjects.Count);
                AssertBaseIndices(original, result);
                Assert.True(build.SemanticEntries > 0);
                CgfAssetTypes.Register(mod.Data); CgfAssetTypes.Register(result.Data);
                if (Path.GetFileName(path) == "the-abyss-beta.win") AssertCatalog(result, "Overbite", "Overbite");
                if (Path.GetFileName(path) == "cave-expansion.win") AssertCatalog(result, "Fatneck", "Fatneck");
                AssertCatalog(result, "Bombat", "Bombat");
                // Definition tables have set semantics and retain base entries. Compare identities,
                // not duplicates, local compiler bookkeeping or removed strings.
                Assert.Subset(result.Data.Strings.Select(s => s.Content).ToHashSet(), original.Data.Strings.Select(s => s.Content).ToHashSet());
                Assert.Subset(result.Data.Strings.Select(s => s.Content).ToHashSet(), mod.Data.Strings.Select(s => s.Content).ToHashSet());
                var expectedContext = new GlobalDecompileContext(mod.Data);
                var actualContext = new GlobalDecompileContext(result.Data);
                var codeByName = result.Data.Code.ToDictionary(c => c.Name.Content);
                foreach (var code in mod.Data.Code.Where(c => c.ParentEntry is null))
                {
                    if (code.Name.Content == "gml_Object_CatalogData_Create_0" || code.Name.Content.StartsWith("gml_Object_SaveManager_", StringComparison.Ordinal)) continue;
                    // Menu paging (MenuRuntime) intentionally patches the shared card/upgrade menu handlers in every build.
                    if (code.Name.Content.StartsWith("gml_Object_CardHandler_", StringComparison.Ordinal) || code.Name.Content.StartsWith("gml_Object_UpgradeMenu_", StringComparison.Ordinal) || code.Name.Content == "gml_Object_RodMenu_Draw_0") continue;
                    string expected = LineDiff.Normalize(new DecompileContext(expectedContext, code).DecompileToString());
                    if (code.Name.Content == "gml_Object_Quest_Draw_0") continue;
                    expected = LineDiff.Normalize(Jamm.Core.Building.HudRuntime.TrackDrawState(expected));
                    string actual = LineDiff.Normalize(new DecompileContext(actualContext, codeByName[code.Name.Content]).DecompileToString());
                    // Avoid including copyrighted GML in assertion output.
                    Assert.True(expected == actual, $"Single-mod GML differs for {Path.GetFileName(path)} / {code.Name.Content}.");
                }
            }
            string forward = Path.Combine(directory, "forward.win"), reverse = Path.Combine(directory, "reverse.win");
            var forwardBuild = ArchiveBuilder.Build(basePath, inputs, forward);
            var reverseBuild = ArchiveBuilder.Build(basePath, inputs.AsEnumerable().Reverse().ToArray(), reverse);
            Assert.Equal(forwardBuild.SemanticEntries, forwardBuild.Verification!.SemanticEntries);
            Assert.Equal(reverseBuild.SemanticEntries, reverseBuild.Verification!.SemanticEntries);
            using (var merged = GameFileLoader.Load(reverse))
            { AssertBaseIndices(original, merged); AssertCatalog(merged, "Overbite", "Overbite"); AssertCatalog(merged, "Bombat", "Bombat"); }
            using (var merged = GameFileLoader.Load(forward))
            { AssertBaseIndices(original, merged); AssertCatalog(merged, "Bombat", "Bombat"); AssertCatalog(merged, "Overbite", "Overbite"); AssertCatalog(merged, "Fatneck", "Fatneck"); }
            AssertAllCatalogFish(original, paths, forward);
            AssertAllCatalogFish(original, paths, reverse);
            // Legacy named resolutions remain accepted; all mods now retain their fish.
            string caveChosen = Path.Combine(directory, "cave-chosen.win");
            Assert.NotNull(ArchiveBuilder.Build(basePath, inputs, caveChosen,
                new Dictionary<string, string> { ["code/gml_Object_CatalogData_Create_0"] = "cave-expansion" }).Verification);
            using (var merged = GameFileLoader.Load(caveChosen)) AssertCatalog(merged, "Fatneck", "Fatneck");
            Assert.NotNull(ArchiveVerifier.Verify(forward)); Assert.NotNull(ArchiveVerifier.Verify(reverse));
            string game = Path.Combine(directory, "game");
            string legacy = Path.GetDirectoryName(basePath)!;
            foreach (string file in Directory.EnumerateFiles(legacy, "*", SearchOption.AllDirectories))
            {
                string destination = Path.Combine(game, Path.GetRelativePath(legacy, file));
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!); File.Copy(file, destination);
            }
            GameInstaller.Install(forward, game, hashes[0], [basePath, .. paths]);
            Assert.Equal(BuildFiles.Hash(forward), BuildFiles.Hash(Path.Combine(game, "data.win")));
            GameInstaller.Install(reverse, game, hashes[0], [basePath, .. paths]);
            Assert.Equal(hashes[0], GameInstaller.Restore(game));
            Assert.Equal(hashes[0], BuildFiles.Hash(Path.Combine(game, "data.win")));
            Assert.Equal(hashes[0], BuildFiles.Hash(Path.Combine(game, "data.win.cgfmm-original")));
        }
        finally
        {
            Directory.Delete(directory, true);
            Assert.Equal(hashes, new[] { basePath }.Concat(paths).Select(BuildFiles.Hash).ToArray());
        }
    }
    private static void AssertAllCatalogFish(LoadedGameFile original, string[] paths, string output)
    {
        const string create = "gml_Object_CatalogData_Create_0";
        var expected = new HashSet<string>(StringComparer.Ordinal);
        var pattern = new Regex(@"^object\[\d+, \d+\] = (\w+);", RegexOptions.Multiline);
        foreach (string path in paths)
        {
            using var mod = GameFileLoader.Load(path);
            foreach (Match match in pattern.Matches(PatchBuilder.ReadCode(mod, create).Text)) expected.Add(match.Groups[1].Value);
        }
        string baseline = PatchBuilder.ReadCode(original, create).Text;
        foreach (Match match in pattern.Matches(baseline)) expected.Add(match.Groups[1].Value);
        using var merged = GameFileLoader.Load(output);
        string text = PatchBuilder.ReadCode(merged, create).Text;
        var actual = pattern.Matches(text).Select(m => m.Groups[1].Value).ToArray();
        Assert.Equal(expected.Order(StringComparer.Ordinal), actual.Order(StringComparer.Ordinal));
        Assert.Equal(actual.Length, actual.Distinct(StringComparer.Ordinal).Count());
        // Every original literal assignment remains in the original prefix, including map order.
        Assert.StartsWith(baseline.TrimEnd(), text.TrimEnd());
    }

    private static void AssertBaseIndices(LoadedGameFile original, LoadedGameFile merged)
    {
        Assert.Equal(original.Data.GameObjects.Select(o => o.Name.Content), merged.Data.GameObjects.Take(original.Data.GameObjects.Count).Select(o => o.Name.Content));
        Assert.Equal(original.Data.Sprites.Select(o => o.Name.Content), merged.Data.Sprites.Take(original.Data.Sprites.Count).Select(o => o.Name.Content));
    }

    private static void AssertCatalog(LoadedGameFile file, string displayName, string objectName)
    {
        string text = PatchBuilder.ReadCode(file, "gml_Object_CatalogData_Create_0").Text;
        var name = Regex.Matches(text, @"name\[([^\]]+)\] = ""([^""]+)"";").Cast<Match>().Last(m => m.Groups[2].Value == displayName);
        string slot = name.Groups[1].Value;
        var value = Regex.Matches(text, @"object\[" + Regex.Escape(slot) + @"\] = (\w+);").Cast<Match>().Last();
        Assert.Equal(objectName, value.Groups[1].Value);
        Assert.Contains(file.Data.GameObjects, o => o.Name.Content == objectName);
    }

}
