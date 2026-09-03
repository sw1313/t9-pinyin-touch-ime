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
            // 看不清就不决断。不在这一拍用时钟收盘。
            return false;
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
    /// SampleIME 只在候选窗自己的文档指针变了才藏。TSF 上下文无效本身不够：
    /// Chromium 换框会先 SetFocus(null)，SearchHost 交接也会打无效。
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
        int y) =>
        fromClicked || OpenedBy(fieldBox, caret, x, y);
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
    private readonly OfficialTouchKeyboardGuard _officialTouch = new();
    /// <summary>一次点击最多等多久。任务栏搜索交接给 SearchHost 实测约 700ms。</summary>
    private static readonly TimeSpan PointerIntentWindow = TimeSpan.FromSeconds(2);

    /// <summary>重判间隔。只在有待决点击时走，点击落定就停，不是常态轮询。</summary>
    private static readonly TimeSpan PointerRetryInterval =
        TimeSpan.FromMilliseconds(70);

    private readonly System.Windows.Threading.DispatcherTimer _firstShowFallback;
    private readonly System.Windows.Threading.DispatcherTimer _pointerRetry;
    private bool _userDismissed;
    private DateTime _lastActiveContextUtc = DateTime.MinValue;
    private IntPtr _lastActiveContextHost;
    private IntPtr _pendingFirstShowSurface;
    private bool _pendingFirstShowFallbackAuthorized;
    private bool _invocationAuthorized;
    private PointerInvocationOrigin _invocationOrigin;
    private IntPtr _invocationSurface;
    private IntPtr _invocationForeground;
    private bool _repositionRequested;
    private (int X, int Y, PointerInvocationOrigin Origin, DateTime CreatedUtc)?
        _pendingPointer;
    private (int X, int Y)? _placementClick;

    /// <summary>
    /// "已经授权要弹，但还没拿到坐标"的等待截止时间。
    ///
    /// 取不到输入框时 SyncCore 会早退，而同步只由事件驱动：这段时间里恰好没有别的
    /// 事件路过，这一次就再也没人管了，键盘一直不出来。系统搜索框换框后 UIA 要
    /// 100~600ms 才交出真坐标，正好落在这个空档里，所以必须自己驱动重试。
    /// </summary>
    private DateTime _awaitingCaretUntil = DateTime.MinValue;

    /// <summary>等坐标的上限。真坐标实测 100~600ms 内到达，留足余量即可。</summary>
    private static readonly TimeSpan CaretWaitWindow = TimeSpan.FromMilliseconds(1500);

    /// <summary>
    /// 刚因这一下落点收起键盘。同一坐标在极短时间内再来一次 Inside
    /// （日志里 Outside 后 24ms 又 Inside）是同一次点击的回声，不能再授权。
    /// </summary>
    private (int X, int Y, DateTime Utc)? _dismissedClick;
    private NativeRect _lastShownCaret;
    private NativeRect _authorizedFieldBox;
    private string _authorizedFieldId = string.Empty;
    private static readonly TimeSpan DismissEchoWindow = TimeSpan.FromMilliseconds(400);

    public KeyboardSession(AppSettings settings, T9OverlayWindow overlay, ForegroundTracker foreground)
    {
        _settings = settings;
        _overlay = overlay;
        _foreground = foreground;
        _pointerRetry = new System.Windows.Threading.DispatcherTimer
        {
            Interval = PointerRetryInterval
        };
        _pointerRetry.Tick += (_, _) =>
        {
            // 超时判定必须在这里，不能只放在 SyncCore 里：SyncCore 有几条早退路径
            // (租约未就绪、托盘分支、取不到输入框)位于判定点击之前，走那些路径时
            // 判定和丢弃都跑不到，点击会永远悬着、定时器空转。
            if (_pendingPointer is { } pending
                && DateTime.UtcNow - pending.CreatedUtc > PointerIntentWindow)
            {
                _pendingPointer = null;
            }

            if (DateTime.UtcNow > _awaitingCaretUntil)
            {
                _awaitingCaretUntil = DateTime.MinValue;
            }

            if (_pendingPointer is null && _awaitingCaretUntil == DateTime.MinValue)
            {
                _pointerRetry.Stop();
                return;
            }

            SyncCore();
            if (_pendingPointer is null && _awaitingCaretUntil == DateTime.MinValue)
            {
                _pointerRetry.Stop();
            }
        };
        _firstShowFallback = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(600)
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
        _overlay.UserClosed += () =>
        {
            _userDismissed = true;
            _invocationAuthorized = false;
            _invocationOrigin = PointerInvocationOrigin.Unknown;
            ClearAuthorizedField();
            _repositionRequested = false;
            ResetPendingFirstShow();
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

    public void NotePointerInput(int x, int y, PointerInvocationOrigin origin)
    {
        // 点到当前授权框外面就清掉上一个点中的框，否则 TryGet 还会交出
        // 上一个框的坐标，键盘停在旧位置。点在框里则保留，供这一拍定位。
        if (!InputInvocationProbe.Contains(_authorizedFieldBox, x, y, FieldClickPolicy.EdgeTolerance))
        {
            InputInvocationProbe.ClearClickedField();
        }

        InputInvocationProbe.HitTestPointerTarget(x, y, _authorizedFieldBox);

        _pendingPointer = (x, y, origin, DateTime.UtcNow);
        _placementClick = (x, y);
        SyncCore();
        DrivePendingPointer();
    }

    /// <summary>
    /// 待决点击必须自己驱动重判。
    ///
    /// 点任务栏搜索时第一下落在的是 explorer 的搜索按钮，真正的文本框要等
    /// SearchHost 接管后才存在(实测约 700ms)，所以首次判定拿到 Outside 是正常的，
    /// 判定逻辑本来也设计成"还没准备好就继续等"。但同步只由事件触发：这段时间里
    /// 恰好没有别的事件路过，这一下就再也没人看它，连超时兜底都没机会跑，
    /// 用户只能再点一次。成功与否取决于有没有事件凑巧经过，这就是那个随机性的来源。
    /// </summary>
    private void DrivePendingPointer()
    {
        if (_pendingPointer is null)
        {
            _pointerRetry.Stop();
            return;
        }

        if (!_pointerRetry.IsEnabled)
        {
            _pointerRetry.Start();
        }
    }

    public void Sync(bool imeDocument = false) => SyncCore();

    public void Shutdown() => _officialTouch.Dispose();

    private void SyncCore()
    {
        using var scope = Perf.Begin("session.sync");
        _officialTouch.Sync(OfficialTouchKeyboardPolicy.ShouldSuppress(
            _settings.Enabled,
            T9ProfileProbe.IsSelected()
                || ImeHost.Shared.CanCommitForeground()
                || ImeHost.Shared.HasForegroundProfileLease()
                || ImeHost.Shared.HasSystemProfileLease()));
        var fg = NativeMethods.GetForegroundWindow();
        var top = NativeMethods.GetAncestor(fg, NativeMethods.GaRoot);
        var hasTaskbarSearch = InputFieldProbe.TryGetFocusedTaskbarSearch(
            out var taskbarSearchField);
        var systemTextHost = ShellProcess.IsForegroundSystemTextHost()
            || hasTaskbarSearch;
        var t9ContextActive = KeyboardVisibilityPolicy.IsT9ContextActive(
            ImeHost.Shared.CanCommitForeground());

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
            || (_invocationAuthorized
                && KeyboardInvocationPolicy.IsSearchInvocation(_invocationOrigin));
        var holdFocusLeft = KeyboardInvocationPolicy.ShouldHoldFocusLeftForSearch(
            pendingSearch,
            _overlay.IsVisible || _invocationAuthorized);
        if (_foreground.FocusLeftTextInput && !holdFocusLeft)
        {
            if (_invocationAuthorized)
            {
                _invocationAuthorized = false;
                _invocationOrigin = PointerInvocationOrigin.Unknown;
                _repositionRequested = false;
                _awaitingCaretUntil = DateTime.MinValue;
                _pendingPointer = null;
                ClearAuthorizedField();
                InputInvocationProbe.ClearClickedField();
                Log.Info("焦点离开文本框，收起键盘");
            }

            _foreground.ClearFocusLeft();
        }
        else if (holdFocusLeft)
        {
            _foreground.ClearFocusLeft();
        }

        // Chromium 换框 / SearchHost 交接都会短暂打出上下文无效。
        // 只有“这一下点的不是输入框，而且 UIA 也不是输入框”才按离开收。
        if (KeyboardInvocationPolicy.ShouldDismissForLostDocument(
                ImeHost.Shared.HasDocumentFocus,
                InputInvocationProbe.FocusedLooksLikeTextInput(),
                searchSession,
                _pendingPointer is not null)
            && _invocationAuthorized)
        {
            _invocationAuthorized = false;
            _invocationOrigin = PointerInvocationOrigin.Unknown;
            _repositionRequested = false;
            _awaitingCaretUntil = DateTime.MinValue;
            _pendingPointer = null;
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
            _overlay.IsVisible,
            top != IntPtr.Zero && top == _lastActiveContextHost,
            ImeHost.Shared.HasProfileLeaseFor(top),
            DateTime.UtcNow - _lastActiveContextUtc);

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
        var visibleT9Lease = t9ContextActive || hasProfileLease || desktopContextGrace;
        if (!_settings.Enabled)
        {
            ResetPendingFirstShow();
            _overlay.SetPinned(false);
            _overlay.HideOverlay();
            return;
        }

        if (!visibleT9Lease)
        {
            if (searchSession)
            {
                AwaitCaret();
                return;
            }

            ResetPendingFirstShow();
            HideUnlessPinned();
            return;
        }

        _overlay.PixelSize(out var boardW, out var boardH);
        if (_settings.PreviewMode)
        {
            _overlay.PlaceOn(KeyboardPlacer.Preview(boardW, boardH), IntPtr.Zero);
            return;
        }

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

        // 坐标采纳在离开判定之后。套不住这一下落点的光标不能摆——
        // 日志里先重定位 (1016,931) 再读到 (1244,136)，就是焦点还在聊天框。
        var placementClick = _pendingPointer is { } pending
            ? (pending.X, pending.Y)
            : _placementClick;
        if (hasField
            && placementClick is { } click
            && !FieldClickPolicy.Trusts(
                field.FromClicked, field.FieldBox, field.Caret, click.X, click.Y))
        {
            Log.Info(
                $"丢弃焦点光标 光标=({field.Caret.Left},{field.Caret.Top}) "
                + $"落点=({click.X},{click.Y})");
            hasField = false;
            field = default;
        }
        else if (hasField
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

        if (!hasField)
        {
            // 已经授权要弹、或还有一次点击悬着，就必须自己驱动重试。否则这一轮
            // 早退之后没有任何事件会再来同步，键盘就一直不出来——"点了不弹"。
            if ((!_invocationAuthorized || rejectedWrongBox)
                && _overlay.IsVisible
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
        _awaitingCaretUntil = DateTime.MinValue;
        field = InputFieldSelectionPolicy.NormalizeDesktopSurface(
            systemTextHost,
            field,
            top,
            _foreground.Generation);
        _invocationSurface = field.TopLevel;

        if (!KeyboardVisibilityPolicy.ShouldShow(
                _settings.Enabled,
                _userDismissed,
                visibleT9Lease,
                _invocationAuthorized))
        {
            ResetPendingFirstShow();
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
                if (_repositionRequested && _overlay.IsVisible)
                {
                    HideUnlessPinned();
                }
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
        if (KeyboardPositionSession.ShouldFollowTypingLine(_lastShownCaret, field.Caret))
        {
            _repositionRequested = true;
        }
        _lastShownCaret = field.Caret;
        if (field.FromClicked
            || string.Equals(field.FieldId, _authorizedFieldId, StringComparison.Ordinal))
        {
            if (!field.FieldBox.IsEmpty)
            {
                _authorizedFieldBox = field.FieldBox;
            }

            if (!string.IsNullOrEmpty(field.FieldId))
            {
                _authorizedFieldId = field.FieldId;
            }
        }
        var repositionRequested = _repositionRequested;
        if (repositionRequested)
        {
            Log.Info(
                $"重定位键盘 表面={ShellProcess.Name(field.TopLevel)} "
                + $"光标=({field.Caret.Left},{field.Caret.Top})");
        }
        _overlay.PlaceOn(
            KeyboardPlacer.Place(field, boardW, boardH),
            field.TopLevel,
            field.Owner,
            field.Context,
            repositionRequested);
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

        var hit = InputInvocationProbe.HitTestFocusedInput(
            pointer.X,
            pointer.Y,
            _authorizedFieldBox);
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
        if (KeyboardInvocationPolicy.ShouldReleaseAuthorizedField(
                _invocationAuthorized,
                clickInsideAuthorized,
                hit))
        {
            _invocationAuthorized = false;
            _invocationOrigin = PointerInvocationOrigin.Unknown;
            _repositionRequested = false;
            ClearAuthorizedField();
            Log.Info("落点离开当前输入框，收起键盘");
            if (KeyboardInvocationPolicy.ShouldConsumeLeaveClick(hit))
            {
                _dismissedClick = (pointer.X, pointer.Y, DateTime.UtcNow);
                _pendingPointer = null;
                _awaitingCaretUntil = DateTime.MinValue;
                InputInvocationProbe.ClearClickedField();
                return;
            }
        }

        var expired = DateTime.UtcNow - pointer.CreatedUtc > PointerIntentWindow;
        var resolved = KeyboardInvocationPolicy.TryResolvePointer(
            hit,
            systemTextHost,
            pointer.Origin,
            _invocationAuthorized,
            _userDismissed,
            expired,
            out var authorized);
        Log.Info(
            $"系统指针解析 origin={pointer.Origin} hit={hit} "
            + $"host={systemTextHost} resolved={resolved} "
            + $"authorized={authorized} expired={expired} "
            + $"surface={ShellProcess.Name(field.TopLevel)} "
            + $"ui={hasUiField} native={hasNativeField} "
            + $"surfaceChanged={surfaceChanged} "
            + $"foregroundChanged={foregroundChanged}");
        if (!resolved)
        {
            return;
        }

        // 命中了输入，当前坐标却属于另一个框：不消费这次点击，也不授权摆窗。
        // GetTextExt / OnCaretBoundsChanged 都只接受当前文档的光标。
        if (authorized && !field.FromClicked && !clickBelongsToField)
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
            _foreground.ClearFocusLeft();
            _userDismissed = false;
            _dismissedClick = null;
            if (field.FromClicked || clickBelongsToField)
            {
                if (!string.IsNullOrEmpty(field.FieldId))
                {
                    _authorizedFieldId = field.FieldId;
                }

                if (!field.FieldBox.IsEmpty)
                {
                    _authorizedFieldBox = field.FieldBox;
                }
            }
        }
        else
        {
            _dismissedClick = (pointer.X, pointer.Y, DateTime.UtcNow);
            ClearAuthorizedField();
            InputInvocationProbe.ClearClickedField();
        }

        _pendingPointer = null;
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

    private void ClearAuthorizedField()
    {
        _authorizedFieldBox = default;
        _authorizedFieldId = string.Empty;
    }

    private void AwaitCaret()
    {
        if (_awaitingCaretUntil == DateTime.MinValue)
        {
            _awaitingCaretUntil = DateTime.UtcNow + CaretWaitWindow;
        }

        if (!_pointerRetry.IsEnabled)
        {
            _pointerRetry.Start();
        }
    }

    private void ResetPendingFirstShow()
    {
        _firstShowFallback.Stop();
        _pendingFirstShowSurface = IntPtr.Zero;
        _pendingFirstShowFallbackAuthorized = false;
    }

    private void HideUnlessPinned()
    {
        if (!KeyboardPinPolicy.ShouldAutoHide(_overlay.IsPinned))
        {
            return;
        }

        _overlay.HideOverlay();
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
        if (!KeyboardAnchorPolicy.ShouldFollowInput(_overlay.IsPinned, _invocationAuthorized))
        {
            return;
        }

        _repositionRequested = true;
        SyncCore();
    }

}
