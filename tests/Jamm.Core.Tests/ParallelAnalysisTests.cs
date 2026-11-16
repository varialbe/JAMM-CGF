using System.Text.RegularExpressions;
using Jamm.Core.Browsing;
using Jamm.Core.Building;
using Jamm.Core.Patching;
using Jamm.Core.Management;
using UndertaleModLib;
using UndertaleModLib.Compiler;
using UndertaleModLib.Models;

namespace Jamm.Core.Tests;

public sealed partial class BuildingTests
{
    [Fact]
    public void ParallelAndCachedAnalysisPreservePatchFactsAndArchiveBytes()
    {
        using var temp = new TemporaryDirectory();
        string basePath = temp.PathFor("base.win"), modPath = temp.PathFor("mod.win");
        void AddEntries(UndertaleData data, bool changed)
        {
            var compiler = new CompileGroup(data);
            for (int i = 0; i < 32; i++)
            {
                var entry = UndertaleCode.CreateEmptyEntry(data, $"parallel_{i}");
                compiler.QueueCodeReplace(entry, $"return {i + (changed ? 1 : 0)};");
            }
            Assert.True(compiler.Compile().Successful);
            if (changed) data.GameObjects[0].Depth = 13;
        }
        WriteModel(basePath, d => AddEntries(d, false));
        WriteModel(modPath, d => AddEntries(d, true));
        ComparePaths(basePath, modPath, temp.PathFor("cache"), temp.PathFor("sequential.win"), temp.PathFor("parallel.win"));
        var first = new ManagerStore(temp.PathFor("facts-sequential")); first.Set("base", basePath);
        var second = new ManagerStore(temp.PathFor("facts-parallel")); second.Set("base", basePath);
        var sequentialFacts = new ModFactAnalyzer(first, new() { MaxDegreeOfParallelism = 1, UseCache = false }).Analyze(modPath, default);
        var parallelFacts = new ModFactAnalyzer(second, new() { MaxDegreeOfParallelism = 4 }).Analyze(modPath, default);
        Assert.Equal(sequentialFacts with { Adds = [] }, parallelFacts with { Adds = [] });
        Assert.Equal(sequentialFacts.Adds, parallelFacts.Adds);
        string cacheRoot = Path.Combine(temp.PathFor("cache"), "base-analysis");
        string oversized = Path.Combine(cacheRoot, "oversized.json");
        using (var stream = File.Create(oversized)) stream.SetLength(300L * 1024 * 1024);
        File.SetLastWriteTimeUtc(oversized, DateTime.UnixEpoch);
        foreach (string file in Directory.GetFiles(cacheRoot, "*.json").Where(p => p != oversized)) File.WriteAllText(file, "{");
        using var original = GameFileLoader.Load(basePath);
        using var mod = GameFileLoader.Load(modPath);
        var options = new AnalysisOptions { MaxDegreeOfParallelism = 4, DataDirectory = temp.PathFor("cache") };
        var patch = PatchBuilder.Create(original, mod, options: options);
        Assert.False(File.Exists(oversized));
        Assert.InRange(Directory.GetFiles(cacheRoot).Sum(p => new FileInfo(p).Length), 1, 256L * 1024 * 1024);
        string changedBase = temp.PathFor("different-base.win");
        WriteModel(changedBase, d => AddEntries(d, true));
        using var different = GameFileLoader.Load(changedBase);
        var actual = PatchBuilder.Create(different, mod, options: options);
        var expected = PatchBuilder.Create(different, mod, options: options with { UseCache = false });
        Assert.True(PatchJson.Serialize(actual with { CreatedUtc = DateTimeOffset.UnixEpoch })
            == PatchJson.Serialize(expected with { CreatedUtc = DateTimeOffset.UnixEpoch }), "Different base reused stale cache.");
        Assert.True(Directory.GetFiles(cacheRoot, "*.json").Length >= 4);
        if (!OperatingSystem.IsWindows())
        {
            string target = temp.PathFor("linked-target"); Directory.CreateDirectory(target);
            string link = temp.PathFor("linked-cache"); Directory.CreateSymbolicLink(link, target);
            PatchBuilder.Create(original, mod, options: options with { DataDirectory = link });
            Assert.Empty(Directory.GetFiles(target, "*", SearchOption.AllDirectories));
        }
    }

    [RealFilesFact]
    public void RealParallelAndSequentialPatchesFactsAndArchiveHashesMatch()
    {
        using var temp = new TemporaryDirectory();
        foreach (string path in Directory.GetFiles(Environment.GetEnvironmentVariable("JAMM_MODS_DIR")!, "*.win").Order())
            ComparePaths(Environment.GetEnvironmentVariable("JAMM_ORIGINAL")!, path,
                temp.PathFor("cache"), temp.PathFor("sequential.win"), temp.PathFor("parallel.win"));
    }

    private static void ComparePaths(string basePath, string modPath, string cache, string sequentialOutput, string parallelOutput)
    {
        var sequential = new AnalysisOptions { MaxDegreeOfParallelism = 1, UseCache = false };
        var parallel = new AnalysisOptions { MaxDegreeOfParallelism = 4, DataDirectory = cache };
        using var original = GameFileLoader.Load(basePath);
        using var mod = GameFileLoader.Load(modPath);
        var a = PatchBuilder.Create(original, mod, options: sequential);
        var progress = new List<string>();
        var b = PatchBuilder.Create(original, mod, progress: progress.Add, options: parallel);
        var warm = PatchBuilder.Create(original, mod, options: parallel);
        string Normalize(ModPatch patch) => PatchJson.Serialize(patch with { CreatedUtc = DateTimeOffset.UnixEpoch });
        Assert.True(Normalize(a) == Normalize(b), "Parallel patch JSON differs.");
        Assert.True(Normalize(a) == Normalize(warm), "Cached patch JSON differs.");
        Assert.Equal(ModFactAnalyzer.Extract(a, mod.Data.GameObjects), ModFactAnalyzer.Extract(b, mod.Data.GameObjects));
        // Each batch is serialized through its own progress gate and ends at m/m.
        int previous = 0;
        foreach (string message in progress.Where(m => m.StartsWith("Decompiling", StringComparison.Ordinal)))
        {
            var match = Regex.Match(message, @" (\d+)/(\d+)$");
            Assert.True(match.Success);
            int count = int.Parse(match.Groups[1].Value), total = int.Parse(match.Groups[2].Value);
            if (count == 1) previous = 0;
            Assert.Equal(previous + 1, count); Assert.InRange(count, 1, total); previous = count;
        }
        ArchiveBuilder.Build(basePath, [new(a, modPath)], sequentialOutput, options: sequential);
        ArchiveBuilder.Build(basePath, [new(b, modPath)], parallelOutput, options: parallel);
        Assert.Equal(BuildFiles.Hash(sequentialOutput), BuildFiles.Hash(parallelOutput));
        // Corrupt cache entries must be disposable and recover without changing results.
        foreach (string file in Directory.GetFiles(Path.Combine(cache, "base-analysis"), "*.json")) File.WriteAllText(file, "{");
        Assert.True(Normalize(a) == Normalize(PatchBuilder.Create(original, mod, options: parallel)), "Recovered cache changed patch.");
    }
}
