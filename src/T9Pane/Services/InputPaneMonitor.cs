using T9Pane.Native;

namespace T9Pane.Services;

internal sealed class InputPaneSnapshot
{
    public bool Visible { get; init; }
    public NativeRect Rect { get; init; }
    public IntPtr Hwnd { get; init; }
    public string Source { get; init; } = "";
}

internal sealed class InputPaneMonitor : IDisposable
{
    private readonly System.Windows.Threading.DispatcherTimer _timer;
    private readonly SipWindowLocator _locator = new();
    private int _emptyTicks;

    public event Action<InputPaneSnapshot>? Changed;

    public InputPaneSnapshot Current { get; private set; } = new();

    public InputPaneMonitor()
    {
        _timer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(80)
        };
        _timer.Tick += (_, _) => Poll();
    }

    public void Start() => _timer.Start();

    public void Stop() => _timer.Stop();

    public void PollNow() => Poll();

    private void Poll()
    {
        var snapshot = Capture();
        if (!snapshot.Visible)
        {
            _emptyTicks++;
            if (_emptyTicks < 3 && Current.Visible)
            {
                return;
            }

            snapshot = new InputPaneSnapshot();
        }
        else
        {
            _emptyTicks = 0;
        }

        if (Same(Current, snapshot))
        {
            return;
        }

        Current = snapshot;
        Changed?.Invoke(snapshot);
    }

    private InputPaneSnapshot Capture()
    {
        // 贴底停靠：官方 InputPane 最准。
        if (InputPaneInterop.TryGetLocation(out var pane) && SipWindowLocator.LooksLikeTouchKeyboard(pane))
        {
            _locator.TryFindTarget(pane, out var target);
            return new InputPaneSnapshot
            {
                Visible = true,
                Rect = pane,
                Hwnd = target.Hwnd != IntPtr.Zero ? target.Hwnd : _locator.FindHwndNear(pane),
                Source = "IFrameworkInputPane"
            };
        }

        // 桌面/浮动小键盘：InputPane 经常是空的，必须跟 TextInputHost 的实际窗口。
        if (_locator.TryFindKeyboard(null, out var hwnd, out var found))
        {
            return new InputPaneSnapshot
            {
                Visible = true,
                Rect = found,
                Hwnd = hwnd,
                Source = "Win11TouchKeyboard"
            };
        }

        return new InputPaneSnapshot();
    }

    private static bool Same(InputPaneSnapshot a, InputPaneSnapshot b)
    {
        return a.Visible == b.Visible
               && a.Hwnd == b.Hwnd
               && a.Rect.Left == b.Rect.Left
               && a.Rect.Top == b.Rect.Top
               && a.Rect.Right == b.Rect.Right
               && a.Rect.Bottom == b.Rect.Bottom;
    }

    public void Dispose() => _timer.Stop();
}
