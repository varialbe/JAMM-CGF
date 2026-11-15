using System.Text.Json;
using UndertaleModLib.Models;
using UndertaleModLib;

namespace Jamm.Core.Patching;

// Explicit semantic projections avoid serializing UTML graphs, IDs, addresses or binary data.
internal sealed class AssetFields(Action<string> warning)
{
    private readonly ContentFingerprints fingerprints = new(warning);
    private static readonly JsonSerializerOptions Options = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    internal static JsonElement Json(object? value) => JsonSerializer.SerializeToElement(value, Options);
    private static string? Name(UndertaleNamedResource? resource) => resource?.Name?.Content;
    private string Region(UndertaleTexturePageItem? item) => fingerprints.Region(item);

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
        UndertaleTimeline x => new { Moments = x.Moments.Select(m => new { m.Step, Code = m.Event.Select(e => Name(e.CodeId)).ToArray() }).ToArray() },
        UndertaleVariable x => new { x.InstanceType },
        UndertaleFunction x => new { x.Classification },
        _ => throw new NotSupportedException($"No semantic fields for {asset.GetType().Name}.")
    });

    internal static object Object(UndertaleGameObject x) => new { Sprite = Name(x.Sprite), Parent = Name(x.ParentId),
                    Mask = Name(x.TextureMaskId), x.Visible, x.Managed, x.Solid, x.Depth, x.Persistent,
                    x.UsesPhysics, x.IsSensor, x.CollisionShape, x.Density, x.Restitution, x.Group,
                    x.LinearDamping, x.AngularDamping, x.Friction, x.Awake, x.Kinematic,
                    Vertices = x.PhysicsVertices.Select(v => new { v.X, v.Y }).ToArray(),
                    Events = Events(x) };

    private static SortedDictionary<string, object> Events(UndertaleGameObject x)
    {
        SortedDictionary<string, object> result = new(StringComparer.Ordinal);
        for (int type = 0; type < x.Events.Count; type++)
            foreach (var e in x.Events[type])
                result.Add($"{type}:{e.EventSubtype}", new { Code = e.Actions.Select(a => Name(a.CodeId)).ToArray() });
        return result;
    }

    internal static object Sound(UndertaleSound x) => new { x.Flags, x.Effects, x.Volume, x.Pitch, x.Preload,
                    Type = x.Type?.Content, File = x.File?.Content, x.GroupID,
                    Audio = x.AudioFile is null ? null : ContentFingerprints.Hash(x.AudioFile.Data) };

    private object Font(UndertaleFont x) => new { DisplayName = x.DisplayName?.Content, x.EmSize,
                    x.Bold, x.Italic, x.RangeStart, x.RangeEnd, x.Charset, x.AntiAliasing, x.ScaleX, x.ScaleY,
                    x.Ascender, x.AscenderOffset, x.SDFSpread, x.LineHeight, Region = Region(x.Texture),
                    Glyphs = x.Glyphs.Select(g => new { g.Character, g.SourceX, g.SourceY, g.SourceWidth,
                        g.SourceHeight, g.Shift, g.Offset, Kerning = g.Kerning.Select(k => new { k.Character, k.ShiftModifier }).ToArray() }).ToArray() };

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
        Frames = x.Textures.Select((frame, index) => new { Index = index,
            ContentHash = fingerprints.FramePixels(frame.Texture), RegionHash = Region(frame.Texture) }).ToArray(),
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
        Tiles = new { Count = x.Tiles.Count, ContentHash = ContentFingerprints.Hash(System.Text.Encoding.UTF8.GetBytes(Json(x.Tiles.Select(ContentFingerprints.Tile).ToArray()).GetRawText())) },
        Layers = x.Layers.Select(l => new { Name = l.LayerName?.Content, l.LayerType, l.LayerDepth,
            l.XOffset, l.YOffset, l.HSpeed, l.VSpeed, l.IsVisible, Elements = ContentFingerprints.LayerElements(l.Data) }).ToArray()
    };

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
