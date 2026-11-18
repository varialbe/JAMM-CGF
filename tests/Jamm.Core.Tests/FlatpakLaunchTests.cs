using Jamm.Core.Management;

namespace Jamm.Core.Tests;

public sealed class FlatpakLaunchTests
{
    private static ManagerConfig Config => new()
    {
        GameDir = "/run/media/game space;literal",
        Wine = new() { Mode = "proton-flatpak" }
    };

    [Fact]
    public void EntersLowestSteamInstanceWithItsEnvironmentAndFlatpakDefaults()
    {
        var host = new FakeHost();
        var command = GameLauncher.Create(Config, "/unused manager", host);
        var info = command.ToStartInfo();
        Assert.Equal("flatpak", info.FileName);
        Assert.False(info.UseShellExecute);
        Assert.Equal(new[] { "enter", "100", "env", "-i" }, info.ArgumentList.Take(4));
        Assert.Contains("HOME=/home/test", info.ArgumentList);
        Assert.Contains("DISPLAY=:1", info.ArgumentList);
        Assert.Contains("TOKEN=space;value=literal", info.ArgumentList);
        Assert.Contains("SteamAppId=343780", info.ArgumentList);
        Assert.Contains("SteamGameId=343780", info.ArgumentList);
        string steam = "/home/test/.var/app/com.valvesoftware.Steam/.local/share/Steam";
        Assert.Contains("STEAM_COMPAT_CLIENT_INSTALL_PATH=" + steam, info.ArgumentList);
        Assert.Contains("STEAM_COMPAT_DATA_PATH=" + steam + "/steamapps/compatdata/343780", info.ArgumentList);
        Assert.Equal(new[] { "python3", steam + "/steamapps/common/Proton - Experimental/proton", "run", Config.GameDir + "/Cat Goes Fishing.exe" }, info.ArgumentList.TakeLast(4));
        Assert.Equal(steam + "/steamapps/compatdata/343780", command.FlatpakCompatDataPath);
        Assert.DoesNotContain("TOKEN", command.Display());
    }

    [Fact]
    public void HonorsExplicitProtonAndSteamPaths()
    {
        var config = Config with { Proton = new() { Path = "/custom proton", ClientInstallPath = "/custom steam", CompatDataPath = "/custom compat" } };
        var command = GameLauncher.Create(config, "/unused", new FakeHost());
        Assert.Contains("/custom proton/proton", command.Arguments);
        Assert.Contains("STEAM_COMPAT_CLIENT_INSTALL_PATH=/custom steam", command.Arguments);
        Assert.Contains("STEAM_COMPAT_DATA_PATH=/custom compat", command.Arguments);
    }

    [Theory]
    [InlineData("missing", "Install Flatpak")]
    [InlineData("home", "Steam environment")]
    [InlineData("stopped", "Start Steam first")]
    [InlineData("proc", "/proc/100")]
    [InlineData("game", "Game folder")]
    [InlineData("proton", "proton.path")]
    [InlineData("compat", "proton.compatDataPath")]
    public void DiscoveryFailuresExplainRecovery(string failure, string expected)
    {
        Assert.Contains(expected, Assert.Throws<IOException>(() => GameLauncher.Create(Config, "/unused", new FakeHost { Failure = failure })).Message);
    }

