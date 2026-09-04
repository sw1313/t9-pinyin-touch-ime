using System.Windows.Threading;
using T9Pane.Native;
using T9Pane.Overlay;

namespace T9Pane.Services;

internal static class KeyboardVisibilityPolicy
{
    public static bool ShouldShow(
        bool enabled,
        bool userDismissed,
        bool t9ContextActive,
        bool invocationAuthorized = true) =>
        enabled && !userDismissed && t9ContextActive && invocationAuthorized;

    public static bool IsT9ContextActive(bool hasFocusedClient) => hasFocusedClient;
}

internal static class DesktopContextGracePolicy
{
    public static bool ShouldBridge(
        bool overlayVisible,
        bool sameForegroundHost,
        bool profileActive,
        TimeSpan elapsed,
        int graceMilliseconds = 500) =>
        overlayVisible
        && sameForegroundHost
        && profileActive
        && elapsed >= TimeSpan.Zero
        && elapsed <= TimeSpan.FromMilliseconds(graceMilliseconds);

    /// <summary>
    /// TipTsfHelper 不因短暂 TSF 无效 Hide。Chromium 换框会先 SetFocus(null)，
    /// 授权后一两秒内租约抖动不能把刚弹出来的盘收掉。
    /// </summary>
    public static bool ShouldHoldAfterAuthorize(
        bool overlayVisible,
        bool sameForegroundHost,
        TimeSpan sinceAuthorize,
        int holdMilliseconds = 2000) =>
        overlayVisible
        && sameForegroundHost
        && sinceAuthorize >= TimeSpan.Zero
        && sinceAuthorize <= TimeSpan.FromMilliseconds(holdMilliseconds);
}

internal static class KeyboardInvocationPolicy
{
    public static bool ShouldDisarmForForegroundChange(
        IntPtr currentForeground,
        IntPtr nextForeground,
        bool searchHandoff = false) =>
        currentForeground != IntPtr.Zero
        && nextForeground != IntPtr.Zero
        && currentForeground != nextForeground
        && !searchHandoff;

    public static bool TryResolvePointer(
        PointerInputHit hit,
        bool systemTextHost,
        PointerInvocationOrigin origin,
        bool previouslyAuthorized,
        bool userDismissed,
        bool expired,
        out bool authorized)
    {
        authorized = false;
        if (origin == PointerInvocationOrigin.TaskbarStart)
        {
            return true;
        }
        if (origin is PointerInvocationOrigin.TaskbarSearch
            or PointerInvocationOrigin.StartMenuSearch)
        {
            var targetReady =
                systemTextHost && hit == PointerInputHit.Inside;
            if (targetReady)
            {
                authorized = true;
                return true;
            }
            return expired;
        }
        if (origin == PointerInvocationOrigin.StartMenuSurface)
        {
            if (systemTextHost && hit == PointerInputHit.Inside)
            {
                authorized = true;
                return true;
            }
            return expired;
        }
        if (hit == PointerInputHit.Unavailable)
        {
            // 触摸点在整页容器上时补判定还没到。到期必须消费掉，
            // 否则 2 秒里每 70ms 全量 UIA，打字会被拖死。
            return expired;
        }

        authorized = ShouldAuthorize(
            hit == PointerInputHit.Inside,
            systemTextHost,
            origin,
            previouslyAuthorized,
            userDismissed);
        return true;
    }

    public static bool IsSearchInvocation(PointerInvocationOrigin origin) =>
        origin is PointerInvocationOrigin.TaskbarSearch
            or PointerInvocationOrigin.StartMenuSearch
            or PointerInvocationOrigin.StartMenuSurface;

    /// <summary>
    /// 平板授权经常看不到钩子原点。开始菜单 / SearchHost 仍是同一次搜索会话，
    /// 必须标成搜索原点，否则联想 Button 会被当成离开。
    /// </summary>
    public static PointerInvocationOrigin OriginForSearchSurface(
        string processName,
        bool trayChrome,
        bool searchFlyoutVisible)
    {
        if (processName is "searchhost" or "searchapp" or "searchui" or "searchapp.desktop"
            or "startmenuexperiencehost")
        {
            return PointerInvocationOrigin.StartMenuSearch;
        }

        if (processName == "explorer" && (trayChrome || searchFlyoutVisible))
        {
            return trayChrome
                ? PointerInvocationOrigin.TaskbarSearch
                : PointerInvocationOrigin.StartMenuSearch;
        }

        return PointerInvocationOrigin.Unknown;
    }

    /// <summary>
    /// 只有“还在等搜索框出现”时才忽略焦点落到按钮。
    /// 键盘已经弹出来之后，失焦必须收；否则收起后又被同一下点击重新授权。
    /// </summary>
    public static bool ShouldHoldFocusLeftForSearch(
        bool pendingSearchInvocation,
        bool keyboardAlreadyShown) =>
        pendingSearchInvocation && !keyboardAlreadyShown;

    /// <summary>
    /// 已经判定离开当前框，且这一下也不是新输入：消费掉，避免 70ms 重试再授权。
    /// Unavailable 仍留给搜索交接，那一拍还看不清。
    /// </summary>
    public static bool ShouldConsumeLeaveClick(PointerInputHit hit) =>
        hit == PointerInputHit.Outside;

    /// <summary>
    /// 这一下落在当前授权框外面，又还没点中新的输入框：先放下旧框。
    /// 待决点击继续等——新框的 UIA 常要再过一拍才出现，这时收成 Outside
    /// 就会把换框的第一次点击吃掉，键盘要么不弹，要么还停在上一个框。
    /// </summary>
    public static bool ShouldReleaseAuthorizedField(
        bool authorized,
        bool clickInsideAuthorizedField,
        PointerInputHit hit) =>
        authorized
        && !clickInsideAuthorizedField
        && hit != PointerInputHit.Inside;

    /// <summary>
    /// 已经由一次点击授权的框，不能被焦点/TSF 里另一个框的坐标换掉。
    /// Cursor 会把焦点留在聊天框；换到对话框后这两条路交出来的仍是聊天框。
    /// 有待决点击时只信“用户刚点中的那个框”。
    /// </summary>
    public static bool ShouldAdoptIncomingField(
        bool pendingClick,
        bool incomingFromClickedField,
        string authorizedFieldId,
        string incomingFieldId)
    {
        // 还没锁过框：第一次点击必须能用焦点/UIA 交出来的坐标，
        // 否则搜索框那种 FromPoint 停在 Custom 上的表面永远不弹。
        if (string.IsNullOrEmpty(authorizedFieldId))
        {
            return true;
        }

        if (pendingClick)
        {
            return incomingFromClickedField
                || string.Equals(authorizedFieldId, incomingFieldId, StringComparison.Ordinal);
        }

        return !string.IsNullOrEmpty(incomingFieldId)
            && string.Equals(authorizedFieldId, incomingFieldId, StringComparison.Ordinal);
    }

    public static bool ShouldAuthorize(
        bool pointerInsideFocusedInput,
        bool systemTextHost,
        PointerInvocationOrigin origin = PointerInvocationOrigin.Unknown,
        bool previouslyAuthorized = false,
        bool userDismissed = false)
    {
        if (origin == PointerInvocationOrigin.TaskbarStart)
        {
            return false;
        }

        return pointerInsideFocusedInput
            || (systemTextHost
                && ((origin is PointerInvocationOrigin.TaskbarSearch
                    or PointerInvocationOrigin.StartMenuSearch)
                    || (previouslyAuthorized && !userDismissed)));
    }

    /// <summary>
    /// 平板上触摸经常升不成鼠标，全局钩子看不到按下。焦点已经进了输入框
    /// 就允许弹——这是触屏系统键盘的常规行为，桌面仍必须点到框里。
    /// </summary>
    public static bool ShouldAuthorizeSlateFocus(
        bool slateDevice,
        bool focusedLooksLikeTextInput) =>
        slateDevice && focusedLooksLikeTextInput;

    /// <summary>
    /// 平板上点到系统键盘盘面（看不清）或焦点刚进输入框，都可以授权。
    /// 已经判定为离开的点击不能再靠“还有输入框”拉回来。
    /// </summary>
    public static bool ShouldAuthorizeSlateOcclusion(
        bool slateDevice,
        bool hasInputField,
        bool focusEnteredTextInput,
        bool pointerOnOfficialSip,
        bool pageSurface = false,
        bool editTap = true) =>
        slateDevice
        && hasInputField
        && editTap
        && (focusEnteredTextInput || pointerOnOfficialSip)
        && !(pageSurface && !focusEnteredTextInput);

    public static bool ShouldAwaitSlateFocus(
        bool searchSession,
        bool slateDevice,
        bool focusEnteredTextInput) =>
        searchSession || (slateDevice && focusEnteredTextInput);

    /// <summary>
    /// 系统插入点/选区手柄是透明窗，UIA 焦点会漂到 Thumb、选区菜单。
    /// 只有这类铬才忽略离开；点按钮、链接、整页必须收。
    /// </summary>
    public static bool ShouldIgnoreFocusLeft(
        bool documentFocused,
        bool slateDevice,
        bool selectionChrome) =>
        documentFocused && slateDevice && selectionChrome;

    /// <summary>
    /// SampleIME 只在候选窗自己的文档指针变了才藏。TSF 上下文无效本身不够：
    /// Chromium / Cursor 打字时会先 SetFocus(null)。日志里授权弹出后十几毫秒
    /// 就「文档焦点离开，收起」再立刻再授权，就是这条被平板豁免点出来的。
    /// 必须同时有一次尚未判成输入的点击，且 UIA 也不是输入框，才是真离开。
    /// </summary>
    public static bool ShouldDismissForLostDocument(
        bool documentFocused,
        bool uiaLooksLikeTextInput,
        bool searchSession,
        bool hasUnresolvedLeaveClick) =>
        !documentFocused
        && !uiaLooksLikeTextInput
        && !searchSession
        && hasUnresolvedLeaveClick;

