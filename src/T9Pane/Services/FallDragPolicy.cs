namespace T9Pane.Services;

/// <summary>
/// 瀑布流跟手：只有指针按住才许拖。惯性飞行时鼠标划过不能把列表吸过去。
/// </summary>
internal static class FallDragPolicy
{
    /// <summary>
    /// 没按住不能跟手。按住才能接住正在飞的列表。
    /// </summary>
    public static bool Follows(bool pressed) => pressed;

    /// <summary>
    /// 已经在跟踪（按下起点或正在拖），但指针已经抬起：残留下的跟手必须丢掉。
    /// </summary>
    public static bool ShouldDrop(bool pressed, bool tracking) =>
        !pressed && tracking;

    /// <summary>
    /// 丢掉残留下的跟手时：已经在飞就别再甩一次；还没飞才按速度甩出去。
    /// </summary>
    public static bool FlingAfterDrop(bool wasDragging, bool inertiaRunning) =>
        wasDragging && !inertiaRunning;

    /// <summary>
    /// 触摸已经自己走完松手甩动。提升出来的鼠标按下不能再停惯性，也不能留下起点。
    /// </summary>
    public static bool IgnorePromotedTouch(bool fromTouch) => fromTouch;
}
