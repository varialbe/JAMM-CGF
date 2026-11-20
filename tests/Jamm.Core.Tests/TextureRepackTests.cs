using System.Security.Cryptography;
using Jamm.Core.Building;
using Jamm.Core.Patching;
using UndertaleModLib;
using UndertaleModLib.Models;
using UndertaleModLib.Util;

namespace Jamm.Core.Tests;

public sealed partial class BuildingTests
{
    [Fact]
    public void MultiModRepackDeduplicatesImagesAndPreservesRenderedPixelsAndMasks()
    {
        using var temp = new TemporaryDirectory();
        string basePath = temp.PathFor("base.win"), aPath = temp.PathFor("a.win"), bPath = temp.PathFor("b.win");
        WriteModel(basePath, data => AddFixture(data, "old"));
        WriteModel(aPath, data => AddFixture(data, "a"));
        WriteModel(bPath, data => AddFixture(data, "b"));
        using var original = GameFileLoader.Load(basePath);
        using var a = GameFileLoader.Load(aPath);
        using var b = GameFileLoader.Load(bPath);
        BuildInput[] inputs = [new(PatchBuilder.Create(original, a, "A"), aPath), new(PatchBuilder.Create(original, b, "B"), bPath)];
        string unpacked = temp.PathFor("unpacked.win"), repacked = temp.PathFor("repacked.win"), second = temp.PathFor("second.win");
        var off = ArchiveBuilder.Build(basePath, inputs, unpacked, noRepack: true);
        var on = ArchiveBuilder.Build(basePath, inputs, repacked);
        var repeat = ArchiveBuilder.Build(basePath, inputs, second);
        Assert.DoesNotContain(off.Plan.Decisions, Decision);
        Assert.Contains("2 duplicates merged, 1 unused regions dropped", Assert.Single(on.Plan.Decisions, Decision));
        Assert.Equal(Assert.Single(on.Plan.Decisions, Decision), Assert.Single(repeat.Plan.Decisions, Decision));
        Assert.Equal(BuildFiles.Hash(repacked), BuildFiles.Hash(second));

        using var before = GameFileLoader.Load(unpacked);
        using var after = GameFileLoader.Load(repacked);
        Assert.Equal(6, before.Data.TexturePageItems.Count);
        Assert.Equal(5, after.Data.TexturePageItems.Count);
        Assert.Single(after.Data.EmbeddedTextures);
        var first = after.Data.Sprites.ByName("a").Textures[0].Texture;
        var other = after.Data.Sprites.ByName("b").Textures[0].Texture;
        var vanilla = after.Data.Sprites.ByName("vanilla").Textures[0].Texture;
        Assert.NotSame(first, other);
        Assert.Equal((vanilla.TexturePage, vanilla.SourceX, vanilla.SourceY), (first.TexturePage, first.SourceX, first.SourceY));
        Assert.Equal((first.TexturePage, first.SourceX, first.SourceY), (other.TexturePage, other.SourceX, other.SourceY));
        Assert.NotEqual(first.TargetX, other.TargetX);
        CompareTextureBuilds(before.Data, after.Data, compareBytes: true);

        string singlePath = temp.PathFor("single.win"), singleBaseline = temp.PathFor("single-baseline.win");
        var single = ArchiveBuilder.Build(basePath, [inputs[0]], singlePath);
        var singleOff = ArchiveBuilder.Build(basePath, [inputs[0]], singleBaseline, noRepack: true);
        Assert.Contains("1 duplicates merged", Assert.Single(single.Plan.Decisions, Decision));
        Assert.Equal(1, single.Counts[AssetKind.TexturePages]);
        Assert.DoesNotContain(singleOff.Plan.Decisions, Decision);
        Assert.Equal(a.Data.EmbeddedTextures.Count, singleOff.Counts[AssetKind.TexturePages]);
        using var singleBefore = GameFileLoader.Load(singleBaseline);
        using var singleAfter = GameFileLoader.Load(singlePath);
        CompareTextureBuilds(singleBefore.Data, singleAfter.Data, compareBytes: true);

        static bool Decision(string line) => line.StartsWith("[CGF-TEX-001]", StringComparison.Ordinal);
    }

