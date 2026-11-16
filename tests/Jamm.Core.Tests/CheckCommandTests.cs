using System.Security.Cryptography;
using System.Text.Json;
using Jamm.Core.Patching;

namespace Jamm.Core.Tests;

/// <summary>Patch-only CLI checks, exit statuses and protections for all input paths.</summary>
public class CheckCommandTests
{
    [Fact]
    public void PatchOnlyChecksNeedNoLoadableGameArchiveAndPreserveEveryInput()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"jamm-patch-check-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            string basePath = Path.Combine(directory, "base.win");
            File.WriteAllText(basePath, "Synthetic bytes used only for hash verification.");
            string hash = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(basePath)));
            string aPath = Path.Combine(directory, "a.cgfmm-patch.json"), bPath = Path.Combine(directory, "b.cgfmm-patch.json");
            var changes = new SortedDictionary<string, AssetChanges>(StringComparer.Ordinal);
            foreach (string family in new[] { "code", "scripts", "objects", "rooms", "sprites", "texturePages", "sounds", "fonts", "backgrounds", "paths", "timelines", "strings", "variables", "functions" })
                changes[family] = new([], [], []);
            var a = new ModPatch(PatchJson.FormatVersion, "test", DateTimeOffset.UnixEpoch,
                new("a.win", "a", 1, "1.4", 16, "A"), new("base.win", hash, 1, "1.4", 16), [], changes, new(StringComparer.Ordinal), []);
            File.WriteAllText(aPath, PatchJson.Serialize(a));
            File.WriteAllText(bPath, PatchJson.Serialize(a with { Mod = a.Mod with { Name = "B" } }));
            string before = File.ReadAllText(aPath);
            string output = Path.Combine(directory, "report.json");
            Assert.Equal(0, Jamm.Cli.CheckCommand.Run([aPath, bPath, "--base", basePath, "--json", output, "--verbose"]));
            var report = JsonDocument.Parse(File.ReadAllText(output)).RootElement.GetProperty("report");
            Assert.Equal(hash, report.GetProperty("baseSha256").GetString());
            Assert.Equal(0, report.GetProperty("counts").GetProperty("CONFLICT").GetInt32());
            Assert.Equal(before, File.ReadAllText(aPath));
            Assert.Throws<ArgumentException>(() => Jamm.Cli.CheckCommand.Run([aPath, bPath, "--base", basePath, "--json", aPath]));
            Assert.Throws<ArgumentException>(() => Jamm.Cli.CheckCommand.Run([aPath, bPath, "--base", basePath, "--json", Path.Combine(directory, "result.win")]));
            File.WriteAllText(bPath, PatchJson.Serialize(a with { Base = a.Base with { Sha256 = "wrong" } }));
            Assert.Contains("targets", Assert.Throws<InvalidDataException>(() => Jamm.Cli.CheckCommand.Run([aPath, bPath, "--base", basePath])).Message);
            File.WriteAllText(bPath, PatchJson.Serialize(a with { FormatVersion = 1 }));
            Assert.Contains("--force", Assert.Throws<InvalidDataException>(() => Jamm.Cli.CheckCommand.Run([aPath, bPath, "--base", basePath])).Message);
        }
        finally { Directory.Delete(directory, true); }
    }
}
