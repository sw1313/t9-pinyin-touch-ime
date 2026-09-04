using T9Pane.Native;

namespace T9Pane.Services;

/// <summary>
/// 系统触屏键盘认的是「最近一次交互是手指」，不是鼠标指针位置。
/// 平板上触摸经常不移动光标，GetCursorPos 会一直停在旧坐标。
/// </summary>
internal static class OverlayTouch
{
    public const int RecentWindowMs = 800;
    private static long _lastTicks;
    private static Action? _contacted;

    public static event Action? Contacted
    {
        add => _contacted += value;
        remove => _contacted -= value;
    }

    public static void Note()
    {
        Interlocked.Exchange(ref _lastTicks, DateTime.UtcNow.Ticks);
        SipLifecycle.Shared.NoteOverlayContact();
        _contacted?.Invoke();
    }

    public static bool IsRecent(int windowMs = RecentWindowMs)
    {
        _ = windowMs;
        return SipLifecycle.Shared.OwnsOverlayContact(false);
    }
}

/// <summary>
/// 官方 IInputPanelInvocationConfiguration.RequireTouchInEditControl：
/// 必须在输入框上点一下才弹，程序把焦点放进框、或点列表后窗口自己聚焦编辑框都不弹。
/// </summary>
internal static class EditTouch
{
    public const int SettleMilliseconds = 250;
    public const int RecentWindowMs = 800;
    private static int _kind;
    private static long _lastTicks;

    public const int KindNone = 0;
    public const int KindPending = 1;
    public const int KindOnEdit = 2;
    public const int KindOnLeave = 3;

    public static void Note(bool onText, bool onLeave)
    {
        var kind = EditTouchPolicy.Classify(onText, onLeave);
        Interlocked.Exchange(ref _kind, kind);
        Interlocked.Exchange(ref _lastTicks, DateTime.UtcNow.Ticks);
        var gesture = kind == KindOnLeave
            ? SipGesture.OnLeave
            : kind == KindOnEdit
                ? SipGesture.OnEdit
                : SipGesture.None;
        SipLifecycle.Shared.NoteSettled(gesture);
    }

    public static bool AllowsShow(bool nowLooksLikeText) =>
        SipLifecycle.Shared.HasRecentExternalGesture() && nowLooksLikeText;
}

internal static class EditTouchPolicy
{
    public static int Classify(bool onText, bool onLeave)
    {
        if (onLeave)
        {
            return EditTouch.KindOnLeave;
        }

        return onText ? EditTouch.KindOnEdit : EditTouch.KindPending;
    }

    public static bool AllowsShow(
        int kind,
        bool nowLooksLikeText,
        int ageMs,
        int settleMs = EditTouch.SettleMilliseconds,
        int recentMs = EditTouch.RecentWindowMs)
    {
        if (kind == EditTouch.KindOnLeave || ageMs < 0)
        {
            return false;
        }

        if (kind == EditTouch.KindOnEdit && ageMs <= recentMs)
        {
            return true;
        }

        return kind == EditTouch.KindPending
            && nowLooksLikeText
            && ageMs <= settleMs;
    }
}

/// <summary>
/// 官方一次 WM_POINTER 调用（不是点我们键盘）。
/// TipTsfHelper 每次焦点 TryShow，按当前 GetTextExt 摆；打字同行不挪。
/// </summary>
internal static class ManualTap
{
    public const int RecentWindowMs = 800;

    public static void Note() =>
        SipLifecycle.Shared.NoteExternalTouch();

    public static bool IsRecent(int windowMs = RecentWindowMs) =>
        SipLifecycle.Shared.HasRecentExternalGesture(windowMs);
}

internal static class TouchModality
{
    public const int RecentWindowMs = 2500;

    public static void Note() =>
        SipLifecycle.Shared.NoteExternalTouch();

    public static bool IsRecent(int windowMs = RecentWindowMs) =>
        SipLifecycle.Shared.HasRecentTouch(windowMs);
}

/// <summary>
/// 平板/触摸屏的命中放宽。只放大“焦点框套落点”，不放大 FromPoint 祖先行走，
/// 避免把输入框旁边的按钮算进来。
/// </summary>
internal static class TouchDevicePolicy
{
    public const int MouseEdgeTolerance = 3;
    public const int TouchEdgeTolerance = 16;
    private const int SmConvertibleSlateMode = 0x2003;
    private const int SmMaximumTouches = 95;
    private const int SmDigitizer = 94;
    private const int NidIntegratedTouch = 0x01;
    private const int NidExternalTouch = 0x02;

    public static bool PreferTouchHitSlop(bool slateOrTouchScreen) => slateOrTouchScreen;

    public static int EdgeTolerance(bool preferTouch) =>
        preferTouch ? TouchEdgeTolerance : MouseEdgeTolerance;

    /// <summary>
    /// 有触摸屏，并且处于平板模式，或刚用过手指。接了键盘盖时
    /// SM_CONVERTIBLESLATEMODE 会变成桌面，仍应按触屏键盘来。
    /// </summary>
    public static bool PreferTouchInvocation(
        bool hasTouchScreen,
        bool slateMode,
        bool recentTouch) =>
        hasTouchScreen && (slateMode || recentTouch);

    public static bool CurrentPreferTouchHitSlop()
    {
        var touches = NativeMethods.GetSystemMetrics(SmMaximumTouches);
        var digitizer = NativeMethods.GetSystemMetrics(SmDigitizer);
        var hasTouch = touches > 0
            || (digitizer & (NidIntegratedTouch | NidExternalTouch)) != 0;
        var slateMode = NativeMethods.GetSystemMetrics(SmConvertibleSlateMode) == 0;
        return PreferTouchInvocation(hasTouch, slateMode, TouchModality.IsRecent());
    }
}
