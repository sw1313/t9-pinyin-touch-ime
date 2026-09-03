namespace T9Pane.Services;

/// <summary>
/// 瀑布流按像素滚：格子排成固定列数的长条，视口裁切，手指移动多少内容就走多少。
/// </summary>
internal static class FallFlow
{
    public const int Columns = 5;

    public static int RowCount(int itemCount) =>
        itemCount <= 0 ? 0 : (itemCount + Columns - 1) / Columns;

    public static double ContentHeight(int itemCount, double cellHeight) =>
        RowCount(itemCount) * cellHeight;

    public static bool Fits(double contentHeight, double viewportHeight) =>
        contentHeight <= viewportHeight + 0.5;

    public static double Clamp(double offset, double contentHeight, double viewportHeight)
    {
        if (Fits(contentHeight, viewportHeight))
        {
            return 0;
        }

        var max = Math.Max(0, contentHeight - viewportHeight);
        return Math.Clamp(offset, 0, max);
    }

    public static double Shift(double offset, double delta, double contentHeight, double viewportHeight) =>
        Clamp(offset + delta, contentHeight, viewportHeight);

    public static double Wheel(int wheelDelta, double cellHeight) =>
        -wheelDelta / 120.0 * cellHeight;
}
