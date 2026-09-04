namespace T9Pane.Services;

/// <summary>
/// 官方触摸键盘只有三条规则（WPF TipTsfHelper / RequireTouchInEditControl / Raymond Chen）：
/// 弹出：手指点进带 UIA Text 的框。窗口自带焦点、程序 SetFocus 都不弹。
/// 收起：焦点落定后不再是 Text（Edit↔Button）。点在盘面上不收。
/// 保持：盘面按键、同行连打。同一行再点新位置要跟 GetTextExt 重摆。
/// 第三方盘必须自己 Hide。禁止用「已经弹出」把下一次手指吞掉。
/// </summary>
internal enum SipGesture
{
    None,
    OnOverlay,
    OnEdit,
    OnLeave
}

internal enum SipPhase
{
    Hidden,
    Pending,
    Visible
}

internal enum SipVisibilityAction
{
    Stay,
    Show,
    Hide,
    Pending,
    Relayout
}

internal static class SipLifecyclePolicy
{
    /// <summary>本次接触是否落在盘面。只认正在按，或同一帧刚按下。</summary>
    public const int OverlayContactWindowMs = 160;

    /// <summary>点进才弹的手势窗。RequireTouchInEditControl 语义。</summary>
    public const int GestureWindowMs = 800;

    /// <summary>
    /// TipTsfHelper / Raymond Chen：Text 才 Show；已弹出时焦点离开 Text 才 Hide。
    /// 还没弹出时焦点不是 Text，是 Pending，不是离开——按下瞬间旧焦点作废。
    /// </summary>
    public static SipGesture ClassifySettled(
        bool looksLikeText,
        bool hardLeave,
        bool holdForSearch = false,
        bool alreadyVisible = false)
    {
        if (hardLeave)
        {
            return SipGesture.OnLeave;
        }

        if (looksLikeText)
        {
            return SipGesture.OnEdit;
        }

        if (holdForSearch || !alreadyVisible)
        {
            return SipGesture.None;
        }

        return SipGesture.OnLeave;
    }

    public static SipPhase NextPhase(
        SipPhase current,
        SipGesture gesture,
        bool focusIsText,
        bool hasExternalGesture)
    {
        if (gesture == SipGesture.OnOverlay)
        {
            return current;
        }

        if (gesture == SipGesture.OnLeave)
        {
            return SipPhase.Hidden;
        }

        if (gesture == SipGesture.OnEdit && hasExternalGesture)
        {
            return SipPhase.Visible;
        }

        if (current == SipPhase.Pending && focusIsText && hasExternalGesture)
        {
            return SipPhase.Visible;
        }

        if (current == SipPhase.Hidden && hasExternalGesture && !focusIsText)
        {
            return SipPhase.Pending;
        }

        if (current == SipPhase.Visible && !focusIsText && !hasExternalGesture)
        {
            return SipPhase.Hidden;
        }

        return current;
    }

    /// <summary>
    /// 一次手指或一次焦点落定后的唯一判决。不看授权残留，只看：
    /// 盘面是否正被按、焦点是不是 Text、有没有框外手指。
    /// </summary>
    public static SipVisibilityAction Decide(
        bool alreadyVisible,
        bool overlayLive,
        bool overlayContactFresh,
        bool focusIsText,
        bool hardLeave,
        bool selectionChrome,
        bool hasExternalGesture,
        bool contextMenu = false,
        bool searchSession = false)
    {
        if (overlayLive)
        {
            return SipVisibilityAction.Stay;
        }

        if (contextMenu)
        {
            return alreadyVisible ? SipVisibilityAction.Hide : SipVisibilityAction.Stay;
        }

        if (selectionChrome || (searchSession && alreadyVisible))
        {
            return SipVisibilityAction.Stay;
        }

        if (overlayContactFresh && !hasExternalGesture)
        {
            return SipVisibilityAction.Stay;
        }

        if (alreadyVisible && (hardLeave || !focusIsText && hasExternalGesture))
        {
            return SipVisibilityAction.Hide;
        }

        if (focusIsText && hasExternalGesture)
        {
            return alreadyVisible ? SipVisibilityAction.Relayout : SipVisibilityAction.Show;
        }

        if (!alreadyVisible && hasExternalGesture && !focusIsText)
        {
            return SipVisibilityAction.Pending;
        }

        return SipVisibilityAction.Stay;
    }

