using System.Globalization;
using Jamm.Core.ModKit;
using Jamm.Core.Patching;
using UndertaleModLib;
using UndertaleModLib.Models;
using RoomInstance = UndertaleModLib.Models.UndertaleRoom.GameObject;

namespace Jamm.Core.Building;

internal static class AreaPlacement
{
    private sealed record Placement(AreaDeclaration Declaration, UndertaleRoom Room, ModKitAreaBounds Bounds);
    private sealed record Override(bool? Solid, UndertaleSprite? Mask = null);

    internal static void ValidateSource(UndertaleData data, ModKitManifest package)
    {
        foreach (var area in package.Areas)
        {
            var room = Room(data, area);
            if (area.Anchor.Target is { } anchor)
                Exact(room, area, anchor);
            foreach (var target in area.Removes)
                Exact(room, area, target);
            if (area.Opens is { } opens)
            {
                var target = Exact(room, area, opens.Target);
                if (opens.MaskFrame is int frame)
                    RequireMask(area, target, frame);
                if (opens.Sprite is { } sprite && package.Sprites.All(s => s.Key != sprite))
                    RequireSprite(data, area, sprite);
            }
            foreach (var piece in area.Pieces.Concat(area.Lights))
            {
                // Package-owned objects are added by ModKitBuilder before this validation.
                RequireObject(data, area, piece.Object);
            }
        }
    }

    internal static Dictionary<string, string> Apply(UndertaleData data, BuildPlan plan, IReadOnlyList<ModPatch> patches, IReadOnlyDictionary<string, CodeText> baseCode,
        Dictionary<string, string> recompile, List<string> decisions)
    {
        var declarations = plan.Assets.Where(a => a.Family == "code" && a.Patch is not null && AreaDeclarations.IsDeclaration(a.Name))
            .OrderBy(a => a.Source).ThenBy(a => a.Name, StringComparer.Ordinal)
            .Select(a => AreaDeclarations.Read(a.Name, a.Gml)).ToArray();
        var placements = declarations.Select(d => Locate(data, d, patches)).ToArray();
        Dictionary<RoomInstance, Override> overrides = [];
        Dictionary<string, string> generated = new(StringComparer.Ordinal);

        foreach (var placement in placements)
        {
            var (definition, room, bounds) = placement;
            var area = definition.Area;
            string prefix = $"jamm_area_{ModKitBuilder.SanitizeIdent(definition.Package)}_{ModKitBuilder.SanitizeIdent(area.Id)}";
            int offsetX = checked(bounds.X - area.Bounds.X);
            int offsetY = checked(bounds.Y - area.Bounds.Y);
            foreach (var target in area.Removes)
            {
                var translated = target with { X = checked(target.X + offsetX), Y = checked(target.Y + offsetY) };
                var matches = Matches(room, translated);
                if (matches.Length > 1)
                    throw Error(area, $"removal of {AreaDeclarations.Describe(translated)} is ambiguous. Select a unique instance.");
                if (matches.Length == 1)
                    room.GameObjects.Remove(matches[0]);
                else
                    decisions.Add($"[CGF-AREA-001] Area '{definition.Package}:{area.Id}' found no {AreaDeclarations.Describe(translated)} to remove in {area.Room}.");
            }

            int index = 0;
            foreach (var piece in area.Pieces.Concat(area.Lights))
            {
                var obj = RequireObject(data, area, piece.Object);
                var placed = Place(room, obj, bounds.X + piece.X, bounds.Y + piece.Y);
                placed.ScaleX = (float)piece.ScaleX;
                placed.ScaleY = (float)piece.ScaleY;
                placed.Rotation = (float)piece.Rotation;
                overrides.Add(placed, new(piece.Solid));
                if (piece.Set.Count > 0)
                    AddCreationCode(placed, $"{prefix}_piece_{index}", Assignments(piece.Set));
                index++;
            }
            if (area.Opens is { } opening)
            {
                var target = opening.Target == area.Anchor.Target
                    ? opening.Target with { X = opening.Target.X + offsetX, Y = opening.Target.Y + offsetY }
                    : opening.Target;
                var instance = Exact(room, area, target);
                string assignments = Assignments(opening.Set);
                UndertaleSprite? mask = null;
                if (opening.Sprite is { } sprite)
                {
                    mask = RequireSprite(data, area, sprite);
                    assignments = $"sprite_index = {mask.Name.Content};\n" + assignments;
                }
                if (opening.MaskFrame is int frame)
                    mask = CreateMask(data, RequireMask(area, instance, frame), frame, $"{prefix}_mask");
                if (mask is not null)
                {
                    overrides[instance] = new(null, mask);
                    assignments += $"mask_index = {mask.Name.Content};\n";
                }
                AddCreationCode(instance, $"{prefix}_open", assignments);
            }

            void AddCreationCode(RoomInstance instance, string id, string assignments)
            {
                string name = $"gml_RoomCC_{id}";
                if (data.Code.ByName(name) is not null)
                    throw Error(area, $"creation code '{name}' already exists. Choose a unique area id.");
                string previous = "";
                if (instance.CreationCode is { } original)
                {
                    string existing = original.Name.Content;
                    if (!recompile.TryGetValue(existing, out string? planned))
                    {
                        if (!baseCode.TryGetValue(existing, out var code) || code.Format != "gml")
                            throw Error(area, $"cannot read prior instance creation code '{existing}'. Keep a version with readable GML.");
                        previous = code.Text;
                    }
                    else
                        previous = planned;
                }
                instance.CreationCode = UndertaleCode.CreateEmptyEntry(data, name);
                var body = previous + "\n" + assignments;
                generated.Add(name, body);
                recompile.Add(name, body);
            }
        }

        foreach (var placement in placements.Where(p => p.Declaration.Fish.Count > 0))
        {
            var occupied = placement.Room.GameObjects.Select(i => (i.X, i.Y)).ToHashSet();
            var obstacles = placement.Room.GameObjects.Select(i =>
            {
                var settings = overrides.GetValueOrDefault(i);
                return SolidAt.Create(i, settings?.Solid, settings?.Mask);
            }).OfType<SolidAt>().ToArray();
            foreach (var fish in placement.Declaration.Fish)
            {
                var obj = RequireObject(data, placement.Declaration.Area, fish.Object);
                var region = fish.Within?.Translate(placement.Bounds.X, placement.Bounds.Y) ?? placement.Bounds;
                region = ClipToWater(placement.Room, region);
                var points = WaterPoints(region, fish.Count, obstacles, occupied).ToArray();
                if (points.Length < fish.Count)
                    throw Error(placement.Declaration.Area, $"could not find {fish.Count} free-water spawn points for '{fish.Object}'. Enlarge the spawn bounds or reduce its population.");
                foreach (var (x, y) in points)
                    Place(placement.Room, obj, x, y);
            }
        }
        foreach (var placement in placements)
        {
            var definition = placement.Declaration;
            var area = definition.Area;
            decisions.Add($"Area API: placed '{definition.Package}:{area.Id}' in {area.Room} at ({placement.Bounds.X}, {placement.Bounds.Y}), "
                + $"{area.Pieces.Count} piece(s), {area.Lights.Count} light(s), {definition.Fish.Sum(f => f.Count)} fish.");
        }
        return generated;
    }

