using ImageMagick;
using Jamm.Core.ModKit;
using UndertaleModLib;
using UndertaleModLib.Models;
using UndertaleModLib.Util;

namespace Jamm.ModMaker;

internal static class EndlessOceanSprites
{
    internal const int CellSize = 480;
    internal const int VariantCount = 16;
    internal const int WrapPad = 2;

    private const int Width = 512;
    private const int Rise = 256;
    private const int CapHeight = Rise + 16;
    private const int EdgeDepth = 36;
    private const int CornerReach = 60;
    private const int PatchSize = 132;
    private const int EdgePatchSize = 120;
    private const int Overlap = 24;
    private const int Step = PatchSize - Overlap; // 108; 12 + 4 * 108 + 24 = 468
    private const int EdgeStep = EdgePatchSize - Overlap; // 96; 36 + 3 * 96 + 120 = 444
    private const int InteriorInset = 12;

    internal static UndertaleSprite Add(UndertaleData data, UndertaleSprite rock, UndertaleSprite boat)
    {
        if (rock.Textures.FirstOrDefault()?.Texture is not { } frame || boat.Textures.Count == 0)
            throw new InvalidDataException("Endless Ocean needs the original CaveRockBig and Boat3 sprites. Choose a compatible legacy archive.");

        using var textures = new TextureWorker();
        var patches = ExtractPatches(data, textures, rock);
        var tiles = BuildWangTiles(patches, EndlessOceanMod.DefaultSeed);

        var packer = new TextureGroupPacker(maxTextureSize: 2048, border: 2, allowCrop: false, format: GMImage.ImageFormat.Png);
        var texItems = new List<UndertaleTexturePageItem>(VariantCount);
        foreach (var tile in tiles)
        {
            var img = new MagickImage(tile, new PixelReadSettings(
                (uint)(CellSize + 2 * WrapPad), (uint)(CellSize + 2 * WrapPad), StorageType.Char, PixelMapping.RGBA));
            texItems.Add(packer.AddImage(img, TextureGroupPacker.BorderFlags.None, allowCrop: false));
        }

        var ground = AddGround("spr_jmk_ocean_underlay", Rise, false, _ => 0, tiles[0]);
        AddGround("spr_jmk_ocean_slope_down", CapHeight, true, x => x / 2, tiles[0]);
        AddGround("spr_jmk_ocean_slope_up", CapHeight, true, x => (Width - 1 - x) / 2, tiles[0]);

        // Compose the wreck from a stock boat hull and foreground rock rubble at build time.
        using var composer = new ModKitComposition(data);
        var wreck = new ModKitSprite("wreck", 896, 360, null,
            [[new(boat.Name.Content, null, 0, new(120, 175, 720, 140), 86, 66,
                  1, 1, -12, "x", 0x50606a, 0, new Dictionary<int, int>(), 1, 0),
              new(rock.Name.Content, null, 0, null, 8, 166,
                  0.25, 0.18, 0, "none", null, 0, new Dictionary<int, int>(), 1, 1),
              new(rock.Name.Content, null, 0, null, 648, 184,
                  0.20, 0.15, 0, "x", null, 0, new Dictionary<int, int>(), 1, 1)]]);
        AddSprite("spr_jmk_ocean_wreck", [composer.Render(wreck, 0)], wreck.Width, wreck.Height, 448, 258, false, null);

        packer.PackPages();
        packer.ImportToData(data);

        // Trim the 2px neighbor padding from the TPAG crop so GPU bilinear filtering samples real neighbors.
        if (data.Sprites.ByName("spr_jmk_ocean_underlay_tex") is not null)
            throw new InvalidDataException("Endless Ocean needs the sprite name 'spr_jmk_ocean_underlay_tex'. Remove the conflicting mod and rebuild.");

        var atlasSprite = new UndertaleSprite
        {
            Name = data.Strings.MakeString("spr_jmk_ocean_underlay_tex"),
            Width = CellSize,
            Height = CellSize,
            MarginRight = CellSize - 1,
            MarginBottom = CellSize - 1,
            OriginX = 0,
            OriginY = 0,
            BBoxMode = 2,
            Transparent = false,
            Preload = true,
            SepMasks = UndertaleSprite.SepMaskType.AxisAlignedRect
        };
        foreach (var item in texItems)
        {
            item.SourceX += WrapPad;
            item.SourceY += WrapPad;
            item.SourceWidth = item.TargetWidth = item.BoundingWidth = CellSize;
            item.SourceHeight = item.TargetHeight = item.BoundingHeight = CellSize;
            atlasSprite.Textures.Add(new UndertaleSprite.TextureEntry { Texture = item });
        }
        data.Sprites.Add(atlasSprite);
        return ground;

        UndertaleSprite AddGround(string name, int height, bool precise, Func<int, int> topAt, byte[] paddedTile)
        {
            byte[] rgba = new byte[Width * height * 4];
            byte[] mask = new byte[Width / 8 * height];
            int paddedStride = (CellSize + 2 * WrapPad) * 4;
            for (int x = 0; x < Width; x++)
            {
                int sx = x % CellSize;
                for (int y = topAt(x); y < height; y++)
                {
                    int sy = (y - topAt(x)) % CellSize;
                    int from = (sy + WrapPad) * paddedStride + (sx + WrapPad) * 4;
                    int to = (Width * y + x) * 4;
                    paddedTile.AsSpan(from, 3).CopyTo(rgba.AsSpan(to, 3));
                    rgba[to + 3] = 255;
                    mask[y * (Width / 8) + (x >> 3)] |= (byte)(0x80 >> (x & 7));
                }
            }
            var tile = new MagickImage(rgba, new PixelReadSettings(Width, (uint)height, StorageType.Char, PixelMapping.RGBA));
            return AddSprite(name, [tile], Width, height, 0, 0, precise, mask);
        }

        UndertaleSprite AddSprite(string name, IEnumerable<MagickImage> frames, int width, int height, int ox, int oy, bool precise, byte[]? mask)
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
            foreach (var img in frames)
            {
                sprite.Textures.Add(new UndertaleSprite.TextureEntry
                {
                    Texture = packer.AddImage(img, TextureGroupPacker.BorderFlags.None, allowCrop: false)
                });
            }
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

    internal static List<byte[]> BuildWangTiles(IReadOnlyList<byte[]> patches, int seed)
    {
        if (patches.Count == 0)
            throw new ArgumentException("At least one rock patch is required.", nameof(patches));

        // 120x120 shared corner patch centered at grid vertices (0, 0) mod CellSize.
        // Keeping [0..36) and [444..480) pure corner guarantees horizontal and vertical collars agree in all 36x36 corners.
        int cornerSize = CornerReach * 2;
        int collarThickness = EdgeDepth * 2;
        byte[] corner = new byte[cornerSize * cornerSize * 4];
        CopyPatchRegion(patches[0], 0, 0, corner, cornerSize, 0, 0, cornerSize, cornerSize, 0);

        byte[][] horizEdges = new byte[2][];
        byte[][] vertEdges = new byte[2][];
        for (int e = 0; e < 2; e++)
        {
            horizEdges[e] = BuildHorizontalEdge(patches, corner, cornerSize, collarThickness, seed + e * 7919);
            vertEdges[e] = BuildVerticalEdge(patches, corner, cornerSize, collarThickness, seed + (e + 10) * 7919);
        }

        var result = new List<byte[]>(VariantCount);
        int paddedSize = CellSize + 2 * WrapPad;
        for (int variant = 0; variant < VariantCount; variant++)
        {
            int top = (variant >> 3) & 1;
            int right = (variant >> 2) & 1;
            int bottom = (variant >> 1) & 1;
            int left = variant & 1;

            byte[] tile = new byte[CellSize * CellSize * 4];
            // Stamp the invariant edge collars; the 36x36 corners agree identically on `corner`.
            Blit(horizEdges[top], CellSize, 0, EdgeDepth, tile, CellSize, 0, 0, CellSize, EdgeDepth);
            Blit(horizEdges[bottom], CellSize, 0, 0, tile, CellSize, 0, CellSize - EdgeDepth, CellSize, EdgeDepth);
            Blit(vertEdges[left], collarThickness, EdgeDepth, 0, tile, CellSize, 0, 0, EdgeDepth, CellSize);
            Blit(vertEdges[right], collarThickness, 0, 0, tile, CellSize, CellSize - EdgeDepth, 0, EdgeDepth, CellSize);

            int[,] chosenPatch = new int[4, 4];
            for (int r = 0; r < 4; r++)
            {
                int y0 = InteriorInset + r * Step;
                bool overlapBottom = r == 3;
                for (int c = 0; c < 4; c++)
                {
                    int x0 = InteriorInset + c * Step;
                    bool overlapRight = c == 3;
                    int avoidA = c > 0 ? chosenPatch[r, c - 1] : -1;
                    int avoidB = r > 0 ? chosenPatch[r - 1, c] : -1;
                    int bestIdx = ChoosePatch(
                        patches, tile, CellSize, x0, y0, PatchSize, PatchSize,
                        overlapLeft: true, overlapTop: true, overlapRight, overlapBottom,
                        avoidA, avoidB, Hash(seed, variant, r, c), out int bestTransform);
                    chosenPatch[r, c] = bestIdx;
                    QuiltPatch(
                        patches[bestIdx], bestTransform, tile, CellSize, x0, y0, PatchSize, PatchSize,
                        overlapLeft: true, overlapTop: true, overlapRight, overlapBottom);
                }
            }

            // Pad by 2px using the exact adjacent Wang edges and corner so bilinear sampling is seamless.
            byte[] padded = new byte[paddedSize * paddedSize * 4];
            Blit(tile, CellSize, 0, 0, padded, paddedSize, WrapPad, WrapPad, CellSize, CellSize);
            // Top / bottom 2px strips
            Blit(horizEdges[top], CellSize, 0, EdgeDepth - WrapPad, padded, paddedSize, WrapPad, 0, CellSize, WrapPad);
            Blit(horizEdges[bottom], CellSize, 0, EdgeDepth, padded, paddedSize, WrapPad, WrapPad + CellSize, CellSize, WrapPad);
            // Left / right 2px strips
            Blit(vertEdges[left], collarThickness, EdgeDepth - WrapPad, 0, padded, paddedSize, 0, WrapPad, WrapPad, CellSize);
            Blit(vertEdges[right], collarThickness, EdgeDepth, 0, padded, paddedSize, WrapPad + CellSize, WrapPad, WrapPad, CellSize);
            // 2x2 corners from the shared vertex corner patch
            Blit(corner, cornerSize, CornerReach - WrapPad, CornerReach - WrapPad, padded, paddedSize, 0, 0, WrapPad, WrapPad);
            Blit(corner, cornerSize, CornerReach, CornerReach - WrapPad, padded, paddedSize, WrapPad + CellSize, 0, WrapPad, WrapPad);
            Blit(corner, cornerSize, CornerReach - WrapPad, CornerReach, padded, paddedSize, 0, WrapPad + CellSize, WrapPad, WrapPad);
            Blit(corner, cornerSize, CornerReach, CornerReach, padded, paddedSize, WrapPad + CellSize, WrapPad + CellSize, WrapPad, WrapPad);
            result.Add(padded);
        }

        return result;
    }

    private static List<byte[]> ExtractPatches(UndertaleData data, TextureWorker textures, UndertaleSprite primary)
    {
        var list = new List<byte[]>();
        double? baseLuma = null;

        var candidates = new[]
        {
            primary,
            data.GameObjects.ByName("CaveRockWide")?.Sprite,
            data.GameObjects.ByName("CaveRockLeg")?.Sprite,
            data.GameObjects.ByName("CaveRockSig")?.Sprite,
            data.GameObjects.ByName("CaveRockTear")?.Sprite,
            data.GameObjects.ByName("CaveRockTee")?.Sprite,
            data.GameObjects.ByName("CaveRockWedge")?.Sprite,
        };

        foreach (var sprite in candidates)
        {
            if (sprite?.Textures.FirstOrDefault()?.Texture is not { } tex)
                continue;

            using var image = textures.GetTextureFor(tex, sprite.Name.Content, includePadding: true);
            using var pixels = image.GetPixels();
            int w = (int)image.Width;
            int h = (int)image.Height;
            var rgba = pixels.ToByteArray(0, 0, (uint)w, (uint)h, PixelMapping.RGBA);
            if (rgba is null || w < PatchSize || h < PatchSize)
                continue;

            var extracted = ExtractOpaquePatches(rgba, w, h, margin: 6);
            if (extracted.Count == 0 && ReferenceEquals(sprite, primary))
                extracted = ExtractOpaquePatches(rgba, w, h, margin: 0);

            if (extracted.Count == 0)
            {
                if (ReferenceEquals(sprite, primary))
                {
                    if (w < 512 || h < 512)
                        throw new InvalidDataException("Endless Ocean could not read the CaveRockBig texture. Choose a compatible legacy archive.");
                    // Fallback to the guaranteed interior rectangle in stock CaveRockBig.
                    extracted.Add(CropPatch(rgba, w, 256, 256));
                }
                else
                {
                    continue;
                }
            }

            double luma = MeanLuminance(extracted);
            if (baseLuma is null)
                baseLuma = luma;
            else if (Math.Abs(luma - baseLuma.Value) > 18.0)
                continue;

            list.AddRange(extracted);
            if (list.Count >= 64)
                break;
        }

        if (list.Count == 0)
            throw new InvalidDataException("Endless Ocean could not read the CaveRockBig texture. Choose a compatible legacy archive.");

        return list;
    }

    private static List<byte[]> ExtractOpaquePatches(byte[] rgba, int width, int height, int margin)
    {
        int[] bad = new int[(width + 1) * (height + 1)];
        for (int y = 0; y < height; y++)
        {
            int rowSum = 0;
            for (int x = 0; x < width; x++)
            {
                if (rgba[(y * width + x) * 4 + 3] < 250)
                    rowSum++;
                bad[(y + 1) * (width + 1) + (x + 1)] = bad[y * (width + 1) + (x + 1)] + rowSum;
            }
        }

        var patches = new List<byte[]>();
        int box = PatchSize + 2 * margin;
        int stride = 24;
        for (int y = margin; y + PatchSize + margin <= height; y += stride)
        {
            for (int x = margin; x + PatchSize + margin <= width; x += stride)
            {
                int x0 = x - margin;
                int y0 = y - margin;
                int x1 = x0 + box;
                int y1 = y0 + box;
                int count = bad[y1 * (width + 1) + x1] - bad[y0 * (width + 1) + x1]
                          - bad[y1 * (width + 1) + x0] + bad[y0 * (width + 1) + x0];
                if (count == 0)
                {
                    patches.Add(CropPatch(rgba, width, x, y));
                    if (patches.Count >= 24)
                        return patches;
                }
            }
        }
        return patches;
    }

    private static byte[] CropPatch(byte[] rgba, int srcWidth, int sx, int sy)
    {
        byte[] patch = new byte[PatchSize * PatchSize * 4];
        for (int y = 0; y < PatchSize; y++)
        {
            int from = ((sy + y) * srcWidth + sx) * 4;
            int to = y * PatchSize * 4;
            for (int x = 0; x < PatchSize; x++)
            {
                patch[to + x * 4 + 0] = rgba[from + x * 4 + 0];
                patch[to + x * 4 + 1] = rgba[from + x * 4 + 1];
                patch[to + x * 4 + 2] = rgba[from + x * 4 + 2];
                patch[to + x * 4 + 3] = 255;
            }
        }
        return patch;
    }

    private static double MeanLuminance(IReadOnlyList<byte[]> patches)
    {
        double sum = 0;
        long count = 0;
        foreach (var p in patches)
        {
            for (int i = 0; i < p.Length; i += 16)
            {
                sum += 0.299 * p[i] + 0.587 * p[i + 1] + 0.114 * p[i + 2];
                count++;
            }
        }
        return count > 0 ? sum / count : 0;
    }

    private static byte[] BuildHorizontalEdge(IReadOnlyList<byte[]> patches, byte[] corner, int cornerSize, int collarThickness, int seed)
    {
        int cornerOffset = CornerReach - EdgeDepth;
        byte[] strip = new byte[CellSize * collarThickness * 4];
        Blit(corner, cornerSize, CornerReach, cornerOffset, strip, CellSize, 0, 0, CornerReach, collarThickness);
        Blit(corner, cornerSize, 0, cornerOffset, strip, CellSize, CellSize - CornerReach, 0, CornerReach, collarThickness);

        int prev = -1;
        for (int c = 0; c < 4; c++)
        {
            int x0 = EdgeDepth + c * EdgeStep;
            bool overlapRight = c == 3;
            int idx = ChoosePatch(
                patches, strip, CellSize, x0, 0, EdgePatchSize, collarThickness,
                overlapLeft: true, overlapTop: false, overlapRight, overlapBottom: false,
                prev, -1, Hash(seed, 1, c, 0), out int transform);
            prev = idx;
            QuiltPatch(
                patches[idx], transform, strip, CellSize, x0, 0, EdgePatchSize, collarThickness,
                overlapLeft: true, overlapTop: false, overlapRight, overlapBottom: false);
        }
        return strip;
    }

    private static byte[] BuildVerticalEdge(IReadOnlyList<byte[]> patches, byte[] corner, int cornerSize, int collarThickness, int seed)
    {
        int cornerOffset = CornerReach - EdgeDepth;
        byte[] strip = new byte[collarThickness * CellSize * 4];
        Blit(corner, cornerSize, cornerOffset, CornerReach, strip, collarThickness, 0, 0, collarThickness, CornerReach);
        Blit(corner, cornerSize, cornerOffset, 0, strip, collarThickness, 0, CellSize - CornerReach, collarThickness, CornerReach);

        int prev = -1;
        for (int r = 0; r < 4; r++)
        {
            int y0 = EdgeDepth + r * EdgeStep;
            bool overlapBottom = r == 3;
            int idx = ChoosePatch(
                patches, strip, collarThickness, 0, y0, collarThickness, EdgePatchSize,
                overlapLeft: false, overlapTop: true, overlapRight: false, overlapBottom,
                prev, -1, Hash(seed, 2, r, 0), out int transform);
            prev = idx;
            QuiltPatch(
                patches[idx], transform, strip, collarThickness, 0, y0, collarThickness, EdgePatchSize,
                overlapLeft: false, overlapTop: true, overlapRight: false, overlapBottom);
        }
        return strip;
    }

    private static int ChoosePatch(
        IReadOnlyList<byte[]> patches, byte[] dst, int dstW, int x0, int y0, int pw, int ph,
        bool overlapLeft, bool overlapTop, bool overlapRight, bool overlapBottom,
        int avoidA, int avoidB, uint rng, out int bestTransform)
    {
        int bestIdx = 0;
        bestTransform = 0;
        long bestScore = long.MaxValue;
        int candidates = Math.Min(16, patches.Count * 8);

        for (int k = 0; k < candidates; k++)
        {
            rng = NextHash(rng);
            int idx = (int)(rng % (uint)patches.Count);
            int transform = (int)((rng >> 16) & 7);
            if (patches.Count > 2 && (idx == avoidA || idx == avoidB))
                idx = (idx + 1) % patches.Count;

            long score = OverlapError(
                patches[idx], transform, dst, dstW, x0, y0, pw, ph,
                overlapLeft, overlapTop, overlapRight, overlapBottom);
            if (score < bestScore)
            {
                bestScore = score;
                bestIdx = idx;
                bestTransform = transform;
            }
        }
        return bestIdx;
    }

    private static long OverlapError(
        byte[] patch, int transform, byte[] dst, int dstW, int x0, int y0, int pw, int ph,
        bool overlapLeft, bool overlapTop, bool overlapRight, bool overlapBottom)
    {
        long err = 0;
        if (overlapLeft)
        {
            for (int y = 0; y < ph; y++)
                for (int x = 0; x < Overlap; x++)
                    err += PixelDiffSq(patch, x, y, transform, dst, dstW, x0 + x, y0 + y);
        }
        if (overlapRight)
        {
            for (int y = 0; y < ph; y++)
                for (int x = pw - Overlap; x < pw; x++)
                    err += PixelDiffSq(patch, x, y, transform, dst, dstW, x0 + x, y0 + y);
        }
        if (overlapTop)
        {
            for (int y = 0; y < Overlap; y++)
                for (int x = 0; x < pw; x++)
                    err += PixelDiffSq(patch, x, y, transform, dst, dstW, x0 + x, y0 + y);
        }
        if (overlapBottom)
        {
            for (int y = ph - Overlap; y < ph; y++)
                for (int x = 0; x < pw; x++)
                    err += PixelDiffSq(patch, x, y, transform, dst, dstW, x0 + x, y0 + y);
        }
        return err;
    }

    private static void QuiltPatch(
        byte[] patch, int transform, byte[] dst, int dstW, int x0, int y0, int pw, int ph,
        bool overlapLeft, bool overlapTop, bool overlapRight, bool overlapBottom)
    {
        int[] leftCut = overlapLeft ? VerticalCut(patch, transform, dst, dstW, x0, y0, ph, 0) : [];
        int[] rightCut = overlapRight ? VerticalCut(patch, transform, dst, dstW, x0, y0, ph, pw - Overlap) : [];
        int[] topCut = overlapTop ? HorizontalCut(patch, transform, dst, dstW, x0, y0, pw, 0) : [];
        int[] bottomCut = overlapBottom ? HorizontalCut(patch, transform, dst, dstW, x0, y0, pw, ph - Overlap) : [];

        for (int y = 0; y < ph; y++)
        {
            for (int x = 0; x < pw; x++)
            {
                double w = 1.0;
                if (overlapLeft && x < Overlap)
                    w = Math.Min(w, Feather(x - leftCut[y]));
                if (overlapRight && x >= pw - Overlap)
                    w = Math.Min(w, Feather(rightCut[y] - (x - (pw - Overlap))));
                if (overlapTop && y < Overlap)
                    w = Math.Min(w, Feather(y - topCut[x]));
                if (overlapBottom && y >= ph - Overlap)
                    w = Math.Min(w, Feather(bottomCut[x] - (y - (ph - Overlap))));

                if (w <= 0.0)
                    continue;

                int di = ((y0 + y) * dstW + (x0 + x)) * 4;
                ReadPatchRgb(patch, x, y, transform, out byte sr, out byte sg, out byte sb);
                if (w >= 1.0)
                {
                    dst[di + 0] = sr;
                    dst[di + 1] = sg;
                    dst[di + 2] = sb;
                }
                else
                {
                    dst[di + 0] = (byte)Math.Round(dst[di + 0] * (1.0 - w) + sr * w);
                    dst[di + 1] = (byte)Math.Round(dst[di + 1] * (1.0 - w) + sg * w);
                    dst[di + 2] = (byte)Math.Round(dst[di + 2] * (1.0 - w) + sb * w);
                }
                dst[di + 3] = 255;
            }
        }
    }

    private static double Feather(int delta) => Math.Clamp((delta + 1.5) / 3.0, 0.0, 1.0);

    private static int[] VerticalCut(byte[] patch, int transform, byte[] dst, int dstW, int x0, int y0, int ph, int ox)
    {
        long[,] dp = new long[ph, Overlap];
        for (int x = 0; x < Overlap; x++)
            dp[0, x] = PixelDiffSq(patch, ox + x, 0, transform, dst, dstW, x0 + ox + x, y0);

        for (int y = 1; y < ph; y++)
        {
            for (int x = 0; x < Overlap; x++)
            {
                long best = dp[y - 1, x];
                if (x > 0) best = Math.Min(best, dp[y - 1, x - 1]);
                if (x + 1 < Overlap) best = Math.Min(best, dp[y - 1, x + 1]);
                dp[y, x] = best + PixelDiffSq(patch, ox + x, y, transform, dst, dstW, x0 + ox + x, y0 + y);
            }
        }

        int[] cut = new int[ph];
        int cur = 0;
        for (int x = 1; x < Overlap; x++)
            if (dp[ph - 1, x] < dp[ph - 1, cur])
                cur = x;
        cut[ph - 1] = cur;

        for (int y = ph - 2; y >= 0; y--)
        {
            int next = cur;
            if (cur > 0 && dp[y, cur - 1] < dp[y, next]) next = cur - 1;
            if (cur + 1 < Overlap && dp[y, cur + 1] < dp[y, next]) next = cur + 1;
            cur = next;
            cut[y] = cur;
        }
        return cut;
    }

    private static int[] HorizontalCut(byte[] patch, int transform, byte[] dst, int dstW, int x0, int y0, int pw, int oy)
    {
        long[,] dp = new long[pw, Overlap];
        for (int y = 0; y < Overlap; y++)
            dp[0, y] = PixelDiffSq(patch, 0, oy + y, transform, dst, dstW, x0, y0 + oy + y);

        for (int x = 1; x < pw; x++)
        {
            for (int y = 0; y < Overlap; y++)
            {
                long best = dp[x - 1, y];
                if (y > 0) best = Math.Min(best, dp[x - 1, y - 1]);
                if (y + 1 < Overlap) best = Math.Min(best, dp[x - 1, y + 1]);
                dp[x, y] = best + PixelDiffSq(patch, x, oy + y, transform, dst, dstW, x0 + x, y0 + oy + y);
            }
        }

        int[] cut = new int[pw];
        int cur = 0;
        for (int y = 1; y < Overlap; y++)
            if (dp[pw - 1, y] < dp[pw - 1, cur])
                cur = y;
        cut[pw - 1] = cur;

        for (int x = pw - 2; x >= 0; x--)
        {
            int next = cur;
            if (cur > 0 && dp[x, cur - 1] < dp[x, next]) next = cur - 1;
            if (cur + 1 < Overlap && dp[x, cur + 1] < dp[x, next]) next = cur + 1;
            cur = next;
            cut[x] = cur;
        }
        return cut;
    }

    private static long PixelDiffSq(byte[] patch, int px, int py, int transform, byte[] dst, int dstW, int dx, int dy)
    {
        ReadPatchRgb(patch, px, py, transform, out byte sr, out byte sg, out byte sb);
        int di = (dy * dstW + dx) * 4;
        long dr = sr - dst[di + 0];
        long dg = sg - dst[di + 1];
        long db = sb - dst[di + 2];
        return dr * dr + dg * dg + db * db;
    }

    private static void CopyPatchRegion(
        byte[] patch, int sx, int sy, byte[] dst, int dstW, int dx, int dy, int w, int h, int transform)
    {
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                ReadPatchRgb(patch, sx + x, sy + y, transform, out byte r, out byte g, out byte b);
                int di = ((dy + y) * dstW + (dx + x)) * 4;
                dst[di + 0] = r;
                dst[di + 1] = g;
                dst[di + 2] = b;
                dst[di + 3] = 255;
            }
        }
    }

    private static void ReadPatchRgb(byte[] patch, int x, int y, int transform, out byte r, out byte g, out byte b)
    {
        if ((transform & 4) != 0)
            x = PatchSize - 1 - x;
        (int tx, int ty) = (transform & 3) switch
        {
            1 => (PatchSize - 1 - y, x),
            2 => (PatchSize - 1 - x, PatchSize - 1 - y),
            3 => (y, PatchSize - 1 - x),
            _ => (x, y)
        };
        int si = (ty * PatchSize + tx) * 4;
        r = patch[si + 0];
        g = patch[si + 1];
        b = patch[si + 2];
    }

    private static void Blit(byte[] src, int srcW, int sx, int sy, byte[] dst, int dstW, int dx, int dy, int w, int h)
    {
        int bytes = w * 4;
        for (int y = 0; y < h; y++)
        {
            int from = ((sy + y) * srcW + sx) * 4;
            int to = ((dy + y) * dstW + dx) * 4;
            Buffer.BlockCopy(src, from, dst, to, bytes);
        }
    }

    private static uint Hash(int a, int b, int c, int d)
    {
        uint h = 2166136261u;
        h = (h ^ (uint)a) * 16777619u;
        h = (h ^ (uint)b) * 16777619u;
        h = (h ^ (uint)c) * 16777619u;
        h = (h ^ (uint)d) * 16777619u;
        return h == 0 ? 1u : h;
    }

    private static uint NextHash(uint h)
    {
        h ^= h << 13;
        h ^= h >> 17;
        h ^= h << 5;
        return h == 0 ? 1u : h;
    }
}
