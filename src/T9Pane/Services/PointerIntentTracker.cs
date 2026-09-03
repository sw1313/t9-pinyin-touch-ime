using System.Runtime.InteropServices;
using System.Windows.Automation;
using System.Diagnostics;
using T9Pane.Native;

namespace T9Pane.Services;

internal enum PointerInvocationOrigin
{
    Unknown,
    TaskbarStart,
    TaskbarSearch,
    StartMenuSurface,
    StartMenuSearch
}

internal readonly record struct ShellInvocationTarget(
    PointerInvocationOrigin Origin,
    NativeRect Bounds);

internal static class PointerIntentTrackingPolicy
{
    public static bool ShouldEnable(
        bool canCommitForeground,
        bool hasForegroundProfileLease,
        bool hasObservedActiveProfile) =>
        canCommitForeground
        || hasForegroundProfileLease
        || hasObservedActiveProfile;

    public static bool IsKeyboardWindow(string className) =>
        className.Equals("T9Ime.BandHost", StringComparison.Ordinal);

    public static PointerInvocationOrigin ClassifyShellPoint(
        IReadOnlyList<ShellInvocationTarget> targets,
        int x,
        int y)
    {
        foreach (var target in targets)
        {
            if (InputInvocationProbe.Contains(target.Bounds, x, y))
            {
                return target.Origin;
            }
        }
        return PointerInvocationOrigin.Unknown;
    }
}

internal sealed class PointerIntentTracker : IDisposable
{
    private const uint WmEnable = 0x8000 + 31;
    private const uint WmDisable = 0x8000 + 32;
    private const uint WmRefreshShell = 0x8000 + 33;
    private readonly Action<Action> _post;
    private readonly NativeMethods.LowLevelMouseDelegate _callback;
    private readonly ManualResetEventSlim _ready = new();
    private readonly Thread _thread;
    private IntPtr _hook;
    private uint _threadId;
    private int _enabled;
    private int _clickSequence;
    private int _shellRefreshRunning;
    private int _shellRefreshRequested;
    private ShellInvocationTarget[] _shellTargets = [];
    private volatile bool _disposed;

    public event Action<int, int, IntPtr, uint, PointerInvocationOrigin>? PointerDown;

    public PointerIntentTracker(Action<Action> post)
    {
        _post = post;
        _callback = HookProc;
        _thread = new Thread(Run)
        {
            IsBackground = true,
            Name = "T9 pointer intent"
        };
        _thread.SetApartmentState(ApartmentState.MTA);
        _thread.Start();
        _ready.Wait();
    }

    public void SetEnabled(bool enabled)
    {
        if (_disposed
            || Interlocked.Exchange(ref _enabled, enabled ? 1 : 0) == (enabled ? 1 : 0))
        {
            return;
        }
        if (!enabled)
        {
            Interlocked.Increment(ref _clickSequence);
        }

        if (!NativeMethods.PostThreadMessage(
                _threadId,
                enabled ? WmEnable : WmDisable,
                IntPtr.Zero,
                IntPtr.Zero))
        {
            Log.Warn($"指针意图线程通知失败 err={Marshal.GetLastWin32Error()}");
        }
    }

    public void RefreshShellTargets()
    {
        if (!_disposed)
        {
            NativeMethods.PostThreadMessage(
                _threadId,
                WmRefreshShell,
                IntPtr.Zero,
                IntPtr.Zero);
        }
    }

    private void Run()
    {
        _threadId = NativeMethods.GetCurrentThreadId();
        _ = NativeMethods.PeekMessage(
            out _,
            IntPtr.Zero,
            0,
            0,
            0);
        _ready.Set();

        while (NativeMethods.GetMessage(out var message, IntPtr.Zero, 0, 0) > 0)
        {
            if (message.Message == WmEnable)
            {
                Install();
            }
            else if (message.Message == WmDisable)
            {
                Uninstall();
            }
            else if (message.Message == WmRefreshShell)
            {
                QueueShellTargetRefresh();
            }
        }

        Uninstall();
    }

    private void Install()
    {
        if (_hook != IntPtr.Zero)
        {
            return;
        }

        RefreshShellTargetCache();
        _hook = NativeMethods.SetWindowsHookEx(
            NativeMethods.WhMouseLowLevel,
            _callback,
            NativeMethods.GetModuleHandle(null),
            0);
        if (_hook == IntPtr.Zero)
        {
            Log.Warn($"全局指针意图钩子安装失败 err={Marshal.GetLastWin32Error()}");
        }
        else
        {
            Log.Info("全局指针意图钩子已启用");
        }
    }

    private void Uninstall()
    {
        if (_hook == IntPtr.Zero)
        {
            return;
        }

        NativeMethods.UnhookWindowsHookEx(_hook);
        _hook = IntPtr.Zero;
        Log.Info("全局指针意图钩子已停用");
    }

