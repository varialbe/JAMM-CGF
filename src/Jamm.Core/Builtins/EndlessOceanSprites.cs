using ImageMagick;
using Jamm.Core.ModKit;
using UndertaleModLib;
using UndertaleModLib.Models;
using UndertaleModLib.Util;

namespace Jamm.ModMaker;

internal static class EndlessOceanSprites
{
    internal const int CellSize = 480;
    internal const int SlotPad = 16;
    internal const int SlotSize = CellSize + 2 * SlotPad; // 512
    internal const int AtlasGrid = 4;
    internal const int AtlasSize = AtlasGrid * SlotSize; // 2048
    internal const int VariantCount = 16;
    internal const int EdgeMargin = 6;
    private const int MortarGap = 9;

    private const int Width = 512;
    private const int Rise = 256;
    private const int CapHeight = Rise + 16;

    internal readonly record struct Rgb(byte R, byte G, byte B);
    internal readonly record struct CobbleMask(int Width, int Height, byte[] Alpha, int Area, double Solidity, double Aspect);
    private readonly record struct CobblePlacement(int Index, int X, int Y);

    internal static UndertaleSprite Add(UndertaleData data, UndertaleSprite rock, UndertaleSprite boat)
    {
        if (rock.Textures.FirstOrDefault()?.Texture is null || boat.Textures.Count == 0)
            throw new InvalidDataException("Endless Ocean needs the original CaveRockBig and Boat3 sprites. Choose a compatible legacy archive.");

        if (data.Sprites.ByName("spr_jmk_ocean_underlay_tex") is not null)
            throw new InvalidDataException("Endless Ocean needs the sprite name 'spr_jmk_ocean_underlay_tex'. Remove the conflicting mod and rebuild.");

        using var textures = new TextureWorker();
        var (darkRgb, lightRgb) = SamplePalette(textures, rock);
        var rawCobbles = ExtractCobbles(data, textures, rock, darkRgb, lightRgb);
        var slots = BuildWangSlots(rawCobbles, darkRgb, lightRgb, EndlessOceanMod.DefaultSeed);
        var atlasRgba = AssembleAtlas(slots);

        var packer = new TextureGroupPacker(maxTextureSize: 2048, border: 2, allowCrop: false, format: GMImage.ImageFormat.Png);
        var ground = AddGround("spr_jmk_ocean_underlay", Rise, false, _ => 0, slots[0]);
        AddGround("spr_jmk_ocean_slope_down", CapHeight, true, x => x / 2, slots[0]);
        AddGround("spr_jmk_ocean_slope_up", CapHeight, true, x => (Width - 1 - x) / 2, slots[0]);

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

        // Store all 16 padded Wang tiles in a dedicated 2048x2048 page so UVs are [0..1]
        // and every underlay strip draws in a single texture batch with zero page swaps.
        var atlasTex = new UndertaleEmbeddedTexture
        {
            Name = data.Strings.MakeString($"Texture {data.EmbeddedTextures.Count}")
        };
        using (var atlasImg = new MagickImage(atlasRgba, new PixelReadSettings(AtlasSize, AtlasSize, StorageType.Char, PixelMapping.RGBA)))
        {
            atlasImg.Format = MagickFormat.Png32;
            atlasTex.TextureData.Image = GMImage.FromMagickImage(atlasImg).ConvertToFormat(GMImage.ImageFormat.Png);
        }
        data.EmbeddedTextures.Add(atlasTex);

        var atlasItem = new UndertaleTexturePageItem
        {
            Name = data.Strings.MakeString($"PageItem {data.TexturePageItems.Count}"),
            TexturePage = atlasTex,
            SourceX = 0,
            SourceY = 0,
            SourceWidth = AtlasSize,
            SourceHeight = AtlasSize,
            TargetX = 0,
            TargetY = 0,
            TargetWidth = AtlasSize,
            TargetHeight = AtlasSize,
            BoundingWidth = AtlasSize,
            BoundingHeight = AtlasSize
        };
        data.TexturePageItems.Add(atlasItem);

        var atlasSprite = new UndertaleSprite
        {
            Name = data.Strings.MakeString("spr_jmk_ocean_underlay_tex"),
            Width = AtlasSize,
            Height = AtlasSize,
            MarginRight = AtlasSize - 1,
            MarginBottom = AtlasSize - 1,
            OriginX = 0,
            OriginY = 0,
            BBoxMode = 2,
            Transparent = false,
            Smooth = false,
            Preload = true,
            SepMasks = UndertaleSprite.SepMaskType.AxisAlignedRect
        };
        atlasSprite.Textures.Add(new UndertaleSprite.TextureEntry { Texture = atlasItem });
        data.Sprites.Add(atlasSprite);
        return ground;

        UndertaleSprite AddGround(string name, int height, bool precise, Func<int, int> topAt, byte[] paddedSlot)
        {
            byte[] rgba = new byte[Width * height * 4];
            byte[] mask = new byte[Width / 8 * height];
            int slotStride = SlotSize * 4;
            for (int x = 0; x < Width; x++)
            {
                int sx = x % CellSize;
                for (int y = topAt(x); y < height; y++)
                {
                    int sy = (y - topAt(x)) % CellSize;
                    int from = (sy + SlotPad) * slotStride + (sx + SlotPad) * 4;
                    int to = (Width * y + x) * 4;
                    paddedSlot.AsSpan(from, 3).CopyTo(rgba.AsSpan(to, 3));
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

    internal static List<byte[]> BuildWangSlots(IReadOnlyList<CobbleMask> rawCobbles, Rgb darkRgb, Rgb lightRgb, int seed)
    {
        if (rawCobbles.Count == 0)
            throw new ArgumentException("At least one cobble mask is required.", nameof(rawCobbles));

        var lib = BuildCobbleLibrary(rawCobbles);
        var edgePool = lib.Where(c => c.Aspect <= 2.2 && c.Solidity >= 0.45 && c.Area >= 900 && Math.Max(c.Width, c.Height) <= 135).ToList();
        if (edgePool.Count == 0) edgePool = lib;
        var largeLib = lib.Where(c => c.Area >= 3200).ToList();
        var medLib = lib.Where(c => c.Area >= 1200 && c.Area < 4500).ToList();
        var smallLib = lib.Where(c => c.Area >= 350 && c.Area < 1600).ToList();
        if (largeLib.Count == 0) largeLib = lib;
        if (medLib.Count == 0) medLib = lib;
        if (smallLib.Count == 0) smallLib = lib;

        var rng = new Random(seed);
        var cornerPlacements = new List<CobblePlacement>();
        var baseOcc = new bool[CellSize * CellSize];
        for (int attempt = 0; attempt < 400; attempt++)
        {
            var c = edgePool[rng.Next(edgePool.Count)];
            int idx = lib.IndexOf(c);
            int px = rng.Next(-40, 41) - c.Width / 2;
            int py = rng.Next(-40, 41) - c.Height / 2;
            if (CanPlace(c, px, py, baseOcc, wrap: true, clipToInterior: false))
            {
                MarkOccupied(c, px, py, baseOcc, wrap: true, MortarGap);
                cornerPlacements.Add(new(idx, px, py));
                if (cornerPlacements.Count >= 4) break;
            }
        }

        var horizPlacements = new List<CobblePlacement>[2];
        var vertPlacements = new List<CobblePlacement>[2];
        for (int e = 0; e < 2; e++)
        {
            horizPlacements[e] = [];
            var hOcc = (bool[])baseOcc.Clone();
            for (int attempt = 0; attempt < 1500; attempt++)
            {
                var c = edgePool[rng.Next(edgePool.Count)];
                int idx = lib.IndexOf(c);
                int cx = rng.Next(30, CellSize - 30);
                int cy = rng.Next(-38, 39);
                int px = cx - c.Width / 2;
                int py = cy - c.Height / 2;
                if (px < EdgeMargin || px + c.Width > CellSize - EdgeMargin) continue;
                if (CanPlace(c, px, py, hOcc, wrap: true, clipToInterior: false))
                {
                    MarkOccupied(c, px, py, hOcc, wrap: true, MortarGap);
                    horizPlacements[e].Add(new(idx, px, py));
                }
            }

            vertPlacements[e] = [];
            var vOcc = (bool[])baseOcc.Clone();
            for (int attempt = 0; attempt < 1500; attempt++)
            {
                var c = edgePool[rng.Next(edgePool.Count)];
                int idx = lib.IndexOf(c);
                int cx = rng.Next(-38, 39);
                int cy = rng.Next(30, CellSize - 30);
                int px = cx - c.Width / 2;
                int py = cy - c.Height / 2;
                if (py < EdgeMargin || py + c.Height > CellSize - EdgeMargin) continue;
                if (CanPlace(c, px, py, vOcc, wrap: true, clipToInterior: false))
                {
                    MarkOccupied(c, px, py, vOcc, wrap: true, MortarGap);
                    vertPlacements[e].Add(new(idx, px, py));
                }
            }
        }

        var slots = new List<byte[]>(VariantCount);
        for (int v = 0; v < VariantCount; v++)
        {
            int top = (v >> 3) & 1;
            int right = (v >> 2) & 1;
            int bottom = (v >> 1) & 1;
            int left = v & 1;

            byte[] slot = new byte[SlotSize * SlotSize * 4];
            for (int i = 0; i < slot.Length; i += 4)
            {
                slot[i + 0] = darkRgb.R;
                slot[i + 1] = darkRgb.G;
                slot[i + 2] = darkRgb.B;
                slot[i + 3] = 255;
            }

            bool[] occ = new bool[CellSize * CellSize];

            void AddBoundary(CobblePlacement p, int ox, int oy)
            {
                var c = lib[p.Index];
                int wx = p.X + ox;
                int wy = p.Y + oy;
                MarkOccupied(c, wx, wy, occ, wrap: false, MortarGap);
                StampCobble(c, wx + SlotPad, wy + SlotPad, slot, SlotSize, SlotSize, lightRgb);
            }

            foreach (var p in cornerPlacements)
            {
                AddBoundary(p, 0, 0);
                AddBoundary(p, CellSize, 0);
                AddBoundary(p, 0, CellSize);
                AddBoundary(p, CellSize, CellSize);
            }
            foreach (var p in horizPlacements[top]) AddBoundary(p, 0, 0);
            foreach (var p in horizPlacements[bottom]) AddBoundary(p, 0, CellSize);
            foreach (var p in vertPlacements[left]) AddBoundary(p, 0, 0);
            foreach (var p in vertPlacements[right]) AddBoundary(p, CellSize, 0);

            var vrng = new Random(seed + v * 997);
            foreach (var (pool, trials) in new[] { (largeLib, 1200), (medLib, 1800), (smallLib, 2600) })
            {
                for (int t = 0; t < trials; t++)
                {
                    var c = pool[vrng.Next(pool.Count)];
                    int maxX = Math.Max(EdgeMargin + 1, CellSize - EdgeMargin - c.Width + 1);
                    int maxY = Math.Max(EdgeMargin + 1, CellSize - EdgeMargin - c.Height + 1);
                    int px = vrng.Next(EdgeMargin, maxX);
                    int py = vrng.Next(EdgeMargin, maxY);
                    if (CanPlace(c, px, py, occ, wrap: false, clipToInterior: true))
                    {
                        MarkOccupied(c, px, py, occ, wrap: false, MortarGap);
                        StampCobble(c, px + SlotPad, py + SlotPad, slot, SlotSize, SlotSize, lightRgb);
                    }
                }
            }

            slots.Add(slot);
        }

        return slots;
    }

    internal static byte[] AssembleAtlas(IReadOnlyList<byte[]> slots)
    {
        if (slots.Count != VariantCount)
            throw new ArgumentException($"Expected {VariantCount} slots.", nameof(slots));

        byte[] atlas = new byte[AtlasSize * AtlasSize * 4];
        int rowBytes = SlotSize * 4;
        for (int v = 0; v < VariantCount; v++)
        {
            int gx = v % AtlasGrid;
            int gy = v / AtlasGrid;
            byte[] src = slots[v];
            for (int y = 0; y < SlotSize; y++)
            {
                int from = y * rowBytes;
                int to = ((gy * SlotSize + y) * AtlasSize + gx * SlotSize) * 4;
                Buffer.BlockCopy(src, from, atlas, to, rowBytes);
            }
        }
        return atlas;
    }

    internal static byte[] RenderPreview3x3(IReadOnlyList<byte[]> slots, int seed, int startCx = 380, int startCy = 25)
    {
        int outSize = CellSize * 3;
        byte[] preview = new byte[outSize * outSize * 4];
        int cellRowBytes = CellSize * 4;
        for (int ry = 0; ry < 3; ry++)
        {
            for (int rx = 0; rx < 3; rx++)
            {
                int cx = startCx + rx;
                int cy = startCy + ry;
                int top = JammChunkUnit(seed, cx + cy * 8192, 91) >= 0.5 ? 1 : 0;
                int bottom = JammChunkUnit(seed, cx + (cy + 1) * 8192, 91) >= 0.5 ? 1 : 0;
                int left = JammChunkUnit(seed, cx + cy * 8192, 92) >= 0.5 ? 1 : 0;
                int right = JammChunkUnit(seed, (cx + 1) + cy * 8192, 92) >= 0.5 ? 1 : 0;
                int v = top * 8 + right * 4 + bottom * 2 + left;
                byte[] slot = slots[v];
                for (int y = 0; y < CellSize; y++)
                {
                    int from = ((y + SlotPad) * SlotSize + SlotPad) * 4;
                    int to = ((ry * CellSize + y) * outSize + rx * CellSize) * 4;
                    Buffer.BlockCopy(slot, from, preview, to, cellRowBytes);
                }
            }
        }
        return preview;
    }

    internal static double JammChunkUnit(double seed, double sampleIndex, double channel)
    {
        const double modulus = 2147483647.0;
        double h = (seed + sampleIndex * 104729.0 + channel * 8191.0) % modulus;
        if (h < 0) h += modulus;
        double low = h % 65521.0;
        h = (h * 48271.0 + low * low) % modulus;
        low = h % 65521.0;
        return ((h * 48271.0 + low * low) % modulus) / modulus;
    }

    private static (Rgb Dark, Rgb Light) SamplePalette(TextureWorker textures, UndertaleSprite primary)
    {
        var tex = primary.Textures[0].Texture;
        using var image = textures.GetTextureFor(tex, primary.Name.Content, includePadding: true);
        using var pixels = image.GetPixels();
        byte[]? rgba = pixels.ToByteArray(0, 0, image.Width, image.Height, PixelMapping.RGBA);
        if (rgba is null || rgba.Length == 0)
            throw new InvalidDataException("Endless Ocean could not read the CaveRockBig texture. Choose a compatible legacy archive.");

        var counts = new Dictionary<Rgb, int>();
        for (int i = 0; i < rgba.Length; i += 4)
        {
            if (rgba[i + 3] < 250) continue;
            var rgb = new Rgb(rgba[i], rgba[i + 1], rgba[i + 2]);
            counts[rgb] = counts.GetValueOrDefault(rgb) + 1;
        }
        if (counts.Count == 0)
            throw new InvalidDataException("Endless Ocean could not read the CaveRockBig texture. Choose a compatible legacy archive.");

        var top = counts.OrderByDescending(kv => kv.Value).Take(2).Select(kv => kv.Key)
            .OrderBy(c => 0.299 * c.R + 0.587 * c.G + 0.114 * c.B).ToArray();
        return top.Length == 1 ? (top[0], top[0]) : (top[0], top[1]);
    }

    private static List<CobbleMask> ExtractCobbles(UndertaleData data, TextureWorker textures, UndertaleSprite primary, Rgb darkRgb, Rgb lightRgb)
    {
        var list = new List<CobbleMask>();
        double baseMidLuma = 0.5 * (Luminance(darkRgb.R, darkRgb.G, darkRgb.B) + Luminance(lightRgb.R, lightRgb.G, lightRgb.B));

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
            byte[]? rgba = pixels.ToByteArray(0, 0, (uint)w, (uint)h, PixelMapping.RGBA);
            if (rgba is null || w < 64 || h < 64)
                continue;

            var lumas = new List<double>(w * h / 4);
            for (int i = 0; i < rgba.Length; i += 16)
            {
                if (rgba[i + 3] >= 250)
                    lumas.Add(Luminance(rgba[i], rgba[i + 1], rgba[i + 2]));
            }
            if (lumas.Count == 0) continue;
            lumas.Sort();
            double dLuma = lumas[lumas.Count / 10];
            double lLuma = lumas[(lumas.Count * 9) / 10];
            double midLuma = 0.5 * (dLuma + lLuma);
            if (lLuma - dLuma < 8.0) continue;
            if (!ReferenceEquals(sprite, primary) && Math.Abs(midLuma - baseMidLuma) > 25.0) continue;

            ExtractComponents(rgba, w, h, dLuma, lLuma, midLuma, list);
        }

        if (list.Count == 0)
            list.AddRange(BuildFallbackCobbles());

        return list;
    }

    private static void ExtractComponents(byte[] rgba, int w, int h, double dLuma, double lLuma, double midLuma, List<CobbleMask> list)
    {
        bool[] vis = new bool[w * h];
        var q = new Queue<int>();
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                int p = y * w + x;
                if (vis[p] || rgba[p * 4 + 3] < 250 || PixelLuma(rgba, p * 4) <= midLuma) continue;

                var pts = new List<int>();
                vis[p] = true;
                q.Enqueue(p);
                int minX = x, maxX = x, minY = y, maxY = y;
                while (q.Count > 0)
                {
                    int cur = q.Dequeue();
                    pts.Add(cur);
                    int cx = cur % w;
                    int cy = cur / w;
                    if (cx < minX) minX = cx;
                    if (cx > maxX) maxX = cx;
                    if (cy < minY) minY = cy;
                    if (cy > maxY) maxY = cy;
                    TryVisit(cx > 0 ? cur - 1 : -1);
                    TryVisit(cx + 1 < w ? cur + 1 : -1);
                    TryVisit(cy > 0 ? cur - w : -1);
                    TryVisit(cy + 1 < h ? cur + w : -1);
                }

                void TryVisit(int nb)
                {
                    if (nb >= 0 && !vis[nb] && rgba[nb * 4 + 3] >= 250 && PixelLuma(rgba, nb * 4) > midLuma)
                    {
                        vis[nb] = true;
                        q.Enqueue(nb);
                    }
                }

                if (pts.Count < 800) continue;

                int bx0 = Math.Max(0, minX - 2);
                int by0 = Math.Max(0, minY - 2);
                int bx1 = Math.Min(w - 1, maxX + 2);
                int by1 = Math.Min(h - 1, maxY + 2);
                int bw = bx1 - bx0 + 1;
                int bh = by1 - by0 + 1;
                byte[] mask = new byte[bw * bh];
                var compSet = new HashSet<int>(pts);
                for (int yy = by0; yy <= by1; yy++)
                {
                    for (int xx = bx0; xx <= bx1; xx++)
                    {
                        int idx = yy * w + xx;
                        if (compSet.Contains(idx))
                        {
                            mask[(yy - by0) * bw + (xx - bx0)] = 255;
                        }
                        else if (rgba[idx * 4 + 3] >= 250)
                        {
                            bool adj = (xx > 0 && compSet.Contains(idx - 1)) || (xx + 1 < w && compSet.Contains(idx + 1))
                                    || (yy > 0 && compSet.Contains(idx - w)) || (yy + 1 < h && compSet.Contains(idx + w));
                            if (adj)
                            {
                                double t = Math.Clamp((PixelLuma(rgba, idx * 4) - dLuma) / Math.Max(1.0, lLuma - dLuma), 0.0, 1.0);
                                mask[(yy - by0) * bw + (xx - bx0)] = (byte)Math.Round(t * 255);
                            }
                        }
                    }
                }
                double aspect = Math.Max((double)bw / bh, (double)bh / bw);
                double solidity = (double)pts.Count / (bw * bh);
                list.Add(new(bw, bh, mask, pts.Count, solidity, aspect));
            }
        }
    }

    private static List<CobbleMask> BuildFallbackCobbles()
    {
        var list = new List<CobbleMask>();
        foreach (var (w, h) in new[] { (180, 140), (140, 120), (110, 90) })
        {
            byte[] mask = new byte[w * h];
            int area = 0;
            double rx = w * 0.45, ry = h * 0.45;
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    double dx = (x - w * 0.5) / rx;
                    double dy = (y - h * 0.5) / ry;
                    if (dx * dx + dy * dy <= 1.0)
                    {
                        mask[y * w + x] = 255;
                        area++;
                    }
                }
            }
            list.Add(new(w, h, mask, area, (double)area / (w * h), (double)w / h));
        }
        return list;
    }

    private static List<CobbleMask> BuildCobbleLibrary(IReadOnlyList<CobbleMask> rawCobbles)
    {
        var lib = new List<CobbleMask>(rawCobbles.Count * 4);
        foreach (var c in rawCobbles)
        {
            int maxD = Math.Max(c.Width, c.Height);
            double s = maxD > 250 ? 150.0 / maxD : (maxD > 140 ? 0.65 : 0.80);
            lib.Add(TransformCobble(c, s, 0, false));
            lib.Add(TransformCobble(c, s * 0.88, 1, true));
            lib.Add(TransformCobble(c, s * 0.72, 2, false));
            lib.Add(TransformCobble(c, s * 0.58, 3, true));
        }
        return lib.OrderByDescending(c => c.Area).ToList();
    }

    private static CobbleMask TransformCobble(CobbleMask src, double scale, int rot90, bool flipX)
    {
        int sw = Math.Max(12, (int)Math.Round(src.Width * scale));
        int sh = Math.Max(12, (int)Math.Round(src.Height * scale));
        byte[] scaled = new byte[sw * sh];
        for (int y = 0; y < sh; y++)
        {
            double sy = (y + 0.5) * src.Height / sh - 0.5;
            int y0 = Math.Clamp((int)Math.Floor(sy), 0, src.Height - 1);
            int y1 = Math.Clamp(y0 + 1, 0, src.Height - 1);
            double fy = Math.Clamp(sy - y0, 0, 1);
            for (int x = 0; x < sw; x++)
            {
                double sx = (x + 0.5) * src.Width / sw - 0.5;
                int x0 = Math.Clamp((int)Math.Floor(sx), 0, src.Width - 1);
                int x1 = Math.Clamp(x0 + 1, 0, src.Width - 1);
                double fx = Math.Clamp(sx - x0, 0, 1);
                double a0 = src.Alpha[y0 * src.Width + x0] * (1 - fx) + src.Alpha[y0 * src.Width + x1] * fx;
                double a1 = src.Alpha[y1 * src.Width + x0] * (1 - fx) + src.Alpha[y1 * src.Width + x1] * fx;
                scaled[y * sw + x] = (byte)Math.Round(a0 * (1 - fy) + a1 * fy);
            }
        }

        int rw = (rot90 & 1) == 1 ? sh : sw;
        int rh = (rot90 & 1) == 1 ? sw : sh;
        byte[] outA = new byte[rw * rh];
        int area = 0;
        for (int y = 0; y < rh; y++)
        {
            for (int x = 0; x < rw; x++)
            {
                int sx = rot90 switch { 1 => y, 2 => sw - 1 - x, 3 => sw - 1 - y, _ => x };
                int sy = rot90 switch { 1 => sh - 1 - x, 2 => sh - 1 - y, 3 => x, _ => y };
                if (flipX) sx = sw - 1 - sx;
                byte v = scaled[sy * sw + sx];
                outA[y * rw + x] = v;
                if (v >= 128) area++;
            }
        }
        double aspect = Math.Max((double)rw / rh, (double)rh / rw);
        double solidity = (double)area / (rw * rh);
        return new(rw, rh, outA, area, solidity, aspect);
    }

    private static bool CanPlace(CobbleMask c, int px, int py, bool[] occ, bool wrap, bool clipToInterior)
    {
        if (clipToInterior && (px < EdgeMargin || py < EdgeMargin || px + c.Width > CellSize - EdgeMargin || py + c.Height > CellSize - EdgeMargin))
            return false;
        for (int y = 0; y < c.Height; y++)
        {
            for (int x = 0; x < c.Width; x++)
            {
                if (c.Alpha[y * c.Width + x] < 96) continue;
                int gx = px + x;
                int gy = py + y;
                if (wrap)
                {
                    gx = ((gx % CellSize) + CellSize) % CellSize;
                    gy = ((gy % CellSize) + CellSize) % CellSize;
                }
                else if ((uint)gx >= CellSize || (uint)gy >= CellSize)
                {
                    continue;
                }
                if (occ[gy * CellSize + gx]) return false;
            }
        }
        return true;
    }

    private static void MarkOccupied(CobbleMask c, int px, int py, bool[] occ, bool wrap, int gap)
    {
        int gapSq = gap * gap;
        for (int y = 0; y < c.Height; y++)
        {
            for (int x = 0; x < c.Width; x++)
            {
                if (c.Alpha[y * c.Width + x] < 96) continue;
                bool inner = x > 0 && y > 0 && x + 1 < c.Width && y + 1 < c.Height
                    && c.Alpha[(y - 1) * c.Width + x] >= 96 && c.Alpha[(y + 1) * c.Width + x] >= 96
                    && c.Alpha[y * c.Width + (x - 1)] >= 96 && c.Alpha[y * c.Width + (x + 1)] >= 96;
                if (inner)
                {
                    int gx = px + x;
                    int gy = py + y;
                    if (wrap)
                    {
                        gx = ((gx % CellSize) + CellSize) % CellSize;
                        gy = ((gy % CellSize) + CellSize) % CellSize;
                    }
                    if ((uint)gx < CellSize && (uint)gy < CellSize)
                        occ[gy * CellSize + gx] = true;
                    continue;
                }
                for (int dy = -gap; dy <= gap; dy++)
                {
                    for (int dx = -gap; dx <= gap; dx++)
                    {
                        if (dx * dx + dy * dy > gapSq) continue;
                        int gx = px + x + dx;
                        int gy = py + y + dy;
                        if (wrap)
                        {
                            gx = ((gx % CellSize) + CellSize) % CellSize;
                            gy = ((gy % CellSize) + CellSize) % CellSize;
                        }
                        if ((uint)gx < CellSize && (uint)gy < CellSize)
                            occ[gy * CellSize + gx] = true;
                    }
                }
            }
        }
    }

    private static void StampCobble(CobbleMask c, int px, int py, byte[] canvas, int cw, int ch, Rgb lightRgb)
    {
        for (int y = 0; y < c.Height; y++)
        {
            int gy = py + y;
            if ((uint)gy >= (uint)ch) continue;
            for (int x = 0; x < c.Width; x++)
            {
                int gx = px + x;
                if ((uint)gx >= (uint)cw) continue;
                byte a = c.Alpha[y * c.Width + x];
                if (a == 0) continue;
                int di = (gy * cw + gx) * 4;
                double t = a / 255.0;
                canvas[di + 0] = (byte)Math.Round(canvas[di + 0] * (1 - t) + lightRgb.R * t);
                canvas[di + 1] = (byte)Math.Round(canvas[di + 1] * (1 - t) + lightRgb.G * t);
                canvas[di + 2] = (byte)Math.Round(canvas[di + 2] * (1 - t) + lightRgb.B * t);
            }
        }
    }

    private static double PixelLuma(byte[] rgba, int i) => Luminance(rgba[i], rgba[i + 1], rgba[i + 2]);
    private static double Luminance(byte r, byte g, byte b) => 0.299 * r + 0.587 * g + 0.114 * b;
}
