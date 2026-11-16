using System.Collections;
using System.Reflection;
using System.Text.Json;
using Jamm.Core.Patching;
using UndertaleModLib;
using UndertaleModLib.Models;

namespace Jamm.Core.Building;

internal sealed class StructuredAssets(UndertaleData data, IReadOnlyList<UndertaleData> sources)
{
    private static readonly Dictionary<string, string> Aliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["parent"] = "ParentId", ["mask"] = "TextureMaskId", ["vertices"] = "PhysicsVertices",
        ["creationCode"] = "CreationCodeId", ["object"] = "ObjectDefinition", ["resource"] = "ObjectDefinition",
        ["background"] = "BackgroundDefinition", ["preCreate"] = "PreCreateCode", ["code"] = "Code"
    };

    internal void Apply(object target, JsonElement snapshot, int? preferredSource = null)
    {
        if (target is UndertaleRoom.Tile tile && snapshot.TryGetProperty("spriteMode", out var mode)) tile.spriteMode = mode.GetBoolean();
        foreach (var field in snapshot.EnumerateObject())
        {
            if (target is UndertaleTimeline timeline && field.Name == "moments") { Moments(timeline, field.Value, preferredSource); continue; }
            if (target is UndertaleFont font)
            {
                if (field.Name == "glyphs")
                {
                    font.Glyphs.Clear();
                    foreach (var group in field.Value.EnumerateObject().OrderBy(g => ushort.Parse(g.Name, System.Globalization.CultureInfo.InvariantCulture)))
                        foreach (var value in group.Value.EnumerateArray())
                        { var glyph = new UndertaleFont.Glyph(); Apply(glyph, value); font.Glyphs.Add(glyph); }
                    continue;
                }
                if (field.Name == "region")
                {
                    if (font.Texture is not null && field.Value.GetProperty("geometry") is { ValueKind: JsonValueKind.Object } geometry) Apply(font.Texture, geometry);
                    continue;
                }
            }
            if (target is UndertaleGameObject obj && field.Name == "events") { Events(obj, field.Value, preferredSource); continue; }
            if (target is UndertaleRoom room)
            {
                if (field.Name == "instances") { Instances(room, field.Value, preferredSource); continue; }
                if (field.Name == "tiles") { Tiles(room, field.Value, preferredSource); continue; }
                if (field.Name == "layers") { Layers(room, field.Value); continue; }
            }
            string propertyName = Aliases.GetValueOrDefault(field.Name, field.Name);
            if (target is UndertaleScript && field.Name == "code") propertyName = "Code";
            if (target is UndertaleRoom.View && field.Name == "object") propertyName = "ObjectId";
            if (target is UndertaleRoom.GameObject && field.Name == "creationCode") propertyName = "CreationCode";
            PropertyInfo? property = target.GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
            if (property is null && target.GetType().GetField(propertyName, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase) is { } publicField)
            { publicField.SetValue(target, ConvertValue(field.Value, publicField.FieldType)); continue; }
            if (property is null) throw new InvalidDataException($"Cannot apply field '{field.Name}' on {target.GetType().Name}. This archive format is not supported by the builder.");
            Type type = property.PropertyType;
            if (typeof(IList).IsAssignableFrom(type))
            {
                var list = (IList)property.GetValue(target)!;
                if (field.Value.ValueKind != JsonValueKind.Array) throw new InvalidDataException($"Expected an ordered array for '{field.Name}'.");
                Type elementType = type.GetInterfaces().Append(type).First(t => t.IsGenericType && t.GetGenericTypeDefinition() == typeof(IList<>)).GenericTypeArguments[0];
                list.Clear();
                foreach (var element in field.Value.EnumerateArray())
                {
                    if (element.ValueKind == JsonValueKind.Object)
                    { var item = Activator.CreateInstance(elementType)!; Apply(item, element); list.Add(item); }
                    else list.Add(ConvertValue(element, elementType));
                }
            }
            else if (target is UndertaleRoom.Tile roomTile && field.Name == "resource")
            {
                string? name = field.Value.GetString();
                roomTile.ObjectDefinition = name is null ? null : roomTile.spriteMode
                    ? Required(data.Sprites, name, "room tile sprite") : Required(data.Backgrounds, name, "room tile background");
            }
            else if (property.CanWrite) property.SetValue(target, ConvertValue(field.Value, type));
            else throw new InvalidDataException($"Cannot update '{field.Name}' on {target.GetType().Name}.");
        }
    }

    private object? ConvertValue(JsonElement value, Type type)
    {
        if (value.ValueKind == JsonValueKind.Null) return null;
        if (type == typeof(UndertaleString)) return data.Strings.MakeString(value.GetString()!);
        if (typeof(UndertaleNamedResource).IsAssignableFrom(type))
        {
            string name = value.GetString()!;
            return ResourceCatalog.Tables(data).Values.SelectMany(t => t.Cast<object>())
                .SingleOrDefault(x => type.IsInstanceOfType(x) && ResourceCatalog.Name(x) == name)
                ?? throw new InvalidDataException($"Missing {type.Name} '{name}'. Keep the referenced asset in the resolution file.");
        }
        return JsonSerializer.Deserialize(value.GetRawText(), type);
    }

    private void Events(UndertaleGameObject obj, JsonElement events, int? preferredSource)
    {
        var templates = new[] { (Object: obj, Data: data) }.Concat(SourceOrder(preferredSource)
            .SelectMany(s => s.GameObjects.Where(o => o.Name.Content == obj.Name.Content).Select(o => (Object: o, Data: s))))
            .SelectMany(o => o.Object.Events.SelectMany((list, type) => list.SelectMany(e => e.Actions.Select(a =>
                (Type: type, Subtype: type == 4 && e.EventSubtype < o.Data.GameObjects.Count
                    ? o.Data.GameObjects[(int)e.EventSubtype].Name.Content : e.EventSubtype.ToString(System.Globalization.CultureInfo.InvariantCulture), Action: a)))))
            .ToArray();
        foreach (var list in obj.Events) list.Clear();
        foreach (var entry in events.EnumerateObject())
        {
            int colon = entry.Name.IndexOf(':');
            int type = int.Parse(entry.Name[..colon], System.Globalization.CultureInfo.InvariantCulture);
            uint subtype = type == 4 ? checked((uint)data.GameObjects.IndexOf(Required(data.GameObjects, entry.Name[(colon + 1)..], $"collision event on object '{obj.Name.Content}'")))
                : uint.Parse(entry.Name[(colon + 1)..], System.Globalization.CultureInfo.InvariantCulture);
            if (type < 0 || type >= obj.Events.Count) throw new InvalidDataException($"Invalid event type '{entry.Name}'.");
            var ev = new UndertaleGameObject.Event { EventSubtype = subtype };
            foreach (var code in entry.Value.GetProperty("code").EnumerateArray())
            {
                string? name = code.GetString();
                var action = templates.LastOrDefault(t => t.Type == type && t.Subtype == entry.Name[(colon + 1)..] && t.Action.CodeId?.Name.Content == name).Action;
                // These are the standard GameMaker execute-code action settings.
                action ??= new() { LibID = 1, ID = 603, Kind = 7, UseApplyTo = true, ExeType = 2,
                    ActionName = data.Strings.MakeString(""), ArgumentCount = 1, Who = -1 };
                // Do not share an action between events; only CodeId is a table reference.
                var copy = new UndertaleGameObject.EventAction { LibID = action.LibID, ID = action.ID, Kind = action.Kind,
                    UseRelative = action.UseRelative, IsQuestion = action.IsQuestion, UseApplyTo = action.UseApplyTo,
                    ExeType = action.ExeType, ActionName = data.Strings.MakeString(action.ActionName?.Content ?? ""), ArgumentCount = action.ArgumentCount,
                    Who = action.Who, Relative = action.Relative, IsNot = action.IsNot, UnknownAlwaysZero = action.UnknownAlwaysZero,
                    CodeId = name is null ? null : Required(data.Code, name, $"event on object '{obj.Name.Content}'") };
                ev.Actions.Add(copy);
            }
            obj.Events[type].Add(ev);
        }
    }

    private void Moments(UndertaleTimeline timeline, JsonElement map, int? preferredSource)
    {
        var templates = new[] { timeline }.Concat(SourceOrder(preferredSource).SelectMany(s => s.Timelines.Where(t => t.Name.Content == timeline.Name.Content)))
            .SelectMany(t => t.Moments).SelectMany(m => m.Event.Select(a => (m.Step, Action: a))).ToArray();
        timeline.Moments.Clear();
        foreach (var group in map.EnumerateObject().OrderBy(g => uint.Parse(g.Name, System.Globalization.CultureInfo.InvariantCulture)))
            foreach (var value in group.Value.EnumerateArray())
            {
                var moment = new UndertaleTimeline.UndertaleTimelineMoment { Step = value.GetProperty("step").GetUInt32(), Event = [] };
                foreach (var code in value.GetProperty("code").EnumerateArray())
                {
                    string? name = code.GetString();
                    var action = templates.LastOrDefault(a => a.Step == moment.Step && a.Action.CodeId?.Name.Content == name).Action;
                    moment.Event.Add(new()
                    {
                        LibID = action?.LibID ?? 1, ID = action?.ID ?? 603, Kind = action?.Kind ?? 7,
                        UseRelative = action?.UseRelative ?? false, IsQuestion = action?.IsQuestion ?? false,
                        UseApplyTo = action?.UseApplyTo ?? true, ExeType = action?.ExeType ?? 2,
                        ActionName = data.Strings.MakeString(action?.ActionName?.Content ?? ""), ArgumentCount = action?.ArgumentCount ?? 1,
                        Who = action?.Who ?? -1, Relative = action?.Relative ?? false, IsNot = action?.IsNot ?? false,
                        UnknownAlwaysZero = action?.UnknownAlwaysZero ?? 0,
                        CodeId = name is null ? null : Required(data.Code, name, $"moment on timeline '{timeline.Name.Content}'")
                    });
                }
                timeline.Moments.Add(moment);
            }
    }

    private void Instances(UndertaleRoom room, JsonElement map, int? preferredSource)
    {
        var order = SourceOrder(preferredSource).Reverse().SelectMany(s => s.Rooms.Where(r => r.Name.Content == room.Name.Content)).SelectMany(r => r.GameObjects).Concat(room.GameObjects)
            .Select(i => AssetFields.Json(new object?[] { i.ObjectDefinition?.Name.Content, i.X, i.Y }).GetRawText()).ToArray();
        var values = OrderedValues(map, order);
        room.GameObjects.Clear();
        foreach (var value in values)
        { var item = new UndertaleRoom.GameObject(); Apply(item, value); room.GameObjects.Add(item); }
    }
    private void Tiles(UndertaleRoom room, JsonElement map, int? preferredSource)
    {
        var order = SourceOrder(preferredSource).Reverse().SelectMany(s => s.Rooms.Where(r => r.Name.Content == room.Name.Content)).SelectMany(r => r.Tiles).Concat(room.Tiles)
            .Select(t => AssetFields.Tiles([t]).Keys.Single()).ToArray();
        var values = OrderedValues(map, order);
        room.Tiles.Clear();
        foreach (var value in values)
        { var item = new UndertaleRoom.Tile(); Apply(item, value); room.Tiles.Add(item); }
    }
    private IEnumerable<UndertaleData> SourceOrder(int? preferredSource) => sources.Where((_, i) => i != preferredSource)
        .Concat(preferredSource is { } index ? new[] { sources[index] } : []);

    private static T Required<T>(IEnumerable<T> table, string name, string context) where T : UndertaleNamedResource =>
        table.SingleOrDefault(a => a.Name.Content == name)
            ?? throw new InvalidDataException($"Cannot rebuild {context} in the merged archive: referenced {typeof(T).Name} '{name}' is missing. Keep that dependency in --resolve or regenerate the referring mod's patch.");

    private static IReadOnlyList<JsonElement> OrderedValues(JsonElement map, IEnumerable<string> originalOrder)
    {
        var pending = map.EnumerateObject().ToDictionary(p => p.Name, p => new Queue<JsonElement>(p.Value.EnumerateArray()));
        List<JsonElement> result = [];
        foreach (string key in originalOrder)
            if (pending.TryGetValue(key, out var values) && values.Count > 0) result.Add(values.Dequeue());
        foreach (var remaining in pending.Values) result.AddRange(remaining);
        return result;
    }
    private void Layers(UndertaleRoom room, JsonElement map)
    {
        // GM1.4 has no layers. Reject unsupported reconstruction rather than silently dropping fields.
        if (map.EnumerateObject().Any())
            throw new InvalidDataException($"Room '{room.Name.Content}' has GMS2 layers. This builder currently supports the legacy GM1.4 room format; use the documented legacy base.");
        room.Layers.Clear();
    }
}
