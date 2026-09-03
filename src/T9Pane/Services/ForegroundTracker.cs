using System.Windows.Automation;
using T9Pane.Native;

namespace T9Pane.Services;

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

    public void ClearFocusLeft() => FocusLeftTextInput = false;

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
            if (InputInvocationProbe.IsTextField(type))
            {
                FocusLeftTextInput = false;
                return;
            }

            // 容器(Chromium 的 Document/Pane)里可能就装着输入框，说明不了问题，
            // 保持原判。只有按钮一类可操作控件才是确定的"不是要输入"。
            if (InputInvocationProbe.SignalsLeftTextInput(type))
            {
                FocusLeftTextInput = true;
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

        Interlocked.Increment(ref _generation);
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
