using System.Numerics;
using System.Runtime.InteropServices;
using ImageMagick;
using UndertaleModLib;
using UndertaleModLib.Models;
using UndertaleModLib.Util;

namespace Jamm.Core.Building;

internal static class TextureRepacker
{
    internal const string Revision = "texture-repack-2";
    private const int PageSize = 2048;
    private const int Border = 2;
    private const long BatchBytes = 128 * 1024 * 1024;

    internal static bool HasAtlas(UndertaleData data) =>
        data.EmbeddedTextures is { Count: > 0 } || data.TexturePageItems is { Count: > 0 };

    internal static string RepackForBuild(UndertaleData data, string output, Action<string>? progress, CancellationToken cancellationToken)
    {
        try { return Repack(data, progress, cancellationToken); }
        catch (Exception ex) when (ex is not (OutOfMemoryException or OperationCanceledException))
        {
            throw new InvalidDataException($"Could not repack textures while building '{Path.GetFileName(output)}'. Retry with --no-repack or turn repacking off for the profile. {ex.Message}", ex);
        }
    }

    internal static HashSet<UndertaleTexturePageItem> ReferencedItems(UndertaleData data)
    {
        // These are the TPAG pointer owners in UndertaleModLib. Room tiles and layers
        // resolve through sprites/backgrounds, while Spine and SWF store their own images.
        return data.Sprites.SelectMany(sprite => sprite.Textures.Select(entry => entry?.Texture))
            .Concat(data.Backgrounds.Select(background => background.Texture))
            .Concat(data.Fonts.Select(font => font.Texture))
            .Concat((data.EmbeddedImages ?? []).Select(image => image.TextureEntry))
            .OfType<UndertaleTexturePageItem>().ToHashSet<UndertaleTexturePageItem>(ReferenceEqualityComparer.Instance);
    }

    internal static string Repack(UndertaleData data, Action<string>? progress, CancellationToken cancellationToken)
    {
        if (data.IsVersionAtLeast(2) || data.TextureGroupInfo is not null || data.EmbeddedTextures.Any(p => p.TextureExternal))
            return "[CGF-TEX-001] Texture repack skipped for this archive format. Repacking requires embedded GameMaker 1.x pages without texture groups.";

        var sourcePages = data.EmbeddedTextures.ToArray();
        long beforeBytes = sourcePages.Sum(PageBytes);
        var referenced = ReferencedItems(data);
        var live = data.TexturePageItems.Where(referenced.Contains).ToArray();
        var liveRegions = live.Where(item => item.TexturePage is not null).Select(SourceRegion.From).ToHashSet();
        int unused = data.TexturePageItems.Where(item => item.TexturePage is not null && !referenced.Contains(item))
            .Select(SourceRegion.From).Distinct().Count(region => !liveRegions.Contains(region));
        var byPage = live.Where(item => item.TexturePage is not null).ToLookup(item => item.TexturePage);
        List<UndertaleEmbeddedTexture> preserved = [];
        Dictionary<(uint Scaled, int Width, int Height, string Hash), Region> unique = [];
        Dictionary<SourceRegion, Region> placements = [];
        int duplicates = 0;

        progress?.Invoke($"Repacking {sourcePages.Length} texture pages");
        foreach (var page in sourcePages)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var items = byPage[page];
            if (!items.Any()) continue;
            var image = page.TextureData.Image;
            if (items.Any(item => !CanMove(item, image)))
            {
                // Keep the original sampler behavior for malformed rectangles and placeholders.
                preserved.Add(page);
                continue;
            }
            var pixels = ContentFingerprints.DecodeTexture(image);
            foreach (var item in items)
            {
                var source = SourceRegion.From(item);
                if (placements.ContainsKey(source)) continue;
                var key = (page.Scaled, source.Width, source.Height, ContentFingerprints.HashTextureRegion(pixels, item));
                if (!unique.TryGetValue(key, out var region))
                {
                    region = new(source);
                    unique.Add(key, region);
                }
                else duplicates++;
                placements.Add(source, region);
            }
        }

        var layouts = Pack(unique.Values);
        // Negative IDs are reserved by the TPAG format. Keep the input atlas if splitting
        // very large items would make the result exceed the legacy page index range.
        if (preserved.Count + layouts.Count > short.MaxValue + 1)
            return "[CGF-TEX-001] Texture repack skipped because the result would exceed the legacy texture page limit.";

