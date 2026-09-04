using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Interop;
using System.Windows.Threading;
using T9Pane.Native;
using T9Pane.Services;
using Button = System.Windows.Controls.Button;
using Brush = System.Windows.Media.Brush;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using MouseEventHandler = System.Windows.Input.MouseEventHandler;
using Point = System.Windows.Point;

namespace T9Pane.Overlay;

internal partial class T9OverlayWindow
{
    private readonly T9Engine _engine;
    private readonly AppSettings _settings;
    private readonly ForegroundTracker _foreground;
    private readonly List<T9Candidate> _candidates = [];
    private readonly List<Button> _barButtons = [];
    private readonly List<T9Candidate> _barCommit = [];
    private bool _chromeBound;
    private bool _rebuildKeysWhenIdle;
    private bool _keyVisuallyPressed;
    private readonly DispatcherTimer _longPressTimer;
    private string _digits = "";
    private enum Board { Pinyin, Pinyin26, English, Full, Number, SymbolCn, SymbolEn }

    private Board _board = Board.Pinyin;
    private Board _homeBoard = Board.Pinyin;
    private bool _english;
    private bool _latin;
    private bool _numberPad;
    private TouchModifierPhase _shift;
    private bool _caps;
    private bool _fn;
    private TouchModifierPhase _ctrl;
    private TouchModifierPhase _alt;
    private TouchModifierPhase _win;
    private string _letters = "";
    private static readonly string[] QuickCn = ["，", "。", "！", "？", "、"];
    private bool _candidatesExpanded;
    private double _candidateFallOffset;
    private double _candidateBarOffset;
    private double _railFallOffset;
    private bool _symbolLock;
    private string _symbolCategory = SymbolCatalog.Chinese;
    private double _symbolFallOffset;
    private double _categoryFallOffset;
    private bool _fallDragging;
    private Point? _fallDragLast;
    private readonly FallScroller _fallInertia = new();
    private FallScrollTarget? _fallTarget;
    private double _fallPressTime;
    private bool _inertiaTicking;
    private ScrollViewer? _markScroll;
    private ScrollViewer? _categoryScroll;
    private ScrollViewer? _candidateScroll;
    private ScrollViewer? _railScroll;
    private bool _railSyllables;
    private readonly List<string> _symbolRecent = [];
    private readonly List<string> _confirmedSyllables = [];
    private ContentControl? _leftRail;
    private bool _movedByUser;
    private NativeRect _placeBeforeSymbol;
    private Board _boardBeforeSymbol = Board.Pinyin;
    private bool _holdPlaceOnLayout;
    private bool _placingLayout;
    private bool _hosting;

    public bool IsHosting => _hosting;
    private readonly List<HostHitRegion<Button>> _hostHitRegions = [];
    private readonly HostActionMap<Button> _hostTapActions = new();
    private readonly HostActionMap<Button> _hostPressActions = new();
    private readonly Dictionary<Button, Func<bool>> _hostRepeats = [];
    private readonly HostPressGate _hostPressGate = new();
    private readonly DispatcherTimer _keyRepeatTimer;
    private Button? _keyRepeatButton;
    private Action? _keyRepeatAction;
    private Func<bool>? _keyRepeatGate;
    private bool _immediateTapFired;
    private Button? _pressedHostKey;
    private bool _holdHostFrame;

    /// <summary>按下效果生效的时刻，用来保证它在屏上留够能看见的时间。</summary>
    private DateTime _hostPressShownUtc = DateTime.MinValue;

    /// <summary>
    /// 按下效果的最短可见时间。
    ///
    /// 触摸抬手往往不到 50ms，而按下动画本身就要 50ms，抬起一来动画立刻反向，
    /// 于是"手感上按了、看上去没动"。鼠标按得久所以一直正常。系统浮层那条路更慢：
    /// 用户看到的是位图，按下要经原生上报、线程池、命名管道再加一次 BeginInvoke
    /// 才变成一帧，抬起常常先到。两条路都只推迟抬起的视觉，输入照旧立刻执行。
    /// </summary>
    private static readonly TimeSpan KeyPressMinVisible = TimeSpan.FromMilliseconds(90);

    private readonly DispatcherTimer _hostPressRelease;

    /// <summary>WPF 那条路上等着回弹的键，同上，为的是让按下看得见。</summary>
    private readonly DispatcherTimer _keyReleaseTimer;
    private Button? _keyReleasePending;
    private DateTime _keyPressedUtc = DateTime.MinValue;

    /// <summary>
    /// 长按已经替这颗键发过动作，抬起时就不该再补一次点击。
    /// 原先靠共用的长按定时器还开着没开着来判断，快速连打时会张冠李戴。
    /// </summary>
    private Button? _longPressFired;
    private bool _dragging;
    private NativePoint _dragCursor;
    private NativePoint _dragWindow;
    private char? _pendingDigit;
    private int _multiTapIndex;
    private DateTime _lastTapUtc;
    private char? _lastTapDigit;
    private static readonly string[] Punctuation = ["？", "！"];
    private int _punctIndex;
    private Point? _swipeStart;
    private double _hostScaleX = 1;
    private double _hostScaleY = 1;
    private double _hostLayoutW = KeyboardChromeSize.CompactWidth;
    private double _hostLayoutH = KeyboardChromeSize.CompactHeight;
    private int _hostFrameW;
    private int _hostFrameH;
    private int _boardSlide;
    private int _activeAnimations;
    private DateTime _lastAnimationFrameUtc;
    private bool _touchDragging;
    private Point _touchDragScreen;
    private NativePoint _touchDragWindow;