    internal static IEnumerable<(int X, int Y)> WaterPoints(ModKitAreaBounds bounds, int count,
        IReadOnlyList<SolidAt> obstacles, HashSet<(int X, int Y)> occupied)
    {
        if (bounds.W <= 0 || bounds.H <= 0) yield break;
        var nearby = obstacles.Where(o => o.Intersects(bounds)).ToArray();
        // A deterministic low-discrepancy sequence visits both sides of the region even at small populations.
        for (int i = 1; i <= 16384 && count > 0; i++)
        {
            int x = bounds.X + (int)(bounds.W * Fraction(i, 2));
            int y = bounds.Y + (int)(bounds.H * Fraction(i, 3));
            if (nearby.Any(o => o.Contains(x, y)) || !occupied.Add((x, y))) continue;
            count--;
            yield return (x, y);
        }

        static double Fraction(int index, int radix)
        {
            double value = 0;
            double factor = 1;
            for (; index > 0; index /= radix)
            {
                factor /= radix;
                value += (index % radix) * factor;
            }
            return value;
        }
    }

    private static ModKitAreaBounds ClipToWater(UndertaleRoom room, ModKitAreaBounds bounds)
    {
        int x = Math.Max(0, bounds.X);
        int y = Math.Max(room.Name.Content == "NewWorld" ? 1294 : 0, bounds.Y);
        return new(x, y, (int)Math.Max(0, Math.Min(bounds.Right, room.Width) - x),
            (int)Math.Max(0, Math.Min(bounds.Bottom, room.Height) - y));
    }

