using Jamm.Core;
using UndertaleModLib;

namespace Jamm.Core.Tests;

/// <summary>Read-only loading, failure isolation and synthetic archive round trips.</summary>
public class LoadingTests
{
    [Fact]
    public void MissingAndTruncatedFilesProduceHelpfulFailures()
    {
        string missing = Path.Combine(Path.GetTempPath(), $"jamm-missing-{Guid.NewGuid():N}.win");
        var result = GameFileAnalyzer.Analyze(missing);
        Assert.Contains($"could not read {Path.GetFileName(missing)}:", result.Error);
        Assert.DoesNotContain(Path.GetDirectoryName(missing)!, System.Text.Json.JsonSerializer.Serialize(result));
        Assert.Contains("could not read", GameFileAnalyzer.Analyze("bad\0path.win").Error);
        Assert.Contains("readable", result.Error);
        string truncated = Path.GetTempFileName();
        try
        {
            System.IO.File.WriteAllBytes(truncated, [70, 79, 82, 77, 100, 0, 0, 0]);
            result = GameFileAnalyzer.Analyze(truncated);
            Assert.Contains("truncated", result.Error);
            Assert.Null(result.Inspection);
        }
        finally { System.IO.File.Delete(truncated); }
    }

    [Fact]
    public void SyntheticFileWithEmptyCodeIsUnmergeableAndReadOnly()
    {
        string path = Path.GetTempFileName();
        try
        {
            using (var data = UndertaleData.CreateNew())
            using (var stream = System.IO.File.Create(path)) UndertaleIO.Write(stream, data);
            byte[] before = System.IO.File.ReadAllBytes(path);
            var result = GameFileAnalyzer.Analyze(path);
            Assert.Null(result.Error);
            Assert.NotNull(result.Inspection);
            Assert.True(result.Inspection.IsYyc);
            Assert.DoesNotContain(result.Assets.Keys, x => x.RequiresBytecode());
            Assert.Equal(before, System.IO.File.ReadAllBytes(path));
        }
        finally { System.IO.File.Delete(path); }
    }

    [Fact]
    public void SyntheticVmRoundTripPreservesStoredVersionAndFingerprints()
    {
        string path = Path.GetTempFileName();
        try
        {
            using (var data = UndertaleData.CreateNew())
            {
                data.GeneralInfo.Major = 1;
                data.GeneralInfo.Minor = 0;
                data.GeneralInfo.Release = 0;
                data.GeneralInfo.Build = 1773;
                var code = UndertaleModLib.Models.UndertaleCode.CreateEmptyEntry(data, "synthetic");
                code.Instructions.Add(new() { Kind = UndertaleModLib.Models.UndertaleInstruction.Opcode.Exit,
                    Type1 = UndertaleModLib.Models.UndertaleInstruction.DataType.Int32 });
                using var stream = System.IO.File.Create(path);
                UndertaleIO.Write(stream, data);
            }
            var first = GameFileAnalyzer.Analyze(path);
            var second = GameFileAnalyzer.Analyze(path);
            Assert.Null(first.Error);
            Assert.Null(second.Error);
            Assert.False(first.Inspection!.IsYyc);
            Assert.Equal("1.0.0.1773", first.Inspection.GameMakerVersion);
            Assert.Equal(Path.GetFileName(path), first.FileName);
            Assert.Equal(first.Inspection.Sha256, second.Inspection!.Sha256);
            Assert.Equal(100, AssetComparer.Compare(first, second).OriginalAssetsUnchangedPercent);
            using var loaded = GameFileLoader.Load(path);
            Assert.NotEmpty(loaded.Data.Code);
            Assert.Null(GameFileAnalyzer.Fingerprint(loaded).Error);
            Assert.NotEmpty(loaded.Data.Code); // Fingerprinting does not dispose the caller's model.
            loaded.Data.Sprites.Add(new() { Name = new("broken"), CollisionMasks = null! });
            var failure = GameFileAnalyzer.Fingerprint(loaded);
            loaded.Data.Sprites[0].CollisionMasks = []; // Restore validity for UTML disposal.
            Assert.StartsWith($"could not analyse sprites 'broken' in {Path.GetFileName(path)}:", failure.Error);
            Assert.NotNull(failure.Inspection);
            Assert.DoesNotContain("could not read", failure.Error);
        }
        finally { System.IO.File.Delete(path); }
    }

}

/// <summary>Enables real-file tests only when both documented environment variables are set.</summary>
public sealed class RealFilesFactAttribute : FactAttribute
{
    public RealFilesFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("JAMM_ORIGINAL"))
            || string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("JAMM_MODS_DIR")))
            Skip = "Set JAMM_ORIGINAL and JAMM_MODS_DIR to opt into real-file tests.";
    }
}

/// <summary>Explicitly opted-in inspection and comparison of local legal game files.</summary>
public class RealFileTests
{
    [RealFilesFact]
    public void InspectAndCompareAllConfiguredFiles()
    {
        var original = GameFileAnalyzer.Analyze(Environment.GetEnvironmentVariable("JAMM_ORIGINAL")!);
        Assert.Null(original.Error);
        var paths = Directory.GetFiles(Environment.GetEnvironmentVariable("JAMM_MODS_DIR")!, "*.win");
        Assert.NotEmpty(paths);
        foreach (string path in paths)
        {
            var mod = GameFileAnalyzer.Analyze(path);
            Assert.Null(mod.Error);
            var comparison = AssetComparer.Compare(original, mod);
            Assert.Equal(original.Inspection!.IsYyc || mod.Inspection!.IsYyc ? 8 : 12, comparison.AssetTypes.Count);
            Assert.Equal(!original.Inspection!.IsYyc && !mod.Inspection!.IsYyc
                && original.Inspection.BytecodeSupported && mod.Inspection.BytecodeSupported,
                comparison.HasComparableBytecode);
        }
    }
}
