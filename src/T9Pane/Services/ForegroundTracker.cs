using System.Windows.Automation;
using T9Pane.Native;

namespace T9Pane.Services;

internal static class FocusGenerationPolicy
{
    /// <summary>
    /// 只有前台窗口真的换了才换代。
    ///
    /// 代号会进 InputContextKey，定位那边靠上下文是否相同来决定「同一行就别动」
    /// 和「要不要收起重来」。焦点事件在打字时照样会来，Chromium 和 UWP 尤其密，
    /// 每来一次就换代的话上下文看着一直在变，键盘就跟着光标左右漂、甚至被当成
    /// 换了表面而重启。前台拿不到时按没换算，免得来回抖。
    /// </summary>
    public static bool ShouldAdvance(IntPtr current, IntPtr next) =>
        next != IntPtr.Zero && next != current;
}

internal sealed class ForegroundTracker : IDisposable
{
    private readonly HashSet<IntPtr> _ignored = [];
    private readonly Action<Action> _post;
    private readonly NativeMethods.WinEventDelegate _hookCallback;
    private readonly IntPtr _foregroundHook;
    private readonly IntPtr _focusHook;
    private readonly AutomationFocusChangedEventHandler _automationFocusCallback;
    private readonly TrailingEdgeGate _gate = new();
    private bool _automationFocusHooked;
    private long _generation;
    private IntPtr _generationTarget;

    public IntPtr LastTarget { get; private set; }
    public long Generation => Interlocked.Read(ref _generation);

    public IReadOnlyCollection<IntPtr> Ignored => _ignored;

    public event Action? Changed;

    public ForegroundTracker(Action<Action> post)
    {
        _post = post;
        _hookCallback = OnWinEvent;
        _automationFocusCallback = OnAutomationFocusChanged;
        _foregroundHook = NativeMethods.SetWinEventHook(
            NativeMethods.EventSystemForeground,
            NativeMethods.EventSystemForeground,
            IntPtr.Zero,
            _hookCallback,
            0,
            0,
            NativeMethods.WineventOutofcontext | NativeMethods.WineventSkipownprocess);
        _focusHook = NativeMethods.SetWinEventHook(
            NativeMethods.EventObjectFocus,
            NativeMethods.EventObjectFocus,
            IntPtr.Zero,
            _hookCallback,
            0,
            0,
            NativeMethods.WineventOutofcontext | NativeMethods.WineventSkipownprocess);
        try
        {
            Automation.AddAutomationFocusChangedEventHandler(_automationFocusCallback);
            _automationFocusHooked = true;
        }
        catch
        {
            _automationFocusHooked = false;
        }
    }

    public void Ignore(IntPtr hwnd)
    {
        if (hwnd != IntPtr.Zero)
        {
            _ignored.Add(hwnd);
        }
    }

    public void Remember(IntPtr hwnd)
    {
        if (hwnd != IntPtr.Zero && !_ignored.Contains(hwnd))
        {
            LastTarget = hwnd;
        }
    }

    private void OnWinEvent(IntPtr hook, uint type, IntPtr hwnd, int objectId, int childId, uint thread, uint time)
    {
        QueueChanged(hwnd);
    }

    /// <summary>
    /// 焦点已经落到明确不可输入的控件上。
    ///
    /// 这是官方焦点跟踪模型里的隐藏条件——焦点移到非文本控件就收起键盘。必须用
    /// 这个确定信号，不能靠"探测不到光标"去反推：Chromium 为了无障碍在失焦后仍会
    /// 交出光标，反推法在 Cursor 里就表现为键盘赖在原地不走。
    ///
    /// 只在焦点事件里判定，不轮询——事件本身就是官方给的通知。
    /// </summary>
    public bool FocusLeftTextInput { get; private set; }

    /// <summary>
    /// 焦点刚进输入框。平板上用来补一次弹出授权；指针已经判定过则清掉，
    /// 避免点了列表还按旧输入框的焦点把键盘拉起来。
    /// </summary>
    public bool FocusEnteredTextInput { get; private set; }

    /// <summary>
    /// 焦点落到整页 Document/Pane，不是矮输入框。
    /// </summary>
    public bool FocusOnPageSurface { get; private set; }

    /// <summary>
    /// 这次离开是选区手柄/选区菜单，不是按钮或页面。
    /// </summary>
    public bool FocusLeftIsSelectionChrome { get; private set; }

    /// <summary>焦点在右键/长按弹出的菜单上。</summary>
    public bool FocusIsContextMenu { get; private set; }

    /// <summary>
    /// 最近一次 UIA 焦点元素的屏幕外框。用来区分「地址栏边上的按钮」和「侧栏列表」。
    /// </summary>
    public NativeRect LastFocusBounds { get; private set; }

    public void ClearFocusLeft()
    {
        FocusLeftTextInput = false;
        FocusOnPageSurface = false;
        FocusLeftIsSelectionChrome = false;
        FocusIsContextMenu = false;
    }

    public void ClearFocusEntered() => FocusEnteredTextInput = false;