    private static Placement Locate(UndertaleData data, AreaDeclaration definition, IReadOnlyList<ModPatch> patches)
    {
        var area = definition.Area;
        var room = Room(data, area);
        var bounds = area.Bounds;
        if (area.Anchor.Target is { } reference)
        {
            var selection = AreaDeclarations.ResolveAnchor(room.GameObjects.Where(i => i.ObjectDefinition is not null)
                .Select(i => new ModKitAreaTarget(i.ObjectDefinition.Name.Content, i.X, i.Y)),
                reference, AreaDeclarations.RoomEdits(patches, area.Room));
            if (selection is null)
                throw Error(area, $"follow anchor {AreaDeclarations.Describe(reference)} is missing or ambiguous. Keep one anchor or change the ModKit definition.");
            foreach (var alternative in selection.Superseded)
                room.GameObjects.Remove(Exact(room, area, alternative));
            bounds = bounds.Translate(checked(selection.Target.X - reference.X), checked(selection.Target.Y - reference.Y));
        }
        return new(definition, room, bounds);
    }

    private static UndertaleRoom Room(UndertaleData data, ModKitArea area) =>
        data.Rooms.ByName(area.Room) ?? throw Error(area, $"room '{area.Room}' was not found. Select an existing legacy room.");

    private static RoomInstance[] Matches(UndertaleRoom room, ModKitAreaTarget target) => room.GameObjects
        .Where(i => i.ObjectDefinition?.Name.Content == target.Object && i.X == target.X && i.Y == target.Y).ToArray();

    private static RoomInstance Exact(UndertaleRoom room, ModKitArea area, ModKitAreaTarget target)
    {
        var matches = Matches(room, target);
        return matches.Length == 1 ? matches[0] : throw Error(area,
            $"expected one {AreaDeclarations.Describe(target)} in {area.Room}, found {matches.Length}. Select a unique instance.");
    }

    private static UndertaleGameObject RequireObject(UndertaleData data, ModKitArea area, string name) =>
        data.GameObjects.ByName(name) ?? throw Error(area, $"object '{name}' was not found. Select an existing object in the base or package.");

    private static UndertaleSprite RequireSprite(UndertaleData data, ModKitArea area, string name) =>
        data.Sprites.ByName(name) ?? throw Error(area, $"sprite '{name}' was not found. Select a base sprite or a key in sprites[].");

    private static RoomInstance Place(UndertaleRoom room, UndertaleGameObject obj, int x, int y)
    {
        var instance = new RoomInstance { ObjectDefinition = obj, X = x, Y = y };
        room.GameObjects.Add(instance);
        return instance;
    }

    private static string Assignments(IReadOnlyDictionary<string, double> settings) => string.Concat(settings
        .Select(pair => $"{pair.Key} = {pair.Value.ToString("0.################", CultureInfo.InvariantCulture)};\n"));

    private static UndertaleSprite RequireMask(ModKitArea area, RoomInstance instance, int frame)
    {
        var source = instance.ObjectDefinition.TextureMaskId ?? instance.ObjectDefinition.Sprite;
        return source is not null && frame < source.CollisionMasks.Count
            ? source : throw Error(area, $"{instance.ObjectDefinition.Name.Content} has no collision mask frame {frame}. Select an existing mask frame.");
    }

    private static UndertaleSprite CreateMask(UndertaleData data, UndertaleSprite source, int frame, string name)
    {
        if (data.Sprites.ByName(name) is not null)
            throw new InvalidDataException($"Area mask '{name}' already exists. Choose a unique area id.");
        var mask = new UndertaleSprite
        {
            Name = data.Strings.MakeString(name),
            Width = source.Width, Height = source.Height, OriginX = source.OriginX, OriginY = source.OriginY,
            MarginLeft = source.MarginLeft, MarginRight = source.MarginRight,
            MarginTop = source.MarginTop, MarginBottom = source.MarginBottom,
            Transparent = true, BBoxMode = source.BBoxMode, SepMasks = UndertaleSprite.SepMaskType.Precise
        };
        var collision = mask.NewMaskEntry(data);
        source.CollisionMasks[frame].Data.CopyTo(collision.Data, 0);
        mask.CollisionMasks.Add(collision);
        if (source.Textures.FirstOrDefault(t => t?.Texture is not null) is { } texture)
            mask.Textures.Add(new UndertaleSprite.TextureEntry { Texture = texture.Texture });
        data.Sprites.Add(mask);
        return mask;
    }

    private static InvalidDataException Error(ModKitArea area, string message) =>
        new($"Area '{area.Id}' in jamm-mod.json: {message}");
}