    /// <summary>
    /// 同一拍里刚因失焦撤了授权，不能马上靠「焦点还像输入」再弹回来。
    /// </summary>
    public static bool ShouldReauthorizeAfterRevoke(bool revokedThisSync) =>
        !revokedThisSync;
}

/// <summary>
/// 布局变化只跟线，不重新做弹出/隐藏。官方 OnLayoutChange 也是这个职责。
/// </summary>
internal static class LayoutSyncPolicy
{
    public static bool IsLayoutOnly(
        uint source,
        bool documentActive = true,
        bool rangeSelected = false)
    {
        // 组词/预编辑会把 TSF 选区打成 sel=1。官方候选窗这时只跟 GetTextExt，
        // 不重新查 UIA。选区手柄让开走 ForegroundTracker，不靠这一位。
        _ = rangeSelected;
        return source is 1 or 2 && documentActive;
    }

    public static bool ShouldUseLayoutFastPath(
        bool overlayVisible,
        bool authorized,
        bool rangeSelected,
        bool focusAway = false) =>
        overlayVisible && authorized && !rangeSelected && !focusAway;

    public static bool ShouldProbeUiAutomation(bool layoutOnly) => !layoutOnly;

    public static bool ShouldDecideVisibility(bool layoutOnly) => !layoutOnly;

    public static bool ShouldKeepVisibleWithoutField(
        bool overlayVisible,
        bool authorized,
        bool rejectedWrongBox,
        bool focusAway = false) =>
        overlayVisible && authorized && !rejectedWrongBox && !focusAway;
}

/// <summary>
/// 官方 ITfContext::GetSelection：空范围是插入点，非空是高亮选区。
/// 系统 SIP 点进编辑框就会弹，地址栏点一下的全选、Ctrl+A 都要弹。
/// 只有长按出现选区手柄/选区菜单时才让键盘让开。
/// </summary>
internal static class SelectionVisibilityPolicy
{
    /// 插入点旁边的单头 Thumb 不是长按选区。必须手柄和选区同时在。
    /// 右键/长按菜单本身就要让开，不必再等选区。
    public static bool ShouldHide(
        bool touchSelectionChrome,
        bool rangeSelected = true,
        bool contextMenu = false) =>
        contextMenu || (touchSelectionChrome && rangeSelected);

    public static bool ShouldShowForCaret(
        bool touchSelectionChrome,
        bool rangeSelected = false,
        bool contextMenu = false) =>
        !ShouldHide(touchSelectionChrome, rangeSelected, contextMenu);
}

/// <summary>
/// 对齐官方触摸键盘（不是鼠标点击模型）。
/// <list type="bullet">
/// <item>Win8+ 调用改为跟踪 WM_POINTER，不跟踪鼠标坐标（WPF TipTsfHelper）。</item>
/// <item>IsUIBusy=False 后同步查询当前焦点控件；RequireTouchInEditControl
/// 要求手指点在编辑框上，程序把焦点放进框不够。</item>
/// <item>ShouldShow = 焦点有 UIA Text 模式。Raymond Chen：Edit↔Button 跟着显隐。</item>
/// <item>位置用 ITfContextView.GetTextExt，不用落点。点一下就是一次 TryShow，
/// 必须按新光标摆；只有打字引起的同行 GetTextExt 变化不挪窗。</item>
/// </list>
/// </summary>
internal static class SipFocusTrackingPolicy
{
    public static bool ShouldIgnoreClickGeometry(
        bool sipTouchGesture,
        bool overlayOwnsTouch,
        bool hasScreenPoint) =>
        SipLifecyclePolicy.ShouldIgnoreClickGeometry(
            sipTouchGesture,
            overlayOwnsTouch,
            hasScreenPoint);

    public static bool ShouldRepositionForTouchInvoke(
        bool sipTouchGesture,
        bool overlayOwnsTouch) =>
        sipTouchGesture && !overlayOwnsTouch;

    public static bool ShouldHoldHideForFocusQuery(
        bool authorized,
        bool gestureRecent,
        bool focusedText) =>
        !authorized && gestureRecent && focusedText;

    /// <summary>
    /// 本次接触是否落在我们盘面上。只能看「正在按」或刚按下的几十毫秒，
    /// 不能用 800ms 粘滞——否则打完字再点侧栏会被当成还在点键盘，第二次失焦不收。
    /// </summary>
    public const int OverlayContactWindowMs = SipLifecyclePolicy.OverlayContactWindowMs;

    public static bool OwnsCurrentContact(
        bool liveOverOverlay,
        bool overlayContactFresh) =>
        SipLifecyclePolicy.OwnsCurrentContact(liveOverOverlay, overlayContactFresh);
}

/// <summary>
/// 系统触摸键盘的显隐：焦点落定后同步查询当前焦点控件。
/// WPF TipTsfHelper 几乎不 Hide，只在每次焦点时 TryShow；系统再判断当前
/// 焦点有没有 UIA Text 模式，没有就自己关。
/// 官方 IInputPanelInvocationConfiguration：IsUIBusy 结束后「同步查询
/// 当前焦点控件」。Raymond Chen：焦点在 Edit 和 Button 之间移动时键盘跟着显隐。
/// 第三方盘必须自己做这步查询。按下瞬间焦点还在旧 Edit，不能当已经离开。
/// </summary>
internal static class SipVisibilityPolicy
{
    public const int SettleMilliseconds = 200;

    public static bool ShouldRemainAfterSettle(
        bool overlayOwnsTouch,
        bool focusedLooksLikeText,
        bool focusEnteredText,
        bool selectionChrome,
        bool nearbyFieldChrome = false) =>
        overlayOwnsTouch
        || focusedLooksLikeText
        || focusEnteredText
        || selectionChrome
        || nearbyFieldChrome;

    public static bool ShouldHideAfterTouchSettle(
        bool touchSettled,
        bool overlayOwnsTouch,
        bool focusedLooksLikeText,
        bool focusEnteredText,
        bool selectionChrome,
        bool nearbyFieldChrome = false,
        bool searchSession = false) =>
        !searchSession
        && touchSettled
        && !ShouldRemainAfterSettle(
            overlayOwnsTouch,
            focusedLooksLikeText,
            focusEnteredText,
            selectionChrome,
            nearbyFieldChrome);

    /// <summary>
    /// 地址栏刷新键贴在当前框边上，TSF 还在。Cursor 侧栏按钮离编辑框很远。
    /// </summary>
    public static bool IsNearbyFieldChrome(
        NativeRect authorizedBox,
        NativeRect focusBounds,
        int slop = 16)
    {
        if (authorizedBox.IsEmpty || focusBounds.IsEmpty)
        {
            return false;
        }

        return focusBounds.Left < authorizedBox.Right + slop
            && focusBounds.Right > authorizedBox.Left - slop
            && focusBounds.Top < authorizedBox.Bottom + slop
            && focusBounds.Bottom > authorizedBox.Top - slop;
    }
}

/// <summary>
/// 触摸失焦：按钮焦点经常是地址栏/工具栏误报，文档还在时先确认再收。
/// 点整页是离开；长按手柄只让开键盘，不撤权。
/// </summary>
internal static class TouchFocusLeavePolicy
{
    public const int ConfirmMilliseconds = 280;

    public static bool ShouldRevokeNow(
        bool focusLeft,
        bool selectionChrome,
        bool looksLikeText,
        bool documentFocused,
        bool touchInvocation)
    {
        if (!focusLeft || selectionChrome || looksLikeText)
        {
            return false;
        }

        return !touchInvocation || !documentFocused;
    }

    public static bool ShouldRevokeAfterConfirm(
        bool confirmExpired,
        bool selectionChrome,
        bool looksLikeText,
        bool focusEntered,
        bool pageSurface,
        bool focusLeft,
        bool touchDismissArmed) =>
        confirmExpired
        && !selectionChrome
        && !looksLikeText
        && !focusEntered
        && (pageSurface || focusLeft || touchDismissArmed);

    public static bool ShouldRevokeForPageTap(
        bool touchDismissArmed,
        bool pageSurface,
        bool selectionChrome,
        bool looksLikeText,
        bool focusEntered) =>
        touchDismissArmed
        && pageSurface
        && !selectionChrome
        && !looksLikeText
        && !focusEntered;
}

/// <summary>
/// 落点是否属于这个输入框。只用于认框和采纳坐标，不能挡离开判定。
/// SampleIME OnSetFocus 先比较候选窗所属 ITfDocumentMgr 和当前焦点文档，
/// 不同就 OnKillThreadFocus；GetTextExt 只问新焦点文档。Chromium 的
/// OnCaretBoundsChanged 也只接受 focused client。套不住的坐标就是上一个框。
/// </summary>
internal static class FieldClickPolicy
{
    public const int EdgeTolerance = 3;
    public const int CaretRowSlop = 12;
    public const int CaretRowWidth = 480;
    /// <summary>
    /// Chromium/monaco 折叠输入框：第一次点在折叠条上，展开后 Edit 出现在
    /// 落点下方或右侧。IUIAutomationExpandCollapsePattern 在 Electron 上
    /// 通常拿不到，只能按展开后的紧凑框去认这一下。
    /// </summary>
    public const int ExpandClickSlop = 48;

    public static bool Belongs(NativeRect fieldBox, NativeRect caret, int x, int y)
    {
        if (InputInvocationProbe.Contains(fieldBox, x, y, EdgeTolerance))
        {
            return true;
        }

        if (!fieldBox.IsEmpty || caret.IsEmpty)
        {
            return false;
        }

        var row = new NativeRect
        {
            Left = caret.Left - 8,
            Top = caret.Top - CaretRowSlop,
            Right = caret.Left + CaretRowWidth,
            Bottom = caret.Bottom + CaretRowSlop
        };
        return InputInvocationProbe.Contains(row, x, y);
    }