        Render(layouts, sourcePages, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        foreach (var item in live)
        {
            if (item.TexturePage is null || !placements.TryGetValue(SourceRegion.From(item), out var region)) continue;
            item.TexturePage = region.Output.Texture;
            item.SourceX = (ushort)(region.X + region.Output.PaddingX);
            item.SourceY = (ushort)(region.Y + region.Output.PaddingY);
        }
        data.TexturePageItems.Clear();
        foreach (var item in live) data.TexturePageItems.Add(item);
        data.EmbeddedTextures.Clear();
        foreach (var page in preserved) data.EmbeddedTextures.Add(page);
        foreach (var layout in layouts) data.EmbeddedTextures.Add(layout.Texture);

        long afterBytes = data.EmbeddedTextures.Sum(PageBytes);
        string decision = FormattableString.Invariant($"[CGF-TEX-001] Texture pages: {sourcePages.Length} -> {data.EmbeddedTextures.Count}, {beforeBytes / 1_000_000d:F0} MB -> {afterBytes / 1_000_000d:F0} MB ({duplicates} duplicates merged, {unused} unused regions dropped)");
        if (preserved.Count > 0)
            decision += $". Preserved {preserved.Count} source pages with empty or out-of-bounds rectangles.";
        return decision;
    }

    private static bool CanMove(UndertaleTexturePageItem item, GMImage image) =>
        item.SourceWidth > 0 && item.SourceHeight > 0 &&
        item.SourceX + item.SourceWidth <= image.Width && item.SourceY + item.SourceHeight <= image.Height;

    private static long PageBytes(UndertaleEmbeddedTexture page) => ImageBytes(page.TextureData.Image.Width, page.TextureData.Image.Height);

    private static long ImageBytes(int width, int height) => (long)width * height * 4;

    private static List<Layout> Pack(IEnumerable<Region> regions)
    {
        List<Layout> layouts = [];
        foreach (var group in regions.GroupBy(region => region.Source.Page.Scaled))
        {
            List<Layout> shared = [];
            foreach (var region in group.OrderByDescending(r => Math.Max(r.Source.Width, r.Source.Height))
                .ThenByDescending(r => r.Source.Width * r.Source.Height))
            {
                if (region.Source.Width + 2 * Border > PageSize || region.Source.Height + 2 * Border > PageSize)
                {
                    // Use a dedicated page. When a region spans an axis of the old page,
                    // the new page edge retains the runner's clamping along that axis.
                    var image = region.Source.Page.TextureData.Image;
                    int padX = Math.Min(Border, (image.Width - region.Source.Width) / 2);
                    int padY = Math.Min(Border, (image.Height - region.Source.Height) / 2);
                    var dedicated = new Layout(group.Key, padX, padY, dedicated: true);
                    dedicated.Place(region, 0, 0);
                    layouts.Add(dedicated);
                    continue;
                }
                if (shared.Any(layout => layout.TryPlace(region))) continue;
                var newPage = new Layout(group.Key, Border, Border);
                newPage.TryPlace(region);
                shared.Add(newPage);
                layouts.Add(newPage);
            }
        }
        return layouts;
    }

    private static void Render(List<Layout> layouts, UndertaleEmbeddedTexture[] sources, CancellationToken cancellationToken)
    {
        // Bound decoded output memory by building batches, with one decoded source
        // page at a time. A dedicated page larger than the budget forms its own batch.
        int next = 0;
        while (next < layouts.Count)
        {
            cancellationToken.ThrowIfCancellationRequested();
            List<Layout> batch = [];
            long bytes = 0;
            while (next < layouts.Count)
            {
                var layout = layouts[next];
                if (layout.ReuseWholePage())
                {
                    next++;
                    continue;
                }
                long cost = ImageBytes(layout.Width, layout.Height);
                if (batch.Count > 0 && bytes + cost > BatchBytes) break;
                layout.Pixels = new GMImage(layout.Width, layout.Height);
                batch.Add(layout);
                bytes += cost;
                next++;
            }
            var inputs = batch.SelectMany(layout => layout.Regions).ToLookup(region => region.Source.Page);
            foreach (var page in sources)
            {
                if (!inputs.Contains(page)) continue;
                cancellationToken.ThrowIfCancellationRequested();
                var pixels = ContentFingerprints.DecodeTexture(page.TextureData.Image);
                foreach (var region in inputs[page])
                    CopyPixels(pixels, region);
            }
            foreach (var layout in batch)
            {
                cancellationToken.ThrowIfCancellationRequested();
                layout.Texture.TextureData.Image = EncodePage(layout.Pixels!);
                layout.Pixels = null;
            }
        }
    }

