using System.Text.Json;
using System.Text.RegularExpressions;

namespace Jamm.Core.ModKit;

/// <summary>A rectangle in room coordinates. Right and bottom edges are exclusive.</summary>
public sealed record ModKitAreaBounds(int X, int Y, int W, int H)
{
    internal long Right => (long)X + W;
    internal long Bottom => (long)Y + H;
    internal bool Contains(int x, int y) => x >= X && x < Right && y >= Y && y < Bottom;
    internal bool Intersects(ModKitAreaBounds other) => X < other.Right && other.X < Right && Y < other.Bottom && other.Y < Bottom;
    internal ModKitAreaBounds Translate(int x, int y) => this with { X = checked(X + x), Y = checked(Y + y) };
}

/// <summary>An existing room instance selected by object name and position.</summary>
public sealed record ModKitAreaTarget(string Object, int X, int Y);

/// <summary>Controls whether the area moves with its reference instance.</summary>
public sealed record ModKitAreaAnchor(string Mode, ModKitAreaTarget? Target);

/// <summary>An instance of an existing object, relative to the area bounds.</summary>
public sealed record ModKitAreaPiece(string Object, int X, int Y, double ScaleX, double ScaleY, double Rotation,
    bool? Solid, IReadOnlyDictionary<string, double> Set);

/// <summary>Assignments to append to the selected instance's creation code.</summary>
public sealed record ModKitAreaOpening(ModKitAreaTarget Target, IReadOnlyDictionary<string, double> Set, int? MaskFrame = null, string? Sprite = null);

/// <summary>A room area that is placed after other selected room edits have been merged.</summary>
public sealed record ModKitArea(string Id, string Room, ModKitAreaBounds Bounds, ModKitAreaAnchor Anchor,
    IReadOnlyList<ModKitAreaPiece> Pieces, IReadOnlyList<ModKitAreaPiece> Lights,
    IReadOnlyList<ModKitAreaTarget> Removes, ModKitAreaOpening? Opens);