    /// <summary>
    /// 折叠条点开后的认框。只向上/向左放宽，不向下扩——否则点聊天记录
    /// 会被底下那个一直展开的输入框领走。
    /// </summary>
    public static bool OpenedBy(NativeRect fieldBox, NativeRect caret, int x, int y)
    {
        if (Belongs(fieldBox, caret, x, y))
        {
            return true;
        }

        NativeRect box;
        if (!fieldBox.IsEmpty)
        {
            if (fieldBox.Height > 160)
            {
                return false;
            }

            box = fieldBox;
        }
        else if (!caret.IsEmpty)
        {
            box = new NativeRect
            {
                Left = caret.Left - CaretRowWidth,
                Top = caret.Top,
                Right = caret.Left + CaretRowWidth,
                Bottom = caret.Bottom
            };
        }
        else
        {
            return false;
        }

        return x >= box.Left - ExpandClickSlop
            && x < box.Right + EdgeTolerance
            && y >= box.Top - ExpandClickSlop
            && y < box.Bottom + EdgeTolerance;
    }

    /// <summary>
    /// 这一下落点对应的坐标才能拿来摆窗。点中的框直接信；
    /// 焦点/TSF 交出来的必须套得住落点——GetTextExt 也只问当前文档。
    /// 折叠框第一次点击用 OpenedBy，严格 Belongs 会把展开前的落点丢掉。
    /// </summary>
    public static bool Trusts(
        bool fromClicked,
        NativeRect fieldBox,
        NativeRect caret,
        int x,
        int y,
        bool hasScreenPoint = true,
        bool touchInvocation = false) =>
        (touchInvocation && !hasScreenPoint)
        || fromClicked
        || OpenedBy(fieldBox, caret, x, y);
}

/// <summary>
/// 两个系统搜索框共用同一个 SearchHost 窗口。原生 TSF 光标属于上一个框时，
/// 不能拿来摆这一次的键盘。用光标相对窗口顶/底的位置区分：
/// 开始菜单搜索贴窗口顶边（日志 y≈87），任务栏搜索贴窗口底边（日志 y≈1037）。
/// </summary>
internal static class SearchCaretPolicy
{
    public const int EdgeBand = 160;

    public static bool Matches(
        PointerInvocationOrigin origin,
        NativeRect caret,
        NativeRect hostWindow)
    {
        if (origin is not (PointerInvocationOrigin.TaskbarSearch
            or PointerInvocationOrigin.StartMenuSurface
            or PointerInvocationOrigin.StartMenuSearch))
        {
            return true;
        }

        if (caret.IsEmpty || hostWindow.IsEmpty)
        {
            return false;
        }

        var nearTop = caret.Top - hostWindow.Top <= EdgeBand;
        var nearBottom = hostWindow.Bottom - caret.Bottom <= EdgeBand;
        return origin == PointerInvocationOrigin.TaskbarSearch
            ? nearBottom
            : nearTop && !nearBottom;
    }
}

internal sealed class KeyboardSession
{
    private readonly AppSettings _settings;
    private readonly T9OverlayWindow _overlay;
    private readonly ForegroundTracker _foreground;
    /// <summary>一次点击最多等多久。任务栏搜索交接给 SearchHost 实测约 700ms。</summary>
    private static readonly TimeSpan PointerIntentWindow = TimeSpan.FromSeconds(2);

    /// <summary>
    /// 整页容器上的 Unavailable 不能挂满 2 秒。系统 SIP 等的是随后的焦点进入，
    /// 不是反复跨进程 UIA。超过这一档还看不清就丢掉，让出 UI 线程。
    /// </summary>
    private static readonly TimeSpan UnavailableIntentWindow =
        TimeSpan.FromMilliseconds(400);

    private readonly System.Windows.Threading.DispatcherTimer _firstShowFallback;
    private bool _userDismissed;
    private DateTime _lastActiveContextUtc = DateTime.MinValue;
    private IntPtr _lastActiveContextHost;
    private IntPtr _pendingFirstShowSurface;
    private bool _pendingFirstShowFallbackAuthorized;
    private bool _invocationAuthorized;
    private bool _searchSession;
    private bool _revokedThisSync;
    private PointerInvocationOrigin _invocationOrigin;
    private IntPtr _invocationSurface;
    private IntPtr _invocationForeground;
    private bool _repositionRequested;
    private (int X, int Y, PointerInvocationOrigin Origin, DateTime CreatedUtc)?
        _pendingPointer;
    private (int X, int Y)? _placementClick;

    /// <summary>
    /// "已经授权要弹，但还没拿到坐标"的等待起点。
    ///
    /// 取不到输入框时 SyncCore 会早退，而同步只由事件驱动：这段时间里恰好没有别的
    /// 事件路过，这一次就再也没人管了，键盘一直不出来。系统搜索框换框后 UIA 要
    /// 100~600ms 才交出真坐标，正好落在这个空档里，所以必须自己驱动重试。
    ///
    /// 记的是起点而不是截止时间：SyncCore 每轮都会再调一次 AwaitCaret，
    /// 若存的是截止时间就会被一路往后推，重试定时器永远停不下来。
    /// </summary>
    private DateTime _caretWaitStartedUtc = DateTime.MinValue;

    /// <summary>
    /// 这一轮已经等超时了。超时后必须停住不再续期，否则全量 SyncCore
    /// （内含多次跨进程 UIA 探测，实测单轮约 30ms）会一直占着 UI 线程。
    /// 拿到坐标或者用户再点一次才重新开始等。不再用 70ms 轮询驱动。
    /// </summary>
    private bool _caretWaitExpired;

    /// <summary>等坐标的上限。真坐标实测 100~600ms 内到达，留足余量即可。</summary>
    private static readonly TimeSpan CaretWaitWindow = TimeSpan.FromMilliseconds(1500);

    /// <summary>
    /// 刚因这一下落点收起键盘。同一坐标在极短时间内再来一次 Inside
    /// （日志里 Outside 后 24ms 又 Inside）是同一次点击的回声，不能再授权。
    /// </summary>
    private (int X, int Y, DateTime Utc)? _dismissedClick;
    private bool _pendingHitUnavailable;
    private PointerInputHit _pendingHit;
    private bool _pendingHitTested;
    private PointerInputHit? _notedPointerHit;
    private bool _pointerHasScreenPoint = true;
    private DateTime _lastTouchSyncUtc = DateTime.MinValue;
    private bool _touchDismissArmed;
    private bool _focusLeavePending;
    private readonly System.Windows.Threading.DispatcherTimer _focusLeaveConfirm;
    private NativeRect _lastShownCaret;
    private bool _lastShownCaretIsInsertion = true;
    private NativeRect _authorizedFieldBox;
    private string _authorizedFieldId = string.Empty;
    private DateTime _lastAuthorizedUtc = DateTime.MinValue;
    private DateTime _shownUtc = DateTime.MinValue;
    private static readonly TimeSpan DismissEchoWindow = TimeSpan.FromMilliseconds(400);
    private DispatcherOperation? _sipQueryOp;
    private bool _settledRetryQueued;
    private bool _sipHideRetryQueued;
    private bool _contactDown;
    private bool _contextMenuArmed;

