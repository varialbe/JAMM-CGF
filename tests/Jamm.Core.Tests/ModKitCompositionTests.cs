using ImageMagick;
using Jamm.Core.ModKit;
using UndertaleModLib;

namespace Jamm.Core.Tests;

/// <summary>Verifies that composed sprites read base pixels, respect layer order and fail clearly on missing inputs.</summary>
public sealed class ModKitCompositionTests
{
    [Fact]
    public void ComposesMultipleFramesWithStableOrderAndTransparentSvg()
    {
        using var temp = new ModKitAreaTests.AreaTestDirectory();
        using var data = UndertaleData.CreateNew();
        AddDonor(temp, data);
        string package = temp.Package("""
            {"id":"kit","name":"Composition Test","sprites":[
              {"key":"fish","width":8,"height":6,"origin":[2,3],"compose":[
                [{"from":"spr_donor","crop":{"x":0,"y":0,"w":1,"h":2},"x":2,"y":1,"scaleX":2,"paletteMap":{"#ff0000":"#2255aa"},"z":0},
                 {"file":"fin.svg","x":2,"y":1,"z":1},
                 {"from":"spr_donor","x":2,"y":1,"opacity":0,"z":1}],
                [{"from":"spr_donor","crop":{"x":0,"y":0,"w":1,"h":1},"x":1,"y":3,"tint":"#4400ff"},
                 {"from":"spr_donor","x":2,"y":2,"opacity":0.5,"tones":{"colors":["#263348","#8fb2de"],"cutoffs":[51]} }]
              ]}
            ]}
            """);
        File.WriteAllText(Path.Combine(package, "fin.svg"), """
            <svg xmlns="http://www.w3.org/2000/svg" width="2" height="2"><rect x="1" width="1" height="1" fill="#00ff00"/></svg>
            """);
        var recipe = Assert.Single(ModKitPackage.Load(package).Sprites);
        using var compositor = new ModKitComposition(data);
        using var first = compositor.Render(recipe, 0);
        using var repeat = compositor.Render(recipe, 0);
        using var second = compositor.Render(recipe, 1);
        Assert.Equal(first.ToByteArray(MagickFormat.Rgba), repeat.ToByteArray(MagickFormat.Rgba));
        Assert.Equal(new byte[] { 34, 85, 170, 255 }, Color(first, 2, 1));
        Assert.Equal(new byte[] { 0, 255, 0, 255 }, Color(first, 3, 1));
        Assert.Equal(0, Color(first, 0, 0)[3]);
        Assert.Equal(new byte[] { 68, 0, 0, 255 }, Color(second, 1, 3));
        Assert.Equal(new byte[] { 143, 178, 222, 128 }, Color(second, 2, 2));
        ModKitBuilder.Apply(data, ModKitPackage.Load(package));
        var sprite = data.Sprites.ByName("jmk_kit_recipe_fish")!;
        Assert.Equal(2, sprite.Textures.Count);
        Assert.Equal(2, sprite.OriginX);
        Assert.Equal(3, sprite.OriginY);

        static byte[] Color(MagickImage image, int x, int y)
        {
            using var pixels = image.GetPixels();
            return pixels.GetArea(x, y, 1, 1)!;
        }
    }

    [Theory]
    [InlineData("[90, 45]")]
    [InlineData("[90, \"45\"]")]
    [InlineData("[90]")]
    public void ToneCutoffsMustBeAscendingAndMatchThePalette(string values)
    {
        using var temp = new ModKitAreaTests.AreaTestDirectory();
        string package = temp.Package($$$"""
            {"id":"kit","name":"Palette Test","sprites":[{"key":"fish","width":8,"height":8,
            "compose":[{"from":"spr_donor","tones":{"colors":["#112233","#445566","#778899"],"cutoffs":{{{values}}}}}]}]}
            """);
        Assert.Contains("tones", Assert.Throws<InvalidDataException>(() => ModKitPackage.Load(package)).Message);
    }

    [Theory]
    [InlineData("spr_missing", 0)]
    [InlineData("spr_donor", 2)]
    public void MissingBaseAssetOrFrameNamesTheRecipeAndSource(string source, int frame)
    {
        using var temp = new ModKitAreaTests.AreaTestDirectory();
        using var data = UndertaleData.CreateNew();
        AddDonor(temp, data);
        string package = temp.Package($$$"""
            {"id":"kit","name":"Composition Test","sprites":[
              {"key":"fish","width":4,"height":4,"compose":[{"from":"{{{source}}}","frame":{{{frame}}}}]}]}
            """);
        var error = Assert.Throws<InvalidDataException>(() => ModKitBuilder.Apply(data, ModKitPackage.Load(package)));
        Assert.Contains("Sprite recipe 'fish', output frame 0, layer 0", error.Message);
        Assert.Contains($"'{source}' has no usable frame {frame}", error.Message);
    }

    private static void AddDonor(ModKitAreaTests.AreaTestDirectory temp, UndertaleData data)
    {
        string png = temp.PathFor("donor.png");
        using (var image = new MagickImage(MagickColors.Red, 3, 2))
            image.Write(png);
        ModKitSpriteImporter.ImportAll(data, [new("spr_donor", png, 1, null, true, true)]);
    }
}