    [Fact]
    public void FlatpakModeRoundTripsThroughConfig()
    {
        string root = Path.Combine(Path.GetTempPath(), "jamm-flatpak-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new ManagerStore(root);
            store.Set("wine.mode", "proton-flatpak");
            Assert.Equal("proton-flatpak", new ManagerStore(root).Config().Wine.Mode);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Theory]
    [InlineData("/path/to/compatdata/343780")]
    [InlineData("/path/to/compatdata/480")]
    [InlineData("/path/to/...")]
    [InlineData("path/to/...")]
    [InlineData("<data-dir>/proton/<app-id>")]
    [InlineData("<Flatpak Steam>/steamapps/compatdata/343780")]
    public void StockCompatPlaceholdersInExistingAndNewConfigsUseFlatpakDefault(string placeholder)
    {
        string root = Path.Combine(Path.GetTempPath(), "jamm-flatpak-ph-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new ManagerStore(root);
            string steam = "/home/test/.var/app/com.valvesoftware.Steam/.local/share/Steam";
            string expectedCompat = steam + "/steamapps/compatdata/343780";

            File.WriteAllText(Path.Combine(root, "config.json"), System.Text.Json.JsonSerializer.Serialize(new
            {
                gameDir = Config.GameDir,
                wine = new { mode = "proton-flatpak" },
                proton = new { compatDataPath = placeholder }
            }));
            var loaded = new ManagerStore(root).Config();
            Assert.Null(loaded.Proton.CompatDataPath);
            var fromExisting = GameLauncher.Create(loaded, root, new FakeHost());
            Assert.Equal(expectedCompat, fromExisting.FlatpakCompatDataPath);
            Assert.Contains("STEAM_COMPAT_DATA_PATH=" + expectedCompat, fromExisting.Arguments);

            store.Set("gameDir", Config.GameDir!);
            store.Set("wine.mode", "proton-flatpak");
            store.Set("proton.compatDataPath", placeholder);
            Assert.Null(store.Config().Proton.CompatDataPath);
            store.SetMany(new Dictionary<string, string> { ["proton.compatDataPath"] = placeholder });
            Assert.Null(new ManagerStore(root).Config().Proton.CompatDataPath);

            var direct = GameLauncher.Create(Config with { Proton = new() { CompatDataPath = placeholder } }, root, new FakeHost());
            Assert.Equal(expectedCompat, direct.FlatpakCompatDataPath);
            Assert.Contains("STEAM_COMPAT_DATA_PATH=" + expectedCompat, direct.Arguments);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Theory]
    [InlineData("/path/to/custom")]
    [InlineData("/tmp/<custom>/compat")]
    [InlineData("/tmp/.../compat")]
    public void ValidCompatOverridesIncludingNonPlaceholderSubpathsArePreserved(string customCompat)
    {
        string root = Path.Combine(Path.GetTempPath(), "jamm-flatpak-custom-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new ManagerStore(root);
            store.Set("gameDir", Config.GameDir!);
            store.Set("wine.mode", "proton-flatpak");
            store.Set("proton.compatDataPath", customCompat);
            Assert.Equal(customCompat, new ManagerStore(root).Config().Proton.CompatDataPath);

            var command = GameLauncher.Create(new ManagerStore(root).Config(), root, new FakeHost());
            Assert.Equal(customCompat, command.FlatpakCompatDataPath);
            Assert.Contains("STEAM_COMPAT_DATA_PATH=" + customCompat, command.Arguments);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    private sealed class FakeHost : FlatpakLaunchHost
    {
        public string? Failure { get; init; }
        public override bool DirectoryExists(string path) => path == "/";
        public override string Home => "/home/test";
        public override string ReadProc(string path) => Failure == "proc" ? throw new IOException("unreadable") : path switch
        {
            "/proc/100/task/100/children" => "101 102",
            "/proc/101/comm" => "other\n",
            "/proc/102/comm" => "bwrap\n",
            "/proc/102/task/102/children" => "103 104",
            "/proc/103/environ" when Failure == "home" => "DISPLAY=:1\0",
            "/proc/103/environ" => "HOME=/home/test\0DISPLAY=:1\0TOKEN=space;value=literal\0SteamAppId=wrong\0",
            _ => throw new IOException("Unexpected proc read: " + path)
        };
        public override (int ExitCode, string Output) Run(params string[] arguments)
        {
            if (Failure == "missing") throw new IOException("Cannot run flatpak. Install Flatpak and ensure it is on PATH.");
            if (arguments[0] == "ps") return (0, Failure == "stopped" ? "" : "PID APPLICATION\n200 com.valvesoftware.Steam\n1 unrelated\n100 com.valvesoftware.Steam\n");
            string path = arguments[^1];
            bool fail = Failure switch
            {
                "game" => path == Config.GameDir,
                "proton" => path.EndsWith("/proton", StringComparison.Ordinal),
                "compat" => path == "/",
                _ => false
            };
            return (fail ? 1 : 0, "");
        }
    }
}
