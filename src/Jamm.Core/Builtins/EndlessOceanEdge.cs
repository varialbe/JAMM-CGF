using Jamm.Core.Building;
using UndertaleModLib.Models;

namespace Jamm.ModMaker;

// The connector starts at the last collidable terrain column. Fish and scenery
// start beyond the original room and any placed artwork farther east.
internal readonly record struct EndlessOceanEdge(int X, int Floor, int ContentChunk, uint End)
{
    internal const int ChunkWidth = 2048;

    internal static EndlessOceanEdge Find(UndertaleRoom world, uint sourceWidth)
    {
        uint protectedEdge = ProtectedEastEdge(world, sourceWidth);
        var (x, floor) = LastTerrain(world) ?? (checked((int)protectedEdge), checked((int)world.Height - 1));
        int contentChunk = Math.Max(3, checked(((int)protectedEdge - x + ChunkWidth - 1) / ChunkWidth));
        uint end = checked((uint)(x + (contentChunk + EndlessOceanMod.ChunkCount) * ChunkWidth));
        return new(x, floor, contentChunk, end);
    }

    internal static uint ProtectedEastEdge(UndertaleRoom world, uint sourceWidth)
    {
        double east = sourceWidth;
        foreach (var placement in world.GameObjects)
        {
            east = Math.Max(east, placement.X);
            if (placement.ObjectDefinition is not { } obj)
                continue;

            Include(obj.Sprite);
            Include(obj.TextureMaskId);

            void Include(UndertaleSprite? sprite)
            {
                if (sprite is null)
                    return;
                double radians = placement.Rotation * Math.PI / 180;
                double horizontal = placement.ScaleX * Math.Cos(radians);
                double vertical = placement.ScaleY * Math.Sin(radians);
                double right = Math.Max(-sprite.OriginX * horizontal, (sprite.Width - (double)sprite.OriginX) * horizontal);
                double bottom = Math.Max(-sprite.OriginY * vertical, (sprite.Height - (double)sprite.OriginY) * vertical);
                east = Math.Max(east, placement.X + right + bottom);
            }
        }
        return checked((uint)Math.Ceiling(east));
    }

    private static (int X, int Floor)? LastTerrain(UndertaleRoom world)
    {
        int last = 0;
        int floor = 0;
        var rocks = world.GameObjects.Select(i => SolidAt.Create(i)).OfType<SolidAt>()
            .OrderByDescending(rock => rock.Bounds.Right);
        foreach (var rock in rocks)
        {
            var bounds = rock.Bounds;
            int right = (int)Math.Floor(bounds.Right);
            if (right < last)
                break;
            int top = Math.Max(1400, (int)Math.Ceiling(bounds.Top));
            int bottom = Math.Min(checked((int)world.Height - 1), (int)Math.Floor(bounds.Bottom));
            if (bottom < top)
                continue;

            for (int x = right; x >= Math.Max(last, (int)Math.Ceiling(bounds.Left)); x--)
            {
                int y = top;
                while (y <= bottom && !rock.Contains(x, y))
                    y++;
                if (y > bottom)
                    continue;
                floor = x == last && floor != 0 ? Math.Min(floor, y) : y;
                last = x;
                break;
            }
        }
        return floor != 0 ? (last + 1, floor) : null;
    }
}