    private static GMImage EncodePage(GMImage pixels)
    {
        using var png = pixels.GetMagickImage();
        png.Format = MagickFormat.Png32;
        png.Settings.SetDefine(MagickFormat.Png32, "compression-level", 4);
        png.Settings.SetDefine(MagickFormat.Png32, "compression-filter", 5);
        // Default DEFLATE can match repeated scanline bytes. UTML's Huffman-only
        // setting cannot, which makes the new atlas pages much larger on disk.
        png.Settings.SetDefine(MagickFormat.Png32, "compression-strategy", 0);
        return GMImage.FromPng(png.ToByteArray());
    }

    private static void CopyPixels(GMImage image, Region region)
    {
        var source = region.Source;
        var output = region.Output;
        int padX = output.PaddingX, padY = output.PaddingY;
        var input = MemoryMarshal.Cast<byte, uint>(image.GetRawImageData());
        var destination = MemoryMarshal.Cast<byte, uint>(output.Pixels!.GetRawImageData());
        for (int y = -padY; y < source.Height + padY; y++)
        {
            var row = destination.Slice((region.Y + y + padY) * output.Width + region.X, source.Width + 2 * padX);
            if (y >= 0 && y < source.Height)
            {
                input.Slice((source.Y + y) * image.Width + source.X, source.Width).CopyTo(row[padX..]);
                for (int x = 0; x < padX; x++)
                {
                    row[x] = GutterPixel(input, image, source, x - padX, y);
                    row[padX + source.Width + x] = GutterPixel(input, image, source, source.Width + x, y);
                }
            }
            else
            {
                for (int x = 0; x < row.Length; x++)
                    row[x] = GutterPixel(input, image, source, x - padX, y);
            }
        }
    }

    private static uint GutterPixel(ReadOnlySpan<uint> input, GMImage image, SourceRegion region, int x, int y)
    {
        uint edge = input[(region.Y + Math.Clamp(y, 0, region.Height - 1)) * image.Width +
            region.X + Math.Clamp(x, 0, region.Width - 1)];
        int sourceX = region.X + x, sourceY = region.Y + y;
        // The legacy atlas has two extruded or transparent-black texels around each
        // edge. Preserve empty gutters and keep neighboring art out of the filter.
        if ((uint)sourceX < image.Width && (uint)sourceY < image.Height && input[sourceY * image.Width + sourceX] == 0)
            return 0;
        return edge;
    }

    private readonly record struct SourceRegion(UndertaleEmbeddedTexture Page, int X, int Y, int Width, int Height)
    {
        internal static SourceRegion From(UndertaleTexturePageItem item) =>
            new(item.TexturePage, item.SourceX, item.SourceY, item.SourceWidth, item.SourceHeight);
    }

    private sealed class Region(SourceRegion source)
    {
        internal SourceRegion Source { get; } = source;
        internal Layout Output { get; set; } = null!;
        internal int X { get; set; }
        internal int Y { get; set; }
    }

    private sealed class Layout(uint scaled, int paddingX, int paddingY, bool dedicated = false)
    {
        private readonly TextureSkyline? skyline = dedicated ? null : new(PageSize);
        private int usedWidth;
        private int usedHeight;

        internal List<Region> Regions { get; } = [];
        internal int PaddingX { get; } = paddingX;
        internal int PaddingY { get; } = paddingY;
        internal int Width => dedicated ? usedWidth : (int)BitOperations.RoundUpToPowerOf2((uint)usedWidth);
        internal int Height => dedicated ? usedHeight : (int)BitOperations.RoundUpToPowerOf2((uint)usedHeight);
        internal UndertaleEmbeddedTexture Texture { get; private set; } = new() { Scaled = scaled };
        internal GMImage? Pixels { get; set; }

        internal bool TryPlace(Region region)
        {
            if (!skyline!.TryPlace(region.Source.Width + 2 * PaddingX, region.Source.Height + 2 * PaddingY, out int x, out int y))
                return false;
            Place(region, x, y);
            return true;
        }

        internal void Place(Region region, int x, int y)
        {
            region.Output = this;
            region.X = x;
            region.Y = y;
            Regions.Add(region);
            usedWidth = Math.Max(usedWidth, x + region.Source.Width + 2 * PaddingX);
            usedHeight = Math.Max(usedHeight, y + region.Source.Height + 2 * PaddingY);
        }

        internal bool ReuseWholePage()
        {
            if (!dedicated || PaddingX != 0 || PaddingY != 0) return false;
            var source = Regions[0].Source;
            var image = source.Page.TextureData.Image;
            if (source.Width != image.Width || source.Height != image.Height) return false;
            Texture = source.Page;
            return true;
        }
    }
}