    public static bool AllowsShow(SipGesture gesture, bool focusIsText) =>
        gesture == SipGesture.OnEdit && focusIsText;

    public static bool ShouldShowForTouchFocus(
        bool hasExternalGesture,
        bool focusIsText) =>
        hasExternalGesture && focusIsText;

    public static bool ShouldHideOnFocusSettled(
        bool visible,
        bool overlayOwnsContact,
        bool focusIsText,
        bool hardLeave,
        bool selectionChrome) =>
        visible
        && !overlayOwnsContact
        && !selectionChrome
        && (hardLeave || !focusIsText);

    public static bool ShouldReposition(SipGesture gesture) =>
        gesture == SipGesture.OnEdit;

    /// <summary>
    /// 官方 SIP 贴底，打字不跟光标。我们只在三种情况摆一次：
    /// 刚弹出、焦点换到另一个框、GetTextExt 换行。
    /// 同行续打、退格都不动。不靠时间窗。
    /// </summary>
    public static bool ShouldRepositionNow(
        bool alreadyVisible,
        bool fieldChanged,
        bool lineChanged,
        bool caretTapped = false) =>
        !alreadyVisible || fieldChanged || lineChanged || caretTapped;

    public static bool FieldIdentityChanged(string authorizedId, string incomingId) =>
        !string.IsNullOrEmpty(authorizedId)
        && !string.IsNullOrEmpty(incomingId)
        && !string.Equals(authorizedId, incomingId, StringComparison.Ordinal);

    /// <summary>
    /// 焦点落到我们自己的盘面按钮上不是离开。官方 SIP 点候选/按键不 Hide。
    /// </summary>
    public static bool ShouldStayForKeyboardFocus(
        bool overlayLive,
        bool overlayContactFresh,
        bool focusedOwnPane) =>
        overlayLive || overlayContactFresh || focusedOwnPane;

    public static bool ShouldHideWhenFieldMissing(
        bool alreadyShown,
        bool searchSession,
        bool selectionChrome,
        bool leaveArmed,
        SipGesture gesture,
        bool hasExternalGesture = false)
    {
        // TipTsfHelper 几乎不 Hide。弹出那一下手指还在 GestureWindow 里，
        // Chromium 会先 SetFocus(null) / 报 Button，探测失败不是离开。
        _ = hasExternalGesture;
        return alreadyShown
            && !searchSession
            && !selectionChrome
            && (leaveArmed || gesture == SipGesture.OnLeave);
    }

    /// <summary>
    /// 平板 HID 没有可靠屏幕坐标，整条路径跳过落点锁框。
    /// 鼠标带真实坐标时仍走桌面几何，不要用触摸粘滞去套框。
    /// </summary>
    public static bool ShouldIgnoreClickGeometry(
        bool hasExternalGesture,
        bool overlayOwnsContact,
        bool hasScreenPoint)
    {
        _ = hasExternalGesture;
        _ = overlayOwnsContact;
        return !hasScreenPoint;
    }

    public static bool OwnsCurrentContact(bool liveOverOverlay, bool overlayContactFresh) =>
        liveOverOverlay || overlayContactFresh;

    public static bool IsRecent(long ticks, int windowMs, long nowTicks)
    {
        if (ticks == 0)
        {
            return false;
        }

        var age = nowTicks - ticks;
        return age >= 0 && age <= TimeSpan.FromMilliseconds(windowMs).Ticks;
    }
}

