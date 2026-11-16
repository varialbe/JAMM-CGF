using System.Text.Json;
using UndertaleModLib.Models;
using UndertaleModLib;

namespace Jamm.Core.Patching;

// Explicit semantic projections avoid serializing UTML graphs, IDs, addresses or binary data.
internal sealed class AssetFields(Action<string> warning, UndertaleData? data = null, ContentFingerprints? shared = null)
{
    private readonly ContentFingerprints fingerprints = shared ?? new(warning);
    private static readonly JsonSerializerOptions Options = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    internal static JsonElement Json(object? value) => JsonSerializer.SerializeToElement(value, Options);
    private static string? Name(UndertaleNamedResource? resource) => resource?.Name?.Content;
    private object Region(UndertaleTexturePageItem? item) => new { ContentHash = fingerprints.Region(item), Geometry = Geometry(item) };
    private static object? Geometry(UndertaleTexturePageItem? item) => item is null ? null : new {
        item.SourceX, item.SourceY, item.SourceWidth, item.SourceHeight, item.TargetX, item.TargetY,
        item.TargetWidth, item.TargetHeight, item.BoundingWidth, item.BoundingHeight };
    private static SortedDictionary<string, JsonElement[]> Keyed<T>(IEnumerable<T> values, Func<T, string> key, Func<T, object> value) =>
        new(values.GroupBy(key).ToDictionary(g => g.Key, g => g.Select(x => Json(value(x)))
            .OrderBy(x => x.GetRawText(), StringComparer.Ordinal).ToArray()), StringComparer.Ordinal);

    internal JsonElement Snapshot(object asset) => Json(asset switch
    {
        UndertaleScript x => new { Code = Name(x.Code), x.IsConstructor },
        UndertaleGameObject x => Object(x),
        UndertaleRoom x => Room(x),
        UndertaleSprite x => Sprite(x),
        UndertaleSound x => Sound(x),
        UndertaleFont x => Font(x),
        UndertaleBackground x => Background(x),
        UndertalePath x => new { x.IsSmooth, x.IsClosed, x.Precision, Points = x.Points.Select(p => new { p.X, p.Y, p.Speed }).ToArray() },
        UndertaleTimeline x => new { Moments = Keyed(x.Moments, m => m.Step.ToString(System.Globalization.CultureInfo.InvariantCulture), m => new { m.Step, Code = m.Event.Select(e => Name(e.CodeId)).ToArray() }) },
        UndertaleVariable x => new { x.InstanceType },
        UndertaleFunction x => new { x.Classification },
        _ => throw new NotSupportedException($"No semantic fields for {asset.GetType().Name}.")
    });

    internal object Object(UndertaleGameObject x) => new { Sprite = Name(x.Sprite), Parent = Name(x.ParentId),
                    Mask = Name(x.TextureMaskId), x.Visible, x.Managed, x.Solid, x.Depth, x.Persistent,
                    x.UsesPhysics, x.IsSensor, x.CollisionShape, x.Density, x.Restitution, x.Group,
                    x.LinearDamping, x.AngularDamping, x.Friction, x.Awake, x.Kinematic,
                    Vertices = x.PhysicsVertices.Select(v => new { v.X, v.Y }).ToArray(),
                    Events = Events(x) };

    private SortedDictionary<string, object> Events(UndertaleGameObject x)
    {
        SortedDictionary<string, object> result = new(StringComparer.Ordinal);
        for (int type = 0; type < x.Events.Count; type++)
            foreach (var e in x.Events[type])
            {
                string subtype = type == 4 && data is not null && e.EventSubtype < data.GameObjects.Count
                    ? Name(data.GameObjects[(int)e.EventSubtype])! : e.EventSubtype.ToString(System.Globalization.CultureInfo.InvariantCulture);
                string key = $"{type}:{subtype}";
                var code = e.Actions.Select(a => Name(a.CodeId)).ToArray();
                if (result.TryGetValue(key, out var existing))
                {
                    // Duplicate event records share one semantic event and preserve action order.
                    code = Json(existing).GetProperty("code").EnumerateArray().Select(v => v.GetString()).Concat(code).ToArray();
                }
                result[key] = new { OtherObject = type == 4 ? subtype : null, Code = code };
            }
        return result;
    }

    internal static object Sound(UndertaleSound x) => new { x.Flags, x.Effects, x.Volume, x.Pitch, x.Preload,
                    Type = x.Type?.Content, File = x.File?.Content, AudioGroup = Name(x.AudioGroup) ?? (x.GroupID == 0 ? "audiogroup_default" : $"unresolved:{x.GroupID}"),
                    Audio = x.AudioFile is null ? null : ContentFingerprints.Hash(x.AudioFile.Data) };