    public T9OverlayWindow(T9Engine engine, AppSettings settings, ForegroundTracker foreground)
    {
        _engine = engine;
        _settings = settings;
        _foreground = foreground;
        _symbolLock = settings.SymbolLock;
        InitializeComponent();
        _hostPressRelease = new DispatcherTimer();
        _hostPressRelease.Tick += (_, _) => ReleaseHostPress();
        _keyReleaseTimer = new DispatcherTimer();
        _keyReleaseTimer.Tick += (_, _) =>
        {
            _keyReleaseTimer.Stop();
            var pending = _keyReleasePending;
            _keyReleasePending = null;
            _keyVisuallyPressed = false;
            TouchKeyVisual.Release(pending);
            TryRebuildKeysWhenIdle();
        };
        ImeHost.Shared.HostPress += OnHostPress;
        ImeHost.Shared.HostHit += OnHostHit;
        ImeHost.Shared.HostSwipe += OnHostSwipe;
        ImeHost.Shared.HostMoved += OnHostMoved;
        ImeHost.Shared.HostVisibilityChanged += OnHostVisibilityChanged;
        FrameBorder.AddHandler(
            PreviewMouseLeftButtonDownEvent,
            new MouseButtonEventHandler(OnSwipeMouseDown),
            true);
        FrameBorder.AddHandler(
            PreviewMouseLeftButtonUpEvent,
            new MouseButtonEventHandler(OnSwipeMouseUp),
            true);
        FrameBorder.AddHandler(
            PreviewMouseMoveEvent,
            new MouseEventHandler(OnSwipeMouseMove),
            true);
        FrameBorder.AddHandler(
            PreviewTouchDownEvent,
            new EventHandler<TouchEventArgs>(OnSwipeTouchDown),
            true);
        FrameBorder.AddHandler(
            PreviewTouchUpEvent,
            new EventHandler<TouchEventArgs>(OnSwipeTouchUp),
            true);
        FrameBorder.AddHandler(
            PreviewTouchMoveEvent,
            new EventHandler<TouchEventArgs>(OnSwipeTouchMove),
            true);
        FrameBorder.AddHandler(
            PreviewMouseWheelEvent,
            new MouseWheelEventHandler(OnSwipeWheel),
            true);
        FrameBorder.MouseLeave += OnSwipeMouseLeave;
        _longPressTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(480) };
        _longPressTimer.Tick += OnLongPress;
        _keyRepeatTimer = new DispatcherTimer();
        _keyRepeatTimer.Tick += OnKeyRepeat;
        Loaded += (_, _) =>
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            _foreground.Ignore(hwnd);
            UiAccessBandHost.Shared.TryOwnAndRaise(hwnd);
            BindChromeKeys();
            BuildKeys();
            RefreshChrome();
            ClipFrame();
        };
        SizeChanged += (_, _) => ClipFrame();
        RefreshPinChrome();
        RefreshFunctionIcons();
    }

    public event Action? UserClosed;
    public event Action<bool>? PinChanged;
    public event Action? BoardLayoutChanged;
    public bool IsPinned { get; private set; }
    public static double DesignWidth => KeyboardChromeSize.CompactWidth;
    public static double DesignHeight => KeyboardChromeSize.CompactHeight;
    private NativeRect _placed;

    public NativeRect PlacedRect => _placed;
    private NativeRect _autoPlaced;
    private IntPtr _host;
    private IntPtr _owner;
    private InputContextKey _context;
    private bool _hostReady;
    private bool _publishQueued;

    public void PixelSize(out int width, out int height)
    {
        var hwnd = new WindowInteropHelper(this).EnsureHandle();
        var dpi = NativeMethods.GetDpiForWindow(hwnd);
        if (dpi == 0)
        {
            dpi = 96;
        }

        var (designW, designH) = CurrentDesignSize();
        width = (int)Math.Round(designW * dpi / 96.0);
        height = (int)Math.Round(designH * dpi / 96.0);
    }

    public void PlaceOn(
        NativeRect rect,
        IntPtr host = default,
        IntPtr owner = default,
        InputContextKey context = default,
        bool repositionRequested = false)
    {
        var keepSessionPosition = KeyboardPinPolicy.ShouldKeepSessionPosition(
            IsPinned,
            !_placed.IsEmpty,
            repositionRequested);
        if (keepSessionPosition)
        {
            rect = _placed;
            if (_host == IntPtr.Zero || NativeMethods.IsWindow(_host))
            {
                host = _host;
                owner = _owner;
                context = _context;
            }
        }
        var sameHost = _host == host;
        var sameContext = KeyboardPositionSession.IsSameSurfaceContext(
            _context,
            context);
        var shouldHost = ShellProcess.RequiresHostRender(host);
        var restart = KeyboardPinPolicy.ShouldRestart(
            IsPinned,
            !keepSessionPosition && KeyboardPositionSession.ShouldRestart(
                IsVisible,
                _host,
                host,
                _context,
                context));
        if (KeyboardPositionSession.ShouldTearDownBeforePlace(restart, shouldHost))
        {
            HideOverlay();
            sameHost = false;
            sameContext = false;
        }
        else if (restart)
        {
            sameHost = false;
            sameContext = false;
        }
        var holdForSameLine = !repositionRequested
            && KeyboardPositionSession.ShouldHoldForSameLine(
            IsVisible,
            sameHost,
            sameContext,
            !_placed.IsEmpty,
            _autoPlaced,
            rect);
        _host = host;
        _owner = owner == IntPtr.Zero ? host : owner;
        _context = context;

        PixelSize(out var width, out var height);
        var hwnd = new WindowInteropHelper(this).EnsureHandle();
        if (holdForSameLine)
        {
            rect = _placed;
        }
        else if (KeyboardPositionSession.ShouldKeepMovedPosition(
                _movedByUser,
                _autoPlaced,
                rect))
        {
            rect = _placed;
        }
        else if (KeyboardPinPolicy.ShouldHideForEmptyRect(IsPinned, rect.IsEmpty))
        {
            HideOverlay();
            return;
        }
        else if (rect.IsEmpty)
        {
            return;
        }
        else
        {
            _movedByUser = false;
            _autoPlaced = rect;
        }

        rect.Right = rect.Left + width;
        rect.Bottom = rect.Top + height;
        if (IsVisible && !KeyboardPositionSession.ShouldMoveVisibleWindow(
                SameRect(rect, _placed),
                shouldHost != _hosting))
        {
            if (shouldHost
                && HostFrame.NeedsRepublish(
                    sameHost,
                    sameContext,
                    _hostReady))
            {
                PublishHost();
            }

            return;
        }

        SipLayer.Detach(hwnd);
        SipLayer.RaiseToIhmBand(hwnd);
        _placed = rect;
        var dpi = NativeMethods.GetDpiForWindow(hwnd);
        if (dpi == 0)
        {
            dpi = 96;
        }

        var (designW, designH) = CurrentDesignSize();
        Width = designW;
        Height = designH;
        var bandOwned = !shouldHost
            && UiAccessBandHost.Shared.TryOwnAndRaise(hwnd, _owner);
        if (!bandOwned)
        {
            Left = rect.Left * 96.0 / dpi;
            Top = rect.Top * 96.0 / dpi;
        }
        var parkLocal = HostFrame.ShouldParkLocalWindow(shouldHost, _hostReady);
        var flags = (uint)(NativeMethods.SwpNoActivate | NativeMethods.SwpShowWindow);
        var wasVisible = IsVisible;
        if (!wasVisible)
        {
            Show();
        }

        if (bandOwned)
        {
            UiAccessBandHost.Shared.TryPlace(hwnd, _owner, rect);
        }
        else
        {
            NativeMethods.SetWindowPos(
                hwnd,
                NativeMethods.HwndTopmost,
                parkLocal ? HostFrame.ParkOffset : rect.Left,
                parkLocal ? HostFrame.ParkOffset : rect.Top,
                width,
                height,
                flags);
        }
        if (!shouldHost)
        {
            NotifyImeWindow(
                hwnd,
                wasVisible
                    ? NativeMethods.EventObjectImeChange
                    : NativeMethods.EventObjectImeShow);
        }

        if (shouldHost)
        {
            if (!_hosting)
            {
                _hostReady = false;
            }
            _hosting = true;
            PublishHost();
            return;
        }

        if (_hosting)
        {
            _hosting = false;
            _hostReady = false;
            ImeHost.Shared.HideHost();
        }
    }

    public string CurrentSkinKey =>
        KeyboardSkinPolicy.Key(
            _board == Board.Full,
            _board is Board.English or Board.Pinyin26);

    public void ApplyAppearance()
    {
        if (FrameBorder is null || SkinImage is null || PaneGrid is null)
        {
            return;
        }

        FrameBorder.Opacity = KeyboardSkinPolicy.ClampOverlay(_settings.OverlayOpacity);
        var skin = KeyboardSkinPolicy.For(_settings, CurrentSkinKey);
        var path = skin.Path;
        if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
        {
            SkinImage.Source = LoadSkin(path!);
            SkinImage.Opacity = KeyboardSkinPolicy.ClampImage(skin.Opacity);
            SkinImage.Visibility = Visibility.Visible;
            PaneGrid.Background = System.Windows.Media.Brushes.Transparent;
        }
        else
        {
            SkinImage.Source = null;
            SkinImage.Visibility = Visibility.Collapsed;
            PaneGrid.Background = (Brush)FindResource("PaneBrush");
        }

        RequestPublishHost();
    }

    private static ImageSource? LoadSkin(string path)
    {
        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.UriSource = new Uri(path, UriKind.Absolute);
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch
        {
            return null;
        }
    }

    public void HideOverlay()
    {
        if (!IsVisible && !_hosting)
        {
            return;
        }

        var wasVisible = IsVisible;
        var wasHosting = _hosting;
        _hostPressRelease.Stop();
        _hostPressShownUtc = DateTime.MinValue;
        _keyReleaseTimer.Stop();
        _keyReleasePending = null;
        _keyPressedUtc = DateTime.MinValue;
        _keyVisuallyPressed = false;
        _rebuildKeysWhenIdle = false;
        StopKeyRepeat();
        _longPressFired = null;
        _holdHostFrame = false;
        _pressedHostKey = null;
        _publishQueued = false;
        ReleaseHeldSurface(rebuild: false);
        if (HeldSurfacePolicy.MustPublishHostBeforeHide(wasHosting))
        {
            PublishReleasedHostFrame();
        }

        Log.Info($"收起释放 fn={(_fn ? 1 : 0)} caps={(_caps ? 1 : 0)} host={(wasHosting ? 1 : 0)}");

        _dragging = false;
        if (Mouse.Captured is not null)
        {
            Mouse.Capture(null);
        }
        if (NativeMethods.GetCapture() == new WindowInteropHelper(this).Handle)
        {
            NativeMethods.ReleaseCapture();
        }

        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd != IntPtr.Zero)
        {
            SipLayer.Detach(hwnd);
        }

        if (_hosting)
        {
            ImeHost.Shared.HideHost();
            _hosting = false;
            _hostReady = false;
            _holdHostFrame = false;
            _pressedHostKey = null;
        }

        _host = IntPtr.Zero;
        _owner = IntPtr.Zero;
        _context = default;
        UiAccessBandHost.Shared.Hide();
        Hide();
        if (wasVisible && hwnd != IntPtr.Zero)
        {
            NotifyImeWindow(hwnd, NativeMethods.EventObjectImeHide);
        }
        ResetComposition();
        ForgetUserPlace();
    }

    private void ReleaseHeldSurface(bool rebuild)
    {
        TextOutput.ReleaseAllKeys();
        var next = HeldSurfacePolicy.Dismiss(new HeldSurfaceSnapshot(
            _shift,
            _ctrl,
            _alt,
            _win,
            _fn,
            _caps));
        _shift = next.Shift;
        _ctrl = next.Ctrl;
        _alt = next.Alt;
        _win = next.Win;
        _fn = next.Fn;
        _caps = next.Caps;
        if (rebuild && BoardHost is not null)
        {
            BuildKeys();
        }
    }

    private void PublishReleasedHostFrame()
    {
        if (!_hosting || _placed.IsEmpty)
        {
            return;
        }

        HostFrame.Prepare(this);
        HostFrame.FlushLayout(Dispatcher);
        _publishQueued = false;
        PublishHost();
    }

    private static void NotifyImeWindow(IntPtr hwnd, uint eventId)
    {
        if (hwnd != IntPtr.Zero)
        {
            NativeMethods.NotifyWinEvent(
                eventId,
                hwnd,
                NativeMethods.ObjidClient,
                NativeMethods.ChildIdSelf);
        }
    }

    public void ForgetUserPlace()
    {
        _movedByUser = false;
        _placed = default;
        _autoPlaced = default;
    }

    public void ReleaseDragAnchor()
    {
        _movedByUser = false;
    }

    private void ClipFrame()
    {
        if (FrameBorder is null)
        {
            return;
        }

        FrameBorder.Clip = new RectangleGeometry(
            new Rect(0, 0, FrameBorder.ActualWidth, FrameBorder.ActualHeight),
            14,
            14);
    }

    private void OnDragHandleDown(object sender, MouseButtonEventArgs e)
    {
        BeginDrag();
        e.Handled = true;
    }

    private void OnCandidateBarDown(object sender, MouseButtonEventArgs e)
    {
        if (IsInsideButton(e.OriginalSource as DependencyObject))
        {
            return;
        }

        BeginDrag();
        e.Handled = true;
    }

    private void OnSwipeMouseDown(object sender, MouseButtonEventArgs e)
    {
        // 触摸已经走 Touch 路径。提升出来的鼠标按下不能再停惯性，
        // 也不能留下 swipe 起点，否则松手后鼠标一划就会把列表吸过去。
        if (FallDragPolicy.IgnorePromotedTouch(IsPromotedTouch(e)))
        {
            return;
        }

        // Preview 事件从根向下隧道，这里先于按键自身的处理器执行，
        // 正好用来为本次手势清账。
        _immediateTapFired = false;
        _swipeStart = e.GetPosition(FrameBorder);
        StopFallInertia();
        _fallPressTime = FallInertia.Now;
        _fallInertia.Reset();
    }

    private void OnSwipeMouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (_dragging && NativeMethods.GetCursorPos(out var cursor))
        {
            MoveLocalWindow(
                _dragWindow.X + cursor.X - _dragCursor.X,
                _dragWindow.Y + cursor.Y - _dragCursor.Y);
            e.Handled = true;
            return;
        }

        if (FallDragPolicy.IgnorePromotedTouch(IsPromotedTouch(e)))
        {
            return;
        }

        var pressed = e.LeftButton == MouseButtonState.Pressed;
        if (FallDragPolicy.ShouldDrop(pressed, _swipeStart is not null || _fallDragging))
        {
            DropFallPointer();
            return;
        }

        if (!FallDragPolicy.Follows(pressed))
        {
            return;
        }

        if (_swipeStart is { } start)
        {
            var now = e.GetPosition(FrameBorder);
            if (TryDragFall(start, now))
            {
                _longPressTimer.Stop();
                return;
            }

            if (SwipeNavigation.Detect(start.X, start.Y, now.X, now.Y) != SwipeDirection.None)
            {
                _longPressTimer.Stop();
            }
        }
    }

    private void OnSwipeMouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (_dragging)
        {
            return;
        }

        var pressed = e.LeftButton == MouseButtonState.Pressed;
        if (FallDragPolicy.ShouldDrop(pressed, _swipeStart is not null || _fallDragging))
        {
            DropFallPointer();
        }
    }

    private void OnSwipeMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (FallDragPolicy.IgnorePromotedTouch(IsPromotedTouch(e)))
        {
            return;
        }

        if (_dragging)
        {
            _dragging = false;
            _swipeStart = null;
            Mouse.Capture(null);
            NativeMethods.ReleaseCapture();
            e.Handled = true;
            return;
        }

        if (EndFallDrag())
        {
            _swipeStart = null;
            return;
        }

        if (_swipeStart is not { } start)
        {
            return;
        }

        _swipeStart = null;
        if (_immediateTapFired)
        {
            // 起点落在按下即触发的键上，动作已经发出，不能再当成翻页手势。
            return;
        }

        if (HandleSwipe(start, e.GetPosition(FrameBorder)))
        {
            _longPressTimer.Stop();
            e.Handled = true;
        }
    }

    private void OnSwipeTouchDown(object? sender, TouchEventArgs e)
    {
        OverlayTouch.Note();
        var position = e.GetTouchPoint(FrameBorder).Position;
        _immediateTapFired = false;
        _swipeStart = position;
        StopFallInertia();
        _fallPressTime = FallInertia.Now;
        _fallInertia.Reset();
        _touchDragging = IsInside(DragButton, position);
        if (!_touchDragging)
        {
            return;
        }

        _touchDragScreen = FrameBorder.PointToScreen(position);
        var hwnd = new WindowInteropHelper(this).EnsureHandle();
        if (NativeMethods.GetWindowRect(hwnd, out var rect))
        {
            _touchDragWindow = new NativePoint { X = rect.Left, Y = rect.Top };
        }
        e.TouchDevice.Capture(FrameBorder);
        e.Handled = true;
    }

    private void OnSwipeTouchMove(object? sender, TouchEventArgs e)
    {
        if (_touchDragging)
        {
            var screen = FrameBorder.PointToScreen(e.GetTouchPoint(FrameBorder).Position);
            MoveLocalWindow(
                _touchDragWindow.X + (int)Math.Round(screen.X - _touchDragScreen.X),
                _touchDragWindow.Y + (int)Math.Round(screen.Y - _touchDragScreen.Y));
            e.Handled = true;
            return;
        }

        if (_swipeStart is { } start)
        {
            var current = e.GetTouchPoint(FrameBorder).Position;
            if (TryDragFall(start, current))
            {
                _longPressTimer.Stop();
                return;
            }

            if (SwipeNavigation.Detect(start.X, start.Y, current.X, current.Y) != SwipeDirection.None)
            {
                _longPressTimer.Stop();
            }
        }
    }

    private void OnSwipeTouchUp(object? sender, TouchEventArgs e)
    {
        if (_touchDragging)
        {
            _touchDragging = false;
            _swipeStart = null;
            e.TouchDevice.Capture(null);
            e.Handled = true;
            return;
        }

        if (EndFallDrag())
        {
            _swipeStart = null;
            return;
        }

        if (_swipeStart is not { } start)
        {
            return;
        }

        _swipeStart = null;
        if (_immediateTapFired)
        {
            return;
        }

        if (HandleSwipe(start, e.GetTouchPoint(FrameBorder).Position))
        {
            _longPressTimer.Stop();
            e.Handled = true;
        }
    }

    private void OnSwipeWheel(object sender, MouseWheelEventArgs e)
    {
        var point = e.GetPosition(FrameBorder);
        if (TryGetFallTarget(point, out var target))
        {
            FlingFall(target, FallInertia.WheelVelocity(e.Delta));
            e.Handled = true;
        }
    }

    private bool HandleSwipe(Point start, Point end, double minimum = 32)
    {
        var direction = SwipeNavigation.Detect(start.X, start.Y, end.X, end.Y, minimum);
        if (direction == SwipeDirection.None)
        {
            return false;
        }

        if (!TryGetFallTarget(start, out var target) && !TryGetFallTarget(end, out target))
        {
            return false;
        }

        var delta = target.Horizontal ? start.X - end.X : start.Y - end.Y;
        var dt = Math.Max(0.016, FallInertia.Now - _fallPressTime);
        FlingFall(target, FallInertia.EnsureFling(delta / dt, delta));
        return true;
    }

    private bool IsInside(FrameworkElement element, Point point)
    {
        try
        {
            var bounds = element.TransformToAncestor(FrameBorder)
                .TransformBounds(new Rect(element.RenderSize));
            return bounds.Contains(point);
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private bool TryDragFall(Point start, Point now)
    {
        if (!TryGetFallTarget(start, out var target))
        {
            return false;
        }

        var axis = SwipeNavigation.Detect(start.X, start.Y, now.X, now.Y);
        var started = target.Horizontal
            ? axis is SwipeDirection.Left or SwipeDirection.Right
            : axis is SwipeDirection.Up or SwipeDirection.Down;
        if (!_fallDragging && !started)
        {
            return false;
        }

        var prev = _fallDragLast ?? start;
        var delta = target.Horizontal ? prev.X - now.X : prev.Y - now.Y;
        var logical = target.Get() + delta;
        if (!_fallDragging)
        {
            _fallInertia.Note(target.Get(), FallInertia.Now);
        }

        _fallInertia.Note(logical, FallInertia.Now);
        ApplyFallLogical(target, logical);
        _fallTarget = target;
        _fallDragging = true;
        _fallDragLast = now;
        return true;
    }

    private bool EndFallDrag()
    {
        var dragged = _fallDragging;
        _fallDragging = false;
        _fallDragLast = null;
        if (!dragged || _fallTarget is not { } target)
        {
            return dragged;
        }

        FlingFall(target, _fallInertia.DragVelocity());
        return true;
    }

    private void DropFallPointer()
    {
        var fling = FallDragPolicy.FlingAfterDrop(_fallDragging, _fallInertia.IsRunning);
        var target = _fallTarget;
        var velocity = _fallInertia.DragVelocity();
        _swipeStart = null;
        _fallDragLast = null;
        _fallDragging = false;
        if (fling && target is not null)
        {
            FlingFall(target, velocity);
        }
    }

    private static bool IsPromotedTouch(System.Windows.Input.MouseEventArgs e) =>
        e.StylusDevice?.TabletDevice?.Type == TabletDeviceType.Touch;

    private bool TryGetFallTarget(Point start, out FallScrollTarget target)
    {
        if (CandidateScroller is not null && IsInside(CandidateBar, start))
        {
            target = new FallScrollTarget
            {
                Viewer = CandidateScroller,
                Get = () => _candidateBarOffset,
                Set = value => _candidateBarOffset = value,
                Horizontal = true
            };
            return true;
        }

        if (_railScroll is not null && _leftRail is not null && IsInside(_leftRail, start))
        {
            target = new FallScrollTarget
            {
                Viewer = _railScroll,
                Get = () => _railFallOffset,
                Set = value => _railFallOffset = value,
                Horizontal = false
            };
            return true;
        }

        if (_candidatesExpanded
            && _candidateScroll is not null
            && IsInside(_candidateScroll, start))
        {
            target = new FallScrollTarget
            {
                Viewer = _candidateScroll,
                Get = () => _candidateFallOffset,
                Set = value => _candidateFallOffset = value,
                Horizontal = false
            };
            return true;
        }

        if (IsSymbolBoard && _categoryScroll is not null && IsInside(_categoryScroll, start))
        {
            target = new FallScrollTarget
            {
                Viewer = _categoryScroll,
                Get = () => _categoryFallOffset,
                Set = value => _categoryFallOffset = value,
                Horizontal = false
            };
            return true;
        }

        if (IsSymbolBoard && _markScroll is not null && IsInside(_markScroll, start))
        {
            target = new FallScrollTarget
            {
                Viewer = _markScroll,
                Get = () => _symbolFallOffset,
                Set = value => _symbolFallOffset = value,
                Horizontal = false
            };
            return true;
        }

        target = null!;
        return false;
    }

    private void ApplyFallLogical(FallScrollTarget target, double logical)
    {
        target.Viewer.UpdateLayout();
        var content = target.Horizontal ? target.Viewer.ExtentWidth : target.Viewer.ExtentHeight;
        var viewport = target.Horizontal ? target.Viewer.ViewportWidth : target.Viewer.ViewportHeight;
        var maxRubber = Math.Max(24, viewport * 0.45);
        if (logical < -maxRubber)
        {
            logical = -maxRubber;
        }
        else if (logical > FallInertia.MaxOffset(content, viewport) + maxRubber)
        {
            logical = FallInertia.MaxOffset(content, viewport) + maxRubber;
        }

        target.Set(logical);
        var (scroll, rubber) = FallInertia.Project(logical, content, viewport);
        if (target.Horizontal)
        {
            target.Viewer.ScrollToHorizontalOffset(scroll);
        }
        else
        {
            target.Viewer.ScrollToVerticalOffset(scroll);
        }

        SetRubber(target.Viewer, target.Horizontal, rubber);
        ClipFall(
            target.Viewer,
            target.Horizontal ? target.Viewer.ActualWidth : target.Viewer.ActualWidth,
            target.Viewer.ActualHeight);
        RequestPublishHost();
    }

    private static void ClipFall(FrameworkElement element, double width, double height)
    {
        element.ClipToBounds = true;
        var w = Math.Max(0, width);
        var h = Math.Max(0, height);
        element.Clip = w < 1 || h < 1
            ? null
            : new RectangleGeometry(new Rect(0, 0, w, h));
    }

    private static void SetRubber(ScrollViewer viewer, bool horizontal, double rubber)
    {
        if (viewer.Content is not FrameworkElement content)
        {
            return;
        }

        if (Math.Abs(rubber) < 0.2)
        {
            content.RenderTransform = null;
            return;
        }

        content.RenderTransform = horizontal
            ? new TranslateTransform(rubber, 0)
            : new TranslateTransform(0, rubber);
    }

    private void FlingFall(FallScrollTarget target, double velocity)
    {
        _swipeStart = null;
        _fallDragging = false;
        _fallDragLast = null;
        _fallTarget = target;
        target.Viewer.UpdateLayout();
        var content = target.Horizontal ? target.Viewer.ExtentWidth : target.Viewer.ExtentHeight;
        var viewport = target.Horizontal ? target.Viewer.ViewportWidth : target.Viewer.ViewportHeight;
        var leftover = _fallInertia.LeftoverVelocity(FallInertia.Now);
        var blended = FallInertia.BlendVelocity(leftover, velocity);
        var run = FallRun.Release(target.Get(), blended, content, viewport, FallInertia.Now);
        if (run is null)
        {
            ApplyFallLogical(target, FallFlow.Clamp(target.Get(), content, viewport));
            _fallInertia.Stop();
            EnsureInertiaTick(false);
            return;
        }

        _fallInertia.Begin(run);
        EnsureInertiaTick(true);
    }

    private void StopFallInertia()
    {
        _fallInertia.Stop();
        EnsureInertiaTick(false);
        if (_fallTarget is { } target)
        {
            target.Viewer.UpdateLayout();
            var content = target.Horizontal ? target.Viewer.ExtentWidth : target.Viewer.ExtentHeight;
            var viewport = target.Horizontal ? target.Viewer.ViewportWidth : target.Viewer.ViewportHeight;
            ApplyFallLogical(target, FallFlow.Clamp(target.Get(), content, viewport));
        }
    }

    private void EnsureInertiaTick(bool on)
    {
        if (on == _inertiaTicking)
        {
            return;
        }

        _inertiaTicking = on;
        if (on)
        {
            CompositionTarget.Rendering += OnInertiaRendering;
        }
        else
        {
            CompositionTarget.Rendering -= OnInertiaRendering;
        }
    }

    private void OnInertiaRendering(object? sender, EventArgs e)
    {
        if (_fallTarget is not { } target)
        {
            EnsureInertiaTick(false);
            return;
        }

        var next = _fallInertia.Step(FallInertia.Now);
        if (next is null)
        {
            target.Viewer.UpdateLayout();
            var content = target.Horizontal ? target.Viewer.ExtentWidth : target.Viewer.ExtentHeight;
            var viewport = target.Horizontal ? target.Viewer.ViewportWidth : target.Viewer.ViewportHeight;
            ApplyFallLogical(target, FallFlow.Clamp(target.Get(), content, viewport));
            EnsureInertiaTick(false);
            return;
        }

        ApplyFallLogical(target, next.Value);
    }

    private ScrollViewer MakeFallViewer(
        IReadOnlyList<Button> buttons,
        double width,
        double height,
        double cellHeight,
        double offset)
    {
        var rows = Math.Max(1, FallFlow.RowCount(buttons.Count));
        var contentHeight = cellHeight * rows;
        offset = FallFlow.Clamp(offset, contentHeight, height);
        var grid = new UniformGrid
        {
            Columns = FallFlow.Columns,
            Rows = rows,
            Width = width,
            Height = contentHeight,
            ClipToBounds = true
        };
        foreach (var button in buttons)
        {
            grid.Children.Add(button);
        }

        return MakeScrollViewer(grid, width, height, offset);
    }

    private ScrollViewer MakeSlotViewer(
        IReadOnlyList<Button> buttons,
        double width,
        double height,
        double slotHeight,
        double offset)
    {
        var stack = new StackPanel { Width = width };
        foreach (var button in buttons)
        {
            button.Height = slotHeight;
            button.MinHeight = slotHeight;
            button.MaxHeight = slotHeight;
            stack.Children.Add(button);
        }

        for (var i = buttons.Count; i < LeftRailSlots.Count; i++)
        {
            stack.Children.Add(new Border { Height = slotHeight, IsHitTestVisible = false });
        }

        return MakeScrollViewer(stack, width, height, offset);
    }

    private ScrollViewer MakeScrollViewer(FrameworkElement content, double width, double height, double offset)
    {
        var viewer = new ScrollViewer
        {
            Width = width,
            Height = height,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Hidden,
            PanningMode = PanningMode.None,
            CanContentScroll = false,
            Focusable = false,
            ClipToBounds = true,
            Content = content
        };
        ClipFall(viewer, width, height);
        if (content is FrameworkElement inner)
        {
            inner.ClipToBounds = true;
        }

        viewer.Loaded += (_, _) =>
        {
            ClipFall(viewer, viewer.ActualWidth, viewer.ActualHeight);
            var max = FallInertia.MaxOffset(viewer.ExtentHeight, viewer.ViewportHeight);
            viewer.ScrollToVerticalOffset(Math.Clamp(offset, 0, max));
        };
        viewer.SizeChanged += (_, _) => ClipFall(viewer, viewer.ActualWidth, viewer.ActualHeight);
        viewer.ScrollChanged += (_, _) => RequestPublishHost();
        return viewer;
    }

    private bool TryClipToScrollViewer(FrameworkElement element, out Rect clip)
    {
        clip = Rect.Empty;
        DependencyObject? node = element;
        while (node is not null)
        {
            if (node is ScrollViewer scroll)
            {
                try
                {
                    clip = scroll.TransformToAncestor(FrameBorder)
                        .TransformBounds(new Rect(scroll.RenderSize));
                    return true;
                }
                catch (InvalidOperationException)
                {
                    return false;
                }
            }

            node = VisualTreeHelper.GetParent(node);
        }

        return false;
    }

    private void MoveLocalWindow(int left, int top)
    {
        PixelSize(out var width, out var height);
        var hwnd = new WindowInteropHelper(this).EnsureHandle();
        var dpi = NativeMethods.GetDpiForWindow(hwnd);
        if (dpi == 0)
        {
            dpi = 96;
        }
        var placed = new NativeRect
        {
            Left = left,
            Top = top,
            Right = left + width,
            Bottom = top + height
        };
        if (!UiAccessBandHost.Shared.TryPlace(hwnd, _owner, placed))
        {
            Left = left * 96.0 / dpi;
            Top = top * 96.0 / dpi;
            NativeMethods.SetWindowPos(
                hwnd,
                NativeMethods.HwndTopmost,
                left,
                top,
                width,
                height,
                NativeMethods.SwpNoActivate | NativeMethods.SwpShowWindow);
        }
        _placed = placed;
        NotifyImeWindow(hwnd, NativeMethods.EventObjectImeChange);
        _movedByUser = true;
    }

    private void StartSlide(FrameworkElement element, bool horizontal, bool forward)
    {
        var distance = horizontal
            ? Math.Max(120, element.ActualWidth)
            : Math.Max(90, element.ActualHeight);
        var transform = new TranslateTransform();
        element.RenderTransform = transform;
        var property = horizontal ? TranslateTransform.XProperty : TranslateTransform.YProperty;
        var animation = new DoubleAnimation
        {
            From = SwipeNavigation.InitialOffset(distance, forward),
            To = 0,
            Duration = TimeSpan.FromMilliseconds(190),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };

        _activeAnimations++;
        if (_activeAnimations == 1)
        {
            _lastAnimationFrameUtc = DateTime.MinValue;
            CompositionTarget.Rendering += OnAnimationRendering;
        }

        animation.Completed += (_, _) =>
        {
            transform.BeginAnimation(property, null);
            if (ReferenceEquals(element.RenderTransform, transform))
            {
                element.ClearValue(RenderTransformProperty);
            }
            _activeAnimations = Math.Max(0, _activeAnimations - 1);
            if (_activeAnimations == 0)
            {
                CompositionTarget.Rendering -= OnAnimationRendering;
            }
            PublishHost();
        };
        transform.BeginAnimation(property, animation, HandoffBehavior.SnapshotAndReplace);
    }

    private void OnAnimationRendering(object? sender, EventArgs e)
    {
        if (!_hosting)
        {
            return;
        }

        var now = DateTime.UtcNow;
        if ((now - _lastAnimationFrameUtc).TotalMilliseconds < 25)
        {
            return;
        }

        _lastAnimationFrameUtc = now;
        PublishHost();
    }

    private void BeginDrag()
    {

        if (!NativeMethods.GetCursorPos(out _dragCursor))
        {
            return;
        }

        var hwnd = new WindowInteropHelper(this).EnsureHandle();
        if (!NativeMethods.GetWindowRect(hwnd, out var rect))
        {
            return;
        }

        _dragWindow = new NativePoint { X = rect.Left, Y = rect.Top };
        _dragging = true;
        NativeMethods.SetCapture(hwnd);
    }

    protected override void OnMouseMove(System.Windows.Input.MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (!_dragging)
        {
            return;
        }

        if (!NativeMethods.GetCursorPos(out var now))
        {
            return;
        }

        PixelSize(out var width, out var height);
        var left = _dragWindow.X + now.X - _dragCursor.X;
        var top = _dragWindow.Y + now.Y - _dragCursor.Y;
        var hwnd = new WindowInteropHelper(this).EnsureHandle();
        var dpi = NativeMethods.GetDpiForWindow(hwnd);
        if (dpi == 0)
        {
            dpi = 96;
        }

        var placed = new NativeRect { Left = left, Top = top, Right = left + width, Bottom = top + height };
        if (!UiAccessBandHost.Shared.TryPlace(hwnd, _owner, placed))
        {
            Left = left * 96.0 / dpi;
            Top = top * 96.0 / dpi;
            NativeMethods.SetWindowPos(
                hwnd,
                NativeMethods.HwndTopmost,
                left,
                top,
                width,
                height,
                NativeMethods.SwpNoActivate);
        }
        _placed = placed;
        NotifyImeWindow(hwnd, NativeMethods.EventObjectImeChange);
        _movedByUser = true;
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        if (_dragging)
        {
            _dragging = false;
            Mouse.Capture(null);
            NativeMethods.ReleaseCapture();

            e.Handled = true;
        }

        base.OnMouseLeftButtonUp(e);
    }

    private void OnCandidateMore(object sender, RoutedEventArgs e)
    {
        if ((_digits.Length == 0 && _letters.Length == 0)
            || !CandidateFallPolicy.CanExpand(ToSurface(_board)))
        {
            return;
        }

        _candidatesExpanded = !_candidatesExpanded;
        _candidateFallOffset = 0;
        RefreshChrome();
    }

    private static bool IsInsideButton(DependencyObject? node)
    {
        while (node is not null)
        {
            if (node is Button)
            {
                return true;
            }

            node = VisualTreeHelper.GetParent(node);
        }

        return false;
    }

    private static bool SameRect(NativeRect a, NativeRect b)
    {
        return Math.Abs(a.Left - b.Left) <= 2
               && Math.Abs(a.Top - b.Top) <= 2
               && Math.Abs(a.Width - b.Width) <= 2
               && Math.Abs(a.Height - b.Height) <= 2;
    }

    public void RefreshChrome()
    {
        BuildKeys();
        if (_boardSlide != 0)
        {
            if (BoardHost.Children.Count > 0 && BoardHost.Children[0] is FrameworkElement board)
            {
                StartSlide(board, horizontal: true, _boardSlide > 0);
            }
            _boardSlide = 0;
        }
        RefreshCandidates();
    }

    private void BuildKeys()
    {
        try
        {
            StopFallInertia();
            _fallTarget = null;
            BoardHost.Children.Clear();
            StopKeyRepeat();
            _hostTapActions.Clear();
            _hostPressActions.Clear();
            _hostRepeats.Clear();
            _hostPressGate.Reset();
            _immediateTapFired = false;
            _leftRail = null;
            _markScroll = null;
            _categoryScroll = null;
            _candidateScroll = null;
            _railScroll = null;
            _english = _board == Board.English;
            _numberPad = _board == Board.Number;
            var full = _board == Board.Full;
            if (FunctionBar is not null)
            {
                FunctionBar.Visibility = full ? Visibility.Collapsed : Visibility.Visible;
            }

            if (FunctionRow is not null)
            {
                FunctionRow.Height = full ? new GridLength(0) : new GridLength(52);
            }

            if (LangButton is not null)
            {
                LangButton.Style = (Style)FindResource("FunctionKeyButton");
                LangButton.Content = "中 / EN";
                LangButton.ToolTip = "中 / EN";
            }

            if (DigitButton is not null)
            {
                DigitButton.Content = _board is Board.Number or Board.SymbolCn or Board.SymbolEn ? "返回" : "123";
            }

            HighlightTab();
            RefreshFunctionIcons();
            ApplyDesignSize();
            ApplyAppearance();
            BindChromeKeys();
            RebindCandidateHostActions();

            if (_candidatesExpanded && CandidateFallPolicy.CanExpand(ToSurface(_board)))
            {
                BuildCandidateFall();
                return;
            }

            if (_board == Board.Full)
            {
                BuildFullBoard();
                return;
            }

            if (_board is Board.English or Board.Pinyin26)
            {
                BuildLetterBoard(_board == Board.Pinyin26);
                return;
            }

            if (_board is Board.SymbolCn or Board.SymbolEn)
            {
                BuildSymbolBoard();
                return;
            }

            if (_board == Board.Number)
            {
                BuildNumberBoard();
                return;
            }

            BuildPinyinBoard();
        }
        finally
        {
            BoardHost?.InvalidateMeasure();
            BoardHost?.InvalidateArrange();
            FrameBorder?.InvalidateMeasure();
            RequestPublishHost();
        }
    }

    private void HighlightTab()
    {
        var on = (System.Windows.Media.Brush)FindResource("TabOnBrush");
        var off = System.Windows.Media.Brushes.Transparent;
        var accent = (System.Windows.Media.Brush)FindResource("AccentBrush");
        var muted = (System.Windows.Media.Brush)FindResource("MutedBrush");
        if (PinyinTab is not null)
        {
            PinyinTab.Background = _board == Board.Pinyin ? on : off;
            PinyinTab.Foreground = _board == Board.Pinyin ? accent : muted;
        }

        if (Pinyin26Tab is not null)
        {
            Pinyin26Tab.Background = _board == Board.Pinyin26 ? on : off;
            Pinyin26Tab.Foreground = _board == Board.Pinyin26 ? accent : muted;
        }

        if (EnglishTab is not null)
        {
            EnglishTab.Background = _board == Board.English ? on : off;
            EnglishTab.Foreground = _board == Board.English ? accent : muted;
        }

        if (FullTab is not null)
        {
            FullTab.Background = _board == Board.Full ? on : off;
            FullTab.Foreground = _board == Board.Full ? accent : muted;
        }

        if (DigitTab is not null)
        {
            DigitTab.Background = _board == Board.Number ? on : off;
            DigitTab.Foreground = _board == Board.Number ? accent : muted;
        }
    }

    private (double Width, double Height) CurrentDesignSize() =>
        KeyboardChromeSize.ForBoard(
            _board == Board.Full,
            _board == Board.Number,
            _board is Board.English or Board.Pinyin26);

    private void ApplyDesignSize()
    {
        var (designW, designH) = CurrentDesignSize();
        Width = designW;
        Height = designH;
        if (_placed.IsEmpty)
        {
            return;
        }

        PixelSize(out var width, out var height);
        if (_placed.Width == width && _placed.Height == height)
        {
            return;
        }

        // 键盘贴在输入行上方。切盘钉住底边，只改高度，不要钉顶边再让
        // 会话按 UIA 重摆——数字盘更高，钉顶会把底边压进输入行。
        _placed = BoardPlaceResume.ResizePinnedBottom(_placed, width, height);
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        _placingLayout = true;
        if (!UiAccessBandHost.Shared.TryPlace(hwnd, _owner, _placed))
        {
            NativeMethods.SetWindowPos(
                hwnd,
                NativeMethods.HwndTopmost,
                _placed.Left,
                _placed.Top,
                width,
                height,
                NativeMethods.SwpNoActivate | NativeMethods.SwpShowWindow);
        }

        _placingLayout = false;

        RequestPublishHost();
        if (!IsPinned && !_holdPlaceOnLayout)
        {
            BoardLayoutChanged?.Invoke();
        }
    }

    private void BuildPinyinBoard()
    {
        var (rail, board) = KeyboardChromeSize.CompactColumns();
        var root = new Grid { HorizontalAlignment = HorizontalAlignment.Center };
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(rail) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(board) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(rail) });

        _leftRail = new ContentControl { ClipToBounds = true, Width = rail };
        Grid.SetColumn(_leftRail, 0);
        root.Children.Add(_leftRail);
        RefreshLeftRail([]);

        var keys = new UniformGrid
        {
            Rows = 3,
            Columns = 3,
            Width = board,
            Height = board
        };
        AddT9(keys, "分词", () => OnSplit(), () => OnSplit());
        AddT9(keys, "ABC", () => OnKey('2'), () => MultiTap('2'));
        AddT9(keys, "DEF", () => OnKey('3'), () => MultiTap('3'));
        AddT9(keys, "GHI", () => OnKey('4'), () => MultiTap('4'));
        AddT9(keys, "JKL", () => OnKey('5'), () => MultiTap('5'));
        AddT9(keys, "MNO", () => OnKey('6'), () => MultiTap('6'));
        AddT9(keys, "PQRS", () => OnKey('7'), () => MultiTap('7'));
        AddT9(keys, "TUV", () => OnKey('8'), () => MultiTap('8'));
        AddT9(keys, "WXYZ", () => OnKey('9'), () => MultiTap('9'));
        Grid.SetColumn(keys, 1);
        root.Children.Add(keys);

        var tools = new Grid { Width = rail };
        for (var i = 0; i < 3; i++)
        {
            tools.RowDefinitions.Add(new RowDefinition());
        }

        AddTo(tools, MakeIconKey(KeyGlyphs.Backspace, OnBackspace, "退格", repeatWhileHeld: true), 0, 0);
        AddTo(tools, MakeFunctionKey("清空", OnRetype, 14), 1, 0);
        AddTo(tools, MakeFunctionKey("符号", OnSymbolTray, 14), 2, 0);
        Grid.SetColumn(tools, 2);
        root.Children.Add(tools);

        BoardHost.Children.Add(root);
    }

    private void BuildLetterBoard(bool pinyin)
    {
        var (rail, board) = KeyboardChromeSize.EnglishColumns();
        var unit = KeyboardChromeSize.EnglishLetterUnit;
        var pad = KeyboardChromeSize.EnglishBoardHeight;
        var root = new Grid
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Height = pad
        };
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(rail) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(board) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(rail) });

        _leftRail = new ContentControl { ClipToBounds = true, Width = rail, Height = pad };
        Grid.SetColumn(_leftRail, 0);
        root.Children.Add(_leftRail);
        RefreshLeftRail([]);

        var keys = new Grid { Width = board, Height = pad };
        for (var i = 0; i < EnglishKeyboardLayout.Rows.Count; i++)
        {
            keys.RowDefinitions.Add(new RowDefinition());
        }

        var shiftOn = TouchModifierPolicy.IsOn(_shift);
        var predict = !pinyin;
        for (var r = 0; r < EnglishKeyboardLayout.Rows.Count; r++)
        {
            var letters = EnglishKeyboardLayout.Rows[r];
            var line = new Grid();
            if (r == 2)
            {
                line.ColumnDefinitions.Add(
                    new ColumnDefinition { Width = new GridLength(1.15, GridUnitType.Star) });
                if (predict)
                {
                    line.ColumnDefinitions.Add(
                        new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                }
            }

            foreach (var _ in letters)
            {
                line.ColumnDefinitions.Add(new ColumnDefinition());
            }

            if (r == 2)
            {
                var shift = MakeIconKey(fill => KeyGlyphs.Shift(shiftOn, fill), ToggleEnglishShift, "Shift");
                if (shiftOn)
                {
                    shift.Background = (Brush)FindResource("AccentBrush");
                    shift.Foreground = System.Windows.Media.Brushes.White;
                    shift.Content = KeyGlyphs.Shift(true, shift.Foreground);
                }

                Grid.SetColumn(shift, 0);
                line.Children.Add(shift);
                if (predict)
                {
                    var predictKey = MakePredictKey();
                    Grid.SetColumn(predictKey, 1);
                    line.Children.Add(predictKey);
                }
            }

            var col = r == 2 ? (predict ? 2 : 1) : 0;
            foreach (var letter in letters)
            {
                var face = EnglishKeyboardLayout.Face(letter, shiftOn);
                var key = MakeKey(
                    face,
                    () => OnWideLetter(letter, pinyin),
                    T9KeyFace.EnglishFontSize);
                Grid.SetColumn(key, col++);
                line.Children.Add(key);
            }

            if (r == 1)
            {
                var stagger = EnglishKeyboardLayout.RowStagger(unit);
                line.Margin = new Thickness(stagger, 0, stagger, 0);
            }

            Grid.SetRow(line, r);
            keys.Children.Add(line);
        }

        Grid.SetColumn(keys, 1);
        root.Children.Add(keys);

        var tools = new Grid { Width = rail, Height = pad };
        for (var i = 0; i < 3; i++)
        {
            tools.RowDefinitions.Add(new RowDefinition());
        }

        AddTo(tools, MakeIconKey(KeyGlyphs.Backspace, OnBackspace, "退格", repeatWhileHeld: true), 0, 0);
        AddTo(tools, MakeFunctionKey("清空", OnRetype, 14), 1, 0);
        AddTo(tools, MakeFunctionKey("符号", OnSymbolTray, 14), 2, 0);
        Grid.SetColumn(tools, 2);
        root.Children.Add(tools);

        BoardHost.Children.Add(root);
    }

    private void ToggleEnglishShift()
    {
        _shift = TouchModifierPolicy.Tap(_shift, windowsKey: false);
        BuildKeys();
    }

    private void OnWideLetter(string letter, bool pinyin)
    {
        var face = EnglishKeyboardLayout.Face(letter, TouchModifierPolicy.IsOn(_shift));
        if (pinyin || EnglishPredictOn)
        {
            ComposeLetter(pinyin ? char.ToLowerInvariant(face[0]) : face[0]);
        }
        else
        {
            EmitText(face);
        }

        if (_shift == TouchModifierPhase.Held)
        {
            _shift = TouchModifierPhase.Off;
            RebuildKeysWhenIdle();
        }
    }

    private bool EnglishPredictOn =>
        EnglishPredictPolicy.Composes(
            _settings.EnglishPredict,
            _board == Board.English,
            _board == Board.Full && _latin);

    private void ToggleEnglishPredict()
    {
        _settings.EnglishPredict = !_settings.EnglishPredict;
        _settings.Save();
        if (!_settings.EnglishPredict && _letters.Length > 0 && (_english || _latin))
        {
            EmitText(_letters);
            ResetComposition();
            return;
        }

        RefreshChrome();
    }

    private void ComposeLetter(char ch)
    {
        _letters += ch;
        _digits = T9Engine.ToDigits(_letters);
        _candidateBarOffset = 0;
        ClearConfirmedSyllables();
        RefreshCandidates();
        PushComposition();
    }

    private Button MakePredictKey()
    {
        var on = _settings.EnglishPredict;
        var accent = (Brush)FindResource("AccentBrush");
        var muted = (Brush)FindResource("MutedBrush");
        var button = MakeFunctionKey("联想", ToggleEnglishPredict, 16);
        button.Content = new TextBlock
        {
            Text = EnglishPredictPolicy.Label,
            FontSize = 16,
            TextDecorations = EnglishPredictPolicy.ShowsUnderline(on) ? TextDecorations.Underline : null,
            Foreground = EnglishPredictPolicy.ShowsAccent(on) ? accent : muted,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        button.Foreground = EnglishPredictPolicy.ShowsAccent(on) ? accent : muted;
        button.ToolTip = on ? "关闭英文联想" : "开启英文联想";
        return button;
    }

    private IReadOnlyList<T9Candidate> QueryCurrent(int take = 120)
    {
        using var scope = Perf.Begin("engine.query");
        if (_letters.Length > 0)
        {
            return EnglishPredictOn
                ? _engine.QueryLatin(_letters, take)
                : _engine.QueryLetters(_letters, take);
        }

        if (_digits.Length > 0)
        {
            return _engine.Query(_digits, take);
        }

        return [];
    }

    private void RefreshLeftRail(IReadOnlyList<T9Candidate> all)
    {
        if (_leftRail is null)
        {
            return;
        }

        IReadOnlyList<string> items;
        Action<string> tap;
        string? selected = null;
        var syllables = CandidateFallPolicy.ComposingChinese(
                _board == Board.Pinyin,
                _board == Board.Full,
                _latin,
                _board == Board.Pinyin26)
            && (_digits.Length > 0 || _letters.Length > 0);
        if (syllables != _railSyllables)
        {
            _railFallOffset = 0;
            _railSyllables = syllables;
        }

        if (syllables)
        {
            items = ResolveRailSyllables(all);
            tap = SelectPinyin;
        }
        else
        {
            items = CandidateFallPolicy.UsesLatinMarks(_board == Board.English, _latin)
            ? SymbolCatalog.EnglishMarks
            : QuickCn;
            tap = EmitText;
        }

        var wide26 = _board is Board.English or Board.Pinyin26;
        var width = _leftRail.ActualWidth > 1
            ? _leftRail.ActualWidth
            : wide26
                ? KeyboardChromeSize.EnglishRail
                : KeyboardChromeSize.CompactColumns().Rail;
        var height = _leftRail.ActualHeight > 1
            ? _leftRail.ActualHeight
            : wide26
                ? KeyboardChromeSize.EnglishBoardHeight
                : KeyboardChromeSize.CompactColumns().Board;
        var slot = height / LeftRailSlots.Count;
        var buttons = new List<Button>(items.Count);
        foreach (var value in items)
        {
            var button = MakeKey(value, () => tap(value), 14, gestureRegion: true);
            button.Style = (Style)FindResource("FunctionKeyButton");
            if (string.Equals(value, selected, StringComparison.Ordinal))
            {
                button.Background = (Brush)FindResource("TabOnBrush");
                button.Foreground = (Brush)FindResource("AccentBrush");
            }

            buttons.Add(button);
        }

        _railScroll = MakeSlotViewer(buttons, width, height, slot, _railFallOffset);
        _leftRail.Content = _railScroll;
    }

    private IReadOnlyList<string> ResolveRailSyllables(IReadOnlyList<T9Candidate> all)
    {
        IReadOnlyList<string> items = _digits.Length > 0
            ? _engine.QuerySyllables(_digits, _confirmedSyllables)
            : _letters.Length > 0
                ? _engine.QueryLetterSyllables(_letters, _confirmedSyllables)
                : [];
        return items.Count > 0
            ? items
            : SyllableSelectPolicy.Rail(all.Select(candidate => candidate.Pinyin), _confirmedSyllables);
    }

    private void BuildNumberBoard()
    {
        var unit = KeyboardChromeSize.CompactUnit;
        var stage = new Grid();
        var grid = new UniformGrid
        {
            Rows = 4,
            Columns = 3,
            Width = unit * KeyboardChromeSize.NumberColumns,
            Height = unit * KeyboardChromeSize.NumberRows,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        foreach (var digit in NumberPadLayout.Keys)
        {
            grid.Children.Add(MakeNumberKey(digit, unit));
        }

        stage.Children.Add(grid);
        BoardHost.Children.Add(stage);
    }

    private Button MakeNumberKey(string digit, double unit)
    {
        var button = MakeKey(digit, () => EmitText(digit), T9KeyFace.NumberFontSize);
        button.Padding = new Thickness(0);
        button.Content = new Viewbox
        {
            Stretch = Stretch.Uniform,
            Width = unit * 0.42,
            Height = unit * 0.42,
            Child = new TextBlock
            {
                Text = digit,
                FontSize = T9KeyFace.NumberFontSize,
                FontWeight = FontWeights.SemiBold,
                HorizontalAlignment = HorizontalAlignment.Center
            }
        };
        return button;
    }

    private void BuildSymbolBoard()
    {
        var (rail, board) = KeyboardChromeSize.SymbolColumns();
        var stage = KeyboardChromeSize.CompactBoard;
        var root = new Grid { HorizontalAlignment = HorizontalAlignment.Center };
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(rail) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(board) });

        var categories = new List<Button>(SymbolCatalog.Names.Length);
        foreach (var name in SymbolCatalog.Names)
        {
            var button = MakeKey(name, () => OnSymbolCategory(name), 12, gestureRegion: true);
            button.Style = (Style)FindResource("FunctionKeyButton");
            if (string.Equals(name, _symbolCategory, StringComparison.Ordinal))
            {
                button.Background = (Brush)FindResource("TabOnBrush");
                button.Foreground = (Brush)FindResource("AccentBrush");
            }

            categories.Add(button);
        }

        var slot = stage / LeftRailSlots.Count;
        _categoryScroll = MakeSlotViewer(categories, rail, stage, slot, _categoryFallOffset);
        Grid.SetColumn(_categoryScroll, 0);
        root.Children.Add(_categoryScroll);

        var marks = CurrentSymbolMarks();
        var buttons = new List<Button>(marks.Count);
        foreach (var symbol in marks)
        {
            buttons.Add(MakeSymbolKey(symbol));
        }

        var cell = board / FallFlow.Columns;
        _symbolFallOffset = FallFlow.Clamp(
            _symbolFallOffset,
            FallFlow.ContentHeight(buttons.Count, cell),
            stage);
        _markScroll = MakeFallViewer(buttons, board, stage, cell, _symbolFallOffset);
        Grid.SetColumn(_markScroll, 1);
        root.Children.Add(_markScroll);
        BoardHost.Children.Add(root);
    }

    private void SelectPinyin(string pinyin)
    {
        var next = SyllableSelectPolicy.Confirm(_confirmedSyllables, pinyin);
        _confirmedSyllables.Clear();
        _confirmedSyllables.AddRange(next);
        _candidateBarOffset = 0;
        _candidateFallOffset = 0;
        if (_candidatesExpanded)
        {
            RefreshChrome();
            return;
        }

        RefreshCandidates();
    }

    private void BuildCandidateFall()
    {
        var (rail, _) = KeyboardChromeSize.CompactColumns();
        var width = BoardHost.ActualWidth > 1
            ? BoardHost.ActualWidth
            : KeyboardChromeSize.CompactWidth - KeyboardChromeSize.FramePad;
        var height = BoardHost.ActualHeight > 1
            ? BoardHost.ActualHeight
            : KeyboardChromeSize.CompactColumns().Board;
        var board = Math.Max(1, width - rail);
        var root = new Grid { Width = width, Height = height };
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(rail) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(board) });

        _leftRail = new ContentControl
        {
            ClipToBounds = true,
            Width = rail,
            Height = height
        };
        Grid.SetColumn(_leftRail, 0);
        root.Children.Add(_leftRail);
        RefreshLeftRail([]);

        var all = FilterCandidates(QueryCurrent(CandidateBarSlots.FallQueryTake));
        var buttons = new List<Button>(all.Count);
        for (var index = 0; index < all.Count; index++)
        {
            var candidate = all[index];
            var button = MakeKey(candidate.Word, () => Commit(candidate), T9KeyFace.FontSize, gestureRegion: true);
            button.FontWeight = FontWeights.Normal;
            button.Margin = new Thickness(2, 1.5, 2, 1.5);
            button.Padding = new Thickness(4, 0, 4, 0);
            if (index == 0)
            {
                button.Background = (Brush)FindResource("AccentSoftBrush");
                button.Foreground = (Brush)FindResource("AccentBrush");
            }

            buttons.Add(button);
        }

        _candidateFallOffset = FallFlow.Clamp(
            _candidateFallOffset,
            FallFlow.ContentHeight(buttons.Count, KeyboardChromeSize.CandidateCell),
            height);
        _candidateScroll = MakeFallViewer(
            buttons,
            board,
            height,
            KeyboardChromeSize.CandidateCell,
            _candidateFallOffset);
        Grid.SetColumn(_candidateScroll, 1);
        root.Children.Add(_candidateScroll);
        BoardHost.Children.Add(root);
    }

    private void OnSymbolCategory(string name)
    {
        _symbolCategory = name;
        _symbolFallOffset = 0;
        RefreshChrome();
    }

    private void OnSymbolPicked(string symbol)
    {
        EmitText(symbol);
        var remembered = SymbolPanelPolicy.Remember(_symbolRecent, symbol);
        _symbolRecent.Clear();
        _symbolRecent.AddRange(remembered);
        if (SymbolPanelPolicy.StayAfterPick(_symbolLock))
        {
            if (string.Equals(_symbolCategory, SymbolCatalog.Recent, StringComparison.Ordinal))
            {
                _symbolFallOffset = 0;
                RefreshChrome();
            }

            return;
        }

        ShowBoard(_homeBoard);
    }

    private IReadOnlyList<string> CurrentSymbolMarks() =>
        SymbolCatalog.Marks(_symbolCategory, _symbolRecent);

    private bool IsSymbolBoard =>
        _board is Board.SymbolCn or Board.SymbolEn;

    private void ShowBoard(Board board)
    {
        var enteringSymbol = !IsSymbolBoard && board is Board.SymbolCn or Board.SymbolEn;
        var leavingSymbol = IsSymbolBoard && board is not Board.SymbolCn and not Board.SymbolEn;
        if (enteringSymbol)
        {
            _placeBeforeSymbol = _placed;
            _boardBeforeSymbol = _board;
        }

        if (board != _board)
        {
            _railFallOffset = 0;
            ClearConfirmedSyllables();
        }

        if (BoardNavigation.UpdatesHome(ToSurface(board)))
        {
            _homeBoard = board;
        }

        _candidatesExpanded = false;
        _candidateFallOffset = 0;

        var resume = leavingSymbol
            && BoardPlaceResume.ShouldKeepPlace(ToSurface(_boardBeforeSymbol), ToSurface(board))
            ? _placeBeforeSymbol
            : default;
        _holdPlaceOnLayout = leavingSymbol && !resume.IsEmpty;
        _board = board;
        RefreshChrome();
        _holdPlaceOnLayout = false;
        if (!resume.IsEmpty)
        {
            RestoreBoardPlace(resume);
        }
    }

    private void RestoreBoardPlace(NativeRect before)
    {
        PixelSize(out var width, out var height);
        var next = BoardPlaceResume.At(before, width, height);
        if (next.IsEmpty)
        {
            return;
        }

        _placingLayout = true;
        _placed = next;
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd != IntPtr.Zero)
        {
            if (!UiAccessBandHost.Shared.TryPlace(hwnd, _owner, _placed))
            {
                NativeMethods.SetWindowPos(
                    hwnd,
                    NativeMethods.HwndTopmost,
                    _placed.Left,
                    _placed.Top,
                    width,
                    height,
                    NativeMethods.SwpNoActivate | NativeMethods.SwpShowWindow);
            }
        }

        _placingLayout = false;
        RequestPublishHost();
    }

    private Button MakeSymbolKey(string symbol)
    {
        var size = symbol.Length > 2 ? 13 : T9KeyFace.SymbolFontSize;
        var button = MakeKey(symbol, () => OnSymbolPicked(symbol), size, gestureRegion: true);
        button.FontWeight = T9KeyFace.SymbolSemiBold ? FontWeights.SemiBold : FontWeights.Normal;
        button.Margin = new Thickness(2);
        button.Padding = new Thickness(0);
        return button;
    }

    private Button MakeKey(
        string title,
        Action tap,
        double fontSize = 16,
        bool gestureRegion = false,
        bool repeatWhileHeld = false)
    {
        return MakeKey(title, tap, tap, fontSize, gestureRegion, repeatWhileHeld);
    }

    private Button MakeKey(
        string title,
        Action tap,
        Action longPress,
        double fontSize,
        bool gestureRegion = false,
        bool repeatWhileHeld = false)
    {
        var button = new Button
        {
            Style = (Style)FindResource("KeyButton"),
            Content = title,
            FontSize = fontSize
        };
        BindTap(button, tap, longPress, title, gestureRegion, repeatWhileHeld);
        return button;
    }

    private Button MakeFunctionKey(
        string title,
        Action tap,
        double fontSize,
        bool repeatWhileHeld = false)
    {
        var button = MakeKey(title, tap, fontSize, repeatWhileHeld: repeatWhileHeld);
        button.Style = (Style)FindResource("FunctionKeyButton");
        return button;
    }

    private Button MakeIconKey(
        Func<Brush, FrameworkElement> icon,
        Action tap,
        string title,
        bool repeatWhileHeld = false)
    {
        var button = MakeFunctionKey(title, tap, T9KeyFace.FontSize, repeatWhileHeld);
        button.Content = icon(button.Foreground);
        button.ToolTip = title;
        return button;
    }

    private void RefreshFunctionIcons()
    {
        if (LangButton is not null && IsSymbolBoard)
        {
            if (_symbolLock)
            {
                LangButton.Background = (Brush)FindResource("AccentBrush");
                LangButton.Foreground = System.Windows.Media.Brushes.White;
            }
            else
            {
                LangButton.ClearValue(BackgroundProperty);
                LangButton.ClearValue(ForegroundProperty);
                LangButton.Style = (Style)FindResource("FunctionKeyButton");
            }

            LangButton.Content = KeyGlyphs.Lock(_symbolLock, LangButton.Foreground);
            LangButton.ToolTip = _symbolLock ? "已锁定，点锁键才解锁" : "锁定后可连续输入符号";
        }

        if (EnterButton is not null)
        {
            var backspace = ToolBarPolicy.BackspaceInsteadOfEnter(
                _numberPad,
                IsSymbolBoard,
                _candidatesExpanded);
            EnterButton.Content = backspace
                ? KeyGlyphs.Backspace(EnterButton.Foreground)
                : KeyGlyphs.Enter(EnterButton.Foreground);
            EnterButton.ToolTip = backspace ? "退格" : "回车";
        }
    }

    private void SetSymbolLock(bool locked)
    {
        if (_symbolLock == locked)
        {
            return;
        }

        _symbolLock = locked;
        _settings.SymbolLock = locked;
        _settings.Save();
    }

    private void AddT9(
        System.Windows.Controls.Panel parent,
        string letters,
        Action tap,
        Action longPress)
    {
        var button = new Button
        {
            Style = (Style)FindResource("KeyButton"),
            Content = letters,
            FontSize = T9KeyFace.FaceSizeFor(letters),
            FontWeight = FontWeights.SemiBold
        };
        BindTap(button, tap, longPress, letters, gestureRegion: false);
        parent.Children.Add(button);
    }

    private static void AddTo(Grid grid, UIElement child, int row, int column)
    {
        Grid.SetRow(child, row);
        Grid.SetColumn(child, column);
        grid.Children.Add(child);
    }

    private void BindTap(
        Button button,
        Action tap,
        Action longPress,
        string title,
        bool gestureRegion,
        bool repeatWhileHeld = false,
        Func<bool>? shouldRepeat = null)
    {
        var immediate = KeyTapTimingPolicy.IsImmediate(
            hasDistinctLongPress: !ReferenceEquals(tap, longPress),
            gestureRegion: gestureRegion);
        _hostTapActions.Bind(button, tap);
        if (immediate)
        {
            _hostPressActions.Bind(button, tap);
        }

        if (repeatWhileHeld)
        {
            _hostRepeats[button] = shouldRepeat ?? (() => true);
        }

        // 这一下按下是否还欠一次点击，由每颗键自己记。
        // 早先读的是共用长按定时器的 IsEnabled，快速连打时后一颗键的按下会把它
        // 重置、前一颗键的抬起又把它停掉，轮到后一颗键抬起就整个丢了，也就是吞字。
        var owed = false;

        void Press()
        {
            OverlayTouch.Note();
            PressKeyVisual(button);
            _pendingDigit = title.Length == 1 ? title[0] : null;
            _longPressTimer.Stop();
            if (immediate)
            {
                _immediateTapFired = true;
                tap();
                if (repeatWhileHeld && (shouldRepeat?.Invoke() ?? true))
                {
                    StartKeyRepeat(button, tap, shouldRepeat);
                }

                return;
            }

            owed = true;
            _longPressTimer.Tag = (button, longPress);
            _longPressTimer.Start();
        }

        void Release()
        {
            StopKeyRepeat(button);
            ReleaseKeyVisualSoon(button);

            // 用这颗键自己是不是「按下即触发」来判断，而不是 _immediateTapFired：
            // 后者是留给翻页手势的全局标记，触摸被提升成鼠标事件时不一定清得掉。
            if (immediate || !owed)
            {
                return;
            }

            owed = false;
            if (ReferenceEquals(_longPressFired, button))
            {
                _longPressFired = null;
                return;
            }

            if (_longPressTimer.Tag is ValueTuple<Button, Action> pending
                && ReferenceEquals(pending.Item1, button))
            {
                _longPressTimer.Stop();
            }

            tap();
        }

        button.PreviewMouseLeftButtonDown += (_, e) =>
        {
            e.Handled = true;
            Press();
        };
        button.PreviewMouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            Release();
        };
        button.PreviewTouchDown += (_, e) =>
        {
            e.Handled = true;
            Press();
        };
        button.PreviewTouchUp += (_, e) =>
        {
            e.Handled = true;
            Release();
        };
    }

    private void StartKeyRepeat(Button button, Action tap, Func<bool>? gate)
    {
        _keyRepeatTimer.Stop();
        _keyRepeatButton = button;
        _keyRepeatAction = tap;
        _keyRepeatGate = gate;
        _keyRepeatTimer.Interval = KeyRepeatPolicy.InitialDelay;
        _keyRepeatTimer.Start();
    }

    private void StopKeyRepeat(Button? button = null)
    {
        if (button is not null
            && _keyRepeatButton is not null
            && !ReferenceEquals(_keyRepeatButton, button))
        {
            return;
        }

        _keyRepeatTimer.Stop();
        _keyRepeatButton = null;
        _keyRepeatAction = null;
        _keyRepeatGate = null;
    }

    private void OnKeyRepeat(object? sender, EventArgs e)
    {
        if (_keyRepeatAction is null
            || (_keyRepeatGate is not null && !_keyRepeatGate()))
        {
            StopKeyRepeat();
            return;
        }

        _keyRepeatAction();
        _keyRepeatTimer.Interval = KeyRepeatPolicy.RepeatInterval;
    }

    private void OnLongPress(object? sender, EventArgs e)
    {
        _longPressTimer.Stop();
        if (_longPressTimer.Tag is ValueTuple<Button, Action> entry)
        {
            _longPressFired = entry.Item1;
            entry.Item2();
        }
    }

    private void PressKeyVisual(Button button)
    {
        // 上一颗键可能还在等最短可见时间，先收掉，免得两颗键同时是按下态。
        if (_keyReleasePending is { } pending && !ReferenceEquals(pending, button))
        {
            TouchKeyVisual.Release(pending, animate: false);
        }

        _keyReleaseTimer.Stop();
        _keyReleasePending = null;
        _keyPressedUtc = DateTime.UtcNow;
        _keyVisuallyPressed = true;
        TouchKeyVisual.Press(button, animate: !KeyFeedbackPolicy.InstantPress);
    }

    /// <summary>
    /// 等按下效果留够 <see cref="KeyPressMinVisible"/> 再回弹。
    /// 触摸抬手比动画本身还快，直接回弹就等于没有按下反馈。
    /// </summary>
    private void ReleaseKeyVisualSoon(Button button)
    {
        var remaining = _keyPressedUtc == DateTime.MinValue
            ? TimeSpan.Zero
            : KeyPressMinVisible - (DateTime.UtcNow - _keyPressedUtc);
        if (remaining <= TimeSpan.Zero)
        {
            _keyReleaseTimer.Stop();
            _keyReleasePending = null;
            _keyVisuallyPressed = false;
            TouchKeyVisual.Release(button);
            TryRebuildKeysWhenIdle();
            return;
        }

        _keyReleasePending = button;
        _keyReleaseTimer.Interval = remaining;
        _keyReleaseTimer.Start();
    }

    private void OnSplit()
    {
        if (_candidatesExpanded && _candidateScroll is not null)
        {
            FlingFall(
                new FallScrollTarget
                {
                    Viewer = _candidateScroll,
                    Get = () => _candidateFallOffset,
                    Set = value => _candidateFallOffset = value,
                    Horizontal = false
                },
                2200);
            return;
        }

        if (CandidateScroller is null)
        {
            return;
        }

        FlingFall(
            new FallScrollTarget
            {
                Viewer = CandidateScroller,
                Get = () => _candidateBarOffset,
                Set = value => _candidateBarOffset = value,
                Horizontal = true
            },
            2200);
    }

    private void OnRetype()
    {
        ResetComposition();
        if (ImeHost.Shared.HasClient)
        {
            ImeHost.Shared.Cancel();
        }
    }

    private void OnKey(char digit)
    {
        if (_numberPad)
        {
            SendText(digit.ToString());
            return;
        }

        if (digit == '1')
        {
            CyclePunctuation();
            return;
        }

        if (digit == '0')
        {
            OnSpace();
            return;
        }

        if (_english)
        {
            MultiTap(digit);
            return;
        }

        _letters = "";
        _digits += digit;
        _candidateBarOffset = 0;
        ClearConfirmedSyllables();
        RefreshCandidates();
        PushComposition();
    }

    private void OnSpace()
    {
        if (_numberPad || (_english && !EnglishPredictOn))
        {
            SendVirtual(NativeMethods.VkSpace);
            return;
        }

        if (_candidates.Count > 0)
        {
            Commit(_candidates[0]);
            return;
        }

        if (EnglishPredictOn && _letters.Length > 0)
        {
            SendText(_letters);
            ResetComposition();
            return;
        }

        SendVirtual(NativeMethods.VkSpace);
    }

    private void OnBackspace()
    {
        using var scope = Perf.Begin("tap.backspace");
        if ((_digits.Length > 0 || _letters.Length > 0) && !_numberPad)
        {
            if (_confirmedSyllables.Count > 0)
            {
                var popped = SyllableSelectPolicy.Pop(_confirmedSyllables);
                _confirmedSyllables.Clear();
                _confirmedSyllables.AddRange(popped);
                _candidateBarOffset = 0;
                RefreshCandidates();
                PushComposition();
                return;
            }

            if (_letters.Length > 0)
            {
                _letters = _letters[..^1];
                _digits = T9Engine.ToDigits(_letters);
            }
            else
            {
                _digits = _digits[..^1];
            }

            _candidateBarOffset = 0;
            if (_digits.Length == 0 && _letters.Length == 0)
            {
                RefreshCandidates();
                if (ImeHost.Shared.HasClient)
                {
                    ImeHost.Shared.Cancel();
                }
                return;
            }

            RefreshCandidates();
            PushComposition();
            return;
        }

        SendVirtual(NativeMethods.VkBack);
    }

    private void CyclePunctuation()
    {
        SendText(Punctuation[_punctIndex % Punctuation.Length]);
        _punctIndex++;
    }

    private void MultiTap(char digit)
    {
        var letters = T9Engine.LettersForKey(digit);
        if (letters.Length == 0)
        {
            return;
        }

        var now = DateTime.UtcNow;
        if (_lastTapDigit == digit && (now - _lastTapUtc).TotalMilliseconds < 800)
        {
            _multiTapIndex = (_multiTapIndex + 1) % letters.Length;
            SendVirtual(NativeMethods.VkBack);
        }
        else
        {
            _multiTapIndex = 0;
        }

        _lastTapDigit = digit;
        _lastTapUtc = now;
        SendText(letters[_multiTapIndex].ToString());
    }

    private string PreviewPinyin() =>
        _letters.Length > 0 ? _letters : _engine.PinyinPreview(_digits);

    /// <summary>
    /// 一次按键会连带刷新候选、侧栏和预览，原先每次刷新都同步抓一整帧。
    /// 这里合并到本次布局收敛之后只抓一帧。
    /// </summary>
    private void RequestPublishHost()
    {
        if (!_hosting)
        {
            return;
        }

        if (_holdHostFrame)
        {
            _publishQueued = true;
            return;
        }

        if (_publishQueued)
        {
            return;
        }

        _publishQueued = true;
        Dispatcher.BeginInvoke(
            HostFrame.PublishPriority,
            new Action(() =>
            {
                _publishQueued = false;
                PublishHost();
            }));
    }

    private void PublishHost()
    {
        if (!_hosting)
        {
            return;
        }

        if (_placed.IsEmpty)
        {
            return;
        }

        try
        {
            HostFrame.FlushLayout(Dispatcher);
            var pixels = CaptureHostFrame(out var width, out var height);
            if (pixels is null)
            {
                Log.Warn("系统浮层帧捕获失败");
                return;
            }

            if (!ImeHost.Shared.ShowHost(
                    _placed,
                    pixels,
                    width,
                    height,
                    _context.Client))
            {
                Log.Warn("系统浮层 Band 窗发送失败，继续显示九键本身");
                ShowLocalFallback();
            }
        }
        catch (Exception ex)
        {
            Log.Warn($"系统浮层帧: {ex.Message}");
        }
    }

    private byte[]? CaptureHostFrame(out int width, out int height)
    {
        var pixels = HostFrame.Capture(this, out width, out height);
        if (pixels is null)
        {
            return null;
        }

        RebuildHostHitRegions(width, height);
        if (!HostHitMap.ShouldRetryAfterRebuild(
                _hostHitRegions.Count,
                CountExpectedHostButtons(FrameBorder)))
        {
            return pixels;
        }

        HostFrame.FlushLayout(Dispatcher);
        pixels = HostFrame.Capture(this, out width, out height);
        if (pixels is not null)
        {
            RebuildHostHitRegions(width, height);
        }

        return pixels;
    }

    private static int CountExpectedHostButtons(DependencyObject node)
    {
        var count = 0;
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(node); index++)
        {
            var child = VisualTreeHelper.GetChild(node, index);
            if (child is Button button
                && HostHitMap.CountsAsExpected(
                    button.Visibility == Visibility.Visible,
                    button.IsEnabled))
            {
                count++;
            }

            count += CountExpectedHostButtons(child);
        }

        return count;
    }

    private void OnHostVisibilityChanged(bool shown)
    {
        Dispatcher.BeginInvoke(() =>
        {
            if (!_hosting)
            {
                return;
            }

            _hostReady = shown;
            var hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd == IntPtr.Zero)
            {
                return;
            }

            if (shown)
            {
                NativeMethods.SetWindowPos(
                    hwnd,
                    IntPtr.Zero,
                    HostFrame.ParkOffset,
                    HostFrame.ParkOffset,
                    0,
                    0,
                    NativeMethods.SwpNoSize |
                    NativeMethods.SwpNoActivate |
                    NativeMethods.SwpShowWindow);
            }
            else
            {
                ShowLocalFallback();
            }
        });
    }

    private void ShowLocalFallback()
    {
        if (_placed.IsEmpty)
        {
            return;
        }

        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        NativeMethods.SetWindowPos(
            hwnd,
            NativeMethods.HwndTopmost,
            _placed.Left,
            _placed.Top,
            _placed.Width,
            _placed.Height,
            NativeMethods.SwpNoActivate | NativeMethods.SwpShowWindow);
    }

    private void BeginHostPress(Button button)
    {
        // 上一颗键可能还在等最短可见时间。先收掉，否则定时器到点时会释放错对象，
        // 快速连打就会留下一颗一直是按下态的键。
        _hostPressRelease.Stop();
        if (_pressedHostKey is { } previous && !ReferenceEquals(previous, button))
        {
            TouchKeyVisual.Release(previous, animate: false);
        }

        _pressedHostKey = button;
        _holdHostFrame = true;
        TouchKeyVisual.Press(button, animate: false);
        if (_hosting)
        {
            _publishQueued = false;
            PublishHost();
        }

        _hostPressShownUtc = DateTime.UtcNow;
    }

    private void ReleaseHostPress()
    {
        StopKeyRepeat(_pressedHostKey);
        _hostPressRelease.Stop();
        var button = _pressedHostKey;
        if (button is null && !_holdHostFrame)
        {
            return;
        }

        _pressedHostKey = null;
        _holdHostFrame = false;
        _hostPressShownUtc = DateTime.MinValue;
        TouchKeyVisual.Release(button, animate: false);
        if (_hosting)
        {
            _publishQueued = false;
            PublishHost();
        }

        TryRebuildKeysWhenIdle();
    }

    /// <summary>
    /// 等按下帧留够 <see cref="HostPressMinVisible"/> 再抬起。
    /// 不能在这里同步等：UI 线程一停，按下帧本身也发不出去。
    /// </summary>
    private void ReleaseHostPressSoon()
    {
        if (_hostPressShownUtc == DateTime.MinValue)
        {
            ReleaseHostPress();
            return;
        }

        var remaining = KeyPressMinVisible - (DateTime.UtcNow - _hostPressShownUtc);
        if (remaining <= TimeSpan.Zero)
        {
            ReleaseHostPress();
            return;
        }

        _hostPressRelease.Interval = remaining;
        _hostPressRelease.Start();
    }

    /// <summary>
    /// 系统浮层里按下即触发：原生在指针按下时就上报，这类键不必等抬起。
    /// 命中后记账，随后的 hit / swipe 会被丢弃。
    /// </summary>
    private void OnHostPress(int x, int y)
    {
        Dispatcher.BeginInvoke(() =>
        {
            OverlayTouch.Note();
            _hostPressGate.Reset();
            var button = ResolveHostButton(x, y, "按下");
            if (button is null)
            {
                return;
            }

            BeginHostPress(button);
            if (_hostPressActions.TryInvoke(button))
            {
                _hostPressGate.NotePressHandled();
                if (_hostRepeats.TryGetValue(button, out var gate) && gate())
                {
                    StartKeyRepeat(
                        button,
                        () =>
                        {
                            if (!_hostRepeats.TryGetValue(button, out var live) || !live())
                            {
                                StopKeyRepeat(button);
                                return;
                            }

                            _hostPressActions.TryInvoke(button);
                        },
                        gate);
                }
            }
        });
    }

    private void OnHostHit(int x, int y)
    {
        Dispatcher.BeginInvoke(() =>
        {
            if (_hostPressGate.ConsumeRelease())
            {
                ReleaseHostPressSoon();
                return;
            }

            var button = ResolveHostButton(x, y, "抬起");
            if (button is null)
            {
                ReleaseHostPressSoon();
                LogHostMiss(x, y);
                return;
            }

            // 抬起赶在按下之前到了，按下帧还没画出来，这一下就完全没有反馈。
            // 补一帧按下再往下走。
            if (_pressedHostKey is null)
            {
                BeginHostPress(button);
            }

            // 解除帧合并：下面刷候选时要能照常发帧，否则候选要等到抬起才更新。
            // 按键仍是按下态，所以这一帧里既有按下效果也有新候选。
            _holdHostFrame = false;
            if (!_hostTapActions.TryInvoke(button))
            {
                button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            }

            ReleaseHostPressSoon();
        });
    }

    private void OnHostSwipe(int x1, int y1, int x2, int y2)
    {
        Dispatcher.BeginInvoke(() =>
        {
            if (_hostPressGate.ConsumeRelease())
            {
                ReleaseHostPressSoon();
                return;
            }

            ReleaseHostPress();
            var start = new Point(x1 / _hostScaleX, y1 / _hostScaleY);
            var end = new Point(x2 / _hostScaleX, y2 / _hostScaleY);
            _fallPressTime = FallInertia.Now - 0.08;
            if (HandleSwipe(start, end, 1))
            {
                return;
            }
        });
    }

    private void OnHostMoved(int left, int top)
    {
        Dispatcher.BeginInvoke(() =>
        {
            PixelSize(out var width, out var height);
            _placed = new NativeRect
            {
                Left = left,
                Top = top,
                Right = left + width,
                Bottom = top + height
            };
            if (!_placingLayout)
            {
                _movedByUser = true;
            }

            var hwnd = new WindowInteropHelper(this).Handle;
            var dpi = hwnd == IntPtr.Zero ? 96u : NativeMethods.GetDpiForWindow(hwnd);
            if (dpi == 0)
            {
                dpi = 96;
            }
            Left = left * 96.0 / dpi;
            Top = top * 96.0 / dpi;
            PublishHost();
        });
    }

    private Button? ResolveHostButton(int x, int y, string source)
    {
        var button = HostHitMap.FindLayout(
            _hostHitRegions,
            x,
            y,
            _hostLayoutW,
            _hostLayoutH,
            _hostFrameW,
            _hostFrameH,
            out var space);
        if (button is not null)
        {
            Log.Info(
                $"系统浮层{source} 命中 {HostKeyLabel(button)} x={x} y={y} 空间={space} "
                + $"layout={_hostLayoutW:0}x{_hostLayoutH:0} frame={_hostFrameW}x{_hostFrameH}");
        }

        return button;
    }

    private static string HostKeyLabel(Button button) =>
        button.ToolTip as string
            ?? button.Content as string
            ?? (string.IsNullOrEmpty(button.Name) ? "?" : button.Name);

    private void LogHostMiss(int x, int y)
    {
        var mapped = HostHitMap.MapClientToFrame(
            x,
            y,
            _hostFrameW,
            _hostFrameH,
            (int)Math.Round(_hostLayoutW),
            (int)Math.Round(_hostLayoutH));
        var box = HostHitMap.UnionBounds(_hostHitRegions);
        Log.Warn(
            $"系统浮层点击未命中按钮 x={x} y={y} keys={_hostHitRegions.Count} "
            + $"layout={_hostLayoutW:0}x{_hostLayoutH:0} frame={_hostFrameW}x{_hostFrameH} "
            + $"mapped=({mapped.X},{mapped.Y}) "
            + $"keysBox={box.Left:0},{box.Top:0}-{box.Right:0},{box.Bottom:0}");
    }

    private void RebuildHostHitRegions(int pixelWidth, int pixelHeight)
    {
        if (FrameBorder.ActualWidth <= 0 || FrameBorder.ActualHeight <= 0)
        {
            return;
        }

        _hostFrameW = pixelWidth;
        _hostFrameH = pixelHeight;
        if (!HostHitMap.IsLayoutCollapsed(FrameBorder.ActualHeight, Height))
        {
            _hostLayoutW = FrameBorder.ActualWidth;
            _hostLayoutH = FrameBorder.ActualHeight;
        }

        _hostScaleX = _hostLayoutW > 0 ? _hostFrameW / _hostLayoutW : 1;
        _hostScaleY = _hostLayoutH > 0 ? _hostFrameH / _hostLayoutH : 1;
        var next = new List<HostHitRegion<Button>>();
        CollectHostButtons(FrameBorder, next);
        var maxBottom = 0.0;
        for (var index = 0; index < next.Count; index++)
        {
            if (next[index].Bottom > maxBottom)
            {
                maxBottom = next[index].Bottom;
            }
        }

        var covers = !HostHitMap.IsLayoutCollapsed(FrameBorder.ActualHeight, Height)
            && HostHitMap.CoversFrameHeight(maxBottom, _hostLayoutH);
        if (!HostHitMap.ShouldReplaceRegions(next.Count, _hostHitRegions.Count, covers))
        {
            return;
        }

        _hostHitRegions.Clear();
        _hostHitRegions.AddRange(next);
    }

    private void CollectHostButtons(
        DependencyObject node,
        List<HostHitRegion<Button>> regions)
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(node); index++)
        {
            var child = VisualTreeHelper.GetChild(node, index);
            if (child is Button button
                && HostHitMap.ShouldCollect(
                    button.Visibility == Visibility.Visible,
                    button.IsEnabled,
                    button.RenderSize.Width,
                    button.RenderSize.Height))
            {
                try
                {
                    var bounds = button.TransformToAncestor(FrameBorder)
                        .TransformBounds(new Rect(button.RenderSize));
                    if (TryClipToScrollViewer(button, out var clip))
                    {
                        bounds.Intersect(clip);
                        if (bounds.IsEmpty || bounds.Width < 2 || bounds.Height < 2)
                        {
                            CollectHostButtons(child, regions);
                            continue;
                        }
                    }

                    regions.Add(new HostHitRegion<Button>(
                        bounds.Left,
                        bounds.Top,
                        bounds.Right,
                        bounds.Bottom,
                        button));
                }
                catch (InvalidOperationException)
                {
                    // The visual was rebuilt while the frame map was captured.
                }
            }

            CollectHostButtons(child, regions);
        }
    }

    private void RefreshCandidates()
    {
        using var scope = Perf.Begin("candidates.refresh");
        try
        {
        _candidates.Clear();
        if ((_digits.Length == 0 && _letters.Length == 0)
            || _numberPad
            || (_english && !EnglishPredictOn))
        {
            PaintCodeText("");
            _candidatesExpanded = false;
            _candidateFallOffset = 0;
            _candidateBarOffset = 0;
            HideCandidateMore();
            PaintCandidateBar([]);
            RefreshLeftRail([]);
            return;
        }

        var unfiltered = QueryCurrent(CandidateBarSlots.QueryTake(_candidatesExpanded));
        if (!KeyFeedbackPolicy.ComposeBeforeCandidates)
        {
            PaintCodeText(SyllableSelectPolicy.Display(_confirmedSyllables, PreviewPinyin()));
        }

        RefreshLeftRail(unfiltered);
        var all = FilterCandidates(unfiltered);
        _candidates.AddRange(all);
        var shown = CandidateBarSlots.PaintCount(all.Count, expanded: false);
        PaintCandidateBar(shown == all.Count ? all : all.Take(shown).ToArray());
        ShowCandidateMore(CandidateBarSlots.ShowsMore(unfiltered.Count) || CandidateBarSlots.ShowsMore(all.Count));

        if (CandidateScroller is not null)
        {
            CandidateScroller.UpdateLayout();
            _candidateBarOffset = FallFlow.Clamp(
                _candidateBarOffset,
                CandidateScroller.ExtentWidth,
                CandidateScroller.ViewportWidth);
            CandidateScroller.ScrollToHorizontalOffset(_candidateBarOffset);
        }
        }
        finally
        {
            RequestPublishHost();
        }
    }

    private void PushComposition()
    {
        var preview = PreviewPinyin();
        PaintCodeText(SyllableSelectPolicy.Display(_confirmedSyllables, preview));
        EmitCompose(preview);
        if (_hosting)
        {
            ImeHost.Shared.OfferSearchCandidates(
                SearchCandidatePayload.Encode(
                    preview,
                    _candidates.Select(candidate => candidate.Word)));
        }
    }

    private void PaintCodeText(string preview)
    {
        if (CodeText is null)
        {
            return;
        }

        CodeText.Text = string.IsNullOrEmpty(preview)
            ? (_board == Board.English || (_board == Board.Full && _latin)
                ? (EnglishPredictOn && _letters.Length > 0 ? _letters : "EN")
                : "拼音")
            : preview;
    }

    private void EnsureBarButtons()
    {
        if (CandidatePanel is null)
        {
            return;
        }

        while (_barButtons.Count < CandidateBarSlots.Visible)
        {
            var slot = _barButtons.Count;
            var button = new Button
            {
                Style = (Style)FindResource("CandidateButton"),
                Visibility = Visibility.Collapsed
            };
            BindTap(button, () => CommitBar(slot), () => CommitBar(slot), "", gestureRegion: true);
            _barButtons.Add(button);
            CandidatePanel.Children.Add(button);
        }
    }

    private void PaintCandidateBar(IReadOnlyList<T9Candidate> items)
    {
        EnsureBarButtons();
        _barCommit.Clear();
        _barCommit.AddRange(items);
        var accent = (Brush)FindResource("AccentSoftBrush");
        var accentText = (Brush)FindResource("AccentBrush");
        var text = (Brush)FindResource("TextBrush");
        for (var i = 0; i < _barButtons.Count; i++)
        {
            var button = _barButtons[i];
            if (i >= items.Count)
            {
                button.Visibility = Visibility.Collapsed;
                button.Content = null;
                button.ToolTip = null;
                HostHitMap.RemoveTarget(_hostHitRegions, button);
                continue;
            }

            button.Visibility = Visibility.Visible;
            button.Content = items[i].Word;
            button.ToolTip = items[i].Word;
            if (i == 0)
            {
                button.Background = accent;
                button.Foreground = accentText;
                button.FontWeight = FontWeights.SemiBold;
            }
            else
            {
                button.ClearValue(BackgroundProperty);
                button.Foreground = text;
                button.FontWeight = FontWeights.Normal;
            }
        }

        RebindCandidateHostActions();
        RefreshHostCandidateHits();
    }

    private void RebindCandidateHostActions()
    {
        for (var index = 0; index < _barButtons.Count; index++)
        {
            var slot = index;
            _hostTapActions.Bind(_barButtons[index], () => CommitBar(slot));
            _hostPressActions.Bind(_barButtons[index], () => CommitBar(slot));
        }

        if (CandidateMore is null)
        {
            return;
        }

        void More() => OnCandidateMore(CandidateMore, new RoutedEventArgs());
        _hostTapActions.Bind(CandidateMore, More);
        _hostPressActions.Bind(CandidateMore, More);
    }

    private void RefreshHostCandidateHits()
    {
        if (FrameBorder is null || _hostHitRegions.Count == 0)
        {
            return;
        }

        var incoming = new List<HostHitRegion<Button>>();
        CollectCandidateHits(incoming);
        HostHitMap.Upsert(_hostHitRegions, incoming);
    }

    private void CollectCandidateHits(List<HostHitRegion<Button>> regions)
    {
        foreach (var button in _barButtons)
        {
            TryAddHostHit(button, regions);
        }

        TryAddHostHit(CandidateMore, regions);
    }

    private void TryAddHostHit(Button? button, List<HostHitRegion<Button>> regions)
    {
        if (button is null
            || !HostHitMap.ShouldCollect(
                button.Visibility == Visibility.Visible,
                button.IsEnabled,
                button.RenderSize.Width,
                button.RenderSize.Height))
        {
            return;
        }

        try
        {
            var bounds = button.TransformToAncestor(FrameBorder)
                .TransformBounds(new Rect(button.RenderSize));
            if (TryClipToScrollViewer(button, out var clip))
            {
                bounds.Intersect(clip);
                if (bounds.IsEmpty || bounds.Width < 2 || bounds.Height < 2)
                {
                    return;
                }
            }

            regions.Add(new HostHitRegion<Button>(
                bounds.Left,
                bounds.Top,
                bounds.Right,
                bounds.Bottom,
                button));
        }
        catch (InvalidOperationException)
        {
        }
    }

    private void CommitBar(int slot)
    {
        if ((uint)slot < (uint)_barCommit.Count)
        {
            Commit(_barCommit[slot]);
        }
    }

    private void RebuildKeysWhenIdle()
    {
        _rebuildKeysWhenIdle = true;
        TryRebuildKeysWhenIdle();
    }

    private void TryRebuildKeysWhenIdle()
    {
        if (!_rebuildKeysWhenIdle)
        {
            return;
        }

        if (!KeyFeedbackPolicy.CanRebuildFaces(
                _pressedHostKey is not null,
                _keyVisuallyPressed || _keyReleasePending is not null))
        {
            return;
        }

        _rebuildKeysWhenIdle = false;
        BuildKeys();
    }

    private void BindChromeKeys()
    {
        void Map(Button? button, Action tap, Func<bool>? repeat = null)
        {
            if (button is null)
            {
                return;
            }

            _hostTapActions.Bind(button, tap);
            _hostPressActions.Bind(button, tap);
            if (repeat is not null)
            {
                _hostRepeats[button] = repeat;
            }

            if (_chromeBound)
            {
                return;
            }

            BindTap(
                button,
                tap,
                tap,
                button.Name ?? "",
                gestureRegion: false,
                repeatWhileHeld: repeat is not null,
                shouldRepeat: repeat);
        }

        Map(LangButton, () => OnModeClicked(LangButton!, new RoutedEventArgs()));
        Map(SpaceButton, OnSpace);
        Map(DigitButton, () => OnDigitModeClicked(DigitButton!, new RoutedEventArgs()));
        Map(
            EnterButton,
            OnEnterTap,
            () => ToolBarPolicy.BackspaceInsteadOfEnter(
                _numberPad,
                IsSymbolBoard,
                _candidatesExpanded));
        _chromeBound = true;
    }

    private void OnEnterTap()
    {
        if (ToolBarPolicy.BackspaceInsteadOfEnter(_numberPad, IsSymbolBoard, _candidatesExpanded))
        {
            OnBackspace();
            return;
        }

        OnEnter();
    }

    private void HideCandidateMore() => ShowCandidateMore(false);

    private void ShowCandidateMore(bool visible)
    {
        if (CandidateMore is null)
        {
            return;
        }

        CandidateMore.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        if (!visible)
        {
            return;
        }

        CandidateMore.Content = _candidatesExpanded
            ? KeyGlyphs.ChevronUp(CandidateMore.Foreground)
            : KeyGlyphs.ChevronDown(CandidateMore.Foreground);
        CandidateMore.ToolTip = _candidatesExpanded ? "收起候选" : "展开候选";
    }

    private IReadOnlyList<T9Candidate> FilterCandidates(IReadOnlyList<T9Candidate> candidates)
    {
        if (_confirmedSyllables.Count == 0)
        {
            return candidates;
        }

        return candidates
            .Where(candidate => SyllableSelectPolicy.Matches(candidate.Pinyin, _confirmedSyllables))
            .ToArray();
    }

    private void Commit(T9Candidate candidate)
    {
        SendText(candidate.Word);
        if (ContinueRemainder(candidate))
        {
            return;
        }

        ResetComposition();
    }

    private bool ContinueRemainder(T9Candidate candidate)
    {
        if (_letters.Length > 0)
        {
            var typed = T9Engine.CompactLetters(_letters);
            var consumed = CompositionConsumePolicy.ConsumeLetters(
                candidate.MatchKind,
                candidate.Pinyin,
                typed);
            if (!CompositionConsumePolicy.HasRemainder(consumed, typed.Length))
            {
                return false;
            }

            _letters = typed[consumed..];
            _digits = T9Engine.ToDigits(_letters);
        }
        else
        {
            var consumed = CompositionConsumePolicy.ConsumeDigits(
                candidate.MatchKind,
                candidate.Pinyin,
                _digits);
            if (!CompositionConsumePolicy.HasRemainder(consumed, _digits.Length))
            {
                return false;
            }

            _digits = _digits[consumed..];
        }

        ClearConfirmedSyllables();
        _candidateBarOffset = 0;
        _candidateFallOffset = 0;
        if (_candidatesExpanded)
        {
            RefreshChrome();
        }
        else
        {
            RefreshCandidates();
        }

        PushComposition();

        return true;
    }

    private void ClearConfirmedSyllables() => _confirmedSyllables.Clear();

    private void ResetComposition()
    {
        var wasExpanded = _candidatesExpanded;
        _digits = "";
        _letters = "";
        _candidateBarOffset = 0;
        _railFallOffset = 0;
        ClearConfirmedSyllables();
        _candidatesExpanded = false;
        _candidateFallOffset = 0;
        _candidates.Clear();
        if (CandidateFallPolicy.RebuildHomeAfterCommit(wasExpanded)
            && (_hosting || IsVisible))
        {
            RefreshChrome();
        }
        else
        {
            RefreshCandidates();
        }

        if (ImeHost.Shared.HasClient)
        {
            ImeHost.Shared.Cancel();
        }
    }

    private void OnSpaceClicked(object sender, RoutedEventArgs e) => OnSpace();

    private void OnEnterClicked(object sender, RoutedEventArgs e)
    {
        if (ToolBarPolicy.BackspaceInsteadOfEnter(_numberPad, IsSymbolBoard, _candidatesExpanded))
        {
            OnBackspace();
            return;
        }

        OnEnter();
    }

    private void OnEnter()
    {
        var latin = EnterCommitPolicy.LatinText(
            CandidateFallPolicy.ComposingChinese(
                _board == Board.Pinyin,
                _board == Board.Full,
                _latin,
                _board == Board.Pinyin26),
            _letters,
            PreviewPinyin(),
            _candidates);
        if (!string.IsNullOrEmpty(latin))
        {
            SendText(latin);
            ResetComposition();
            return;
        }

        if (EnglishPredictOn && (_letters.Length > 0 || _candidates.Count > 0))
        {
            if (_candidates.Count > 0)
            {
                Commit(_candidates[0]);
                return;
            }

            SendText(_letters);
            ResetComposition();
            return;
        }

        if (_candidates.Count > 0)
        {
            Commit(_candidates[0]);
            return;
        }

        // 回车是命令。有 TSF 客户端时在目标进程里投递；否则发给当前编辑窗，
        // 避免 SendInput 打到九键面板自己（点虚拟键时面板可能短暂拿到队列）。
        if (ImeHost.Shared.SendReturn())
        {
            return;
        }

        TextOutput.SendVirtualKey(NativeMethods.VkReturn, _host);
    }

    private void OnSymbolTray()
    {
        if (IsSymbolBoard)
        {
            ShowBoard(_homeBoard);
        }
        else
        {
            _symbolFallOffset = 0;
            _categoryFallOffset = 0;
            _symbolCategory = SymbolCatalog.DefaultName(_english);
            ShowBoard(_english ? Board.SymbolEn : Board.SymbolCn);
        }

        ResetComposition();
    }

    private void OnCloseClicked(object sender, RoutedEventArgs e)
    {
        SetPinned(false);
        ForgetUserPlace();
        HideOverlay();
        UserClosed?.Invoke();
    }

    private void OnPinClicked(object sender, RoutedEventArgs e) => SetPinned(!IsPinned);

    public void SetPinned(bool pinned)
    {
        if (IsPinned == pinned)
        {
            return;
        }

        var wasPinned = IsPinned;
        IsPinned = pinned;
        if (KeyboardAnchorPolicy.ShouldClearDragAnchorOnUnlock(wasPinned, pinned))
        {
            ReleaseDragAnchor();
        }

        RefreshPinChrome();
        RequestPublishHost();
        Log.Info($"键盘锁定 {(pinned ? 1 : 0)}");
        PinChanged?.Invoke(pinned);
    }

    private void RefreshPinChrome()
    {
        if (PinButton is null)
        {
            return;
        }

        var on = IsPinned;
        PinButton.Background = on
            ? (Brush)FindResource("TabOnBrush")
            : System.Windows.Media.Brushes.Transparent;
        PinButton.Foreground = on
            ? (Brush)FindResource("AccentBrush")
            : (Brush)FindResource("MutedBrush");
        PinButton.ToolTip = on ? "解除锁定" : "锁定键盘，关闭前保持位置";
        PinButton.Content = KeyGlyphs.Lock(on, PinButton.Foreground);
    }

    private void OnPinyinTab(object sender, RoutedEventArgs e)
    {
        ShowBoard(Board.Pinyin);
        ResetComposition();
    }

    private void OnPinyin26Tab(object sender, RoutedEventArgs e)
    {
        ShowBoard(Board.Pinyin26);
        ResetComposition();
    }

    private void OnEnglishTab(object sender, RoutedEventArgs e)
    {
        ShowBoard(Board.English);
        ResetComposition();
    }

    private void SendText(string text) => EmitText(text);

    private void SendVirtual(ushort vk)
    {
        if (vk == NativeMethods.VkBack)
        {
            using var scope = Perf.Begin("key.backspace");
            if (ImeHost.Shared.Backspace())
            {
                return;
            }

            var surface = ProbeSystemSurface();
            if (SystemBackspacePolicy.ShouldUseUia(
                    surface.TextSurface,
                    surface.ProfileLease,
                    HasCapturedSystemTarget()))
            {
                // UIA 写入在专用线程上完成，失败时由它自己走键盘兜底。
                SystemBoxInput.TryBackspace(_host);
                return;
            }

            TextOutput.SendVirtualKey(vk, _host);
            return;
        }

        var nativeContextActive = ImeHost.Shared.CanCommitForeground();
        if (vk == NativeMethods.VkSpace)
        {
            if (nativeContextActive)
            {
                ImeHost.Shared.Commit(" ");
                return;
            }

            if (HasCapturedSystemTarget() && SystemBoxInput.TryInsert(" ", _host))
            {
                return;
            }

            var spaceSurface = ProbeSystemSurface();
            if (SystemFallbackPolicy.ShouldUse(
                    spaceSurface.TextSurface,
                    spaceSurface.ProfileLease,
                    nativeContextActive))
            {
                SystemBoxInput.TryInsert(" ", _host);
            }

            return;
        }

        TextOutput.SendVirtualKey(vk, _host);
    }

    private void EmitText(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        var nativeContextActive = ImeHost.Shared.CanCommitForeground();
        if (nativeContextActive)
        {
            ImeHost.Shared.Commit(text);
            return;
        }
        if (HasCapturedSystemTarget() && SystemBoxInput.TryInsert(text, _host))
        {
            return;
        }

        var surface = ProbeSystemSurface();
        if (SystemFallbackPolicy.ShouldUse(
                surface.TextSurface,
                surface.ProfileLease,
                nativeContextActive))
        {
            SystemBoxInput.TryInsert(text, _host);
        }
    }

    private void EmitCompose(string text)
    {
        // 官方搜索 IME 把组合写进文档。以前前台是 SearchHost/开始菜单就直接
        // return，HostRender 键会亮、搜索框却永远是空的。
        ImeHost.Shared.Compose(text);
    }

    /// <summary>
    /// 系统框输出前的两个判定原本各自做一次 UIA 焦点探测，一次按键要跑两遍跨进程
    /// 查询。这里合并成一次探测，并在极短的 TTL 内复用，让连打时不再重复付费。
    /// 只用于布尔门控，不参与定位，因此不会引入位置漂移。
    /// </summary>
    private readonly record struct SystemSurfaceProbe(
        bool TextSurface,
        bool ProfileLease);

    private const double SurfaceProbeTtlMs = 120;
    private static long _surfaceProbeTicks;
    private static SystemSurfaceProbe _surfaceProbe;

    private static SystemSurfaceProbe ProbeSystemSurface()
    {
        var now = System.Diagnostics.Stopwatch.GetTimestamp();
        if (_surfaceProbeTicks != 0)
        {
            var ageMs = (now - _surfaceProbeTicks)
                * 1000.0
                / System.Diagnostics.Stopwatch.Frequency;
            if (ageMs <= SurfaceProbeTtlMs)
            {
                return _surfaceProbe;
            }
        }

        using var scope = Perf.Begin("probe.systemSurface");
        var taskbarSearch = InputFieldProbe.TryGetFocusedTaskbarSearch(out _);
        _surfaceProbe = new SystemSurfaceProbe(
            taskbarSearch || ShellProcess.IsForegroundSystemTextHost(),
            taskbarSearch
                ? ImeHost.Shared.HasSystemProfileLease()
                : ImeHost.Shared.HasForegroundProfileLease());
        _surfaceProbeTicks = now;
        return _surfaceProbe;
    }

    private bool HasCapturedSystemTarget() =>
        IsVisible
        && ShellProcess.IsSystemTextSurface(_host)
        && SystemBoxInput.HasCapturedBox;

    private void OnFullTab(object sender, RoutedEventArgs e)
    {
        ShowBoard(Board.Full);
        ResetComposition();
    }

    private void OnModeClicked(object sender, RoutedEventArgs e)
    {
        if (IsSymbolBoard)
        {
            SetSymbolLock(!_symbolLock);
            RefreshChrome();
            return;
        }

        ShowBoard(FromSurface(BoardNavigation.LanguageOrHome(
            ToSurface(_board),
            ToSurface(_homeBoard))));
        ResetComposition();
    }

    private void OnDigitModeClicked(object sender, RoutedEventArgs e)
    {
        if (_board is Board.SymbolCn or Board.SymbolEn or Board.Number)
        {
            ShowBoard(FromSurface(BoardNavigation.BackFromTool(ToSurface(_homeBoard))));
            ResetComposition();
            return;
        }

        ShowBoard(Board.Number);
        ResetComposition();
    }

    private static KeyboardSurface ToSurface(Board board) =>
        board switch
        {
            Board.Pinyin => KeyboardSurface.Pinyin,
            Board.Pinyin26 => KeyboardSurface.Pinyin26,
            Board.English => KeyboardSurface.English,
            Board.Full => KeyboardSurface.Full,
            Board.Number => KeyboardSurface.Number,
            Board.SymbolCn => KeyboardSurface.SymbolCn,
            Board.SymbolEn => KeyboardSurface.SymbolEn,
            _ => KeyboardSurface.Pinyin
        };

    private static Board FromSurface(KeyboardSurface surface) =>
        surface switch
        {
            KeyboardSurface.Pinyin => Board.Pinyin,
            KeyboardSurface.Pinyin26 => Board.Pinyin26,
            KeyboardSurface.English => Board.English,
            KeyboardSurface.Full => Board.Full,
            KeyboardSurface.Number => Board.Number,
            KeyboardSurface.SymbolCn => Board.SymbolCn,
            KeyboardSurface.SymbolEn => Board.SymbolEn,
            _ => Board.Pinyin
        };

    protected override void OnPreviewKeyDown(System.Windows.Input.KeyEventArgs e)
    {
        e.Handled = true;
        base.OnPreviewKeyDown(e);
    }
}

internal sealed class FallScrollTarget
{
    public required ScrollViewer Viewer { get; init; }
    public required Func<double> Get { get; init; }
    public required Action<double> Set { get; init; }
    public required bool Horizontal { get; init; }
}
