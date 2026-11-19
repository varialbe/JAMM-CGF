using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Jamm.Core.Patching;
using UndertaleModLib;
using UndertaleModLib.Models;
using UndertaleModLib.Util;

namespace Jamm.Core;

/// <summary>Semantic fingerprints for comparison and patch candidate filtering; never writes game assets.</summary>
public sealed class ContentFingerprints
{
    // Keep at most one decoded atlas; region hashes are small and safe to retain.
    private UndertaleEmbeddedTexture? decodedPage;
    private GMImage? decodedPixels;
    private readonly Dictionary<UndertaleTexturePageItem, string> regions = [];
    private readonly Dictionary<UndertaleTexturePageItem, string> pixels = [];
    private readonly Action<string>? warning;
    private readonly object warningGate = new();
    private void Warn(string message) { lock (warningGate) warning?.Invoke(message); }
    internal bool RetainRegionHashes { get; init; }
    internal sealed record RegionFingerprint(int Index, string Region, string? Pixels);
    internal RegionFingerprint[] CaptureRegions(UndertaleData data) => data.TexturePageItems.Select((item, index) =>
        regions.TryGetValue(item, out var region) ? new RegionFingerprint(index, region, pixels.GetValueOrDefault(item)) : null)
        .OfType<RegionFingerprint>().ToArray();
    internal void RestoreRegions(UndertaleData data, IEnumerable<RegionFingerprint> snapshots)
    {
        foreach (var snapshot in snapshots)
        {
            var item = data.TexturePageItems[snapshot.Index];
            regions[item] = snapshot.Region;
            if (snapshot.Pixels is not null) pixels[item] = snapshot.Pixels;
        }
    }

    /// <summary>Optionally receives notices about incomplete texture regions.</summary>
    public ContentFingerprints(Action<string>? warning = null) => this.warning = warning;

    /// <summary>SHA-256 of bytes, encoded as lowercase hexadecimal.</summary>
    public static string Hash(ReadOnlySpan<byte> bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

    private static string Of(object value) => Hash(JsonSerializer.SerializeToUtf8Bytes(value));
    private static string? Name(UndertaleNamedResource? resource) => resource?.Name?.Content;

    /// <summary>
    /// Hashes UTML instruction disassembly in stream order, with LF separators and relative addresses.
    /// Child entries share their parent's stream, so include their entry offset and argument count.
    /// No file offsets, string table indices, reference-chain addresses, or decompiled source are included.
    /// </summary>
    public static string Code(UndertaleCode code)
    {
        var root = code.ParentEntry ?? code;
        StringBuilder text = new();
        uint address = 0;
        foreach (var instruction in root.Instructions)
        {
            instruction.ToString(text, root, address);
            // UTML appends @tableIndex to string references. It is a lookup location,
            // not instruction content; preserve the disassembled value and omit that suffix.
            if (instruction.ValueString is { } reference)
                text.Length -= reference.GetMarkerSuffix().Length;
            text.Append('\n');
            address += instruction.CalculateInstructionSize();
        }
        return Of(new { code.Offset, code.LocalsCount, code.ArgumentsCount, code.WeirdLocalFlag, Parent = Name(code.ParentEntry), Children = code.ChildEntries.Select(Name).Order(StringComparer.Ordinal).ToArray(), Stream = text.ToString() });
    }

    /// <summary>Hashes a decoded BGRA region and placement geometry, or returns an unsupported marker.</summary>
    public string Region(UndertaleTexturePageItem? item)
    {
        if (item is null) return Of("no texture");
        if (regions.TryGetValue(item, out string? known)) return known;
        string result;
        var page = item.TexturePage;
        if (page is null) result = Unsupported("missing texture page", item.Name?.Content);
        else if (page.TextureExternal) result = Unsupported("external texture", item.Name?.Content);
        else
        {
            try { result = EmbeddedRegion(item, page); }
            catch (NotSupportedException) { result = Unsupported("texture format", item.Name?.Content); }
        }
        regions.Add(item, result);
        return result;
    }

    /// <summary>Hashes only decoded frame pixels; geometry has a separate region hash.</summary>
    public string FramePixels(UndertaleTexturePageItem? item)
    {
        string region = Region(item);
        return item is not null && pixels.TryGetValue(item, out string? hash) ? hash : region;
    }

    private string Unsupported(string reason, string? asset)
    {
        Warn($"Fingerprint: '{asset ?? "<unnamed>"}' is unsupported ({reason}); content was not compared.");
        return $"unsupported:{reason}";
    }

    private string EmbeddedRegion(UndertaleTexturePageItem item, UndertaleEmbeddedTexture page)
    {
        if (decodedPage != page)
        {
            // Drop the previous atlas before decoding the next one.
            decodedPage = null;
            decodedPixels = null;
            decodedPixels = DecodeTexture(page.TextureData.Image);
            decodedPage = page;
        }
        var image = decodedPixels!;
        if ((long)item.SourceX + item.SourceWidth > image.Width || (long)item.SourceY + item.SourceHeight > image.Height)
            return Unsupported("out-of-range region", item.Name?.Content);
        pixels[item] = HashTextureRegion(image, item);
        return Of(new { item.SourceWidth, item.SourceHeight, item.TargetX, item.TargetY,
            item.TargetWidth, item.TargetHeight, item.BoundingWidth, item.BoundingHeight,
            Pixels = pixels[item] });
    }

    internal static GMImage DecodeTexture(GMImage source)
    {
        if (source.Format is not (GMImage.ImageFormat.Png or GMImage.ImageFormat.Dds))
            return source.ConvertToRawBgra();
        using var decoded = source.GetMagickImage();
        decoded.Alpha(ImageMagick.AlphaOption.Set);
        return GMImage.FromMagickImage(decoded);
    }

    internal static string HashTextureRegion(GMImage image, UndertaleTexturePageItem item)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var bytes = image.GetRawImageData();
        for (int y = item.SourceY; y < item.SourceY + item.SourceHeight; y++)
            hash.AppendData(bytes.Slice(checked((y * image.Width + item.SourceX) * 4), item.SourceWidth * 4));
        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }

