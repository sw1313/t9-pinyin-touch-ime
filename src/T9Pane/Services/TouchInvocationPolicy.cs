namespace T9Pane.Services;

/// <summary>
/// 对齐系统触屏键盘：上一次交互是触摸，焦点进了可编辑控件就弹出；
/// 触摸落在当前输入框外面就收起。Chromium FromPoint 停在整页 Document
/// 会报 Unavailable，那不是“没点到”，要用焦点/授权框补判定。
/// </summary>
internal static class TouchInvocationPolicy
{
    public const uint MouseEventFromTouch = 0xFF515700;

    public static bool IsPromotedTouch(ulong extraInfo) =>
        (extraInfo & 0xFFFFFF00UL) == MouseEventFromTouch;

    /// <summary>
    /// 升格触摸 ExtraInfo 低 7 位是 pointer id，用来取真实落点，不能用停住的鼠标坐标。
    /// </summary>
    public static uint PromotedPointerId(ulong extraInfo) =>
        IsPromotedTouch(extraInfo) ? (uint)(extraInfo & 0x7FUL) : 0;

    public static bool IsTextIntent(
        bool focusEntered,
        bool documentHasCaret,
        bool focusedLooksLikeText) =>
        focusEntered || documentHasCaret || focusedLooksLikeText;

    public static PointerInputHit Promote(
        PointerInputHit hit,
        bool textIntent,
        bool authorized,
        bool clickInsideAuthorizedField)
    {
        if (hit != PointerInputHit.Unavailable)
        {
            return hit;
        }

        if (authorized && !clickInsideAuthorizedField)
        {
            return PointerInputHit.Outside;
        }

        if (textIntent || clickInsideAuthorizedField)
        {
            return PointerInputHit.Inside;
        }

        return PointerInputHit.Unavailable;
    }

    /// <summary>
    /// 只有明确点在输入外才算离开点击。悬着的 Unavailable 不能拿去
    /// 搭配 Chromium 的短暂 SetFocus(null) 收键盘。
    /// </summary>
    public static bool CountsAsLeaveClick(PointerInputHit hit) =>
        hit == PointerInputHit.Outside;

    public static bool ShouldKeepWaiting(PointerInputHit hit, bool expired) =>
        hit == PointerInputHit.Unavailable && !expired;

    /// <summary>
    /// 必须在输入框上点过手指。仅有 TSF/UIA 焦点（窗口打开自带光标）不够。
    /// </summary>
    public static bool ShouldShowForTouchFocus(
        bool touchInvocation,
        bool documentHasCaret,
        bool focusEntered,
        bool focusedLooksLikeText,
        bool editTap = false) =>
        touchInvocation
        && editTap
        && (documentHasCaret || focusEntered || focusedLooksLikeText);
}
