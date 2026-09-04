namespace T9Pane.Services;

/// <summary>
/// 候选条只画一屏可见格。官方条大约 8～10 个词，展开盘才拉全量。
/// 每键 new 120 个 Button 会把按下当帧堵在 UI 线程上。
/// </summary>
internal static class CandidateBarSlots
{
    public const int Visible = 10;
    public const int BarQueryTake = 16;
    public const int FallQueryTake = 120;

    public static int QueryTake(bool expanded) =>
        expanded ? FallQueryTake : BarQueryTake;

    public static int PaintCount(int available, bool expanded) =>
        expanded
            ? Math.Max(0, available)
            : Math.Min(Visible, Math.Max(0, available));

    public static bool ShowsMore(int available) =>
        available > Visible;
}