    /// <summary>Dimensions, origin, flags, collision masks and an ordered hash of each frame region.</summary>
    public string Sprite(UndertaleSprite sprite)
    {
        if (sprite.IsSpecialType && sprite.SSpriteType != UndertaleSprite.SpriteType.Normal)
            return SpecialSpriteContent(sprite)!;
        string[] frames = sprite.Textures.Select(x => Region(x?.Texture)).ToArray();
        if (frames.FirstOrDefault(x => x.StartsWith("unsupported:", StringComparison.Ordinal)) is { } marker)
            return marker;
        return Of(new { sprite.Width, sprite.Height, sprite.OriginX, sprite.OriginY,
            sprite.MarginLeft, sprite.MarginRight, sprite.MarginTop, sprite.MarginBottom,
            sprite.Transparent, sprite.Smooth, sprite.Preload, sprite.BBoxMode, sprite.SepMasks, sprite.IsSpecialType, sprite.SSpriteType,
            sprite.GMS2PlaybackSpeed, sprite.GMS2PlaybackSpeedType,
            FrameCount = sprite.Textures.Count, Frames = frames,
            Masks = sprite.CollisionMasks.Select(x => Hash(x.Data)).ToArray() });
    }

    // These payloads are self-contained trees, unlike the surrounding resource graph.
    // Only their digest enters a patch; incomplete/unknown payloads remain unresolved.
    internal string? SpecialSpriteContent(UndertaleSprite sprite)
    {
        if (!sprite.IsSpecialType || sprite.SSpriteType == UndertaleSprite.SpriteType.Normal) return null;
        if (sprite.SSpriteType == UndertaleSprite.SpriteType.SWF && sprite.YYSWF?.Timeline is { } timeline
            && timeline.UsedItems is not null && timeline.Frames is not null && timeline.CollisionMasks is not null
            && timeline.UsedItems.All(i => i.ItemType == UndertaleYYSWFItemType.ItemShape && i.ShapeData is not null
                || i.ItemType == UndertaleYYSWFItemType.ItemBitmap && i.BitmapData is not null))
            return Of(new { sprite.SWFVersion, sprite.YYSWF });
        if (sprite.SSpriteType == UndertaleSprite.SpriteType.Spine && sprite.IsSpineSprite)
            return Of(new { sprite.SpineVersion, sprite.SpineCacheVersion, sprite.SpineJSON, sprite.SpineAtlas,
                sprite.SpineHasTextureData, Textures = sprite.SpineTextures.Select(t => new
                { t.PageWidth, t.PageHeight, t.TextureEntryLength, ContentHash = t.TexBlob is null ? null : Hash(t.TexBlob) }).ToArray() });
        return Unsupported($"{sprite.SSpriteType} sprite", Name(sprite));
    }

