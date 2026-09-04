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
        bool hasObservedActiveProfile)
    {
        _ = canCommitForeground;
        _ = hasForegroundProfileLease;
        return hasObservedActiveProfile;
    }

    /// <summary>
    /// 只跟语言栏当前键盘。T9 时钩子必须先挂上，否则第一次点进框会错过。
    /// 切到微软拼音后必须立刻卸钩，不能因为后台还留着 T9 客户端就继续拦官方键盘。
    /// </summary>
    public static bool ShouldEnableForSession(
        bool canCommitForeground,
        bool hasForegroundProfileLease,
        bool hasSystemProfileLease,
        bool officialT9Selected)
    {
        _ = canCommitForeground;
        _ = hasForegroundProfileLease;
        _ = hasSystemProfileLease;
        return officialT9Selected;
    }

    public static bool IsKeyboardWindow(string className) =>
        className.Equals("T9Ime.BandHost", StringComparison.Ordinal);

    public static bool IsOverlayContact(bool keyboardWindow, bool hostPointerLive) =>
        keyboardWindow || hostPointerLive;

    /// <summary>
    /// 官方 SIP 窗：宿主位图在目标进程，WPF 盘面在我们自己的进程。
    /// 点在这两处都不是应用内容区的右键菜单。
    /// </summary>
    public static bool IsSipWindow(string className, uint windowPid, uint ourPid) =>
        IsKeyboardWindow(className) || (windowPid != 0 && windowPid == ourPid);

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

    /// <summary>
    /// 升格触摸和 HID 原始输入会各报一次同一下落点。短距离去重，避免
    /// 一次点按跑两轮全量 UIA。
    /// </summary>
    public static bool IsDuplicateDown(
        int x,
        int y,
        int lastX,
        int lastY,
        long nowTicks,
        long lastTicks,
        int windowMs = 50,
        int slopPx = 32)
    {
        if (lastTicks == 0)
        {
            return false;
        }

        var elapsedMs = (nowTicks - lastTicks) / TimeSpan.TicksPerMillisecond;
        if (elapsedMs < 0 || elapsedMs > windowMs)
        {
            return false;
        }

        var dx = x - lastX;
        var dy = y - lastY;
        return dx * dx + dy * dy <= slopPx * slopPx;
    }

    /// <summary>
    /// 手指按住时 digitizer 会连续灌 WM_INPUT。间隔超过这一档才算新的点按。
    /// </summary>
    public static bool IsNewContactBurst(long nowTicks, long lastHidTicks, int gapMs = 80) =>
        lastHidTicks == 0
        || (nowTicks - lastHidTicks) / TimeSpan.TicksPerMillisecond >= gapMs;
}

internal sealed class PointerIntentTracker : IDisposable
{
    private const uint WmEnable = 0x8000 + 31;
    private const uint WmDisable = 0x8000 + 32;
    private const uint WmRefreshShell = 0x8000 + 33;
    private const string SinkClassName = "T9.PointerSink";
    private readonly Action<Action> _post;
    private readonly NativeMethods.LowLevelMouseDelegate _callback;
    private readonly NativeMethods.WndProcFn _wndProc;
    private readonly ManualResetEventSlim _ready = new();
    private readonly Thread _thread;
    private IntPtr _hook;
    private IntPtr _sinkWnd;
    private bool _rawInput;
    private uint _threadId;
    private int _enabled;
    private int _clickSequence;
    private int _shellRefreshRunning;
    private int _shellRefreshRequested;
    private ShellInvocationTarget[] _shellTargets = [];
    private volatile bool _disposed;
    private long _lastEmitTicks;
    private int _lastEmitX;
    private int _lastEmitY;
    private long _lastHidTicks;

    public event Action<int, int, IntPtr, uint, PointerInvocationOrigin>? PointerDown;
    public event Action? PointerUp;
    public event Action? ContextMenu;
    public event Action? TouchContact;

    public PointerIntentTracker(Action<Action> post)
    {
        _post = post;
        _callback = HookProc;
        _wndProc = SinkWndProc;
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
            else if (message.Message == NativeMethods.WmInput)
            {
                OnRawTouch();
            }
            else
            {
                NativeMethods.DispatchMessage(ref message);
            }
        }

