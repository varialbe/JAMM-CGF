using ImageMagick;
using UndertaleModLib;
using UndertaleModLib.Models;
using UndertaleModLib.Util;

namespace Jamm.Core.ModKit;

/// <summary>Specification for one sprite to import from a PNG file (or horizontal sprite strip).</summary>
internal sealed record SpriteImportSpec(
    string Name,
    string PngPath,
    int Frames,
    int[]? Origin,
    bool CenterOrigin,
    bool GenerateMask,
    ModKitSprite? Composition = null,
    bool WrapHorizontal = false);

/// <summary>Packs ModKit PNG assets onto new GameMaker texture pages and creates <see cref="UndertaleSprite"/> entries.</summary>
internal static class ModKitSpriteImporter
{
    internal static Dictionary<string, UndertaleSprite> ImportAll(UndertaleData data, IReadOnlyList<SpriteImportSpec> specs)
    {
        var result = new Dictionary<string, UndertaleSprite>(StringComparer.Ordinal);
        if (specs.Count == 0)
            return result;

        var packer = new TextureGroupPacker(maxTextureSize: 2048, border: 2, allowCrop: false, format: GMImage.ImageFormat.Png);
        var pending = new List<(SpriteImportSpec Spec, int Width, int Height, int WrapPadding, List<UndertaleTexturePageItem> Items, byte[]? MaskBytes, int MinX, int MinY, int MaxX, int MaxY)>();

        using var composer = new ModKitComposition(data);
        foreach (var spec in specs)
        {
            if (data.Sprites.ByName(spec.Name) is not null)
                throw new InvalidDataException($"Sprite '{spec.Name}' already exists in the base archive. Choose a different mod id or item key.");

            using var source = spec.Composition is null ? new MagickImage(spec.PngPath) : null;

            int totalWidth = source is null ? spec.Composition!.Width * spec.Frames : (int)source.Width;
            int height = source is null ? spec.Composition!.Height : (int)source.Height;
            if (spec.Frames < 1 || totalWidth % spec.Frames != 0)
                throw new InvalidDataException($"Sprite '{spec.PngPath}' ({totalWidth}x{height}) is not evenly divisible by frames={spec.Frames}.");

            int frameWidth = totalWidth / spec.Frames;
            int wrapPadding = spec.WrapHorizontal
                ? Math.Clamp((packer.MaxTextureSize - 2 * packer.Border - frameWidth) / 2, 0, Math.Min(2, frameWidth))
                : 0;
            var items = new List<UndertaleTexturePageItem>(spec.Frames);
            byte[]? maskBytes = null;
            int minX = frameWidth, minY = height, maxX = -1, maxY = -1;

            for (int f = 0; f < spec.Frames; f++)
            {
                var tile = source is null ? composer.Render(spec.Composition!, f)
                    : spec.Frames == 1 ? new MagickImage(source) : CropFrame(source, f * frameWidth, frameWidth, height);
                tile.ColorType = ColorType.TrueColorAlpha;
                tile.SetBitDepth(8);
                tile.Alpha(AlphaOption.Set);

                if (f == 0)
                    maskBytes = ComputeBoundsAndMask(tile, frameWidth, height, spec.GenerateMask, out minX, out minY, out maxX, out maxY);

                if (wrapPadding > 0)
                {
                    using var unpadded = tile;
                    tile = PadHorizontalWrap(unpadded, frameWidth, height, wrapPadding);
                }

                // TextureGroupPacker takes ownership of tile and disposes it during PackPages().
                items.Add(packer.AddImage(tile, TextureGroupPacker.BorderFlags.None, allowCrop: false));
            }

            pending.Add((spec, frameWidth, height, wrapPadding, items, maskBytes, minX, minY, maxX, maxY));
        }

        packer.PackPages();
        packer.ImportToData(data);

        foreach (var entry in pending)
        {
            int ox = entry.Spec.Origin is not null ? entry.Spec.Origin[0] : (entry.Spec.CenterOrigin ? entry.Width / 2 : 0);
            int oy = entry.Spec.Origin is not null ? entry.Spec.Origin[1] : (entry.Spec.CenterOrigin ? entry.Height / 2 : 0);

            bool hasOpaque = entry.MaxX >= entry.MinX && entry.MaxY >= entry.MinY;
            var sprite = new UndertaleSprite
            {
                Name = data.Strings.MakeString(entry.Spec.Name),
                Width = (uint)entry.Width,
                Height = (uint)entry.Height,
                OriginX = ox,
                OriginY = oy,
                MarginLeft = hasOpaque ? entry.MinX : 0,
                MarginTop = hasOpaque ? entry.MinY : 0,
                MarginRight = hasOpaque ? entry.MaxX : Math.Max(0, entry.Width - 1),
                MarginBottom = hasOpaque ? entry.MaxY : Math.Max(0, entry.Height - 1),
                Transparent = true,
                Smooth = false,
                Preload = true,
                BBoxMode = 2,
                SepMasks = entry.Spec.Composition is not null ? UndertaleSprite.SepMaskType.Precise : UndertaleSprite.SepMaskType.AxisAlignedRect
            };

            foreach (var item in entry.Items)
            {
                if (entry.WrapPadding > 0)
                {
                    // The adjacent columns stay on the atlas for filtering across the cut.
                    item.SourceX += (ushort)entry.WrapPadding;
                    item.SourceWidth = item.TargetWidth = item.BoundingWidth = (ushort)entry.Width;
                }
                sprite.Textures.Add(new UndertaleSprite.TextureEntry { Texture = item });
            }

            if (entry.MaskBytes is not null)
            {
                var mask = sprite.NewMaskEntry(data);
                Buffer.BlockCopy(entry.MaskBytes, 0, mask.Data, 0, Math.Min(entry.MaskBytes.Length, mask.Data.Length));
                sprite.CollisionMasks.Add(mask);
            }

            data.Sprites.Add(sprite);
            result[entry.Spec.Name] = sprite;
        }

        return result;
    }

