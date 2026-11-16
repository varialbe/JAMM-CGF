using System.Text.Json;
using UndertaleModLib;
using UndertaleModLib.Models;

namespace Jamm.ModMaker;

/// <summary>Exports texture pages for upscaling and decides which pages can safely be upscaled.</summary>
public static class TexturePages
{
    /// <summary>Writes page_N.png for every texture page plus pages.json describing what each page holds.</summary>
    public static void Export(UndertaleData data, string directory)
    {
        Directory.CreateDirectory(directory);
        var info = Describe(data);
        for (int i = 0; i < data.EmbeddedTextures.Count; i++)
        {
            using var file = File.Create(Path.Combine(directory, $"page_{i}.png"));
            data.EmbeddedTextures[i].TextureData.Image.SavePng(file);
        }
        File.WriteAllText(Path.Combine(directory, "pages.json"), JsonSerializer.Serialize(info, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"Exported {data.EmbeddedTextures.Count} pages to {directory}; {info.Count(p => p.Upscale)} can be upscaled.");
    }

    /// <summary>Page dimensions, usage counts, and eligibility for source-only scaling.</summary>
    public sealed record PageInfo(int Index, int Width, int Height, int Items, int Fonts, int TileBackgrounds, int Backgrounds, bool Upscale);

    /// <summary>
    /// A page is upscaled unless it holds font glyphs (glyph rectangles are in page pixels and also give
    /// the drawn size) or backgrounds used as room tiles (tile rectangles are in source pixels too).
    /// </summary>
    public static List<PageInfo> Describe(UndertaleData data)
    {
        var fontItems = data.Fonts.Where(f => f.Texture is not null).Select(f => f.Texture).ToHashSet();
        var tiled = data.Rooms.SelectMany(r => r.Tiles).Select(t => t.BackgroundDefinition?.Texture).Where(t => t is not null).ToHashSet();
        var backgrounds = data.Backgrounds.Where(b => b.Texture is not null).Select(b => b.Texture).ToHashSet();
        var result = new List<PageInfo>();
        for (int i = 0; i < data.EmbeddedTextures.Count; i++)
        {
            var page = data.EmbeddedTextures[i];
            var items = data.TexturePageItems.Where(t => t.TexturePage == page).ToList();
            int fonts = items.Count(fontItems.Contains), tiles = items.Count(tiled.Contains), bgs = items.Count(backgrounds.Contains);
            result.Add(new(i, page.TextureData.Image.Width, page.TextureData.Image.Height, items.Count, fonts, tiles, bgs, fonts == 0 && tiles == 0));
        }
        return result;
    }
    /// <summary>Original texture layout and font metadata used to check the serialized HD archive.</summary>
    public sealed record HdBaseline(List<PageInfo> Pages, int[][] Items, string[] PageHashes, string[] Fonts);

    /// <summary>Captures only metadata and hashes; no extracted game assets are written.</summary>
    public static HdBaseline Capture(UndertaleData data) => new(Describe(data),
        data.TexturePageItems.Select(t => Rectangle(data, t)).ToArray(),
        data.EmbeddedTextures.Select(t => ImageHash(t)).ToArray(), FontSignatures(data));

    private static int[] Rectangle(UndertaleData data, UndertaleTexturePageItem t) =>
        [data.EmbeddedTextures.IndexOf(t.TexturePage), t.SourceX, t.SourceY, t.SourceWidth, t.SourceHeight,
            t.TargetX, t.TargetY, t.TargetWidth, t.TargetHeight, t.BoundingWidth, t.BoundingHeight];

    private static string ImageHash(UndertaleEmbeddedTexture page)
    {
        using var stream = new MemoryStream();
        page.TextureData.Image.SavePng(stream);
        return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(stream.ToArray()));
    }

    private static string[] FontSignatures(UndertaleData data) => data.Fonts.Select(f => JsonSerializer.Serialize(new
    {
        Name = f.Name?.Content, DisplayName = f.DisplayName?.Content,
        f.EmSizeIsFloat, f.EmSize, f.Bold, f.Italic, f.RangeStart, f.RangeEnd, f.Charset, f.AntiAliasing,
        Texture = data.TexturePageItems.IndexOf(f.Texture), f.ScaleX, f.ScaleY, f.Ascender, f.AscenderOffset,
        f.SDFSpread, f.LineHeight, f.Glyphs
    })).ToArray();

    /// <summary>Checks every saved page, TPAG rectangle, and font; reports uncompressed RGBA8 memory.</summary>
    public static void VerifyHd(HdBaseline original, UndertaleData output, bool upscaled)
    {
        var pages = Describe(output);
        if (pages.Count != original.Pages.Count || output.TexturePageItems.Count != original.Items.Length)
            throw new InvalidDataException("HD verification failed: page or TPAG count changed.");
        long before = 0, after = 0;
        foreach (var page in original.Pages)
        {
            int factor = upscaled && page.Upscale ? 2 : 1;
            if (pages[page.Index].Width != page.Width * factor || pages[page.Index].Height != page.Height * factor)
                throw new InvalidDataException($"HD verification failed: page {page.Index} has an unexpected size.");
            if (factor == 1 && ImageHash(output.EmbeddedTextures[page.Index]) != original.PageHashes[page.Index])
                throw new InvalidDataException($"HD verification failed: protected page {page.Index} changed.");
            before += (long)page.Width * page.Height * 4;
            after += (long)pages[page.Index].Width * pages[page.Index].Height * 4;
        }
        for (int i = 0; i < original.Items.Length; i++)
        {
            var expected = (int[])original.Items[i].Clone();
            int factor = upscaled && original.Pages[expected[0]].Upscale ? 2 : 1;
            for (int j = 1; j <= 4; j++) expected[j] *= factor;
            if (!expected.SequenceEqual(Rectangle(output, output.TexturePageItems[i])))
                throw new InvalidDataException($"HD verification failed: TPAG item {i} source, target or bounding rectangle differs.");
        }
        if (!original.Fonts.SequenceEqual(FontSignatures(output)))
            throw new InvalidDataException("HD verification failed: font metadata or glyphs changed.");
        Console.WriteLine($"HD offline verification passed: {pages.Count} pages, {original.Items.Length} TPAG items, {original.Fonts.Length} fonts unchanged.");
        Console.WriteLine($"RGBA8 texture residency: {before / 1048576.0:F0} -> {after / 1048576.0:F0} MiB (+{(after - before) / 1048576.0:F0} MiB). " +
            "Budget the same again if the runner retains decoded CPU copies; excludes MSAA, back buffers, audio and other game memory.");
    }
}
