namespace T9Pane.Services;

/// <summary>
/// 有输入就跟光标摆；没在输入才钉住当前顶边。
/// 解锁后必须立刻丢掉锁定期间攒下的拖拽/会话锚点。
/// </summary>
internal static class KeyboardAnchorPolicy
{
    public static bool ShouldFollowInput(
        bool pinned,
        bool hasInputCaret) =>
        !pinned && hasInputCaret;

    public static bool ShouldClearDragAnchorOnUnlock(bool wasPinned, bool nowPinned) =>
        wasPinned && !nowPinned;

    public static bool ShouldRelayoutOnUnlock(bool nowPinned, bool hasInputCaret) =>
        !nowPinned && hasInputCaret;
}
