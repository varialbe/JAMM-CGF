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
}
