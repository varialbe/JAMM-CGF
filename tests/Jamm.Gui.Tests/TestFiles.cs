using Jamm.Core.Management;
using UndertaleModLib;
using UndertaleModLib.Compiler;
using UndertaleModLib.Models;

namespace Jamm.Gui.Tests;

internal sealed class TestFiles : IDisposable
{
    internal string Root { get; } = Path.Combine(Path.GetTempPath(), "cgfmm-gui-" + Guid.NewGuid().ToString("N"));
    internal ManagerStore Store { get; }
    internal string Original => Path.Combine(Root, "original.win");
    internal TestFiles(bool configure = true, Action<UndertaleData>? customize = null)
    {
        Directory.CreateDirectory(Root);
        Store = new(Path.Combine(Root, "manager"));
        if (configure)
        {
            Write(Original, "x = 0;\n", customize);
            string game = Path.Combine(Root, "game"); Directory.CreateDirectory(game); File.Copy(Original, Path.Combine(game, "data.win"));
            Store.Set("base", Original); Store.Set("gameDir", game);
        }
    }
    internal string Mod(string name, string code, Action<UndertaleData>? customize = null)
    {
        string path = Path.Combine(Root, name + ".win"); Write(path, code, customize); return path;
    }
    private static void Write(string path, string text, Action<UndertaleData>? customize)
    {
        using var data = UndertaleData.CreateNew();
        data.GeneralInfo.Timestamp = 1692207312;
        data.GeneralInfo.Major = 1; data.GeneralInfo.Minor = 0; data.GeneralInfo.Release = 0; data.GeneralInfo.Build = 1773;
        var code = UndertaleCode.CreateEmptyEntry(data, "example_code");
        var compiler = new CompileGroup(data); compiler.QueueCodeReplace(code, text);
        Assert.True(compiler.Compile().Successful);
        customize?.Invoke(data);
        using var stream = File.Create(path); UndertaleIO.Write(stream, data);
    }
    public void Dispose() => Directory.Delete(Root, true);
}