    private static MagickImage CropFrame(MagickImage source, int offsetX, int width, int height)
    {
        var tile = new MagickImage(source);
        tile.Crop(new MagickGeometry(offsetX, 0, (uint)width, (uint)height));
        tile.ResetPage();
        return tile;
    }

    private static MagickImage PadHorizontalWrap(MagickImage tile, int width, int height, int padding)
    {
        using var left = CropFrame(tile, width - padding, padding, height);
        using var right = CropFrame(tile, 0, padding, height);
        var padded = new MagickImage(MagickColors.Transparent, (uint)(width + 2 * padding), (uint)height);
        padded.Composite(left, 0, 0, CompositeOperator.Copy);
        padded.Composite(tile, padding, 0, CompositeOperator.Copy);
        padded.Composite(right, width + padding, 0, CompositeOperator.Copy);
        return padded;
    }

    private static byte[]? ComputeBoundsAndMask(
        MagickImage frame,
        int width,
        int height,
        bool generateMask,
        out int minX,
        out int minY,
        out int maxX,
        out int maxY)
    {
        minX = width;
        minY = height;
        maxX = -1;
        maxY = -1;

        int stride = (width + 7) / 8;
        byte[]? mask = generateMask ? new byte[stride * height] : null;
        using var pixelCollection = frame.GetPixels();
        ReadOnlySpan<byte> pixels = pixelCollection.GetReadOnlyArea(0, 0, (uint)width, (uint)height);

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                byte alpha = pixels[(4 * ((y * width) + x)) + 3];
                if (alpha == 0) continue;

                if (x < minX) minX = x;
                if (y < minY) minY = y;
                if (x > maxX) maxX = x;
                if (y > maxY) maxY = y;

                if (mask is not null)
                    mask[(y * stride) + (x >> 3)] |= (byte)(0x80 >> (x & 7));
            }
        }

        return mask;
    }
}
