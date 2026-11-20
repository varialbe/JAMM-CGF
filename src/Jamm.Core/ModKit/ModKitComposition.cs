using System.Globalization;
using System.Text.Json;
using System.Xml;
using System.Xml.Linq;
using ImageMagick;
using UndertaleModLib;
using UndertaleModLib.Models;
using UndertaleModLib.Util;

namespace Jamm.Core.ModKit;

/// <summary>A pixel rectangle in an untrimmed source frame.</summary>
public sealed record ModKitCrop(int X, int Y, int W, int H);

/// <summary>Maps source luminance ranges to a small set of flat RGB swatches.</summary>
public sealed record ModKitSpriteTones(IReadOnlyList<int> Colors, IReadOnlyList<int> Cutoffs);

/// <summary>A layer sampled from the base archive or an original package asset.</summary>
public sealed record ModKitSpriteLayer(string? From, string? File, int Frame, ModKitCrop? Crop,
    int X, int Y, double ScaleX, double ScaleY, double Rotation, string Flip,
    int? Tint, double HueShift, IReadOnlyDictionary<int, int> PaletteMap, double Opacity, int Z)
{
    /// <summary>Optional flat palette applied to the transformed layer while retaining its alpha.</summary>
    public ModKitSpriteTones? Tones { get; init; }

    /// <summary>Optional subpixel alpha smoothing after geometry transforms.</summary>
    public double EdgeSoftness { get; init; }
}

/// <summary>A fixed-size sprite canvas with an ordered composition for each frame.</summary>
public sealed record ModKitSprite(string Key, int Width, int Height, int[]? Origin,
    IReadOnlyList<IReadOnlyList<ModKitSpriteLayer>> Frames);

public static partial class ModKitPackage
{
    private static ModKitSprite ParseSprite(string root, JsonElement element, string path)
    {
        var obj = ExpectObject(element, path);
        string key = RequireKey(obj, "key", $"{path}.key");
        int width = RequireInt(obj, "width", $"{path}.width", 1);
        int height = RequireInt(obj, "height", $"{path}.height", 1);
        if (width > 2044 || height > 2044)
            throw Error(path, "Sprite width and height must each be at most 2044 pixels.");
        if (!TryGetNonNull(obj, "compose", out var compose) || compose.ValueKind != JsonValueKind.Array || compose.GetArrayLength() == 0)
            throw Error($"{path}.compose", "Choose a non-empty list of layers or one layer list per frame.");
        var frames = compose[0].ValueKind == JsonValueKind.Array
            ? compose.EnumerateArray().Select((frame, i) => ParseLayers(frame, $"{path}.compose[{i}]")).ToArray()
            : [ParseLayers(compose, $"{path}.compose")];
        return new(key, width, height, OptionalOrigin(obj, "origin", $"{path}.origin"), frames);

        IReadOnlyList<ModKitSpriteLayer> ParseLayers(JsonElement frame, string layerPath)
        {
            if (frame.ValueKind != JsonValueKind.Array || frame.GetArrayLength() == 0)
                throw Error(layerPath, "Every frame must contain at least one layer.");
            return frame.EnumerateArray().Select((layer, i) => ParseLayer(root, layer, $"{layerPath}[{i}]")).ToArray();
        }
    }