        Uninstall();
        DestroySinkWindow();
    }

    private void Install()
    {
        if (_hook != IntPtr.Zero)
        {
            return;
        }

        RefreshShellTargetCache();
        EnsureSinkWindow();
        RegisterTouchRawInput();
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
        UnregisterTouchRawInput();
        Log.Info("全局指针意图钩子已停用");
    }

    private IntPtr HookProc(int code, IntPtr wParam, IntPtr lParam)
    {
        if (code >= 0 && Volatile.Read(ref _enabled) != 0)
        {
            var message = wParam.ToInt64();
            if (message == NativeMethods.WmLeftButtonDown)
            {
                var data = Marshal.PtrToStructure<LowLevelMouseHookData>(lParam);
                if (IsOverlayPointer(data.ExtraInfo))
                {
                    return NativeMethods.CallNextHookEx(_hook, code, wParam, lParam);
                }

                if (TouchInvocationPolicy.IsPromotedTouch(data.ExtraInfo.ToUInt64()))
                {
                    // 升格触摸的坐标是注入的鼠标位置。这台平板触摸不移动指针，
                    // 那个点是旧光标，不能拿去套输入框。
                    EmitTouchContact();
                }
                else
                {
                    EmitDown(data.Point.X, data.Point.Y);
                }
            }
            else if (message == NativeMethods.WmLeftButtonUp)
            {
                EmitPointerUp();
            }
            else if (message == NativeMethods.WmRightButtonDown)
            {
                var data = Marshal.PtrToStructure<LowLevelMouseHookData>(lParam);
                if (!IsOverlayPointer(data.ExtraInfo))
                {
                    EmitContextMenu();
                }
            }
        }

        return NativeMethods.CallNextHookEx(_hook, code, wParam, lParam);
    }

    private IntPtr SinkWndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam) =>
        NativeMethods.DefWindowProc(hWnd, msg, wParam, lParam);

    private void OnRawTouch()
    {
        if (Volatile.Read(ref _enabled) == 0 || ImeHost.Shared.HostPointerLive)
        {
            return;
        }

        var now = DateTime.UtcNow.Ticks;
        if (!PointerIntentTrackingPolicy.IsNewContactBurst(now, _lastHidTicks))
        {
            _lastHidTicks = now;
            return;
        }

        _lastHidTicks = now;
        EmitTouchContact();
    }

    private static bool IsOverlayPointer(UIntPtr extraInfo)
    {
        if (ImeHost.Shared.HostPointerLive)
        {
            return true;
        }

        var extra = extraInfo.ToUInt64();
        var pointerId = TouchInvocationPolicy.PromotedPointerId(extra);
        if (pointerId == 0 || !NativeMethods.GetPointerInfo(pointerId, out var info))
        {
            return false;
        }

        NativeMethods.GetWindowThreadProcessId(info.HwndTarget, out var windowPid);
        return PointerIntentTrackingPolicy.IsSipWindow(
            NativeMethods.GetWindowClass(info.HwndTarget),
            windowPid,
            (uint)Environment.ProcessId);
    }

    private void EmitTouchContact()
    {
        var now = DateTime.UtcNow.Ticks;
        if (PointerIntentTrackingPolicy.IsDuplicateDown(
                0,
                0,
                _lastEmitX,
                _lastEmitY,
                now,
                _lastEmitTicks,
                windowMs: 200,
                slopPx: 0))
        {
            return;
        }

        _lastEmitTicks = now;
        _lastEmitX = 0;
        _lastEmitY = 0;
        var sequence = Interlocked.Increment(ref _clickSequence);
        _post(() =>
        {
            if (_disposed || sequence != Volatile.Read(ref _clickSequence))
            {
                return;
            }

            TouchContact?.Invoke();
        });
    }

    private void EmitDown(int x, int y)
    {
        var now = DateTime.UtcNow.Ticks;
        if (PointerIntentTrackingPolicy.IsDuplicateDown(
                x,
                y,
                _lastEmitX,
                _lastEmitY,
                now,
                _lastEmitTicks))
        {
            return;
        }

        _lastEmitTicks = now;
        _lastEmitX = x;
        _lastEmitY = y;
        var point = new NativePoint { X = x, Y = y };
        var target = NativeMethods.WindowFromPoint(point);
        var origin = PointerIntentTrackingPolicy.ClassifyShellPoint(_shellTargets, x, y);
        NativeMethods.GetWindowThreadProcessId(target, out var targetPid);
        if (targetPid == Environment.ProcessId)
        {
            return;
        }

        if (origin == PointerInvocationOrigin.TaskbarStart)
        {
            _ = Task.Delay(120).ContinueWith(
                _ => RefreshShellTargets(),
                CancellationToken.None,
                TaskContinuationOptions.None,
                TaskScheduler.Default);
        }

        var sequence = Interlocked.Increment(ref _clickSequence);
        _post(() =>
        {
            if (_disposed || sequence != Volatile.Read(ref _clickSequence))
            {
                return;
            }

            PointerDown?.Invoke(x, y, target, targetPid, origin);
        });
        Log.Info($"触摸点按 origin={origin} x={x} y={y}");
    }

    private void EmitPointerUp()
    {
        _post(() =>
        {
            if (_disposed)
            {
                return;
            }

            PointerUp?.Invoke();
        });
    }

    private void EmitContextMenu()
    {
        _post(() =>
        {
            if (_disposed)
            {
                return;
            }

            ContextMenu?.Invoke();
        });
    }

    private void EnsureSinkWindow()
    {
        if (_sinkWnd != IntPtr.Zero)
        {
            return;
        }

        var instance = NativeMethods.GetModuleHandle(null);
        var cls = new WndClassEx
        {
            Size = Marshal.SizeOf<WndClassEx>(),
            WndProc = _wndProc,
            Instance = instance,
            ClassName = SinkClassName
        };
        if (NativeMethods.RegisterClassEx(ref cls) == 0)
        {
            var err = Marshal.GetLastWin32Error();
            if (err != 1410)
            {
                Log.Warn($"触摸接收窗口注册失败 err={err}");
                return;
            }
        }

        _sinkWnd = NativeMethods.CreateWindowEx(
            0,
            SinkClassName,
            "T9 pointer sink",
            0,
            0,
            0,
            0,
            0,
            NativeMethods.HwndMessage,
            IntPtr.Zero,
            instance,
            IntPtr.Zero);
        if (_sinkWnd == IntPtr.Zero)
        {
            Log.Warn($"触摸接收窗口创建失败 err={Marshal.GetLastWin32Error()}");
        }
    }

    private void DestroySinkWindow()
    {
        if (_sinkWnd == IntPtr.Zero)
        {
            return;
        }

        NativeMethods.DestroyWindow(_sinkWnd);
        _sinkWnd = IntPtr.Zero;
    }

    private void RegisterTouchRawInput()
    {
        if (_rawInput || _sinkWnd == IntPtr.Zero)
        {
            return;
        }

        RawInputDevice[] devices =
        [
            Device(NativeMethods.HidUsageTouchScreen),
            Device(NativeMethods.HidUsageTouchPad),
            Device(NativeMethods.HidUsageDigitizer)
        ];
        if (!NativeMethods.RegisterRawInputDevices(
                devices,
                (uint)devices.Length,
                (uint)Marshal.SizeOf<RawInputDevice>()))
        {
            devices = [Device(NativeMethods.HidUsageTouchScreen)];
            if (!NativeMethods.RegisterRawInputDevices(
                    devices,
                    1,
                    (uint)Marshal.SizeOf<RawInputDevice>()))
            {
                Log.Warn($"触摸原始输入注册失败 err={Marshal.GetLastWin32Error()}");
                return;
            }
        }

        _rawInput = true;
        Log.Info("触摸原始输入已启用");
    }

    private RawInputDevice Device(ushort usage) =>
        new()
        {
            UsagePage = NativeMethods.HidPageDigitizer,
            Usage = usage,
            Flags = NativeMethods.RidevInputSink,
            Target = _sinkWnd
        };

    private void UnregisterTouchRawInput()
    {
        if (!_rawInput || _sinkWnd == IntPtr.Zero)
        {
            return;
        }

        RawInputDevice[] devices =
        [
            Remove(NativeMethods.HidUsageTouchScreen),
            Remove(NativeMethods.HidUsageTouchPad),
            Remove(NativeMethods.HidUsageDigitizer)
        ];
        _ = NativeMethods.RegisterRawInputDevices(
            devices,
            (uint)devices.Length,
            (uint)Marshal.SizeOf<RawInputDevice>());
        _rawInput = false;
    }

    private static RawInputDevice Remove(ushort usage) =>
        new()
        {
            UsagePage = NativeMethods.HidPageDigitizer,
            Usage = usage,
            Flags = NativeMethods.RidevRemove,
            Target = IntPtr.Zero
        };

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
