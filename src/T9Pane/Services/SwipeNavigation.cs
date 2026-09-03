namespace T9Pane.Services;

internal enum SwipeDirection
{
    None,
    Left,
    Right,
    Up,
    Down
}

internal static class SwipeNavigation
{
    public static SwipeDirection Detect(double startX, double startY, double endX, double endY, double minimum = 32)
    {
        var dx = endX - startX;
        var dy = endY - startY;
        if (Math.Max(Math.Abs(dx), Math.Abs(dy)) < minimum)
        {
            return SwipeDirection.None;
        }

        if (Math.Abs(dx) >= Math.Abs(dy))
        {
            return dx < 0 ? SwipeDirection.Left : SwipeDirection.Right;
        }

        return dy < 0 ? SwipeDirection.Up : SwipeDirection.Down;
    }

    public static int MovePage(int current, int count, bool forward)
    {
        if (count <= 1)
        {
            return 0;
        }

        return forward
            ? (current + 1) % count
            : (current - 1 + count) % count;
    }

    public static double InitialOffset(double distance, bool forward) =>
        forward ? Math.Abs(distance) : -Math.Abs(distance);
}
