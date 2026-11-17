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
    bool GenerateMask);

/// <summary>Packs ModKit PNG assets onto new GameMaker texture pages and creates <see cref="UndertaleSprite"/> entries.</summary>
internal static class ModKitSpriteImporter
{
    internal static Dictionary<string, UndertaleSprite> ImportAll(UndertaleData data, IReadOnlyList<SpriteImportSpec> specs)
    {
        var result = new Dictionary<string, UndertaleSprite>(StringComparer.Ordinal);
        if (specs.Count == 0)
            return result;

        var packer = new TextureGroupPacker(maxTextureSize: 2048, border: 2, allowCrop: false, format: GMImage.ImageFormat.Png);
        var pending = new List<(SpriteImportSpec Spec, int Width, int Height, List<UndertaleTexturePageItem> Items, byte[]? MaskBytes, int MinX, int MinY, int MaxX, int MaxY)>();

        foreach (var spec in specs)
        {
            if (data.Sprites.ByName(spec.Name) is not null)
                throw new InvalidDataException($"Sprite '{spec.Name}' already exists in the base archive. Choose a different mod id or item key.");

            using var source = new MagickImage(spec.PngPath);
            source.Format = MagickFormat.Rgba;
            source.SetBitDepth(8);
            source.Alpha(AlphaOption.Set);

            int totalWidth = (int)source.Width;
            int height = (int)source.Height;
            if (spec.Frames < 1 || totalWidth % spec.Frames != 0)
                throw new InvalidDataException($"Sprite '{spec.PngPath}' ({totalWidth}x{height}) is not evenly divisible by frames={spec.Frames}.");

            int frameWidth = totalWidth / spec.Frames;
            var items = new List<UndertaleTexturePageItem>(spec.Frames);
            byte[]? maskBytes = null;
            int minX = frameWidth, minY = height, maxX = -1, maxY = -1;

            for (int f = 0; f < spec.Frames; f++)
            {
                var tile = spec.Frames == 1
                    ? new MagickImage(source)
                    : CropFrame(source, f * frameWidth, frameWidth, height);

                if (f == 0)
                    maskBytes = ComputeBoundsAndMask(tile, frameWidth, height, spec.GenerateMask, out minX, out minY, out maxX, out maxY);

                // TextureGroupPacker takes ownership of tile and disposes it during PackPages().
                items.Add(packer.AddImage(tile, TextureGroupPacker.BorderFlags.None, allowCrop: false));
            }

            pending.Add((spec, frameWidth, height, items, maskBytes, minX, minY, maxX, maxY));
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
                SepMasks = UndertaleSprite.SepMaskType.AxisAlignedRect
            };

            foreach (var item in entry.Items)
                sprite.Textures.Add(new UndertaleSprite.TextureEntry { Texture = item });

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
        ReadOnlySpan<byte> pixels = frame.GetPixels().GetReadOnlyArea(0, 0, (uint)width, (uint)height);

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