    [Fact]
    public void VanillaBuildCopiesTheBaseByteForByte()
    {
        using var temp = new TemporaryDirectory();
        string basePath = temp.PathFor("base.win"), output = temp.PathFor("copy.win");
        WriteModel(basePath, data => AddFixture(data, "old"));
        string before = BuildFiles.Hash(basePath);
        var copy = ArchiveBuilder.Build(basePath, [], output);
        Assert.Empty(copy.Plan.Decisions);
        Assert.Equal(2, copy.Counts[AssetKind.TexturePages]);
        Assert.Equal(before, copy.Verification!.Inspection.Sha256);
        Assert.Equal(before, BuildFiles.Hash(output));
        Assert.Equal(before, BuildFiles.Hash(basePath));

        string cliCopy = temp.PathFor("cli-copy.win");
        Assert.Equal(0, Jamm.Cli.BuildCommand.Run("build", ["--base", basePath, "--out", cliCopy, "--dry-run"]));
        Assert.False(File.Exists(cliCopy));
        Assert.Equal(0, Jamm.Cli.BuildCommand.Run("build", ["--base", basePath, "--out", cliCopy]));
        Assert.Equal(before, BuildFiles.Hash(cliCopy));
    }

    private static void AddFixture(UndertaleData data, string variant)
    {
        AddSprite(data, "vanilla", AddRegion(data, 31, 1));
        var replacement = AddRegion(data, variant == "a" ? (byte)93 : (byte)71, 2);
        AddSprite(data, "replaced", replacement);
        if (variant == "old") return;
        var item = AddRegion(data, 31, (ushort)(variant == "a" ? 2 : 3));
        var sprite = AddSprite(data, variant, item);
        sprite.Textures.Add(new() { Texture = replacement });
        data.Fonts.Add(new() { Name = data.Strings.MakeString("font_" + variant), DisplayName = data.Strings.MakeString("font"), Texture = item });
        data.Backgrounds.Add(new() { Name = data.Strings.MakeString("background_" + variant), Texture = replacement });

        static UndertaleSprite AddSprite(UndertaleData data, string name, UndertaleTexturePageItem item)
        {
            var sprite = new UndertaleSprite
            {
                Name = data.Strings.MakeString(name), Width = 8, Height = 4, MarginRight = 7, MarginBottom = 3,
                Textures = [new() { Texture = item }], CollisionMasks = [new([0x12, 0x34, 0x56, 0x78], 8, 4)]
            };
            data.Sprites.Add(sprite);
            return sprite;
        }

        static UndertaleTexturePageItem AddRegion(UndertaleData data, byte colour, ushort targetX)
        {
            var image = new GMImage(8, 8);
            var pixels = image.GetRawImageData();
            for (int y = 0; y < 4; y++)
                for (int x = 0; x < 4; x++)
                {
                    int offset = ((y + 2) * image.Width + x + 1) * 4;
                    pixels[offset] = colour;
                    pixels[offset + 1] = (byte)(x + y);
                    pixels[offset + 3] = x == 0 ? (byte)0 : (byte)255;
                }
            var page = new UndertaleEmbeddedTexture { TextureData = new() { Image = image.ConvertToPng() } };
            var item = new UndertaleTexturePageItem
            {
                TexturePage = page, SourceX = 1, SourceY = 2, SourceWidth = 4, SourceHeight = 4,
                TargetX = targetX, TargetWidth = 4, TargetHeight = 4, BoundingWidth = 8, BoundingHeight = 4
            };
            data.EmbeddedTextures.Add(page);
            data.TexturePageItems.Add(item);
            return item;
        }
    }

