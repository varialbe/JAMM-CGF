using Jamm.Core.Building;
using Jamm.Core.Management;
using Jamm.Gui.ViewModels;

namespace Jamm.Gui.Tests;

public sealed class Phase9dTests
{
    [Theory]
    [InlineData(1692207312, 1773, false, GameSuitability.UnverifiedLegacy)]
    [InlineData(1692057440, 1773, false, GameSuitability.UnverifiedLegacy)]
    [InlineData(1692057439, 1773, false, GameSuitability.NotUsable)]
    [InlineData(1692057761, 1773, false, GameSuitability.UnverifiedLegacy)]
    [InlineData(1509155170, 1772, false, GameSuitability.NotUsable)]
    [InlineData(1573603200, 1773, false, GameSuitability.NotUsable)]
    [InlineData(1660521600, 1773, false, GameSuitability.NotUsable)]
    [InlineData(1692207312, 1773, true, GameSuitability.NotUsable)]
    public void HeaderEvidenceClassifiesEveryLegacyEra(long timestamp, int build, bool yyc, GameSuitability expected)
    {
        using var files = new TestFiles(false);
        string path = files.Mod("candidate", "x = 0;", d => {
            d.GeneralInfo.Timestamp = (ulong)timestamp; d.GeneralInfo.Build = (uint)build;
            if (yyc) d.Code.Clear();
        });
        string before = BuildFiles.Hash(path);
        var result = SteamGameDetection.Inspect(path);
        Assert.Equal(expected, result.Suitability);
        if (timestamp == 1692057440)
        {
            Assert.Contains("Older legacy build", result.Reason);
            Assert.True(result.Usable); Assert.Equal("#A66B00", result.Color);
        }
        Assert.Equal(before, BuildFiles.Hash(path));
    }

    [Fact]
    public void HashAndSizeVerifyOriginalAndJammBackupWithoutOfferingMergedArchive()
    {
        using var files = new TestFiles(false);
        string path = Path.Combine(files.Root, "data.win");
        File.Copy(files.Mod("original", "x = 0;"), path);
        var hashes = new Dictionary<string, long> { [BuildFiles.Hash(path)] = new FileInfo(path).Length };
        Assert.Equal(GameSuitability.SupportedOriginal, SteamGameDetection.Inspect(path, supportedOriginals: hashes).Suitability);
        File.Copy(path, path + ".cgfmm-original");
        File.WriteAllText(path + ".cgfmm-build.json", "{}");
        File.Copy(files.Mod("merged", "x = 1;"), path, true);
        var recovered = SteamGameDetection.Inspect(path, supportedOriginals: hashes);
        Assert.Equal(path + ".cgfmm-original", recovered.Archive);
        Assert.Equal(GameSuitability.SupportedOriginal, recovered.Suitability);
        Assert.False(SteamGameDetection.Inspect(path).Usable);
        File.Delete(path + ".cgfmm-original");
        Assert.False(SteamGameDetection.Inspect(path, supportedOriginals: hashes).Usable);
    }

    [Fact]
    public void LargerUnmarkedSteamCopyIsModifiedOrOtherBuild()
    {
        using var files = new TestFiles(false);
        string path = files.Mod("steam-copy", "x = 0;", d => d.GeneralInfo.Timestamp = 1692057761);
        using (var stream = new FileStream(path, FileMode.Open, FileAccess.Write)) stream.SetLength(56078502);
        var result = SteamGameDetection.Inspect(path);
        Assert.Equal(GameSuitability.UnverifiedLegacy, result.Suitability);
        Assert.Contains("Modified or other build", result.Reason);
    }

    [Fact]
    public async Task PlainOriginalPrecedesBackupEvenWhenBackupSortsFirstByPath()
    {
        using var files = new TestFiles(false);
        var backup = new DetectedGame("a-copy/data.win.cgfmm-original", true, "backup") { Suitability = GameSuitability.SupportedOriginal };
        var plain = new DetectedGame("z-original/data.win", true, "original") { Suitability = GameSuitability.SupportedOriginal };
        var vm = new MainViewModel(files.Store, gameSearch: (_, _) => [backup, plain]);
        await vm.SearchGamesAsync();
        Assert.Equal(new[] { plain, backup }, vm.DetectedGames);
        Assert.Equal(plain, vm.DetectedLegacy);
        vm.Browse.Dispose();
    }

    private sealed class Messages : IProgress<string>
    {
        public List<string> Values { get; } = [];
        public void Report(string value) => Values.Add(value);
    }

    [Fact]
    public void CheapChecksAvoidHashingIneligibleCandidates()
    {
        using var files = new TestFiles(false);
        string path = files.Mod("old", "x = 0;", d => d.GeneralInfo.Timestamp = 1509155170);
        var messages = new Messages();
        SteamGameDetection.Inspect(path, supportedOriginals: new Dictionary<string, long> { [BuildFiles.Hash(path)] = new FileInfo(path).Length }, progress: messages);
        Assert.DoesNotContain(messages.Values, p => p.Contains("SHA-256", StringComparison.Ordinal));
        path = files.Mod("legacy", "x = 0;");
        SteamGameDetection.Inspect(path, supportedOriginals: new Dictionary<string, long> { ["fake"] = new FileInfo(path).Length + 1 }, progress: messages);
        Assert.DoesNotContain(messages.Values, p => p.Contains("SHA-256", StringComparison.Ordinal));
    }

    [Fact]
    public async Task OnlyVerifiedOriginalIsPreselectedAndRedResultsAreSeparated()
    {
        using var files = new TestFiles(false);
        var red = new DetectedGame("red", false, "unsupported");
        var yellow = new DetectedGame("yellow", true, "other build");
        var green = new DetectedGame("green", true, "verified") { Suitability = GameSuitability.SupportedOriginal };
        var vm = new MainViewModel(files.Store, gameSearch: (_, _) => [red, yellow]);
        await vm.SearchGamesAsync(); Assert.Null(vm.DetectedLegacy); Assert.Single(vm.OtherGames);
        vm.Browse.Dispose();
        vm = new MainViewModel(files.Store, gameSearch: (_, _) => [red, yellow, green]);
        await vm.SearchGamesAsync(); Assert.Equal(green, vm.DetectedLegacy);
        Assert.Equal(new[] { green, yellow }, vm.DetectedGames); vm.Browse.Dispose();
    }
}
