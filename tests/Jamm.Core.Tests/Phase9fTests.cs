using Jamm.Core.Browsing;
using Jamm.Core.Building;
using Jamm.Core.Management;
using Jamm.Core.Patching;
using UndertaleModLib.Models;
using UndertaleModLib;

namespace Jamm.Core.Tests;

public sealed partial class BuildingTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NullTexturePageWithPixelsIsPreservedOnlyWhenUnused(bool referenced)
    {
        using var temp = new TemporaryDirectory();
        string basePath = temp.PathFor("base.win"), modPath = temp.PathFor("mod.win"), output = temp.PathFor("output.win");
        WriteModel(basePath, _ => { });
        WriteModel(modPath, d =>
        {
            var item = new UndertaleTexturePageItem { SourceWidth = 100, SourceHeight = 100,
                TargetWidth = 100, TargetHeight = 100, BoundingWidth = 100, BoundingHeight = 100 };
            d.TexturePageItems.Add(item);
            if (referenced) d.Sprites.Add(new() { Name = d.Strings.MakeString("test_sprite"),
                Textures = [new() { Texture = item }] });
        });
        string originalHash = BuildFiles.Hash(basePath), modHash = BuildFiles.Hash(modPath);
        using var original = GameFileLoader.Load(basePath);
        using var mod = GameFileLoader.Load(modPath);
        var patch = PatchBuilder.Create(original, mod);
        if (referenced)
        {
            var error = Assert.Throws<InvalidDataException>(() => ArchiveBuilder.Build(basePath, [new(patch, modPath)], output));
            Assert.Contains("is referenced but has pixels and no embedded page", error.Message);
            Assert.False(File.Exists(output));
            var store = new ManagerStore(temp.PathFor("manager"));
            store.SetMany(new Dictionary<string, string> { ["base"] = basePath });
            var facts = new ModFactAnalyzer(store).Analyze(modPath, default);
            Assert.Equal("Tool limitation", facts.Compatibility);
            Assert.Contains(error.Message, facts.Reason);
        }
        else
        {
            var result = ArchiveBuilder.Build(basePath, [new(patch, modPath)], output);
            Assert.Contains(result.Verification!.Warnings, w => w.Contains("Unused texture item"));
            using var merged = GameFileLoader.Load(output);
            var item = Assert.Single(merged.Data.TexturePageItems);
            Assert.Null(item.TexturePage);
            Assert.Equal(100, item.SourceWidth); Assert.Equal(100, item.SourceHeight);
            Assert.Equal(100, item.TargetWidth); Assert.Equal(100, item.BoundingWidth);
        }
        Assert.Equal(originalHash, BuildFiles.Hash(basePath)); Assert.Equal(modHash, BuildFiles.Hash(modPath));
    }
}
