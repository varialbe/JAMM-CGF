using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using UndertaleModLib;
using UndertaleModLib.Models;
using UndertaleModLib.Util;

namespace Jamm.Core;

/// <summary>Phase 0 semantic fingerprints; never decompiles or writes game assets.</summary>
public sealed class ContentFingerprints
{
    private readonly Dictionary<UndertaleEmbeddedTexture, GMImage> pixels = [];
    private readonly Dictionary<UndertaleTexturePageItem, string> regions = [];
    private readonly Action<string>? warning;

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
        return Of(new { code.Offset, code.ArgumentsCount, Stream = text.ToString() });
    }

    /// <summary>
    /// Hashes only the frame's source rectangle in decoded BGRA pixels, row by row, plus target
    /// and bounding geometry. Atlas location, other sprites and PNG compression cannot affect it.
    /// Invalid rectangles are marked explicitly and hashed with their available pixels; missing
    /// pixels are transparent padding, matching a clipped crop. A warning records this limitation.
    /// </summary>
    public string Region(UndertaleTexturePageItem? item)
    {
        if (item is null) return Of("no texture");
        if (regions.TryGetValue(item, out string? known)) return known;
        var page = item.TexturePage ?? throw new InvalidDataException("A texture item has no texture page.");
        if (page.TextureExternal)
            throw new NotSupportedException("External texture pages are not supported in Phase 0; use an embedded-texture data.win.");
        if (!pixels.TryGetValue(page, out var image))
        {
            var source = page.TextureData.Image;
            if (source.Format is GMImage.ImageFormat.Png or GMImage.ImageFormat.Dds)
            {
                // Dispose native decoder memory explicitly; retain only managed BGRA bytes.
                using var decoded = source.GetMagickImage();
                decoded.Alpha(ImageMagick.AlphaOption.Set);
                image = GMImage.FromMagickImage(decoded);
            }
            else image = source.ConvertToRawBgra();
            pixels.Add(page, image);
        }
        bool outside = (long)item.SourceX + item.SourceWidth > image.Width || (long)item.SourceY + item.SourceHeight > image.Height;
        if (outside)
            warning?.Invoke($"Fingerprint: texture item '{item.Name?.Content}' has source rectangle {item.SourceX},{item.SourceY} {item.SourceWidth}x{item.SourceHeight} outside its {image.Width}x{image.Height} texture page '{page.Name?.Content}'; hashed available pixels with transparent padding and an invalid-region marker.");
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var bytes = image.GetRawImageData();
        byte[]? padded = outside ? new byte[item.SourceWidth * 4] : null;
        for (int y = item.SourceY; y < item.SourceY + item.SourceHeight; y++)
        {
            if (padded is null)
                hash.AppendData(bytes.Slice(checked((y * image.Width + item.SourceX) * 4), item.SourceWidth * 4));
            else
            {
                Array.Clear(padded);
                int width = Math.Max(0, Math.Min(item.SourceWidth, image.Width - item.SourceX));
                if (y < image.Height && width > 0)
                    bytes.Slice(checked((y * image.Width + item.SourceX) * 4), width * 4).CopyTo(padded);
                hash.AppendData(padded);
            }
        }
        string result = Of(new { item.SourceWidth, item.SourceHeight, item.TargetX, item.TargetY,
            item.TargetWidth, item.TargetHeight, item.BoundingWidth, item.BoundingHeight,
            InvalidRegion = outside, Pixels = Convert.ToHexStringLower(hash.GetHashAndReset()) });
        regions.Add(item, result);
        return result;
    }

    /// <summary>Dimensions, origin, flags, collision masks and an ordered hash of each frame region.</summary>
    public string Sprite(UndertaleSprite sprite)
    {
        if (sprite.IsSpecialType && sprite.SSpriteType != UndertaleSprite.SpriteType.Normal)
            throw new NotSupportedException("Non-bitmap sprites are not supported by Phase 0 fingerprints.");
        return Of(new { sprite.Width, sprite.Height, sprite.OriginX, sprite.OriginY,
            sprite.MarginLeft, sprite.MarginRight, sprite.MarginTop, sprite.MarginBottom,
            sprite.Transparent, sprite.Smooth, sprite.Preload, sprite.BBoxMode, sprite.SepMasks,
            sprite.GMS2PlaybackSpeed, sprite.GMS2PlaybackSpeedType,
            FrameCount = sprite.Textures.Count, Frames = sprite.Textures.Select(x => Region(x.Texture)).ToArray(),
            Masks = sprite.CollisionMasks.Select(x => Hash(x.Data)).ToArray() });
    }

    /// <summary>Builds the twelve required asset families. Strings match by value hash, pages by index.</summary>
    public IReadOnlyDictionary<string, IReadOnlyList<AssetFingerprint>> Snapshot(UndertaleData data)
    {
        var codeHashes = (data.Code ?? []).ToDictionary(x => x, Code);
        string? CodeHash(UndertaleCode? code) => code is null ? null : codeHashes[code];
        IReadOnlyList<AssetFingerprint> Named<T>(IEnumerable<T>? assets, Func<T, string> fingerprint)
            where T : UndertaleNamedResource => (assets ?? []).Select(x =>
                new AssetFingerprint(Name(x) ?? "<unnamed>", fingerprint(x))).ToArray();

        // Variables/functions exclude table IDs and reference-chain counts: those describe code usage,
        // not the definition. Duplicate names are retained and compared as multisets.
        return new Dictionary<string, IReadOnlyList<AssetFingerprint>>(StringComparer.Ordinal)
        {
            ["code"] = Named(data.Code, x => codeHashes[x]),
            ["scripts"] = Named(data.Scripts, x => Of(new { Code = Name(x.Code), x.IsConstructor, Hash = CodeHash(x.Code) })),
            ["sprites"] = Named(data.Sprites, Sprite),
            ["objects"] = Named(data.GameObjects, x => Of(new { Sprite = Name(x.Sprite), Parent = Name(x.ParentId),
                Mask = Name(x.TextureMaskId), x.Visible, x.Managed, x.Solid, x.Depth, x.Persistent,
                x.UsesPhysics, x.IsSensor, x.CollisionShape, x.Density, x.Restitution, x.Group,
                x.LinearDamping, x.AngularDamping, x.Friction, x.Awake, x.Kinematic,
                Vertices = x.PhysicsVertices.Select(v => new { v.X, v.Y }).ToArray(),
                Events = x.Events.Select((events, type) => new { Type = type,
                    Entries = events.Select(e => new { e.EventSubtype,
                        Code = e.Actions.Select(a => Name(a.CodeId)).ToArray() }).ToArray() }).ToArray() })),
            ["rooms"] = Named(data.Rooms, x => Of(new { x.Width, x.Height, x.Speed, x.Persistent, x.Flags,
                x.BackgroundColor, x.DrawBackgroundColor, CreationCode = Name(x.CreationCodeId),
                Instances = x.GameObjects.Select(i => new { Object = Name(i.ObjectDefinition), i.X, i.Y,
                    i.ScaleX, i.ScaleY, i.Rotation, i.Color, i.ImageIndex, i.ImageSpeed,
                    CreationCode = Name(i.CreationCode), PreCreate = Name(i.PreCreateCode) }).ToArray() })),
            ["sounds"] = Named(data.Sounds, x => Of(new { x.Flags, x.Effects, x.Volume, x.Pitch, x.Preload,
                Type = x.Type?.Content, File = x.File?.Content, x.GroupID,
                Audio = x.AudioFile is null ? null : Hash(x.AudioFile.Data) })),
            ["texture pages"] = (data.EmbeddedTextures ?? []).Select((x, i) =>
                new AssetFingerprint(i.ToString(System.Globalization.CultureInfo.InvariantCulture), Page(x, data.IsVersionAtLeast(2022, 5)))).ToArray(),
            ["strings"] = (data.Strings ?? []).Select(x => { string hash = Of(x.Content);
                return new AssetFingerprint($"sha256:{hash}", hash); }).ToArray(),
            ["variables"] = Named(data.Variables, x => Of(new { x.InstanceType })),
            ["functions"] = Named(data.Functions, x => Of(new { x.Classification })),
            ["fonts"] = Named(data.Fonts, x => Of(new { DisplayName = x.DisplayName?.Content, x.EmSize,
                x.Bold, x.Italic, x.RangeStart, x.RangeEnd, x.Charset, x.AntiAliasing, x.ScaleX, x.ScaleY,
                x.Ascender, x.AscenderOffset, x.SDFSpread, x.LineHeight, Region = Region(x.Texture),
                Glyphs = x.Glyphs.Select(g => new { g.Character, g.SourceX, g.SourceY, g.SourceWidth,
                    g.SourceHeight, g.Shift, g.Offset, Kerning = g.Kerning.Select(k => new { k.Character, k.ShiftModifier }).ToArray() }).ToArray() })),
            ["backgrounds"] = Named(data.Backgrounds, x => Of(new { x.Transparent, x.Smooth, x.Preload,
                Region = Region(x.Texture), x.GMS2TileWidth, x.GMS2TileHeight, x.GMS2OutputBorderX,
                x.GMS2OutputBorderY, x.GMS2TileColumns, x.GMS2ItemsPerTileCount, x.GMS2TileCount,
                x.GMS2FrameLength, x.GMS2TileSeparationX, x.GMS2TileSeparationY,
                TileIds = x.GMS2TileIds.Select(t => t.ID).ToArray() }))
        };
    }

    private static string Page(UndertaleEmbeddedTexture page, bool modernHeader)
    {
        if (page.TextureExternal) throw new NotSupportedException("External texture pages are not supported in Phase 0.");
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