    [Fact]
    public void RepackGivesOversizedRegionsDedicatedPagesAndKeepsMalformedSampling()
    {
        using var data = UndertaleData.CreateNew();
        data.GeneralInfo.Major = 1;
        var large = Track(new GMImage(2050, 8), 1, 2049);
        var malformed = Track(new GMImage(8, 8), 2, 8);
        var packedImage = new GMImage(8, 8);
        byte[] edge = [11, 22, 33, 255];
        edge.CopyTo(packedImage.GetRawImageData());
        new byte[] { 55, 66, 77, 255 }.CopyTo(packedImage.GetRawImageData()[4..]);
        var packed = Track(packedImage, 0, 1);
        var placeholder = new UndertaleTexturePageItem();
        data.TexturePageItems.Add(placeholder);
        data.Fonts.Add(new() { Texture = placeholder });
        var oldMalformedPage = malformed.TexturePage;

        string decision = TextureRepacker.Repack(data, null, default);
        Assert.Equal(3, data.EmbeddedTextures.Count);
        Assert.Equal((2049, 6), (large.TexturePage.TextureData.Width, large.TexturePage.TextureData.Height));
        Assert.NotSame(large.TexturePage, packed.TexturePage);
        Assert.Same(oldMalformedPage, malformed.TexturePage);
        Assert.Equal((2, 8), (malformed.SourceX, malformed.SourceWidth));
        Assert.Null(placeholder.TexturePage);
        Assert.Contains("Preserved 1 source pages", decision);
        var packedPixels = packed.TexturePage.TextureData.Image.ConvertToRawBgra();
        int start = (packed.SourceY * packedPixels.Width + packed.SourceX) * 4;
        Assert.Equal(edge, packedPixels.GetRawImageData().Slice(start + 4, 4).ToArray());
        Assert.Equal(new byte[4], packedPixels.GetRawImageData().Slice(start + 8, 4).ToArray());

        UndertaleTexturePageItem Track(GMImage image, ushort x, ushort width)
        {
            var texture = new UndertaleEmbeddedTexture { TextureData = new() { Image = image } };
            var item = new UndertaleTexturePageItem { TexturePage = texture, SourceX = x, SourceWidth = width, SourceHeight = 2 };
            data.EmbeddedTextures.Add(texture);
            data.TexturePageItems.Add(item);
            data.Backgrounds.Add(new() { Texture = item });
            return item;
        }
    }

    [Fact]
    public void TightlyPackedAtlasIsKeptWhenARepackWouldGrow()
    {
        // A full 16x16 page would need 20x20 with gutters, i.e. a 32x32 page: keep the source.
        using var data = UndertaleData.CreateNew();
        data.GeneralInfo.Major = 1;
        var page = new UndertaleEmbeddedTexture { TextureData = new() { Image = new GMImage(16, 16) } };
        var item = new UndertaleTexturePageItem { TexturePage = page, SourceWidth = 16, SourceHeight = 16 };
        data.EmbeddedTextures.Add(page);
        data.TexturePageItems.Add(item);
        data.Backgrounds.Add(new() { Texture = item });

        string decision = TextureRepacker.Repack(data, null, default);

        Assert.StartsWith("[CGF-TEX-001] Texture repack kept the source atlas", decision);
        Assert.Same(page, Assert.Single(data.EmbeddedTextures));
        Assert.Same(page, item.TexturePage);
        Assert.Equal((0, 0), (item.SourceX, item.SourceY));
    }

    [Fact]
    public void GroupedAtlasIsLeftIntact()
    {
        using var data = UndertaleData.CreateNew();
        data.GeneralInfo.Major = 1;
        var groups = new UndertaleChunkTGIN();
        data.FORM.Chunks["TGIN"] = groups;
        data.FORM.ChunksTypeDict[groups.GetType()] = groups;
        var page = new UndertaleEmbeddedTexture();
        data.EmbeddedTextures.Add(page);
        Assert.Contains("skipped", TextureRepacker.Repack(data, null, default));
        Assert.Same(page, Assert.Single(data.EmbeddedTextures));
    }

    [TextureBuildPairFact]
    public void RealTextureBuildPairKeepsEveryReferencedItem()
    {
        using var before = GameFileLoader.Load(Environment.GetEnvironmentVariable("JAMM_UNPACKED_BUILD")!);
        using var after = GameFileLoader.Load(Environment.GetEnvironmentVariable("JAMM_REPACKED_BUILD")!);
        CompareTextureBuilds(before.Data, after.Data, compareBytes: false);
    }

