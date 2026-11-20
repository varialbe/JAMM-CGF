namespace Jamm.Core.Building;

internal sealed class TextureSkyline(int size)
{
    private readonly List<(int X, int Y, int Width)> segments = [(0, 0, size)];

    internal bool TryPlace(int width, int height, out int x, out int y)
    {
        int best = -1;
        x = 0;
        y = size;
        for (int i = 0; i < segments.Count; i++)
        {
            var segment = segments[i];
            int right = segment.X + width;
            if (right > size) break;
            int top = segment.Y;
            for (int j = i + 1; j < segments.Count && segments[j].X < right; j++)
                top = Math.Max(top, segments[j].Y);
            if (top >= y || top + height > size) continue;
            best = i;
            x = segment.X;
            y = top;
        }
        if (best < 0) return false;

        int end = x + width;
        int next = best;
        while (next < segments.Count && segments[next].X + segments[next].Width <= end) next++;
        segments.RemoveRange(best, next - best);
        if (best < segments.Count && segments[best].X < end)
        {
            var tail = segments[best];
            segments[best] = (end, tail.Y, tail.X + tail.Width - end);
        }
        segments.Insert(best, (x, y + height, width));
        for (int i = Math.Max(1, best); i < segments.Count && i <= best + 1;)
        {
            var left = segments[i - 1];
            var right = segments[i];
            if (left.Y == right.Y)
            {
                segments[i - 1] = (left.X, left.Y, left.Width + right.Width);
                segments.RemoveAt(i);
                best--;
            }
            else i++;
        }
        return true;
    }
}