    private static ModKitSpriteLayer ParseLayer(string root, JsonElement element, string path)
    {
        var obj = ExpectObject(element, path);
        string? from = TryGetNonNull(obj, "from", out _) ? RequireNonEmptyString(obj, "from", $"{path}.from") : null;
        string? file = OptionalAssetFile(root, obj, "file", $"{path}.file", [".png", ".svg"]);
        if ((from is null) == (file is null))
            throw Error(path, "Choose either 'from' for a base sprite or 'file' for an original PNG or SVG.");
        int frame = OptionalInt(obj, "frame", $"{path}.frame", 0, 0);
        if (file is not null && frame != 0)
            throw Error($"{path}.frame", "Only a 'from' layer can select a base sprite frame.");
        ModKitCrop? crop = null;
        if (TryGetNonNull(obj, "crop", out var cropEl))
        {
            var rect = ExpectObject(cropEl, $"{path}.crop");
            crop = new(RequireInt(rect, "x", $"{path}.crop.x", 0), RequireInt(rect, "y", $"{path}.crop.y", 0),
                RequireInt(rect, "w", $"{path}.crop.w", 1), RequireInt(rect, "h", $"{path}.crop.h", 1));
        }
        int x = OptionalInt(obj, "x", $"{path}.x", 0);
        int y = OptionalInt(obj, "y", $"{path}.y", 0);
        double sx = OptionalDouble(obj, "scaleX", $"{path}.scaleX", 1, 0.001, 100);
        double sy = OptionalDouble(obj, "scaleY", $"{path}.scaleY", 1, 0.001, 100);
        double rotation = OptionalDouble(obj, "rotation", $"{path}.rotation", 0, -360, 360);
        string flip = OptionalString(obj, "flip", $"{path}.flip", "none");
        if (flip is not ("none" or "x" or "y" or "both"))
            throw Error($"{path}.flip", "Choose 'none', 'x', 'y' or 'both'.");
        int? tint = TryGetNonNull(obj, "tint", out var tintEl) ? ParseColor(tintEl.ValueKind == JsonValueKind.String ? tintEl.GetString() : null, $"{path}.tint") : null;
        double hue = OptionalDouble(obj, "hueShift", $"{path}.hueShift", 0, -360, 360);
        Dictionary<int, int> palette = [];
        if (TryGetNonNull(obj, "paletteMap", out var paletteEl))
            foreach (var pair in ExpectObject(paletteEl, $"{path}.paletteMap").EnumerateObject())
                if (!palette.TryAdd(ParseColor(pair.Name, $"{path}.paletteMap"), ParseColor(pair.Value.ValueKind == JsonValueKind.String
                    ? pair.Value.GetString() : null, $"{path}.paletteMap")))
                    throw Error($"{path}.paletteMap", $"Source color '{pair.Name}' is listed twice.");
        return new(from, file, frame, crop, x, y, sx, sy, rotation, flip, tint, hue, palette,
            OptionalDouble(obj, "opacity", $"{path}.opacity", 1, 0, 1), OptionalInt(obj, "z", $"{path}.z", 0))
        {
            Tones = TryGetNonNull(obj, "tones", out var tones) ? ParseTones(tones, $"{path}.tones") : null,
            EdgeSoftness = OptionalDouble(obj, "edgeSoftness", $"{path}.edgeSoftness", 0, 0, 1.5)
        };
    }

    private static ModKitSpriteTones ParseTones(JsonElement value, string path)
    {
        var obj = ExpectObject(value, path);
        var colors = ParseArray(obj, "colors", $"{path}.colors", (color, at) =>
            ParseColor(color.ValueKind == JsonValueKind.String ? color.GetString() : null, at));
        var cutoffs = ParseArray(obj, "cutoffs", $"{path}.cutoffs", (cutoff, at) =>
            cutoff.ValueKind == JsonValueKind.Number && cutoff.TryGetInt32(out int number) && number is >= 1 and <= 255
                ? number : throw Error(at, "Use an integer from 1 to 255."));
        if (colors.Count is not (2 or 3) || cutoffs.Count != colors.Count - 1 || (cutoffs.Count == 2 && cutoffs[0] >= cutoffs[1]))
            throw Error(path, "Choose two or three colors and one ascending cutoff between each pair.");
        return new(colors, cutoffs);
    }

    private static int ParseColor(string? value, string path) =>
        value is { Length: 7 } && value[0] == '#' && int.TryParse(value.AsSpan(1), NumberStyles.AllowHexSpecifier,
            CultureInfo.InvariantCulture, out int color) ? color : throw Error(path, "Use a #RRGGBB color.");
}

internal sealed class ModKitComposition(UndertaleData data) : IDisposable
{
    private readonly TextureWorker textures = new();