    private object Font(UndertaleFont x) => new { DisplayName = x.DisplayName?.Content, x.EmSize,
                    x.Bold, x.Italic, x.RangeStart, x.RangeEnd, x.Charset, x.AntiAliasing, x.ScaleX, x.ScaleY,
                    x.Ascender, x.AscenderOffset, x.SDFSpread, x.LineHeight, Region = Region(x.Texture),
                    Glyphs = Keyed(x.Glyphs, g => g.Character.ToString(System.Globalization.CultureInfo.InvariantCulture), g => new { g.Character, g.SourceX, g.SourceY, g.SourceWidth,
                        g.SourceHeight, g.Shift, g.Offset, Kerning = g.Kerning.Select(k => new { k.Character, k.ShiftModifier }).ToArray() }) };

    private object Background(UndertaleBackground x) => new { x.Transparent, x.Smooth, x.Preload,
                    Region = Region(x.Texture), x.GMS2TileWidth, x.GMS2TileHeight, x.GMS2OutputBorderX,
                    x.GMS2OutputBorderY, x.GMS2TileColumns, x.GMS2ItemsPerTileCount, x.GMS2TileCount,
                    x.GMS2FrameLength, x.GMS2TileSeparationX, x.GMS2TileSeparationY,
                    TileIds = x.GMS2TileIds.Select(t => t.ID).ToArray() };

    private object Sprite(UndertaleSprite x) => new
    {
        x.Width, x.Height, x.OriginX, x.OriginY, x.MarginLeft, x.MarginRight, x.MarginTop, x.MarginBottom,
        x.Transparent, x.Smooth, x.Preload, x.BBoxMode, x.SepMasks, x.IsSpecialType, x.SSpriteType,
        x.GMS2PlaybackSpeed, x.GMS2PlaybackSpeedType, FrameCount = x.Textures.Count,
        SpecialContentHash = fingerprints.SpecialSpriteContent(x),
        Frames = x.Textures.Select((frame, index) => new { Index = index,
            ContentHash = fingerprints.FramePixels(frame?.Texture), RegionHash = fingerprints.Region(frame?.Texture), Geometry = Geometry(frame?.Texture) }).ToArray(),
        Masks = x.CollisionMasks.Select(m => ContentFingerprints.Hash(m.Data)).ToArray()
    };

    internal static object Room(UndertaleRoom x) => new
    {
        Caption = x.Caption?.Content, x.Width, x.Height, x.Speed, x.Persistent, x.Flags,
        x.BackgroundColor, x.DrawBackgroundColor, CreationCode = Name(x.CreationCodeId),
        x.World, x.Top, x.Left, x.Right, x.Bottom, x.GravityX, x.GravityY, x.MetersPerPixel,
        Views = x.Views.Select(v => new { v.Enabled, v.ViewX, v.ViewY, v.ViewWidth, v.ViewHeight,
            v.PortX, v.PortY, v.PortWidth, v.PortHeight, v.BorderX, v.BorderY, v.SpeedX, v.SpeedY,
            Object = Name(v.ObjectId) }).ToArray(),
        Backgrounds = x.Backgrounds.Select(b => new { b.Enabled, b.Foreground, Background = Name(b.BackgroundDefinition),
            b.X, b.Y, b.SpeedX, b.SpeedY, b.Stretch, b.TiledHorizontally, b.TiledVertically }).ToArray(),
        Instances = Instances(x.GameObjects),
        Tiles = Tiles(x.Tiles),
        Layers = Keyed(x.Layers, l => l.LayerName?.Content ?? "", l => new { Name = l.LayerName?.Content, l.LayerType, l.LayerDepth,
            l.XOffset, l.YOffset, l.HSpeed, l.VSpeed, l.IsVisible, Elements = ContentFingerprints.LayerElements(l.Data) })
    };

    internal static SortedDictionary<string, JsonElement[]> Tiles(IEnumerable<UndertaleRoom.Tile> tiles) =>
        Keyed(tiles, t => Json(new object?[] { Name(t.ObjectDefinition), t.X, t.Y, t.SourceX, t.SourceY, t.Width, t.Height, t.TileDepth }).GetRawText(), ContentFingerprints.Tile);

    // Values at the same position are a multiset. IDs and input ordering are deliberately excluded.
    internal static SortedDictionary<string, JsonElement[]> Instances(IEnumerable<UndertaleRoom.GameObject> instances)
    {
        var result = new SortedDictionary<string, JsonElement[]>(StringComparer.Ordinal);
        foreach (var group in instances.GroupBy(i => Json(new object?[] { Name(i.ObjectDefinition), i.X, i.Y }).GetRawText()))
            result.Add(group.Key, group.Select(i => Json(ContentFingerprints.Instance(i)))
                .OrderBy(i => i.GetRawText(), StringComparer.Ordinal).ToArray());
        return result;
    }
}
