namespace T9Pane.Services;

/// <summary>
/// 标题栏锁定：收起只走关闭键，位置只走左上角拖拽。
/// 焦点离开、换框、换表面都不得自动收起或改位。
/// </summary>
internal static class KeyboardPinPolicy
{
    public static bool ShouldAutoHide(bool pinned) => !pinned;

    public static bool ShouldKeepSessionPosition(
        bool pinned,
        bool hasPosition,
        bool repositionRequested) =>
        hasPosition && (pinned || !repositionRequested);

    public static bool ShouldRestart(bool pinned, bool wouldRestart) =>
        !pinned && wouldRestart;

    public static bool ShouldHideForEmptyRect(bool pinned, bool rectEmpty) =>
        !pinned && rectEmpty;

    public static bool ShouldHideOnUnlock(bool nowPinned, bool stillAuthorized) =>
        !nowPinned && !stillAuthorized;
}