/// <summary>
/// 一套触摸时钟：外部接触 + 盘面接触。Overlay 认领后清掉外部手势，避免按键被当成换框。
/// </summary>
internal sealed class SipLifecycle
{
    public static SipLifecycle Shared { get; } = new();

    private long _lastTouchTicks;
    private long _lastOverlayTicks;
    private int _gesture;
    private int _phase;

    public SipGesture Gesture => (SipGesture)Volatile.Read(ref _gesture);

    public SipPhase Phase => (SipPhase)Volatile.Read(ref _phase);

    public void NoteExternalTouch() =>
        Interlocked.Exchange(ref _lastTouchTicks, DateTime.UtcNow.Ticks);

    public void NoteOverlayContact()
    {
        var now = DateTime.UtcNow.Ticks;
        Interlocked.Exchange(ref _lastOverlayTicks, now);
        Interlocked.Exchange(ref _gesture, (int)SipGesture.OnOverlay);
        Interlocked.Exchange(ref _lastTouchTicks, 0);
    }

    public void NoteSettled(SipGesture gesture)
    {
        Interlocked.Exchange(ref _gesture, (int)gesture);
        if (gesture == SipGesture.OnEdit)
        {
            Interlocked.Exchange(ref _lastTouchTicks, DateTime.UtcNow.Ticks);
        }
    }

    /// <summary>
    /// 离开必须把「刚用过手指」清掉。否则 TipTsfHelper 式的下一次 TSF
    /// 同步会把同一窗手势当成又点进了输入框，Cursor 里收完立刻再弹。
    /// </summary>
    public void NoteLeave()
    {
        Interlocked.Exchange(ref _gesture, (int)SipGesture.OnLeave);
        Interlocked.Exchange(ref _lastTouchTicks, 0);
    }

    public void SetPhase(SipPhase phase) =>
        Interlocked.Exchange(ref _phase, (int)phase);

    public void ConsumeEditGesture()
    {
        if (Gesture == SipGesture.OnEdit)
        {
            Interlocked.Exchange(ref _gesture, (int)SipGesture.None);
        }
    }

    public void ConsumeTap()
    {
        ConsumeEditGesture();
        Interlocked.Exchange(ref _lastTouchTicks, 0);
    }

    public void ResetForTests()
    {
        Interlocked.Exchange(ref _lastTouchTicks, 0);
        Interlocked.Exchange(ref _lastOverlayTicks, 0);
        Interlocked.Exchange(ref _gesture, (int)SipGesture.None);
        Interlocked.Exchange(ref _phase, (int)SipPhase.Hidden);
    }

    public bool HasRecentTouch(int windowMs = 2500)
    {
        var now = DateTime.UtcNow.Ticks;
        return SipLifecyclePolicy.IsRecent(
                   Interlocked.Read(ref _lastTouchTicks), windowMs, now)
            || SipLifecyclePolicy.IsRecent(
                   Interlocked.Read(ref _lastOverlayTicks), windowMs, now);
    }

    public bool HasRecentExternalGesture(
        int windowMs = SipLifecyclePolicy.GestureWindowMs)
    {
        var now = DateTime.UtcNow.Ticks;
        if (SipLifecyclePolicy.IsRecent(
                Interlocked.Read(ref _lastOverlayTicks),
                SipLifecyclePolicy.OverlayContactWindowMs,
                now))
        {
            return false;
        }

        return SipLifecyclePolicy.IsRecent(
            Interlocked.Read(ref _lastTouchTicks),
            windowMs,
            now);
    }

    public bool OwnsOverlayContact(bool liveOverOverlay) =>
        SipLifecyclePolicy.OwnsCurrentContact(
            liveOverOverlay,
            SipLifecyclePolicy.IsRecent(
                Interlocked.Read(ref _lastOverlayTicks),
                SipLifecyclePolicy.OverlayContactWindowMs,
                DateTime.UtcNow.Ticks));
}
