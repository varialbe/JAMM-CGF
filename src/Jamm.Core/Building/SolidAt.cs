using UndertaleModLib.Models;

namespace Jamm.Core.Building;

// Inverse-transform a room point into the collision mask at its sprite origin.
internal sealed class SolidAt
{
    private readonly UndertaleRoom.GameObject instance;
    private readonly UndertaleSprite sprite;
    private readonly double cos;
    private readonly double sin;
    private readonly double left;
    private readonly double top;
    private readonly double right;
    private readonly double bottom;

    private SolidAt(UndertaleRoom.GameObject instance, UndertaleSprite sprite)
    {
        this.instance = instance;
        this.sprite = sprite;
        double angle = instance.Rotation * Math.PI / 180;
        cos = Math.Cos(angle);
        sin = Math.Sin(angle);
        var corners = new[]
        {
            World(sprite.MarginLeft, sprite.MarginTop), World(sprite.MarginRight + 1, sprite.MarginTop),
            World(sprite.MarginLeft, sprite.MarginBottom + 1), World(sprite.MarginRight + 1, sprite.MarginBottom + 1)
        };
        left = corners.Min(p => p.X);
        right = corners.Max(p => p.X);
        top = corners.Min(p => p.Y);
        bottom = corners.Max(p => p.Y);
    }

    internal static SolidAt? Create(UndertaleRoom.GameObject instance, bool? solid = null, UndertaleSprite? mask = null)
    {
        var obj = instance.ObjectDefinition;
        if (obj is null || !(solid ?? IsSolid(obj)) || instance.ScaleX == 0 || instance.ScaleY == 0)
            return null;
        var sprite = mask ?? obj.TextureMaskId ?? obj.Sprite;
        return sprite is { Width: > 0, Height: > 0 } ? new(instance, sprite) : null;
    }

    private static bool IsSolid(UndertaleGameObject obj)
    {
        HashSet<UndertaleGameObject> seen = [];
        for (var cursor = obj; cursor is not null && seen.Add(cursor); cursor = cursor.ParentId)
            if (cursor.Solid || cursor.Name.Content == "Collidable") return true;
        return false;
    }

    private (double X, double Y) World(int x, int y)
    {
        double dx = (x - sprite.OriginX) * instance.ScaleX;
        double dy = (y - sprite.OriginY) * instance.ScaleY;
        return (instance.X + dx * cos + dy * sin, instance.Y - dx * sin + dy * cos);
    }

    internal bool Intersects(ModKit.ModKitAreaBounds bounds) =>
        left < bounds.Right && right >= bounds.X && top < bounds.Bottom && bottom >= bounds.Y;

    internal (double Left, double Top, double Right, double Bottom) Bounds => (left, top, right, bottom);

    internal bool Contains(int x, int y)
    {
        if (x < left || x > right || y < top || y > bottom)
            return false;
        double dx = (double)x - instance.X;
        double dy = (double)y - instance.Y;
        int sx = (int)Math.Floor((dx * cos - dy * sin) / instance.ScaleX + sprite.OriginX + 1e-8);
        int sy = (int)Math.Floor((dx * sin + dy * cos) / instance.ScaleY + sprite.OriginY + 1e-8);
        if (sx < sprite.MarginLeft || sx > sprite.MarginRight || sy < sprite.MarginTop || sy > sprite.MarginBottom)
            return false;

        if (sprite.CollisionMasks.Count == 0 || sprite.SepMasks != UndertaleSprite.SepMaskType.Precise)
            return true;
        if (sx < 0 || sx >= sprite.Width || sy < 0 || sy >= sprite.Height)
            return true;
        int stride = ((int)sprite.Width + 7) / 8;
        int offset = sy * stride + sx / 8;
        int bit = 0x80 >> (sx & 7);
        return sprite.CollisionMasks.Any(m => offset >= m.Data.Length || (m.Data[offset] & bit) != 0);
    }
}
