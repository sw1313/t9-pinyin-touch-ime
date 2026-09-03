namespace T9Pane.Services;

/// <summary>
/// 窄数字盘上也要给关闭键留出固定列，不能跟标签挤在一起。
/// </summary>
internal static class TitleBarLayout
{
    public const double CloseWidth = 36;

    public static bool CloseReserved(double windowWidth, double leadingWidth) =>
        windowWidth - leadingWidth >= CloseWidth;
}
