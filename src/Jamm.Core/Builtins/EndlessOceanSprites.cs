using ImageMagick;
using Jamm.Core.ModKit;
using UndertaleModLib;
using UndertaleModLib.Models;
using UndertaleModLib.Util;

namespace Jamm.ModMaker;

internal static class EndlessOceanSprites
{
    private const int Width = 512;
    private const int Rise = 256;
    private const int CapHeight = Rise + 16;

    internal static UndertaleSprite Add(UndertaleData data, UndertaleSprite rock, UndertaleSprite boat)
    {
        if (rock.Textures.FirstOrDefault()?.Texture is not { } frame || boat.Textures.Count == 0)
            throw new InvalidDataException("Endless Ocean needs the original CaveRockBig and Boat3 sprites. Choose a compatible legacy archive.");

        using var textures = new TextureWorker();
        using var image = textures.GetTextureFor(frame, rock.Name.Content, includePadding: true);
        using var pixels = image.GetPixels();
        // This interior rectangle is opaque in the stock CaveRockBig frame and mask.
        var sample = pixels.ToByteArray(256, 256, 256, 256, PixelMapping.RGBA)
            ?? throw new InvalidDataException("Endless Ocean could not read the CaveRockBig texture. Choose a compatible legacy archive.");

        var packer = new TextureGroupPacker(maxTextureSize: 2048, border: 2, allowCrop: false, format: GMImage.ImageFormat.Png);
        var ground = AddGround("spr_jmk_ocean_underlay", Rise, false, _ => 0);
        AddGround("spr_jmk_ocean_slope_down", CapHeight, true, x => x / 2);
        AddGround("spr_jmk_ocean_slope_up", CapHeight, true, x => (Width - 1 - x) / 2);

        // Compose the wreck from a stock boat hull and foreground rock rubble at build time.
        using var composer = new ModKitComposition(data);
        var wreck = new ModKitSprite("wreck", 896, 360, null,
            [[new(boat.Name.Content, null, 0, new(120, 175, 720, 140), 86, 66,
                  1, 1, -12, "x", 0x50606a, 0, new Dictionary<int, int>(), 1, 0),
              new(rock.Name.Content, null, 0, null, 8, 166,
                  0.25, 0.18, 0, "none", null, 0, new Dictionary<int, int>(), 1, 1),
              new(rock.Name.Content, null, 0, null, 648, 184,
                  0.20, 0.15, 0, "x", null, 0, new Dictionary<int, int>(), 1, 1)]]);
        AddSprite("spr_jmk_ocean_wreck", composer.Render(wreck, 0), wreck.Width, wreck.Height, 448, 258, false, null);
        packer.PackPages();
        packer.ImportToData(data);
        return ground;

        UndertaleSprite AddGround(string name, int height, bool precise, Func<int, int> topAt)
        {
            byte[] rgba = new byte[Width * height * 4];
            byte[] mask = new byte[Width / 8 * height];
            for (int x = 0; x < Width; x++)
            {
                int sx = Math.Min(x, Width - 1 - x);
                for (int y = topAt(x); y < height; y++)
                {
                    int sy = (y - topAt(x)) % Rise;
                    int from = (256 * sy + sx) * 4;
                    int to = (Width * y + x) * 4;
                    sample.AsSpan(from, 3).CopyTo(rgba.AsSpan(to, 3));
                    rgba[to + 3] = 255;
                    mask[y * (Width / 8) + (x >> 3)] |= (byte)(0x80 >> (x & 7));
                }
            }
            var tile = new MagickImage(rgba, new PixelReadSettings(Width, (uint)height, StorageType.Char, PixelMapping.RGBA));
            return AddSprite(name, tile, Width, height, 0, 0, precise, mask);
        }

        UndertaleSprite AddSprite(string name, MagickImage image, int width, int height, int ox, int oy, bool precise, byte[]? mask)
        {
            if (data.Sprites.ByName(name) is not null)
                throw new InvalidDataException($"Endless Ocean needs the sprite name '{name}'. Remove the conflicting mod and rebuild.");

            var sprite = new UndertaleSprite
            {
                Name = data.Strings.MakeString(name),
                Width = (uint)width, Height = (uint)height,
                MarginRight = width - 1, MarginBottom = height - 1,
                OriginX = ox, OriginY = oy,
                BBoxMode = 2, Transparent = true, Preload = true,
                SepMasks = precise ? UndertaleSprite.SepMaskType.Precise : UndertaleSprite.SepMaskType.AxisAlignedRect
            };
            sprite.Textures.Add(new UndertaleSprite.TextureEntry
            {
                Texture = packer.AddImage(image, TextureGroupPacker.BorderFlags.None, allowCrop: false)
            });
            if (mask is not null)
            {
                var entry = sprite.NewMaskEntry(data);
                mask.CopyTo(entry.Data, 0);
                sprite.CollisionMasks.Add(entry);
            }
            data.Sprites.Add(sprite);
            return sprite;
        }
    }
}