    /// <summary>Snapshots supported families. YYC skips code-dependent families; strings use value hashes.</summary>
    public IReadOnlyDictionary<AssetKind, IReadOnlyList<AssetFingerprint>> Snapshot(UndertaleData data, bool skipBytecode = false, AnalysisOptions? options = null)
    {
        options ??= AnalysisOptions.Default;
        decodedPage = null;
        decodedPixels = null;
        regions.Clear();
        pixels.Clear();
        var fields = new AssetFields(warning ?? (_ => { }), data, this);
        skipBytecode |= data.IsYYC() || data.Code is null || data.Code.Count == 0 || data.UnsupportedBytecodeVersion;
        string Checked(AssetKind kind, string name, Func<string> fingerprint)
        {
            try { return fingerprint(); }
            catch (NotSupportedException) { return Unsupported("asset format", $"{kind.Label()} {name}"); }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            { throw new AssetAnalysisException($"{kind.Label()} '{name}'", ex); }
        }
        var entries = skipBytecode ? [] : (data.Code ?? []).ToArray();
        var hashes = new string[entries.Length];
        AnalysisWork.For(entries.Length, options, i =>
            hashes[i] = Checked(AssetKind.Code, Name(entries[i]) ?? "<unnamed>", () => Code(entries[i])));
        var codeHashes = entries.Select((entry, i) => (entry, hashes[i])).ToDictionary(x => x.entry, x => x.Item2);
        string? CodeHash(UndertaleCode? code)
        {
            if (code is null) return null;
            if (codeHashes.TryGetValue(code, out string? hash)) return hash;
            Warn($"Fingerprint: script code '{Name(code)}' is missing from the CODE table; using a missing-code marker.");
            return "unsupported:missing code entry";
        }
        IReadOnlyList<AssetFingerprint> Named<T>(AssetKind kind, IEnumerable<T>? assets, Func<T, string> fingerprint)
            where T : UndertaleNamedResource => skipBytecode && kind.RequiresBytecode() ? [] : (assets ?? []).Select(x =>
                new AssetFingerprint(Name(x) ?? "<unnamed>", Checked(kind, Name(x) ?? "<unnamed>", () => fingerprint(x)))).ToArray();

        try
        {
            // Hash regions page-by-page before the named assets, so switching between sprites
            // never decodes an atlas repeatedly. Retain hashes, not decoded atlas images.
            var items = (data.Sprites ?? []).Where(x => !x.IsSpecialType || x.SSpriteType == UndertaleSprite.SpriteType.Normal)
                .SelectMany(x => x.Textures).Select(x => x?.Texture)
                .Concat((data.Fonts ?? []).Select(x => x?.Texture))
                .Concat((data.Backgrounds ?? []).Select(x => x?.Texture))
                .Where(x => x is not null).Distinct();
            foreach (var page in items.GroupBy(x => x!.TexturePage))
                foreach (var item in page)
                    Checked(AssetKind.TexturePageItems, Name(item) ?? "<unnamed>", () => Region(item));
            decodedPage = null;
            decodedPixels = null;

            // Variables/functions exclude table IDs and reference-chain counts: those describe code usage,
            // not the definition. Duplicate names are retained and compared as multisets.
            return new Dictionary<AssetKind, IReadOnlyList<AssetFingerprint>>
            {
                [AssetKind.Paths] = Named(AssetKind.Paths, data.Paths, x => Of(fields.Snapshot(x))),
                [AssetKind.Timelines] = Named(AssetKind.Timelines, data.Timelines, x => Of(fields.Snapshot(x))),
                [AssetKind.Code] = Named(AssetKind.Code, data.Code, x => codeHashes[x]),
                [AssetKind.Scripts] = Named(AssetKind.Scripts, data.Scripts, x => Of(new { Code = Name(x.Code), x.IsConstructor, Hash = CodeHash(x.Code) })),
                [AssetKind.Sprites] = Named(AssetKind.Sprites, data.Sprites, x => Sprite(x) is { } hash && hash.StartsWith("unsupported:", StringComparison.Ordinal) ? hash : Of(fields.Snapshot(x))),
                [AssetKind.Objects] = Named(AssetKind.Objects, data.GameObjects, x => Of(fields.Snapshot(x))),
                [AssetKind.Rooms] = Named(AssetKind.Rooms, data.Rooms, x => Of(fields.Snapshot(x))),
                [AssetKind.Sounds] = Named(AssetKind.Sounds, data.Sounds, x => Of(fields.Snapshot(x))),
                [AssetKind.TexturePages] = (data.EmbeddedTextures ?? []).Select((x, i) =>
                    new AssetFingerprint(i.ToString(System.Globalization.CultureInfo.InvariantCulture), Checked(AssetKind.TexturePages, i.ToString(System.Globalization.CultureInfo.InvariantCulture), () => Page(x, data.IsVersionAtLeast(2022, 5))))).ToArray(),
                [AssetKind.Strings] = (data.Strings ?? []).Select(x => { string hash = Of(x.Content);
                    return new AssetFingerprint($"sha256:{hash}", hash); }).ToArray(),
                [AssetKind.Variables] = Named(AssetKind.Variables, data.Variables, x => Of(new { x.InstanceType })),
                [AssetKind.Functions] = Named(AssetKind.Functions, data.Functions, x => Of(new { x.Classification })),
                [AssetKind.Fonts] = Named(AssetKind.Fonts, data.Fonts, x => Of(fields.Snapshot(x))),
                [AssetKind.Backgrounds] = Named(AssetKind.Backgrounds, data.Backgrounds, x => Of(fields.Snapshot(x)))
            }.Where(x => !skipBytecode || !x.Key.RequiresBytecode()).ToDictionary(x => x.Key, x => x.Value);
        }
        finally
        {
            decodedPage = null;
            decodedPixels = null;
            if (!RetainRegionHashes)
            {
                regions.Clear();
                pixels.Clear();
            }
        }
    }