    internal MagickImage Render(ModKitSprite recipe, int frame)
    {
        var canvas = new MagickImage(MagickColors.Transparent, (uint)recipe.Width, (uint)recipe.Height);
        canvas.ColorType = ColorType.TrueColorAlpha;
        try
        {
            foreach (var (layer, index) in recipe.Frames[frame].Select((layer, i) => (layer, i)).OrderBy(p => p.layer.Z))
            {
                try
                {
                    using var image = Load(layer);
                    Transform(image, layer);
                    // Use wider sums so off-canvas positions cannot wrap.
                    if ((long)layer.X + image.Width > 0 && (long)layer.Y + image.Height > 0
                        && layer.X < recipe.Width && layer.Y < recipe.Height)
                        canvas.Composite(image, layer.X, layer.Y, CompositeOperator.Over);
                }
                catch (Exception ex) when (ex is InvalidDataException or MagickException or IOException or XmlException)
                {
                    throw new InvalidDataException($"Sprite recipe '{recipe.Key}', output frame {frame}, layer {index}: {ex.Message} Fix the layer in jamm-mod.json and rebuild.", ex);
                }
            }
            return canvas;
        }
        catch
        {
            canvas.Dispose();
            throw;
        }
    }

    private MagickImage Load(ModKitSpriteLayer layer)
    {
        if (layer.From is { } name)
        {
            var sprite = data.Sprites.ByName(name);
            if (sprite is null || layer.Frame >= sprite.Textures.Count || sprite.Textures[layer.Frame]?.Texture is not { } texture)
                throw new InvalidDataException($"Base sprite '{name}' has no usable frame {layer.Frame}.");
            using var source = textures.GetTextureFor(texture, name, includePadding: true);
            return new MagickImage(source);
        }

        if (string.Equals(Path.GetExtension(layer.File), ".svg", StringComparison.OrdinalIgnoreCase))
            return LoadSvg(layer.File!);
        return new MagickImage(layer.File!, new MagickReadSettings { Format = MagickFormat.Png });
    }