    public KeyboardSession(AppSettings settings, T9OverlayWindow overlay, ForegroundTracker foreground)
    {
        _settings = settings;
        _overlay = overlay;
        _foreground = foreground;
        _focusLeaveConfirm = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(SipVisibilityPolicy.SettleMilliseconds)
        };
        _focusLeaveConfirm.Tick += (_, _) => ConfirmTouchFocusLeave();
        _firstShowFallback = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(800)
        };
        _firstShowFallback.Tick += (_, _) =>
        {
            _firstShowFallback.Stop();
            if (_pendingFirstShowSurface == IntPtr.Zero)
            {
                return;
            }

            _pendingFirstShowFallbackAuthorized = true;
            SyncCore();
        };
        OverlayTouch.Contacted += OnOverlayTouch;
        _overlay.UserClosed += () =>
        {
            _userDismissed = true;
            _invocationAuthorized = false;
            _invocationOrigin = PointerInvocationOrigin.Unknown;
            _foreground.ClearFocusEntered();
            ClearAuthorizedField();
            _repositionRequested = false;
            CancelSipFocusQuery();
            ResetPendingFirstShow();
            SipLifecycle.Shared.NoteLeave();
            SipLifecycle.Shared.SetPhase(SipPhase.Hidden);
            _overlay.HideOverlay();
        };
        _overlay.PinChanged += OnPinChanged;
        _overlay.BoardLayoutChanged += OnBoardLayoutChanged;
    }

    public void NoteImeChanged()
    {
        if (ImeHost.Shared.CanCommitForeground())
        {
            _userDismissed = false;
        }
    }

    /// <summary>
    /// 官方 WM_POINTER：每一次框外手指都要在 Input 优先级再查一次当前焦点。
    /// 只有手指正按在我们盘面上才忽略。已经弹出不能当成「这下不算」——
    /// 否则第二次点框不弹、点按钮也不收。
    /// </summary>
    public void NoteTouchModality()
    {
        if (OverlayLive() || SipLifecycle.Shared.OwnsOverlayContact(false))
        {
            SipLifecycle.Shared.NoteOverlayContact();
            _touchDismissArmed = false;
            _pointerHasScreenPoint = false;
            _userDismissed = false;
            CancelSipFocusQuery();
            return;
        }

        var alreadyRecent = SipLifecycle.Shared.HasRecentTouch();
        SipLifecycle.Shared.NoteExternalTouch();
        _pointerHasScreenPoint = false;
        _placementClick = null;
        _userDismissed = false;
        if (!KeyboardShown())
        {
            SuppressOfficialSipOnce();
        }

        if (PointerContactPolicy.IsHoldBurst(_contactDown))
        {
            return;
        }

        BeginContact();
        if (!alreadyRecent)
        {
            Log.Info("触摸模态，按焦点弹出，不用鼠标坐标");
        }

        _lastTouchSyncUtc = DateTime.UtcNow;
        ResetCaretWait();
        ClearPendingPointer();
        if (PointerContactPolicy.ShouldShowOnContactStart)
        {
            QueueSipFocusQuery();
        }
    }

    public void NotePointerInput(int x, int y, PointerInvocationOrigin origin)
    {
        if (origin == PointerInvocationOrigin.Unknown
            && TouchDevicePolicy.CurrentPreferTouchHitSlop())
        {
            NoteTouchModality();
            return;
        }

        // 点到当前授权框外面就清掉上一个点中的框，否则 TryGet 还会交出
        // 上一个框的坐标，键盘停在旧位置。点在框里则保留，供这一拍定位。
        if (!InputInvocationProbe.Contains(_authorizedFieldBox, x, y, FieldClickPolicy.EdgeTolerance))
        {
            InputInvocationProbe.ClearClickedField();
        }

        _pointerHasScreenPoint = true;
        _notedPointerHit = InputInvocationProbe.HitTestPointerTarget(
            x,
            y,
            _authorizedFieldBox);

        // 新的一下点击是新一轮意图：上一轮等超时留下的停等标记必须清掉，
        // 否则坐标一直等不到之后，后面每一次点击都不再重试，就变成点了不弹。
        ResetCaretWait();
        _pendingPointer = (x, y, origin, DateTime.UtcNow);
        _pendingHit = PointerInputHit.Unavailable;
        _pendingHitUnavailable = false;
        _pendingHitTested = false;
        _placementClick = (x, y);
        ManualTap.Note();
        BeginContact();
        if ((KeyboardShown() || _invocationAuthorized)
            && PointerContactPolicy.ShouldRepositionOnContactStart)
        {
            _repositionRequested = true;
        }

        if (PointerContactPolicy.ShouldShowOnContactStart)
        {
            SyncCore();
            QueueOneSettledRetry();
        }
    }

    public void NotePointerUp()
    {
        if (!_contactDown && _pendingPointer is null)
        {
            return;
        }

        _contactDown = false;
        if (!PointerContactPolicy.ShouldCompleteTap(
                _contextMenuArmed || _foreground.FocusIsContextMenu))
        {
            return;
        }

        if (OverlayLive() || SipLifecycle.Shared.OwnsOverlayContact(false))
        {
            return;
        }

        if (_pendingPointer is not null)
        {
            SyncCore();
            QueueOneSettledRetry();
            return;
        }

        QueueSipFocusQuery();
    }

    public void NoteContextMenu()
    {
        if (!PointerContactPolicy.ShouldYieldToContextMenu(
                true,
                OverlayLive() || SipLifecycle.Shared.OwnsOverlayContact(false),
                InputInvocationProbe.FocusedLooksLikeTextInput()))
        {
            return;
        }

        _contextMenuArmed = true;
        CancelSipFocusQuery();
        ClearPendingPointer();
        YieldForContextMenu();
    }

    private void BeginContact()
    {
        _contactDown = true;
        _contextMenuArmed = false;
    }

    /// <summary>
    /// 待决点击最多再走一次 Input 优先级查询。坐标未到就等 TSF / UIA 焦点事件，
    /// 不再用 70ms 全量 UIA 循环。
    /// </summary>
    private void QueueOneSettledRetry()
    {
        if (_settledRetryQueued)
        {
            return;
        }

        _settledRetryQueued = true;
        _overlay.Dispatcher.BeginInvoke(
            DispatcherPriority.Input,
            new Action(() =>
            {
                _settledRetryQueued = false;
                ExpirePendingPointerIfNeeded();
                ExpireCaretWaitIfNeeded();
                if (_pendingPointer is null && _caretWaitStartedUtc == DateTime.MinValue)
                {
                    return;
                }

                SyncCore();
            }));
    }

    private void ExpirePendingPointerIfNeeded()
    {
        if (_pendingPointer is not { } pending)
        {
            return;
        }

        var age = DateTime.UtcNow - pending.CreatedUtc;
        var dropUnavailable =
            _pendingHitUnavailable
            && pending.Origin == PointerInvocationOrigin.Unknown
            && age > UnavailableIntentWindow;
        if (age > PointerIntentWindow || dropUnavailable)
        {
            ClearPendingPointer();
        }
    }

    private void ExpireCaretWaitIfNeeded()
    {
        if (_caretWaitStartedUtc != DateTime.MinValue
            && DateTime.UtcNow - _caretWaitStartedUtc > CaretWaitWindow)
        {
            _caretWaitStartedUtc = DateTime.MinValue;
            _caretWaitExpired = true;
        }
    }

    public void NoteFocusSettled()
    {
        ExpirePendingPointerIfNeeded();
        ExpireCaretWaitIfNeeded();
        if (OverlayLive() || InputInvocationProbe.FocusedIsOwnPane())
        {
            SipLifecycle.Shared.NoteOverlayContact();
            return;
        }

        var looksLikeText = InputInvocationProbe.FocusedLooksLikeTextInput();
        var hardLeave = InputInvocationProbe.FocusedIsHardLeave();
        var pageSurface = _foreground.FocusOnPageSurface;
        var selectionChrome = _foreground.FocusLeftIsSelectionChrome;
        if (PointerContactPolicy.ShouldYieldToContextMenu(
                _foreground.FocusIsContextMenu,
                OverlayLive() || SipLifecycle.Shared.OwnsOverlayContact(false),
                looksLikeText))
        {
            YieldForContextMenu();
            return;
        }

        var left = hardLeave || pageSurface || _foreground.FocusLeftTextInput;
        if (_pendingPointer is not null
            || (_caretWaitStartedUtc != DateTime.MinValue && !_caretWaitExpired))
        {
            SyncCore();
            return;
        }

        ApplyVisibilityDecision(
            looksLikeText && !left,
            left,
            selectionChrome,
            SipLifecycle.Shared.HasRecentExternalGesture(),
            allowRelayout: true,
            PointerContactPolicy.ShouldYieldToContextMenu(
                _foreground.FocusIsContextMenu,
                OverlayLive() || SipLifecycle.Shared.OwnsOverlayContact(false),
                looksLikeText && !left));
    }

    public void Sync(bool imeDocument = false, bool layoutOnly = false)
    {
        if (layoutOnly)
        {
            SyncLayoutOnly();
            return;
        }

        SyncCore();
    }

    /// <summary>
    /// 打字和拖动手柄只会触发 TSF 布局/编辑通知。官方候选窗这时只重取
    /// GetTextExt，不重新判决显示。跨进程 UIA 是延迟的主要来源，这里跳过。
    /// </summary>
    private void SyncLayoutOnly()
    {
        if (!KeyboardShown() || !_invocationAuthorized)
        {
            return;
        }

        if (!ImeHost.Shared.TryGetNativeInputField(out var field))
        {
            return;
        }

        var fingerOnField = SipLifecycle.Shared.HasRecentExternalGesture()
            && !OverlayLive()
            && !SipLifecycle.Shared.OwnsOverlayContact(false);
        if (fingerOnField
            && KeyboardPositionSession.ShouldHideWhenTapLeavesAuthorizedField(
                alreadyVisible: true,
                hasExternalGesture: true,
                caretBelongs: KeyboardPositionSession.CaretBelongsToAuthorizedField(
                    _authorizedFieldBox,
                    _lastShownCaret,
                    field.Caret,
                    field.FieldBox),
                anotherField: KeyboardPositionSession.LooksLikeAnotherField(
                    _authorizedFieldBox,
                    _lastShownCaret,
                    field.Caret,
                    field.FieldBox),
                searchSession: _searchSession))
        {
            RevokeForFocusLeave("触摸离开当前输入框，收起键盘");
            return;
        }

        // 打字只走 TSF 布局通知。官方候选窗这时不挪窗；组词下划线让
        // GetTextExt 的 Y 抖几像素，跟线会把整窗带着微移。换行等 SyncCore。
        _ = field;
    }

    private void SyncCore()
    {
        using var scope = Perf.Begin("session.sync");
        var fg = NativeMethods.GetForegroundWindow();
        var top = NativeMethods.GetAncestor(fg, NativeMethods.GaRoot);
        var hasTaskbarSearch = InputFieldProbe.TryGetFocusedTaskbarSearch(
            out var taskbarSearchField);
        var systemTextHost = ShellProcess.IsForegroundSystemTextHost()
            || hasTaskbarSearch;
        var t9ContextActive = KeyboardVisibilityPolicy.IsT9ContextActive(
            ImeHost.Shared.CanCommitForeground());
        if (!ImeHost.Shared.HasOfficialT9Profile())
        {
            _invocationAuthorized = false;
            _invocationOrigin = PointerInvocationOrigin.Unknown;
            ResetPendingFirstShow();
            HideUnlessPinned();
            return;
        }

        if (top != IntPtr.Zero && _foreground.Ignored.Contains(top))
        {
            top = _foreground.LastTarget;
        }
        var foregroundChanged =
            KeyboardInvocationPolicy.ShouldDisarmForForegroundChange(
                _invocationForeground,
                top,
                ShellProcess.IsSearchHandoff(_invocationForeground, top));
        if (foregroundChanged)
        {
            _invocationAuthorized = false;
            _invocationOrigin = PointerInvocationOrigin.Unknown;
            _repositionRequested = false;
            ClearAuthorizedField();
            // 换了应用，上一个应用里点中的输入框不能再拿来定位。
            InputInvocationProbe.ClearClickedField();
        }
        if (top != IntPtr.Zero)
        {
            _invocationForeground = top;
        }

        // 官方焦点跟踪模型的隐藏条件：焦点移到非文本控件就收起。
        // 点任务栏/开始菜单搜索时焦点会先落到 Button，这不是用户离开输入，
        // 待决点击还在等文本框出现，这时不能收——日志里刚弹出就被立刻收起，
        // 就是这个粘滞标志在搜索按钮上被置位。
        var pendingSearch =
            _pendingPointer is { } waiting
            && KeyboardInvocationPolicy.IsSearchInvocation(waiting.Origin);
        var searchSession =
            pendingSearch
            || ShellProcess.IsActiveSearchSession(top, hasTaskbarSearch)
            || (_invocationAuthorized
                && KeyboardInvocationPolicy.IsSearchInvocation(_invocationOrigin));
        _searchSession = searchSession;
        _revokedThisSync = false;
        var slate = TouchDevicePolicy.CurrentPreferTouchHitSlop();
        var focusAway = _foreground.FocusLeftTextInput;
        var selectionChrome = _foreground.FocusLeftIsSelectionChrome;
        var looksLikeText = InputInvocationProbe.FocusedLooksLikeTextInput();
        if (OverlayLive() || InputInvocationProbe.FocusedIsOwnPane())
        {
            SipLifecycle.Shared.NoteOverlayContact();
            looksLikeText = true;
            focusAway = false;
        }

        var focusEntered = _foreground.FocusEnteredTextInput;
        var nearbyChrome = SipVisibilityPolicy.IsNearbyFieldChrome(
            _authorizedFieldBox,
            _foreground.LastFocusBounds);
        var holdFocusLeft = KeyboardInvocationPolicy.ShouldHoldFocusLeftForSearch(
            pendingSearch,
            KeyboardShown() || _invocationAuthorized)
            || KeyboardInvocationPolicy.ShouldIgnoreFocusLeft(
                ImeHost.Shared.HasDocumentFocus,
                slate,
                selectionChrome)
            || (searchSession && !_touchDismissArmed);
        // 官方 SIP：按下瞬间焦点还在旧输入框。不能在这一拍把「待确认离开」清掉。
        if (!_touchDismissArmed
            && (_foreground.FocusEnteredTextInput || looksLikeText))
        {
            CancelFocusLeaveConfirm();
        }

        if (TouchFocusLeavePolicy.ShouldRevokeNow(
                focusAway,
                selectionChrome,
                looksLikeText,
                ImeHost.Shared.HasDocumentFocus,
                slate)
            && !holdFocusLeft)
        {
            RevokeForFocusLeave("焦点离开文本框，收起键盘");
            _foreground.ClearFocusLeft();
        }
        else if (focusAway && !holdFocusLeft)
        {
            _focusLeavePending = true;
            ArmFocusLeaveConfirm();
            _foreground.ClearFocusLeft();
        }
        else if (holdFocusLeft)
        {
            _foreground.ClearFocusLeft();
        }

        if (TouchFocusLeavePolicy.ShouldRevokeForPageTap(
                _touchDismissArmed,
                _foreground.FocusOnPageSurface,
                selectionChrome,
                looksLikeText,
                focusEntered))
        {
            RevokeForFocusLeave("触摸离开页面表面，收起键盘");
        }

        if (SipVisibilityPolicy.ShouldHideAfterTouchSettle(
                _touchDismissArmed,
                OverlayOwnsPointer(),
                looksLikeText,
                focusEntered,
                selectionChrome,
                nearbyChrome,
                searchSession)
            && !ShouldHoldHideDuringFocusHandoff())
        {
            RevokeForFocusLeave("焦点已不是可输入控件，收起键盘");
        }

        // Chromium 换框 / SearchHost 交接都会短暂打出上下文无效。
        // 必须同时有一次尚未判成输入的点击，且 UIA 也不是输入框，才是真离开。
        if (KeyboardInvocationPolicy.ShouldDismissForLostDocument(
                ImeHost.Shared.HasDocumentFocus,
                InputInvocationProbe.FocusedLooksLikeTextInput(),
                searchSession,
                _pendingPointer is not null
                    && TouchInvocationPolicy.CountsAsLeaveClick(_pendingHit))
            && _invocationAuthorized)
        {
            _invocationAuthorized = false;
            _revokedThisSync = true;
            _invocationOrigin = PointerInvocationOrigin.Unknown;
            _repositionRequested = false;
            ResetCaretWait();
            ClearPendingPointer();
            ClearAuthorizedField();
            InputInvocationProbe.ClearClickedField();
            Log.Info("TSF 文档焦点离开，收起键盘");
        }

        if (t9ContextActive)
        {
            _lastActiveContextUtc = DateTime.UtcNow;
            _lastActiveContextHost = top;
        }
        var desktopContextGrace = DesktopContextGracePolicy.ShouldBridge(
            KeyboardShown(),
            top != IntPtr.Zero && top == _lastActiveContextHost,
            ImeHost.Shared.HasProfileLeaseFor(top),
            DateTime.UtcNow - _lastActiveContextUtc)
            || DesktopContextGracePolicy.ShouldHoldAfterAuthorize(
                KeyboardShown(),
                top != IntPtr.Zero && top == _invocationForeground,
                DateTime.UtcNow - _lastAuthorizedUtc);

        if (top != IntPtr.Zero
            && ShellProcess.IsTrayChrome(top)
            && !ShellProcess.IsForegroundFlyout()
            && !hasTaskbarSearch)
        {
            ResetPendingFirstShow();
            HideUnlessPinned();
            return;
        }

        var uiField = default(InputField);
        var hasUiField = hasTaskbarSearch;
        if (hasTaskbarSearch)
        {
            uiField = taskbarSearchField with
            {
                Owner = taskbarSearchField.TopLevel,
                Context = InputContextKey.ForFocus(
                    taskbarSearchField.TopLevel,
                    _foreground.Generation)
            };
        }
        else if (systemTextHost)
        {
            hasUiField = InputFieldProbe.TryGet(
                _foreground.Ignored,
                _foreground.LastTarget,
                out uiField);
            if (hasUiField)
            {
                uiField = uiField with
                {
                    Owner = uiField.TopLevel,
                    Context = InputContextKey.ForFocus(
                        uiField.TopLevel,
                        _foreground.Generation)
                };
            }
        }
        var hasProfileLease = systemTextHost
            && hasUiField
            && (hasTaskbarSearch
                ? ImeHost.Shared.HasSystemProfileLease()
                : ImeHost.Shared.HasForegroundProfileLease());
        var consoleHost = ConsoleInputSurface.IsWindow(top);
        var visibleT9Lease = t9ContextActive
            || hasProfileLease
            || desktopContextGrace
            || (consoleHost && ImeHost.Shared.HasOfficialT9Profile());
        if (!visibleT9Lease)
        {
            if (KeyboardInvocationPolicy.ShouldAwaitSlateFocus(
                    searchSession,
                    TouchDevicePolicy.CurrentPreferTouchHitSlop(),
                    _foreground.FocusEnteredTextInput))
            {
                AwaitCaret();
                return;
            }

            ResetPendingFirstShow();
            Log.Info("T9 上下文未就绪，收起键盘");
            HideUnlessPinned();
            return;
        }

        _overlay.PixelSize(out var boardW, out var boardH);
        var field = default(InputField);
        var hasNativeField = ImeHost.Shared.TryGetNativeInputField(out var nativeField);
        if (!systemTextHost)
        {
            hasUiField = InputFieldProbe.TryGet(
                _foreground.Ignored,
                _foreground.LastTarget,
                out uiField);
            if (hasUiField)
            {
                uiField = uiField with
                {
                    Owner = uiField.TopLevel,
                    Context = InputContextKey.ForFocus(
                        uiField.TopLevel,
                        _foreground.Generation)
                };
            }
        }
        var hasField = InputFieldSelectionPolicy.TrySelect(
            systemTextHost,
            hasUiField,
            uiField,
            hasNativeField,
            nativeField,
            out field);
        var intentOrigin = _pendingPointer?.Origin ?? PointerInvocationOrigin.Unknown;
        var rejectedWrongBox = false;
        if (hasField
            && !SearchCaretPolicy.Matches(intentOrigin, field.Caret, field.Occluder))
        {
            Log.Info(
                $"丢弃错框光标 origin={intentOrigin} "
                + $"光标=({field.Caret.Left},{field.Caret.Top})");
            hasField = false;
            field = default;
            rejectedWrongBox = true;
        }

        // SampleIME OnSetFocus：先比较候选窗所属文档和当前焦点文档，再取光标。
        // 离开判定不能等坐标到手——点空白时 TryGet 失败若在这里早退，键盘就不收。
        var surfaceChanged = hasField
            && _invocationSurface != IntPtr.Zero
            && _invocationSurface != field.TopLevel;
        ApplyPendingPointer(
            systemTextHost,
            hasUiField,
            hasNativeField,
            hasField ? field : default,
            surfaceChanged,
            foregroundChanged);
        TryAuthorizeSlateFocus(hasField, top);

        // 坐标采纳在离开判定之后。套不住这一下落点的光标不能摆——
        // 日志里先重定位 (1016,931) 再读到 (1244,136)，就是焦点还在聊天框。
        var placementClick = _pendingPointer is { } pending
            ? (pending.X, pending.Y)
            : _placementClick;
        var touchInvocation = TouchDevicePolicy.CurrentPreferTouchHitSlop();
        var sipTouch = SipLifecyclePolicy.ShouldIgnoreClickGeometry(
            SipLifecycle.Shared.HasRecentExternalGesture(),
            OverlayOwnsPointer(),
            _pointerHasScreenPoint);
        if (hasField
            && !sipTouch
            && placementClick is { } click
            && !FieldClickPolicy.Trusts(
                field.FromClicked,
                field.FieldBox,
                field.Caret,
                click.X,
                click.Y,
                _pointerHasScreenPoint,
                touchInvocation))
        {
            Log.Info(
                $"丢弃焦点光标 光标=({field.Caret.Left},{field.Caret.Top}) "
                + $"落点=({click.X},{click.Y})");
            hasField = false;
            field = default;
        }
        else if (hasField
            && !sipTouch
            && !KeyboardInvocationPolicy.ShouldAdoptIncomingField(
                _pendingPointer is not null,
                field.FromClicked,
                _authorizedFieldId,
                field.FieldId))
        {
            // 焦点在撒谎：只有上一处光标仍套得住这一下，才沿用。
            // 套不住说明用户已经点到另一个框，不能把键盘钉在旧位置。
            var keepLast =
                _invocationAuthorized
                && !_lastShownCaret.IsEmpty
                && (placementClick is not { } leave
                    || FieldClickPolicy.Belongs(
                        _authorizedFieldBox, _lastShownCaret, leave.X, leave.Y));
            if (keepLast)
            {
                field = field with { Caret = _lastShownCaret, FieldId = _authorizedFieldId };
                _repositionRequested = false;
            }
            else
            {
                hasField = false;
                field = default;
            }
        }

        if (hasField
            && !sipTouch
            && _invocationAuthorized
            && (!_authorizedFieldBox.IsEmpty || !_lastShownCaret.IsEmpty)
            && !KeyboardPositionSession.CaretBelongsToAuthorizedField(
                _authorizedFieldBox,
                _lastShownCaret,
                field.Caret,
                field.FieldBox)
            && !KeyboardPositionSession.ShouldReplaceAuthorizedField(
                _authorizedFieldBox,
                _lastShownCaret,
                field.Caret,
                field.FieldBox,
                field.FromClicked,
                hasUiField
                    && hasNativeField
                    && InputFieldSelectionPolicy.IsSameFocusedGeometry(uiField, nativeField),
                hasNativeField && !hasUiField,
                focusEntered,
                field.CaretIsTrusted,
                _authorizedFieldId,
                field.FieldId))
        {
            Log.Info(
                $"锁在当前框，忽略另一处光标 光标=({field.Caret.Left},{field.Caret.Top})");
            field = field with
            {
                Caret = _lastShownCaret.IsEmpty ? field.Caret : _lastShownCaret,
                FieldBox = _authorizedFieldBox.IsEmpty ? field.FieldBox : _authorizedFieldBox,
                FieldId = string.IsNullOrEmpty(_authorizedFieldId) ? field.FieldId : _authorizedFieldId
            };
            _repositionRequested = false;
        }

        if (hasField
            && KeyboardPositionSession.ShouldHideWhenTapLeavesAuthorizedField(
                KeyboardShown(),
                sipTouch
                    && SipLifecycle.Shared.HasRecentExternalGesture()
                    && !OverlayLive(),
                KeyboardPositionSession.CaretBelongsToAuthorizedField(
                    _authorizedFieldBox,
                    _lastShownCaret,
                    field.Caret,
                    field.FieldBox),
                KeyboardPositionSession.LooksLikeAnotherField(
                    _authorizedFieldBox,
                    _lastShownCaret,
                    field.Caret,
                    field.FieldBox),
                surfaceChanged,
                searchSession))
        {
            RevokeForFocusLeave("触摸离开当前输入框，收起键盘");
            return;
        }

        if (!hasField)
        {
            if (SipLifecyclePolicy.ShouldHideWhenFieldMissing(
                    KeyboardShown() || _invocationAuthorized,
                    searchSession,
                    selectionChrome,
                    _touchDismissArmed,
                    SipLifecycle.Shared.Gesture,
                    KeyboardShown()
                        && SipLifecycle.Shared.HasRecentExternalGesture()
                        && !OverlayLive()))
            {
                RevokeForFocusLeave("焦点已不是可输入控件，收起键盘");
                return;
            }

            // 已经授权要弹、或还有一次点击悬着，就必须自己驱动重试。否则这一轮
            // 早退之后没有任何事件会再来同步，键盘就一直不出来——"点了不弹"。
            if (LayoutSyncPolicy.ShouldKeepVisibleWithoutField(
                    KeyboardShown(),
                    _invocationAuthorized,
                    rejectedWrongBox,
                    focusAway)
                && !(_touchDismissArmed && !looksLikeText && !selectionChrome && !nearbyChrome))
            {
                AwaitCaret();
                return;
            }

            if ((!_invocationAuthorized || rejectedWrongBox)
                && KeyboardShown()
                && !searchSession)
            {
                HideUnlessPinned();
            }

            if (_invocationAuthorized || _pendingPointer is not null)
            {
                AwaitCaret();
            }
            return;
        }
        ResetCaretWait();
        field = InputFieldSelectionPolicy.NormalizeDesktopSurface(
            systemTextHost,
            field,
            top,
            _foreground.Generation);
        _invocationSurface = field.TopLevel;

        var yieldMenu = PointerContactPolicy.ShouldYieldToContextMenu(
            _foreground.FocusIsContextMenu || _contextMenuArmed,
            OverlayLive() || SipLifecycle.Shared.OwnsOverlayContact(false),
            hasField);
        if (SelectionVisibilityPolicy.ShouldHide(
                selectionChrome,
                ImeHost.Shared.HasRangeSelection || field.HasRangeSelection,
                yieldMenu))
        {
            ResetPendingFirstShow();
            if (yieldMenu)
            {
                YieldForContextMenu();
            }
            else
            {
                HideUnlessPinned();
                Log.Info("触摸选区手柄，让开键盘");
            }

            return;
        }

        if (!KeyboardVisibilityPolicy.ShouldShow(
                true,
                _userDismissed,
                visibleT9Lease,
                _invocationAuthorized))
        {
            if (KeyboardShown()
                && DateTime.UtcNow - _lastAuthorizedUtc <= TimeSpan.FromMilliseconds(500))
            {
                Log.Info("刚授权弹出，这一拍可见条件抖动，先不收");
                return;
            }

            if (SipFocusTrackingPolicy.ShouldHoldHideForFocusQuery(
                    _invocationAuthorized,
                    SipLifecycle.Shared.HasRecentExternalGesture(),
                    hasField || looksLikeText))
            {
                Log.Info("焦点查询已看到输入框，等授权，先不收");
                return;
            }

            ResetPendingFirstShow();
            Log.Info("可见条件不成立，收起键盘");
            HideUnlessPinned();
            return;
        }

        if (InputFieldSelectionPolicy.NeedsAuthoritativeFirstShow(
                systemTextHost,
                hasUiField,
                uiField,
                hasNativeField,
                nativeField))
        {
            if (_pendingFirstShowSurface != uiField.TopLevel)
            {
                _pendingFirstShowSurface = uiField.TopLevel;
                _pendingFirstShowFallbackAuthorized = false;
                _firstShowFallback.Stop();
                _firstShowFallback.Start();
            }

            if (!_pendingFirstShowFallbackAuthorized)
            {
                return;
            }
            _pendingFirstShowFallbackAuthorized = true;
        }
        else
        {
            ResetPendingFirstShow();
        }

        if (systemTextHost && !hasNativeField)
        {
            SystemBoxInput.CaptureFocused();
        }
        else
        {
            SystemBoxInput.ClearCapture();
        }
        _foreground.Remember(field.TopLevel);
        var alreadyVisible = KeyboardShown() && _invocationAuthorized;
        var lineChanged = KeyboardPositionSession.ShouldFollowTypingLine(
            _lastShownCaret,
            field.Caret,
            _lastShownCaretIsInsertion,
            field.IsInsertionCaret);
        var fieldChanged = SipLifecyclePolicy.FieldIdentityChanged(
            _authorizedFieldId,
            field.FieldId);
        var caretTapped = SipLifecycle.Shared.Gesture == SipGesture.OnEdit
            && SipLifecycle.Shared.HasRecentExternalGesture()
            && !OverlayLive();
        if (SipLifecyclePolicy.ShouldRepositionNow(
                alreadyVisible, fieldChanged, lineChanged, caretTapped))
        {
            _repositionRequested = true;
        }
        else
        {
            _repositionRequested = false;
        }
        _lastShownCaret = field.Caret;
        _lastShownCaretIsInsertion = field.IsInsertionCaret;
        RememberAuthorizedField(field);
        var repositionRequested = _repositionRequested;
        if (repositionRequested)
        {
            Log.Info(
                $"重定位键盘 表面={ShellProcess.Name(field.TopLevel)} "
                + $"光标=({field.Caret.Left},{field.Caret.Top})");
        }
        if (!alreadyVisible)
        {
            SuppressOfficialSip(field.TopLevel);
        }

        if (!repositionRequested && alreadyVisible)
        {
            if (KeyboardShown())
            {
                _shownUtc = DateTime.UtcNow;
                SipLifecycle.Shared.SetPhase(SipPhase.Visible);
                SipLifecycle.Shared.ConsumeTap();
            }

            _repositionRequested = false;
            return;
        }

        var target = KeyboardPlacer.Place(field, boardW, boardH);
        if (alreadyVisible && lineChanged && !fieldChanged && !caretTapped)
        {
            target = KeyboardPositionSession.PinHorizontal(_overlay.PlacedRect, target);
        }

        _overlay.PlaceOn(
            target,
            field.TopLevel,
            field.Owner,
            field.Context,
            repositionRequested);
        if (KeyboardShown())
        {
            _shownUtc = DateTime.UtcNow;
            SipLifecycle.Shared.SetPhase(SipPhase.Visible);
            SipLifecycle.Shared.ConsumeTap();
        }
        _repositionRequested = false;
    }

    private void ApplyPendingPointer(
        bool systemTextHost,
        bool hasUiField,
        bool hasNativeField,
        InputField field,
        bool surfaceChanged,
        bool foregroundChanged)
    {
        if (_pendingPointer is not { } pointer)
        {
            return;
        }

        var skipHitTest = _pendingHitTested && _pendingHitUnavailable;
        var hit = skipHitTest
            ? PointerInputHit.Unavailable
            : InputInvocationProbe.HitTestFocusedInput(
                pointer.X,
                pointer.Y,
                _authorizedFieldBox,
                _notedPointerHit);
        _notedPointerHit = null;
        _pendingHitTested = true;
        // FromPoint 还停在整页容器上时命中是 Unavailable。已经拿到套得住
        // 这一下的框，就按 Inside 授权——否则第一次点经常先被 ShouldRelease 收掉。
        // 套不住的框绝不提升，点空白时 TryGet 交出来的仍是聊天框。
        var clickBelongsToField = FieldClickPolicy.OpenedBy(
            field.FieldBox, field.Caret, pointer.X, pointer.Y);
        if (hit != PointerInputHit.Outside && clickBelongsToField)
        {
            hit = PointerInputHit.Inside;
        }

        var clickInsideAuthorized = InputInvocationProbe.Contains(
            _authorizedFieldBox,
            pointer.X,
            pointer.Y,
            FieldClickPolicy.EdgeTolerance);
        var textIntent = TouchInvocationPolicy.IsTextIntent(
            _foreground.FocusEnteredTextInput,
            ImeHost.Shared.HasDocumentFocus && hasNativeField,
            skipHitTest
                ? false
                : InputInvocationProbe.FocusedLooksLikeTextInput());
        hit = TouchInvocationPolicy.Promote(
            hit,
            textIntent,
            _invocationAuthorized,
            clickInsideAuthorized);
        _pendingHit = hit;
        _pendingHitUnavailable = hit == PointerInputHit.Unavailable;
        if (KeyboardInvocationPolicy.ShouldReleaseAuthorizedField(
                _invocationAuthorized,
                clickInsideAuthorized,
                hit))
        {
            _invocationAuthorized = false;
            _revokedThisSync = true;
            _invocationOrigin = PointerInvocationOrigin.Unknown;
            _repositionRequested = false;
            ClearAuthorizedField();
            Log.Info("落点离开当前输入框，收起键盘");
            if (KeyboardInvocationPolicy.ShouldConsumeLeaveClick(hit))
            {
                _dismissedClick = (pointer.X, pointer.Y, DateTime.UtcNow);
                ClearPendingPointer();
                ResetCaretWait();
                _foreground.ClearFocusEntered();
                InputInvocationProbe.ClearClickedField();
                return;
            }
        }

        var age = DateTime.UtcNow - pointer.CreatedUtc;
        var expired = age > PointerIntentWindow
            || (hit == PointerInputHit.Unavailable
                && pointer.Origin == PointerInvocationOrigin.Unknown
                && age > UnavailableIntentWindow);
        var resolved = KeyboardInvocationPolicy.TryResolvePointer(
            hit,
            systemTextHost,
            pointer.Origin,
            _invocationAuthorized,
            _userDismissed,
            expired,
            out var authorized);
        if (resolved || hit != PointerInputHit.Unavailable)
        {
            Log.Info(
                $"系统指针解析 origin={pointer.Origin} hit={hit} "
                + $"host={systemTextHost} resolved={resolved} "
                + $"authorized={authorized} expired={expired} "
                + $"surface={ShellProcess.Name(field.TopLevel)} "
                + $"ui={hasUiField} native={hasNativeField} "
                + $"intent={textIntent} "
                + $"surfaceChanged={surfaceChanged} "
                + $"foregroundChanged={foregroundChanged}");
        }

        if (!resolved)
        {
            return;
        }

        // 鼠标点中了输入，当前坐标却属于另一个框：不消费这次点击。
        // 触摸没有可靠屏幕坐标，系统 SIP 直接信当前文档的 GetTextExt。
        if (authorized
            && _pointerHasScreenPoint
            && !field.FromClicked
            && !clickBelongsToField)
        {
            Log.Info(
                $"等待点中框的光标 落点=({pointer.X},{pointer.Y}) "
                + $"焦点=({field.Caret.Left},{field.Caret.Top})");
            return;
        }

        if (authorized && IsDismissEcho(pointer.X, pointer.Y))
        {
            authorized = false;
            Log.Info("忽略同一次点击的再次授权");
        }

        _invocationAuthorized = authorized;
        _invocationOrigin = authorized
            ? pointer.Origin
            : PointerInvocationOrigin.Unknown;
        _repositionRequested = authorized;
        if (authorized)
        {
            _contextMenuArmed = false;
            _lastAuthorizedUtc = DateTime.UtcNow;
            _foreground.ClearFocusLeft();
            _userDismissed = false;
            _dismissedClick = null;
            if (field.FromClicked || clickBelongsToField || !_pointerHasScreenPoint)
            {
                RememberAuthorizedField(field);
            }
        }
        else
        {
            _dismissedClick = (pointer.X, pointer.Y, DateTime.UtcNow);
            ClearAuthorizedField();
            InputInvocationProbe.ClearClickedField();
        }

        _foreground.ClearFocusEntered();
        ClearPendingPointer();
    }

    private void ClearPendingPointer()
    {
        _pendingPointer = null;
        _pendingHitUnavailable = false;
        _pendingHit = default;
        _pendingHitTested = false;
        _notedPointerHit = null;
    }

    private void TryAuthorizeSlateFocus(bool hasField, IntPtr top)
    {
        if (_invocationAuthorized
            || !KeyboardInvocationPolicy.ShouldReauthorizeAfterRevoke(_revokedThisSync))
        {
            return;
        }

        var slate = TouchDevicePolicy.CurrentPreferTouchHitSlop();
        var looksLikeText = InputInvocationProbe.FocusedLooksLikeTextInput();
        var editTap = EditTouch.AllowsShow(looksLikeText || _foreground.FocusEnteredTextInput);
        if (!editTap)
        {
            return;
        }

        var hasInput = hasField || looksLikeText;
        var textIntent = TouchInvocationPolicy.ShouldShowForTouchFocus(
            slate,
            ImeHost.Shared.HasDocumentFocus,
            _foreground.FocusEnteredTextInput,
            looksLikeText,
            editTap);
        if (!KeyboardInvocationPolicy.ShouldAuthorizeSlateOcclusion(
                slate,
                hasInput,
                _foreground.FocusEnteredTextInput,
                _pendingPointer is { } pending
                    && OfficialSipHit.IsKeyboardSurface(pending.X, pending.Y),
                _foreground.FocusOnPageSurface,
                editTap)
            && !KeyboardInvocationPolicy.ShouldAuthorizeSlateFocus(slate, textIntent))
        {
            return;
        }

        _invocationAuthorized = true;
        _invocationOrigin = KeyboardInvocationPolicy.OriginForSearchSurface(
            ShellProcess.Name(top),
            ShellProcess.IsTrayChrome(top),
            ShellProcess.HasVisibleSearchFlyout());
        _contextMenuArmed = false;
        _userDismissed = false;
        _dismissedClick = null;
        _placementClick = null;
        _lastAuthorizedUtc = DateTime.UtcNow;
        ClearPendingPointer();
        _foreground.ClearFocusLeft();
        _foreground.ClearFocusEntered();
        _repositionRequested = true;
        SipLifecycle.Shared.SetPhase(SipPhase.Visible);
        SuppressOfficialSipOnce();
        Log.Info("平板触摸点进输入框，授权弹出");
    }

    private void SuppressOfficialSipOnce()
    {
        if (!SipSuppressionPolicy.ShouldSuppressOfficialSip(ImeHost.Shared.HasOfficialT9Profile()))
        {
            return;
        }

        SuppressOfficialSip(NativeMethods.GetForegroundWindow());
        if (_sipHideRetryQueued)
        {
            return;
        }

        _sipHideRetryQueued = true;
        _overlay.Dispatcher.BeginInvoke(
            DispatcherPriority.Input,
            new Action(() =>
            {
                _sipHideRetryQueued = false;
                SuppressOfficialSip(NativeMethods.GetForegroundWindow());
            }));
    }

    private static void SuppressOfficialSip(IntPtr hwnd)
    {
        if (!SipSuppressionPolicy.ShouldSuppressOfficialSip(ImeHost.Shared.HasOfficialT9Profile()))
        {
            return;
        }

        InputPaneController.TryHideWinRt(hwnd);
        var tray = NativeMethods.FindWindow("Shell_TrayWnd", null);
        if (tray != IntPtr.Zero && tray != hwnd)
        {
            InputPaneController.TryHideWinRt(tray);
        }

        var secondary = NativeMethods.FindWindow("Shell_SecondaryTrayWnd", null);
        if (secondary != IntPtr.Zero && secondary != hwnd)
        {
            InputPaneController.TryHideWinRt(secondary);
        }

        if (InputPaneInterop.TryGetLocation(out _))
        {
            InputPaneController.TryHideWinRt(hwnd);
            Log.Info("T9 已接管，收起系统触摸键盘");
        }
    }

    private bool IsDismissEcho(int x, int y)
    {
        if (_dismissedClick is not { } dismissed)
        {
            return false;
        }

        var age = DateTime.UtcNow - dismissed.Utc;
        if (age < TimeSpan.Zero || age > DismissEchoWindow)
        {
            return false;
        }

        return Math.Abs(x - dismissed.X) <= 8 && Math.Abs(y - dismissed.Y) <= 8;
    }

    private void RememberAuthorizedField(InputField field)
    {
        if (!field.FieldBox.IsEmpty && field.FieldBox.Bottom - field.FieldBox.Top <= 160)
        {
            _authorizedFieldBox = field.FieldBox;
        }
        else if (!field.Caret.IsEmpty)
        {
            _authorizedFieldBox = new NativeRect
            {
                Left = field.Caret.Left - 16,
                Top = field.Caret.Top - 8,
                Right = field.Caret.Left + 280,
                Bottom = field.Caret.Bottom + 8
            };
        }

        if (!string.IsNullOrEmpty(field.FieldId))
        {
            _authorizedFieldId = field.FieldId;
        }
    }

    private void ClearAuthorizedField()
    {
        _authorizedFieldBox = default;
        _authorizedFieldId = string.Empty;
    }

    private void AwaitCaret()
    {
        if (_caretWaitExpired)
        {
            return;
        }

        if (_caretWaitStartedUtc == DateTime.MinValue)
        {
            _caretWaitStartedUtc = DateTime.UtcNow;
        }

        QueueOneSettledRetry();
    }

    /// <summary>坐标已到手或用户重新点过，等待重新开始计时。</summary>
    private void ResetCaretWait()
    {
        _caretWaitStartedUtc = DateTime.MinValue;
        _caretWaitExpired = false;
    }

    private void ResetPendingFirstShow()
    {
        _firstShowFallback.Stop();
        _pendingFirstShowSurface = IntPtr.Zero;
        _pendingFirstShowFallbackAuthorized = false;
    }

    private bool ShouldHoldHideDuringFocusHandoff() =>
        _pendingFirstShowSurface != IntPtr.Zero
        || DateTime.UtcNow - _lastAuthorizedUtc <= TimeSpan.FromMilliseconds(500);

    private void HideUnlessPinned()
    {
        if (!KeyboardPinPolicy.ShouldAutoHide(_overlay.IsPinned))
        {
            return;
        }

        SipLifecycle.Shared.SetPhase(SipPhase.Hidden);
        _overlay.HideOverlay();
    }

    private void RevokeForFocusLeave(string reason)
    {
        CancelFocusLeaveConfirm();
        _touchDismissArmed = false;
        _focusLeavePending = false;
        SipLifecycle.Shared.NoteLeave();
        _foreground.ClearFocusLeft();
        if (_invocationAuthorized)
        {
            _invocationAuthorized = false;
            _revokedThisSync = true;
            _invocationOrigin = PointerInvocationOrigin.Unknown;
            _repositionRequested = false;
            ResetCaretWait();
            ClearPendingPointer();
            ClearAuthorizedField();
            InputInvocationProbe.ClearClickedField();
            EditTouch.Note(onText: false, onLeave: true);
            Log.Info(reason);
        }

        HideUnlessPinned();
    }

    private void ArmFocusLeaveConfirm()
    {
        if (!_focusLeaveConfirm.IsEnabled)
        {
            _focusLeaveConfirm.Start();
        }
    }

    private void CancelFocusLeaveConfirm()
    {
        _focusLeaveConfirm.Stop();
        _focusLeavePending = false;
        _touchDismissArmed = false;
    }

    private void OnOverlayTouch()
    {
        _touchDismissArmed = false;
        _focusLeavePending = false;
        _focusLeaveConfirm.Stop();
        CancelSipFocusQuery();
    }

    private bool KeyboardShown() =>
        KeyboardSurfacePolicy.IsShown(_overlay.IsVisible, _overlay.IsHosting);

    private bool OverlayLive() =>
        _overlay.AreAnyTouchesOver
        || _overlay.IsStylusOver
        || ImeHost.Shared.HostPointerLive;

    private bool OverlayOwnsPointer() =>
        SipLifecycle.Shared.OwnsOverlayContact(OverlayLive());

    private void ApplyVisibilityDecision(
        bool focusIsText,
        bool hardLeave,
        bool selectionChrome,
        bool hasExternalGesture,
        bool allowRelayout,
        bool contextMenu = false)
    {
        var action = SipLifecyclePolicy.Decide(
            KeyboardShown(),
            OverlayLive() || InputInvocationProbe.FocusedIsOwnPane(),
            SipLifecycle.Shared.OwnsOverlayContact(false),
            focusIsText,
            hardLeave,
            selectionChrome,
            hasExternalGesture,
            contextMenu,
            KeyboardInvocationPolicy.IsSearchInvocation(_invocationOrigin)
                || (_pendingPointer is { } waiting
                    && KeyboardInvocationPolicy.IsSearchInvocation(waiting.Origin)));
        if (action == SipVisibilityAction.Relayout && !allowRelayout)
        {
            action = SipVisibilityAction.Stay;
        }

        switch (action)
        {
            case SipVisibilityAction.Stay:
                return;
            case SipVisibilityAction.Pending:
                if (_invocationAuthorized && !KeyboardShown())
                {
                    _invocationAuthorized = false;
                    _revokedThisSync = true;
                    ClearAuthorizedField();
                }

                SipLifecycle.Shared.SetPhase(SipPhase.Pending);
                return;
            case SipVisibilityAction.Hide:
                if (ShouldHoldHideDuringFocusHandoff())
                {
                    Log.Info("刚授权弹出，焦点查询还在交接，先不收");
                    return;
                }

                RevokeForFocusLeave("焦点已不是可输入控件，收起键盘");
                return;
            case SipVisibilityAction.Show:
            case SipVisibilityAction.Relayout:
                SipLifecycle.Shared.NoteSettled(SipGesture.OnEdit);
                SyncCore();
                return;
        }
    }

    private void QueueSipFocusQuery()
    {
        if (_sipQueryOp is { Status: DispatcherOperationStatus.Pending })
        {
            _sipQueryOp.Abort();
        }

        _sipQueryOp = _overlay.Dispatcher.BeginInvoke(
            DispatcherPriority.Input,
            new Action(ConfirmSipFocusQuery));
    }

    private void CancelSipFocusQuery()
    {
        if (_sipQueryOp is { Status: DispatcherOperationStatus.Pending })
        {
            _sipQueryOp.Abort();
        }

        _sipQueryOp = null;
    }

    /// <summary>
    /// 官方 TipTsfHelper：推迟到 Input 优先级，焦点可能已经变了再查。
    /// 点在盘面上（正在按、刚按下、或焦点已落到我们自己的按钮）不收。
    /// </summary>
    private void ConfirmSipFocusQuery()
    {
        _sipQueryOp = null;
        if (SipLifecyclePolicy.ShouldStayForKeyboardFocus(
                OverlayLive(),
                SipLifecycle.Shared.OwnsOverlayContact(false),
                InputInvocationProbe.FocusedIsOwnPane()))
        {
            SipLifecycle.Shared.NoteOverlayContact();
            return;
        }

        var looksLikeText = InputInvocationProbe.FocusedLooksLikeTextInput();
        var hardLeave = InputInvocationProbe.FocusedIsHardLeave();
        var left = hardLeave
            || _foreground.FocusOnPageSurface
            || _foreground.FocusLeftTextInput;
        var searchHold =
            _pendingPointer is { } waiting
                && KeyboardInvocationPolicy.IsSearchInvocation(waiting.Origin)
            || KeyboardInvocationPolicy.IsSearchInvocation(_invocationOrigin);
        if (PointerContactPolicy.ShouldYieldToContextMenu(
                _foreground.FocusIsContextMenu || _contextMenuArmed,
                OverlayLive() || SipLifecycle.Shared.OwnsOverlayContact(false),
                looksLikeText))
        {
            YieldForContextMenu();
            return;
        }

        ApplyVisibilityDecision(
            looksLikeText && !left,
            left,
            selectionChrome: false,
            hasExternalGesture: SipLifecycle.Shared.HasRecentExternalGesture()
                && (!searchHold || looksLikeText),
            allowRelayout: false,
            contextMenu: false);
    }

    private void YieldForContextMenu()
    {
        CancelSipFocusQuery();
        _contextMenuArmed = true;
        if (!KeyboardShown() && !_invocationAuthorized)
        {
            return;
        }

        RevokeForFocusLeave("右键菜单，让开键盘");
        if (KeyboardShown())
        {
            SipLifecycle.Shared.SetPhase(SipPhase.Hidden);
            _overlay.HideOverlay();
        }
    }

    private void ConfirmTouchFocusLeave()
    {
        _focusLeaveConfirm.Stop();
        if (OverlayLive())
        {
            _focusLeavePending = false;
            _touchDismissArmed = false;
            return;
        }

        var looksLikeText = InputInvocationProbe.FocusedLooksLikeTextInput();
        if (SipVisibilityPolicy.ShouldHideAfterTouchSettle(
                touchSettled: _touchDismissArmed || _focusLeavePending,
                overlayOwnsTouch: false,
                looksLikeText,
                _foreground.FocusEnteredTextInput,
                _foreground.FocusLeftIsSelectionChrome,
                SipVisibilityPolicy.IsNearbyFieldChrome(
                    _authorizedFieldBox,
                    _foreground.LastFocusBounds),
                KeyboardInvocationPolicy.IsSearchInvocation(_invocationOrigin))
            && !ShouldHoldHideDuringFocusHandoff())
        {
            RevokeForFocusLeave("焦点已不是可输入控件，收起键盘");
            return;
        }

        // 官方模型：这一拍焦点还是输入框，不能当成已经离开。
        // 武装留下，等下一次焦点变化再同步查询。
        _focusLeavePending = false;
    }

    private void OnPinChanged(bool pinned)
    {
        if (pinned)
        {
            return;
        }

        _overlay.ReleaseDragAnchor();
        if (KeyboardAnchorPolicy.ShouldRelayoutOnUnlock(pinned, _invocationAuthorized))
        {
            _repositionRequested = true;
        }

        SyncCore();
    }

    private void OnBoardLayoutChanged()
    {
        // 切盘只改窗口尺寸。禁止再走 UIA/SyncCore，否则会拿到整页 Document
        // 顶上的假光标，键盘飞到屏幕顶端挡住输入。
    }

}