    internal static object Instance(UndertaleRoom.GameObject i) => new
    {
        Object = Name(i.ObjectDefinition), i.X, i.Y, i.ScaleX, i.ScaleY, i.Rotation,
        i.Color, i.ImageIndex, i.ImageSpeed,
        CreationCode = Name(i.CreationCode), PreCreate = Name(i.PreCreateCode)
    };

    internal static object Tile(UndertaleRoom.Tile t) => new
    {
        Resource = Name(t.ObjectDefinition), t.spriteMode, t.X, t.Y, t.SourceX, t.SourceY,
        t.Width, t.Height, t.TileDepth, t.ScaleX, t.ScaleY, t.Color
    };

    private static object SpriteInstance(UndertaleRoom.SpriteInstance s) => new
    {
        Sprite = Name(s.Sprite), s.X, s.Y, s.ScaleX, s.ScaleY, s.Color,
        s.AnimationSpeed, s.AnimationSpeedType, s.FrameIndex, s.Rotation
    };

    internal static object? LayerElements(UndertaleRoom.Layer.LayerData? data) => data switch
    {
        UndertaleRoom.Layer.LayerInstancesData instances => new
        {
            Instances = AssetFields.Instances(instances.Instances)
        },
        UndertaleRoom.Layer.LayerTilesData tiles => new
        {
            Background = Name(tiles.Background), tiles.TilesX, tiles.TilesY, tiles.TileData
        },
        UndertaleRoom.Layer.LayerBackgroundData background => new
        {
            Sprite = Name(background.Sprite), background.Visible, background.Foreground,
            background.TiledHorizontally, background.TiledVertically, background.Stretch,
            background.Color, background.FirstFrame, background.AnimationSpeed, background.AnimationSpeedType
        },
        UndertaleRoom.Layer.LayerAssetsData assets => new
        {
            Tiles = assets.LegacyTiles is null ? null : AssetFields.Tiles(assets.LegacyTiles),
            Sprites = assets.Sprites?.Select(SpriteInstance).ToArray(),
            NineSlices = assets.NineSlices?.Select(SpriteInstance).ToArray()
        },
        _ => null
    };

    private string Page(UndertaleEmbeddedTexture page, bool modernHeader)
    {
        if (page.TextureExternal) return Unsupported("external texture", page.Name?.Content);
        var image = page.TextureData.Image;
        if (image.Format != GMImage.ImageFormat.Bz2Qoi) return Hash(image.ToSpan());
        // UTML 0.9.2's ToSpan closes its temporary stream before reading Position for BZ2-QOI.
        // Its public writer supports leaveOpen, so use that path without modifying the submodule.
        using MemoryStream stream = new();
        using (BinaryWriter writer = new(stream, Encoding.UTF8, leaveOpen: true))
            image.WriteToBinaryWriter(writer, modernHeader);
        return Hash(stream.GetBuffer().AsSpan(0, checked((int)stream.Length)));
    }
}
