namespace Content.Shared._Forge.Geometry;

/// <summary>
/// Visits grid cells along a Bresenham line, excluding the starting cell.
/// Return false from the callback to stop before reaching the end.
/// </summary>
public static class GridLineTraversal
{
    public static bool Visit(Vector2i start, Vector2i end, Func<Vector2i, bool> visit)
    {
        var x = start.X;
        var y = start.Y;
        var dx = Math.Abs(end.X - x);
        var sx = x < end.X ? 1 : -1;
        var dy = -Math.Abs(end.Y - y);
        var sy = y < end.Y ? 1 : -1;
        var error = dx + dy;

        while (x != end.X || y != end.Y)
        {
            var twiceError = 2 * error;
            if (twiceError >= dy)
            {
                error += dy;
                x += sx;
            }

            if (twiceError <= dx)
            {
                error += dx;
                y += sy;
            }

            if (!visit(new Vector2i(x, y)))
                return false;
        }

        return true;
    }
}