    private void OnAutomationFocusChanged(object sender, AutomationFocusChangedEventArgs args)
    {
        var hwnd = IntPtr.Zero;
        try
        {
            if (sender is AutomationElement element)
            {
                if (TrayFocusPolicy.IgnoreOwnProcess(
                        unchecked((uint)element.Current.ProcessId),
                        unchecked((uint)Environment.ProcessId)))
                {
                    return;
                }

                hwnd = new IntPtr(element.Current.NativeWindowHandle);
                // 事件送来的元素是权威的，必须留下：随后定位若去查
                // AutomationElement.FocusedElement，拿到的会是尚未更新的旧元素。
                FocusedFieldCache.Note(element);
                NoteFocusKind(element);
            }
        }
        catch
        {
            // The UWP provider may disappear while focus is moving.
        }

        QueueChanged(hwnd);
    }

    private void NoteFocusKind(AutomationElement element)
    {
        try
        {
            // 自己键盘上的按键也是 Button。把它当成"用户点了别处"就会自己把自己收起来。
            if (element.Current.ProcessId == Environment.ProcessId)
            {
                return;
            }

            var type = element.Current.ControlType;
            var box = element.Current.BoundingRectangle;
            if (!box.IsEmpty)
            {
                LastFocusBounds = new NativeRect
                {
                    Left = (int)Math.Floor(box.Left),
                    Top = (int)Math.Floor(box.Top),
                    Right = (int)Math.Ceiling(box.Right),
                    Bottom = (int)Math.Ceiling(box.Bottom)
                };
            }

            if (InputInvocationProbe.IsTextField(type)
                || InputInvocationProbe.IsConsoleOrTerminal(element))
            {
                FocusLeftTextInput = false;
                FocusOnPageSurface = false;
                FocusLeftIsSelectionChrome = false;
                FocusIsContextMenu = false;
                FocusEnteredTextInput = true;
                return;
            }

            var focusable = element.Current.IsKeyboardFocusable
                || element.Current.HasKeyboardFocus;
            if (InputInvocationProbe.IsCompactEditable(
                    type,
                    box.Width,
                    box.Height,
                    focusable))
            {
                FocusLeftTextInput = false;
                FocusOnPageSurface = false;
                FocusLeftIsSelectionChrome = false;
                FocusIsContextMenu = false;
                FocusEnteredTextInput = true;
                return;
            }

            if (InputInvocationProbe.IsSelectionChrome(type))
            {
                FocusLeftIsSelectionChrome = true;
                FocusIsContextMenu = InputInvocationProbe.IsContextMenu(type);
                FocusOnPageSurface = false;
                FocusLeftTextInput = false;
                return;
            }

            if (InputInvocationProbe.SignalsLeftPageSurface(
                    type,
                    box.Width,
                    box.Height,
                    focusable))
            {
                // 整页 Document 说明不了离开。Chromium 打字时焦点会漂到页面，
                // 当成 FocusLeft 会每拍收起再授权，热路径被 UIA 拖死。
                // 但「刚进过输入框」必须清掉，否则点空白后 armed 永远被挡着。
                FocusOnPageSurface = true;
                FocusLeftIsSelectionChrome = false;
                FocusIsContextMenu = false;
                FocusEnteredTextInput = false;
                return;
            }

            // 按钮、链接是确定离开。选区手柄已在上面单独记下。
            if (InputInvocationProbe.SignalsLeftTextInput(type))
            {
                FocusLeftTextInput = true;
                FocusOnPageSurface = false;
                FocusLeftIsSelectionChrome = false;
                FocusIsContextMenu = false;
                FocusEnteredTextInput = false;
            }
        }
        catch
        {
            // 提供方可能在焦点移动中消失，保持原判。
        }
    }

    private void QueueChanged(IntPtr hwnd)
    {
        var top = NativeMethods.GetAncestor(hwnd == IntPtr.Zero ? NativeMethods.GetForegroundWindow() : hwnd, NativeMethods.GaRoot);
        if (top != IntPtr.Zero && _ignored.Contains(top))
        {
            return;
        }

        if (top != IntPtr.Zero)
        {
            NativeMethods.GetWindowThreadProcessId(top, out var pid);
            if (TrayFocusPolicy.IgnoreOwnProcess(pid, unchecked((uint)Environment.ProcessId)))
            {
                return;
            }
        }

        if (_gate.TryEnter())
        {
            _post(Run);
        }
    }

    private void Run()
    {
        var fg = NativeMethods.GetForegroundWindow();
        var fgTop = NativeMethods.GetAncestor(fg, NativeMethods.GaRoot);
        if (fgTop != IntPtr.Zero && !_ignored.Contains(fgTop))
        {
            LastTarget = fgTop;
        }

        if (FocusGenerationPolicy.ShouldAdvance(_generationTarget, fgTop))
        {
            _generationTarget = fgTop;
            Interlocked.Increment(ref _generation);
        }

        Changed?.Invoke();

        if (_gate.ShouldRerun())
        {
            _post(Run);
        }
    }

    public void Dispose()
    {
        if (_automationFocusHooked)
        {
            Automation.RemoveAutomationFocusChangedEventHandler(_automationFocusCallback);
            _automationFocusHooked = false;
        }

        if (_foregroundHook != IntPtr.Zero)
        {
            NativeMethods.UnhookWinEvent(_foregroundHook);
        }

        if (_focusHook != IntPtr.Zero)
        {
            NativeMethods.UnhookWinEvent(_focusHook);
        }
    }
}
