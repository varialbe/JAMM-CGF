using Jamm.Core.Building;
using Jamm.Core.Management;

namespace Jamm.Core.Tests;

/// <summary>Opt-in management workflow against a temporary copy of the complete legacy game directory.</summary>
public sealed class RealManagementTests
{
    [RealFilesFact]
    public void ManagedProfileBuildInstallSwitchDetectionAndRestorePreserveSources()
    {
        string original = Environment.GetEnvironmentVariable("JAMM_ORIGINAL")!;
        string source = Path.Combine(Environment.GetEnvironmentVariable("JAMM_MODS_DIR")!, "the-abyss-beta.win");
        string[] hashes = new[] { original, source }.Select(BuildFiles.Hash).ToArray();
        string directory = Path.Combine(Path.GetTempPath(), "cgfmm-real-management-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            string game = Path.Combine(directory, "game");
            foreach (string file in Directory.EnumerateFiles(Path.GetDirectoryName(original)!, "*", SearchOption.AllDirectories))
            {
                string destination = Path.Combine(game, Path.GetRelativePath(Path.GetDirectoryName(original)!, file));
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!); File.Copy(file, destination);
            }
            var store = new ManagerStore(Path.Combine(directory, "manager"));
            store.Set("base", original); store.Set("gameDir", game);
            var mod = new ModLibrary(store).Add(source).Mod;
            var profiles = new ProfileLibrary(store);
            profiles.Create("abyss", [mod.Id]); profiles.Use("abyss");
            var installer = new ProfileInstaller(store);
            Assert.False(installer.IsInstalled());
            var prepared = installer.Prepare();
            Assert.Equal(hashes[0], BuildFiles.Hash(Path.Combine(game, "data.win")));
            string buildHash = BuildFiles.Hash(prepared.OutputPath);
            installer.Install(prepared);
            Assert.True(installer.IsInstalled());
            Assert.Equal(buildHash, BuildFiles.Hash(Path.Combine(game, "data.win")));
            Assert.Empty(Directory.GetFiles(Path.Combine(store.Root, "builds")));
            Assert.Equal(hashes[0], BuildFiles.Hash(Path.Combine(game, "data.win.cgfmm-original")));
            Assert.Equal(hashes[0], BuildFiles.Hash(Path.Combine(store.Root, "backups", hashes[0] + ".win")));
            profiles.Create("same-mod-new-profile", [mod.Id]);
            Assert.False(installer.IsInstalled("same-mod-new-profile"));
            File.AppendAllText(Path.Combine(game, "data.win"), "tampered");
            Assert.False(installer.IsInstalled());
            Assert.Throws<IOException>(() => installer.Prepare());
            Assert.Equal(hashes[0], installer.Restore());
            Assert.False(installer.IsInstalled());
            Assert.Equal(hashes[0], BuildFiles.Hash(Path.Combine(game, "data.win")));
            Assert.True(File.Exists(Path.Combine(store.Root, "backups", hashes[0] + ".win")));
        }
        finally
        {
            Directory.Delete(directory, true);
            Assert.Equal(hashes, new[] { original, source }.Select(BuildFiles.Hash).ToArray());
        }
    }
}
