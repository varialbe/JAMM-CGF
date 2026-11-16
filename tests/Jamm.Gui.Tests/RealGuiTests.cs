using Jamm.Core.Building;
using Jamm.Core.Management;
using Jamm.Gui.ViewModels;

namespace Jamm.Gui.Tests;

public sealed class RealGuiTests
{
    private sealed class RealGuiFactAttribute : FactAttribute
    {
        public RealGuiFactAttribute()
        {
            if (!File.Exists(Environment.GetEnvironmentVariable("JAMM_ORIGINAL")) || !Directory.Exists(Environment.GetEnvironmentVariable("JAMM_MODS_DIR"))) Skip = "Set JAMM_ORIGINAL and JAMM_MODS_DIR for real GUI installation verification.";
        }
    }

    [RealGuiFact]
    public async Task ViewModelInstallsIntoCompleteLegacyCopyAndRestoresOriginalHash()
    {
        using var files = new TestFiles(false);
        string original = Environment.GetEnvironmentVariable("JAMM_ORIGINAL")!;
        string mod = Path.Combine(Environment.GetEnvironmentVariable("JAMM_MODS_DIR")!, "the-abyss-beta.win");
        string originalHash = BuildFiles.Hash(original), modHash = BuildFiles.Hash(mod);
        string game = Path.Combine(files.Root, "legacy-copy");
        foreach (string source in Directory.EnumerateFiles(Path.GetDirectoryName(original)!, "*", SearchOption.AllDirectories))
        {
            string destination = Path.Combine(game, Path.GetRelativePath(Path.GetDirectoryName(original)!, source));
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!); File.Copy(source, destination);
        }
        files.Store.Set("base", original); files.Store.Set("gameDir", game);
        Assert.Equal("Legacy bytecode build ✓", ManagerOperations.ValidateBase(original));
        var vm = new MainViewModel(files.Store); await vm.InitializeAsync(); await vm.AddAsync([mod]);
        Assert.Single(vm.Mods);
        var profiles = new ProfileLibrary(files.Store); profiles.Create("real-gui", [vm.Mods[0].Id]); profiles.Use("real-gui");
        await vm.InitializeAsync(); await vm.CheckAsync();
        Assert.True(vm.CanInstall);
        await vm.InstallAsync(message => { Assert.Contains(Path.Combine(game, "data.win"), message); return Task.FromResult(true); });
        Assert.Contains("Installed and verified", vm.Log); Assert.True(vm.CanRestore);
        Assert.True(new ProfileInstaller(files.Store).IsInstalled());
        await vm.RestoreAsync();
        Assert.Equal(originalHash, BuildFiles.Hash(Path.Combine(game, "data.win")));
        Assert.Equal(originalHash, BuildFiles.Hash(Path.Combine(game, "data.win.cgfmm-original")));
        Assert.Equal(originalHash, BuildFiles.Hash(original)); Assert.Equal(modHash, BuildFiles.Hash(mod));
        Assert.Contains(originalHash, vm.Status);
    }
}
