using Jamm.Core.Building;
using Jamm.Core.Diffing;
using Jamm.Core.Patching;
using Underanalyzer.Decompiler;
using UndertaleModLib.Decompiler;

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
                foreach (var kind in mod.Inspection.Counts.Keys.Except(new[] { AssetKind.Strings, AssetKind.Variables, AssetKind.Functions }))
                    Assert.Equal(mod.Inspection.Counts[kind], result.Inspection.Counts[kind]);
                // Definition tables have set semantics and retain base entries. Compare identities,
                // not duplicates, local compiler bookkeeping or removed strings.
                Assert.Subset(result.Data.Strings.Select(s => s.Content).ToHashSet(), original.Data.Strings.Select(s => s.Content).ToHashSet());
                Assert.Subset(result.Data.Strings.Select(s => s.Content).ToHashSet(), mod.Data.Strings.Select(s => s.Content).ToHashSet());
                var expectedContext = new GlobalDecompileContext(mod.Data);
                var actualContext = new GlobalDecompileContext(result.Data);
                var codeByName = result.Data.Code.ToDictionary(c => c.Name.Content);
                foreach (var code in mod.Data.Code.Where(c => c.ParentEntry is null))
                {
                    string expected = LineDiff.Normalize(new DecompileContext(expectedContext, code).DecompileToString());
                    string actual = LineDiff.Normalize(new DecompileContext(actualContext, codeByName[code.Name.Content]).DecompileToString());
                    // Avoid including copyrighted GML in assertion output.
                    Assert.True(expected == actual, $"Single-mod GML differs for {Path.GetFileName(path)} / {code.Name.Content}.");
                }
            }
            string forward = Path.Combine(directory, "forward.win"), reverse = Path.Combine(directory, "reverse.win");
            Assert.NotNull(ArchiveBuilder.Build(basePath, inputs, forward).Verification);
            Assert.NotNull(ArchiveBuilder.Build(basePath, inputs.AsEnumerable().Reverse().ToArray(), reverse).Verification);
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
}