    private IntPtr HookProc(int code, IntPtr wParam, IntPtr lParam)
    {
        if (code >= 0
            && wParam.ToInt64() == NativeMethods.WmLeftButtonDown
            && Volatile.Read(ref _enabled) != 0)
        {
            var data = Marshal.PtrToStructure<LowLevelMouseHookData>(lParam);
            var target = NativeMethods.WindowFromPoint(data.Point);
            var origin = PointerIntentTrackingPolicy.ClassifyShellPoint(
                _shellTargets,
                data.Point.X,
                data.Point.Y);
            NativeMethods.GetWindowThreadProcessId(target, out var targetPid);
            if (targetPid != Environment.ProcessId)
            {
                if (origin == PointerInvocationOrigin.TaskbarStart)
                {
                    _ = Task.Delay(120).ContinueWith(
                        _ => RefreshShellTargets(),
                        CancellationToken.None,
                        TaskContinuationOptions.None,
                        TaskScheduler.Default);
                }
                var sequence = Interlocked.Increment(ref _clickSequence);
                _ = Task.Delay(40).ContinueWith(
                    _ =>
                    {
                        if (!_disposed && sequence == Volatile.Read(ref _clickSequence))
                        {
                            _post(() => PointerDown?.Invoke(
                                data.Point.X,
                                data.Point.Y,
                                target,
                                targetPid,
                                origin));
                        }
                    },
                    CancellationToken.None,
                    TaskContinuationOptions.None,
                    TaskScheduler.Default);
            }
        }

        return NativeMethods.CallNextHookEx(_hook, code, wParam, lParam);
    }

    private void RefreshShellTargetCache()
    {
        var targets = new List<ShellInvocationTarget>();
        try
        {
            NativeMethods.EnumWindows((hwnd, _) =>
            {
                var className = NativeMethods.GetWindowClass(hwnd);
                if (className is not ("Shell_TrayWnd" or "Shell_SecondaryTrayWnd"))
                {
                    return true;
                }

                var root = AutomationElement.FromHandle(hwnd);
                var condition = new OrCondition(
                    new PropertyCondition(
                        AutomationElement.AutomationIdProperty,
                        "StartButton"),
                    new PropertyCondition(
                        AutomationElement.AutomationIdProperty,
                        "SearchButton"),
                    new PropertyCondition(
                        AutomationElement.AutomationIdProperty,
                        "SearchBox"));
                var elements = root.FindAll(TreeScope.Descendants, condition);
                foreach (AutomationElement element in elements)
                {
                    if (!element.Current.IsEnabled || element.Current.IsOffscreen)
                    {
                        continue;
                    }

                    var bounds = element.Current.BoundingRectangle;
                    var rect = new NativeRect
                    {
                        Left = (int)Math.Floor(bounds.Left),
                        Top = (int)Math.Floor(bounds.Top),
                        Right = (int)Math.Ceiling(bounds.Right),
                        Bottom = (int)Math.Ceiling(bounds.Bottom)
                    };
                    if (rect.IsEmpty)
                    {
                        continue;
                    }

                    var origin = element.Current.AutomationId == "StartButton"
                        ? PointerInvocationOrigin.TaskbarStart
                        : PointerInvocationOrigin.TaskbarSearch;
                    targets.Add(new ShellInvocationTarget(origin, rect));
                }
                return true;
            }, IntPtr.Zero);

            foreach (var process in Process.GetProcessesByName(
                         "StartMenuExperienceHost"))
            {
                using (process)
                {
                    var processCondition = new PropertyCondition(
                        AutomationElement.ProcessIdProperty,
                        process.Id);
                    var windows = AutomationElement.RootElement.FindAll(
                        TreeScope.Children,
                        processCondition);
                    foreach (AutomationElement window in windows)
                    {
                        var elements = window.FindAll(
                            TreeScope.Descendants,
                            Condition.TrueCondition);
                        foreach (AutomationElement element in elements)
                        {
                            var id = element.Current.AutomationId;
                            var className = element.Current.ClassName;
                            var isSearchEntry =
                                id.Equals(
                                    "SearchBox",
                                    StringComparison.OrdinalIgnoreCase)
                                || id.Equals(
                                    "SearchBoxControl",
                                    StringComparison.OrdinalIgnoreCase)
                                || className.Contains(
                                    "SearchBoxToggleButton",
                                    StringComparison.OrdinalIgnoreCase);
                            if (!isSearchEntry
                                || !element.Current.IsEnabled
                                || element.Current.IsOffscreen)
                            {
                                continue;
                            }

                            var bounds = element.Current.BoundingRectangle;
                            var rect = new NativeRect
                            {
                                Left = (int)Math.Floor(bounds.Left),
                                Top = (int)Math.Floor(bounds.Top),
                                Right = (int)Math.Ceiling(bounds.Right),
                                Bottom = (int)Math.Ceiling(bounds.Bottom)
                            };
                            if (!rect.IsEmpty)
                            {
                                targets.Add(new ShellInvocationTarget(
                                    PointerInvocationOrigin.StartMenuSearch,
                                    rect));
                            }
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Log.Warn($"任务栏输入入口缓存失败: {ex.Message}");
        }

        Volatile.Write(ref _shellTargets, [.. targets]);
    }

    private void QueueShellTargetRefresh()
    {
        Volatile.Write(ref _shellRefreshRequested, 1);
        if (Interlocked.CompareExchange(
                ref _shellRefreshRunning,
                1,
                0) != 0)
        {
            return;
        }

        _ = Task.Run(() =>
        {
            try
            {
                do
                {
                    Interlocked.Exchange(ref _shellRefreshRequested, 0);
                    RefreshShellTargetCache();
                }
                while (!_disposed
                    && Volatile.Read(ref _shellRefreshRequested) != 0);
            }
            finally
            {
                Volatile.Write(ref _shellRefreshRunning, 0);
                if (!_disposed
                    && Volatile.Read(ref _shellRefreshRequested) != 0)
                {
                    QueueShellTargetRefresh();
                }
            }
        });
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Interlocked.Increment(ref _clickSequence);
        NativeMethods.PostThreadMessage(
            _threadId,
            NativeMethods.WmQuit,
            IntPtr.Zero,
            IntPtr.Zero);
        _thread.Join(TimeSpan.FromSeconds(2));
        _ready.Dispose();
    }
}