    private static void CompareTextureBuilds(UndertaleData before, UndertaleData after, bool compareBytes)
    {
        var owners = TextureOwners(before).ToArray();
        var live = owners.Where(item => item is not null).ToHashSet();
        var oldItems = before.TexturePageItems.Where(live.Contains).ToArray();
        var newItems = after.TexturePageItems.ToArray();
        Assert.Equal(oldItems.Length, newItems.Length);
        var mapped = oldItems.Zip(newItems).ToDictionary(pair => pair.First, pair => pair.Second);
        Assert.Equal(owners.Select(item => item is null ? null : mapped[item]), TextureOwners(after));
        Assert.Equal(before.Sprites.Select(s => s.Name.Content), after.Sprites.Select(s => s.Name.Content));
        Assert.Equal(before.Fonts.Select(f => f.Name.Content), after.Fonts.Select(f => f.Name.Content));
        Assert.Equal(before.Backgrounds.Select(b => b.Name.Content), after.Backgrounds.Select(b => b.Name.Content));
        foreach (var (oldSprite, newSprite) in before.Sprites.Zip(after.Sprites))
        {
            Assert.Equal(oldSprite.Textures.Count, newSprite.Textures.Count);
            Assert.Equal(oldSprite.CollisionMasks.Select(m => m.Data), newSprite.CollisionMasks.Select(m => m.Data));
        }

        var oldPixels = PixelSignatures(oldItems, compareBytes);
        var newPixels = PixelSignatures(newItems, compareBytes);
        foreach (var (oldItem, newItem) in oldItems.Zip(newItems))
        {
            Assert.Equal(Geometry(oldItem), Geometry(newItem));
            Assert.Equal(oldItem.TexturePage?.Scaled, newItem.TexturePage?.Scaled);
            Assert.True(oldPixels[oldItem].AsSpan().SequenceEqual(newPixels[newItem]), $"Pixels differ for {oldItem.Name.Content}.");
        }

        static int[] Geometry(UndertaleTexturePageItem item) =>
            [item.SourceWidth, item.SourceHeight, item.TargetX, item.TargetY, item.TargetWidth, item.TargetHeight, item.BoundingWidth, item.BoundingHeight];

        static IEnumerable<UndertaleTexturePageItem?> TextureOwners(UndertaleData data) =>
            data.Sprites.SelectMany(sprite => sprite.Textures.Select(entry => entry?.Texture))
                .Concat(data.Backgrounds.Select(background => background.Texture))
                .Concat(data.Fonts.Select(font => font.Texture))
                .Concat((data.EmbeddedImages ?? []).Select(image => image.TextureEntry));
    }

    private static Dictionary<UndertaleTexturePageItem, byte[]> PixelSignatures(UndertaleTexturePageItem[] items, bool compareBytes)
    {
        Dictionary<UndertaleTexturePageItem, byte[]> result = [];
        foreach (var group in items.GroupBy(t => t.TexturePage))
        {
            if (group.Key is not { } page)
            {
                foreach (var item in group) result.Add(item, []);
                continue;
            }
            var image = page.TextureData.Image;
            var pixels = ReadTexture(image);
            var bytes = pixels.GetRawImageData();
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            foreach (var item in group)
            {
                int width = item.SourceWidth, height = item.SourceHeight, x = item.SourceX, y = item.SourceY;
                if (x + width > image.Width || y + height > image.Height)
                {
                    // An out-of-range rectangle has no standalone image. Check the
                    // entire source and its coordinates to preserve runner sampling.
                    foreach (int value in new[] { image.Width, image.Height, x, y })
                        hash.AppendData(BitConverter.GetBytes(value));
                    hash.AppendData(bytes);
                }
                else
                {
                    byte[]? exact = compareBytes ? new byte[width * height * 4] : null;
                    for (int row = 0; row < height; row++)
                    {
                        var source = bytes.Slice(((y + row) * image.Width + x) * 4, width * 4);
                        if (exact is not null) source.CopyTo(exact.AsSpan(row * width * 4));
                        else hash.AppendData(source);
                    }
                    if (exact is not null)
                    {
                        result.Add(item, exact);
                        continue;
                    }
                }
                result.Add(item, hash.GetHashAndReset());
            }
        }
        return result;

        static GMImage ReadTexture(GMImage image)
        {
            using var decoded = image.GetMagickImage();
            return GMImage.FromMagickImage(decoded);
        }
    }

    private sealed class TextureBuildPairFactAttribute : FactAttribute
    {
        public TextureBuildPairFactAttribute()
        {
            if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("JAMM_UNPACKED_BUILD")) ||
                string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("JAMM_REPACKED_BUILD")))
                Skip = "Set JAMM_UNPACKED_BUILD and JAMM_REPACKED_BUILD to compare an opt-in texture build pair.";
        }
    }
}