public static partial class ModKitPackage
{
    [GeneratedRegex("^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.CultureInvariant)]
    private static partial Regex VariableRegex();

    private static ModKitArea ParseArea(JsonElement element, string path)
    {
        var obj = ExpectObject(element, path);
        string id = RequireKey(obj, "id", $"{path}.id");
        string room = OptionalString(obj, "room", $"{path}.room", "NewWorld");
        if (string.IsNullOrWhiteSpace(room))
            throw Error($"{path}.room", "Enter the name of an existing room.");
        if (!TryGetNonNull(obj, "bounds", out var boundsElement))
            throw Error($"{path}.bounds", "Enter area bounds with x, y, w and h.");
        var value = ExpectObject(boundsElement, $"{path}.bounds");
        var bounds = new ModKitAreaBounds(
            RequireInt(value, "x", $"{path}.bounds.x"), RequireInt(value, "y", $"{path}.bounds.y"),
            RequireInt(value, "w", $"{path}.bounds.w", 1), RequireInt(value, "h", $"{path}.bounds.h", 1));
        if (bounds.Right > int.MaxValue || bounds.Bottom > int.MaxValue)
            throw Error($"{path}.bounds", "The right and bottom edges must fit in room coordinates.");

        var anchor = new ModKitAreaAnchor("fixed", null);
        if (TryGetNonNull(obj, "anchor", out var anchorElement))
        {
            var definition = ExpectObject(anchorElement, $"{path}.anchor");
            string mode = RequireNonEmptyString(definition, "mode", $"{path}.anchor.mode");
            if (mode is not ("fixed" or "follow"))
                throw Error($"{path}.anchor.mode", "Choose 'fixed' or 'follow'.");
            anchor = new(mode, mode == "follow" ? ParseAreaTarget(definition, $"{path}.anchor") : null);
        }

        var pieces = ParseArray(obj, "pieces", $"{path}.pieces", (el, p) => ParseAreaPiece(el, p, bounds));
        var lights = ParseArray(obj, "lights", $"{path}.lights", (el, p) => ParseAreaPiece(el, p, bounds));
        var removes = ParseArray(obj, "removes", $"{path}.removes", ParseAreaTarget);
        if (removes.Any(target => !bounds.Contains(target.X, target.Y)))
            throw Error($"{path}.removes", "Every removal must select a position within the area's bounds.");
        if (removes.Distinct().Count() != removes.Count || anchor.Target is { } followed && removes.Contains(followed))
            throw Error($"{path}.removes", "Select each instance once and keep the follow anchor.");

        ModKitAreaOpening? opens = null;
        if (TryGetNonNull(obj, "opens", out var opensElement))
        {
            var definition = ExpectObject(opensElement, $"{path}.opens");
            var target = ParseAreaTarget(definition, $"{path}.opens");
            var assignments = ParseAssignments(definition, $"{path}.opens");
            int? maskFrame = TryGetNonNull(definition, "maskFrame", out _) ? RequireInt(definition, "maskFrame", $"{path}.opens.maskFrame", 0) : null;
            string? sprite = TryGetNonNull(definition, "sprite", out _) ? RequireNonEmptyString(definition, "sprite", $"{path}.opens.sprite") : null;
            if ((assignments.Count == 0 && maskFrame is null && sprite is null) || removes.Contains(target))
                throw Error($"{path}.opens", "Supply 'set' assignments, a 'maskFrame' or a 'sprite', and keep the selected instance.");
            opens = new(target, assignments, maskFrame, sprite);
        }
        return new(id, room, bounds, anchor, pieces, lights, removes, opens);
    }

    private static ModKitAreaPiece ParseAreaPiece(JsonElement element, string path, ModKitAreaBounds bounds)
    {
        var obj = ExpectObject(element, path);
        var target = ParseAreaTarget(obj, path);
        if (target.X < 0 || target.X >= bounds.W || target.Y < 0 || target.Y >= bounds.H)
            throw Error(path, "Piece coordinates are relative to the bounds and must be inside them.");
        double scale = OptionalDouble(obj, "scale", $"{path}.scale", 1, -100, 100);
        double sx = OptionalDouble(obj, "scaleX", $"{path}.scaleX", scale, -100, 100);
        double sy = OptionalDouble(obj, "scaleY", $"{path}.scaleY", scale, -100, 100);
        if (Math.Abs(sx) < 0.001 || Math.Abs(sy) < 0.001)
            throw Error(path, "Each scale must have magnitude between 0.001 and 100.");
        double rotation = OptionalDouble(obj, "rotation", $"{path}.rotation", 0, -360, 360);
        return new(target.Object, target.X, target.Y, sx, sy, rotation,
            TryGetNonNull(obj, "solid", out _) ? OptionalBool(obj, "solid", $"{path}.solid", false) : null,
            ParseAssignments(obj, path));
    }

    private static ModKitAreaTarget ParseAreaTarget(JsonElement element, string path)
    {
        var obj = ExpectObject(element, path);
        return new(RequireNonEmptyString(obj, "object", $"{path}.object"),
            RequireInt(obj, "x", $"{path}.x"), RequireInt(obj, "y", $"{path}.y"));
    }

    private static SortedDictionary<string, double> ParseAssignments(JsonElement obj, string path)
    {
        SortedDictionary<string, double> result = new(StringComparer.Ordinal);
        if (!TryGetNonNull(obj, "set", out var element))
            return result;
        foreach (var variable in ExpectObject(element, $"{path}.set").EnumerateObject())
        {
            if (!VariableRegex().IsMatch(variable.Name) || result.ContainsKey(variable.Name))
                throw Error($"{path}.set", $"'{variable.Name}' needs to be a unique GML variable name.");
            result.Add(variable.Name, variable.Value.ValueKind switch
            {
                JsonValueKind.True => 1,
                JsonValueKind.False => 0,
                _ => ReadNumber(variable.Value, $"{path}.set.{variable.Name}")
            });
        }
        return result;
    }

    private static int RequireInt(JsonElement obj, string field, string path, int min = int.MinValue)
    {
        if (!TryGetNonNull(obj, field, out _))
            throw Error(path, $"Enter an integer for '{field}'.");
        return OptionalInt(obj, field, path, 0, min);
    }
}