    private static MagickImage LoadSvg(string path)
    {
        // Restrict downloaded SVG parts to self-contained drawing primitives before passing them to a renderer.
        using var reader = XmlReader.Create(path, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, MaxCharactersInDocument = 1_000_000 });
        var svg = XDocument.Load(reader);
        XNamespace ns = "http://www.w3.org/2000/svg";
        string[] elements = ["svg", "g", "path", "rect", "circle", "ellipse", "line", "polyline", "polygon"];
        string[] attributes = ["viewBox", "width", "height", "preserveAspectRatio", "x", "y", "x1", "y1", "x2", "y2",
            "cx", "cy", "r", "rx", "ry", "d", "points", "transform", "fill", "fill-opacity", "fill-rule",
            "stroke", "stroke-width", "stroke-linejoin", "stroke-linecap", "stroke-opacity", "opacity"];
        if (svg.Root?.Name != ns + "svg" || svg.DescendantNodes().OfType<XProcessingInstruction>().Any()
            || svg.Descendants().Any(e => e.Name.Namespace != ns || !elements.Contains(e.Name.LocalName)
                || e.Attributes().Any(a => !a.IsNamespaceDeclaration && (a.Name.Namespace != XNamespace.None
                    || !attributes.Contains(a.Name.LocalName) || a.Value.Contains("url", StringComparison.OrdinalIgnoreCase)))))
            throw new InvalidDataException($"SVG '{Path.GetFileName(path)}' uses unsupported elements or attributes. Export a self-contained SVG with simple shapes, or use PNG.");
        return new MagickImage(System.Text.Encoding.UTF8.GetBytes(svg.ToString()), new MagickReadSettings { Format = MagickFormat.Msvg, BackgroundColor = MagickColors.Transparent });
    }

    private static void Transform(MagickImage image, ModKitSpriteLayer layer)
    {
        if (layer.Crop is { } crop)
        {
            if ((long)crop.X + crop.W > image.Width || (long)crop.Y + crop.H > image.Height)
                throw new InvalidDataException($"Crop ({crop.X}, {crop.Y}, {crop.W}, {crop.H}) is outside the {image.Width}×{image.Height} source frame.");
            image.Crop(new MagickGeometry(crop.X, crop.Y, (uint)crop.W, (uint)crop.H));
            image.ResetPage();
        }
        double width = Math.Max(1, Math.Round(image.Width * layer.ScaleX));
        double height = Math.Max(1, Math.Round(image.Height * layer.ScaleY));
        if (width > 4096 || height > 4096)
            throw new InvalidDataException("Scaled layer is larger than 4096 pixels on one side.");

        image.ColorSpace = ColorSpace.sRGB;
        image.ColorType = ColorType.TrueColorAlpha;
        image.SetBitDepth(8);
        image.Alpha(AlphaOption.Set);
        if (layer.PaletteMap.Count > 0)
            EditPixels(image, layer.PaletteMap, tint: null, opacity: 1);
        if (layer.HueShift != 0)
            image.Modulate(new Percentage(100), new Percentage(100), new Percentage(100 + (layer.HueShift % 360) / 1.8));
        if (layer.Tint is not null || layer.Opacity != 1)
            EditPixels(image, palette: null, layer.Tint, layer.Opacity);
        if (layer.Flip is "x" or "both") image.Flop();
        if (layer.Flip is "y" or "both") image.Flip();
        if (width != image.Width || height != image.Height)
        {
            image.FilterType = FilterType.Triangle;
            image.Resize(new MagickGeometry((uint)width, (uint)height) { IgnoreAspectRatio = true });
        }
        if (layer.Rotation != 0)
        {
            image.BackgroundColor = MagickColors.Transparent;
            image.Rotate(layer.Rotation);
            image.ResetPage();
        }
        if (layer.EdgeSoftness > 0)
        {
            image.VirtualPixelMethod = VirtualPixelMethod.Transparent;
            image.GaussianBlur(0, layer.EdgeSoftness, Channels.Alpha);
        }
        if (layer.Tones is { } tones)
            ApplyTones(image, tones);
    }

    private static void ApplyTones(MagickImage image, ModKitSpriteTones tones)
    {
        image.ColorType = ColorType.TrueColorAlpha;
        using var pixels = image.GetPixels();
        byte[] rgba = pixels.GetArea(0, 0, image.Width, image.Height)!;
        for (int i = 0; i < rgba.Length; i += 4)
        {
            int luminance = (2126 * rgba[i] + 7152 * rgba[i + 1] + 722 * rgba[i + 2] + 5000) / 10000;
            int index = luminance < tones.Cutoffs[0] ? 0 : tones.Cutoffs.Count == 2 && luminance >= tones.Cutoffs[1] ? 2 : 1;
            int color = tones.Colors[index];
            rgba[i] = (byte)(color >> 16);
            rgba[i + 1] = (byte)(color >> 8);
            rgba[i + 2] = (byte)color;
        }
        pixels.SetArea(0, 0, image.Width, image.Height, rgba.AsSpan());
    }

    private static void EditPixels(MagickImage image, IReadOnlyDictionary<int, int>? palette, int? tint, double opacity)
    {
        image.ColorType = ColorType.TrueColorAlpha;
        using var pixels = image.GetPixels();
        byte[] rgba = pixels.GetArea(0, 0, image.Width, image.Height)!;
        for (int i = 0; i < rgba.Length; i += 4)
        {
            int color = (rgba[i] << 16) | (rgba[i + 1] << 8) | rgba[i + 2];
            if (palette is not null && palette.TryGetValue(color, out int mapped))
            {
                rgba[i] = (byte)(mapped >> 16);
                rgba[i + 1] = (byte)(mapped >> 8);
                rgba[i + 2] = (byte)mapped;
            }
            if (tint is int multiplier)
            {
                rgba[i] = (byte)((rgba[i] * (multiplier >> 16) + 127) / 255);
                rgba[i + 1] = (byte)((rgba[i + 1] * ((multiplier >> 8) & 255) + 127) / 255);
                rgba[i + 2] = (byte)((rgba[i + 2] * (multiplier & 255) + 127) / 255);
            }
            rgba[i + 3] = (byte)Math.Round(rgba[i + 3] * opacity);
        }
        pixels.SetArea(0, 0, image.Width, image.Height, rgba.AsSpan());
    }

    public void Dispose() => textures.Dispose();
}
