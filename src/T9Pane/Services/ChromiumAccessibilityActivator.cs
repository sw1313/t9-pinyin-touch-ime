using T9Pane.Native;

namespace T9Pane.Services;

/// <summary>
/// 向 Chromium/Electron 宿主声明“有辅助技术在运行”，让它开启完整的无障碍支持。
///
/// Chromium 出于性能考虑默认不建立无障碍树，官方文档（accessibility/overview.md、
/// design-documents/accessibility）写明它靠一个“蜜罐”握手来探测 AT 客户端：
/// 宿主发出 EVENT_SYSTEM_ALERT 并携带自定义 object id 1，若随后收到针对该 id 的
/// WM_GETOBJECT 查询，就认定有 AT 在运行并开启完整支持。
///
/// 不做这个握手时无障碍树只会被机会性地、部分地建起来，UIA 读取因此时好时坏：
/// 同一个输入框会在“读到真实光标 / 只读到外框 / 完全读不到”三种结果间反复跳，
/// 表现就是键盘有概率不弹、有概率定位到错误位置。
///
/// 握手走两条路：
/// 一是订阅 EVENT_SYSTEM_ALERT，宿主新建渲染窗口时按官方约定应答；
/// 二是在 Chromium 系窗口进入前台时主动补发一次——本进程晚于宿主启动时
/// 那条 alert 早已发过，只等事件会永远等不到。
/// </summary>
internal sealed class ChromiumAccessibilityActivator : IDisposable
{
    /// <summary>承载渲染内容并应答蜜罐查询的窗口类（LegacyRenderWidgetHostHW）。</summary>
    private const string RenderWidgetClass = "Chrome_RenderWidgetHostHWND";

    /// <summary>Chromium 系顶层窗口类前缀，Electron 应用同样使用。</summary>
    private const string WidgetClassPrefix = "Chrome_WidgetWin_";

    private readonly NativeMethods.WinEventDelegate _alertCallback;
    private readonly NativeMethods.EnumWindowsProc _childCallback;
    private readonly IntPtr _alertHook;
    private readonly HashSet<IntPtr> _answered = [];
    private readonly object _gate = new();

    public ChromiumAccessibilityActivator()
    {
        _alertCallback = OnAlert;
        _childCallback = OnChild;
        _alertHook = NativeMethods.SetWinEventHook(
            NativeMethods.EventSystemAlert,
            NativeMethods.EventSystemAlert,
            IntPtr.Zero,
            _alertCallback,
            0,
            0,
            NativeMethods.WineventOutofcontext | NativeMethods.WineventSkipownprocess);
    }

    /// <summary>
    /// 顶层窗口进入前台时调用。只对 Chromium 系窗口生效，其余直接跳过。
    /// </summary>
    public void NoteForeground(IntPtr top)
    {
        if (top == IntPtr.Zero || !IsChromiumHost(top))
        {
            return;
        }

        // 顶层窗口自己也处理 WM_GETOBJECT，但真正建树的是渲染子窗口，两者都要问到。
        Answer(top);
        try
        {
            NativeMethods.EnumChildWindows(top, _childCallback, IntPtr.Zero);
        }
        catch
        {
            // 窗口可能在枚举途中销毁。
        }
    }

    private static bool IsChromiumHost(IntPtr hwnd)
    {
        var cls = NativeMethods.GetWindowClass(hwnd);
        return cls.StartsWith(WidgetClassPrefix, StringComparison.Ordinal)
            || cls == RenderWidgetClass;
    }

    private bool OnChild(IntPtr hwnd, IntPtr _)
    {
        if (NativeMethods.GetWindowClass(hwnd) == RenderWidgetClass)
        {
            Answer(hwnd);
        }

        return true;
    }

    private void OnAlert(
        IntPtr hook, uint type, IntPtr hwnd, int objectId, int childId, uint thread, uint time)
    {
        // 只有携带蜜罐 id 的 alert 才是探测；系统里其他 EVENT_SYSTEM_ALERT 与此无关。
        if (objectId == NativeMethods.ChromiumHoneypotObjectId && hwnd != IntPtr.Zero)
        {
            Answer(hwnd);
        }
    }

    private void Answer(IntPtr hwnd)
    {
        lock (_gate)
        {
            // 一个窗口只需应答一次，宿主开启后不会再退回去。
            if (!_answered.Add(hwnd))
            {
                return;
            }
        }

        // 必须离开当前线程：这是一次跨进程同步发送，而调用方要么是钩子回调
        // 要么是 UI 线程，宿主卡住就会把键盘一起拖住。
        ThreadPool.UnsafeQueueUserWorkItem(
            static state =>
            {
                var (target, owner) = state;
                try
                {
                    NativeMethods.SendMessageTimeout(
                        target,
                        NativeMethods.WmGetObject,
                        IntPtr.Zero,
                        new IntPtr(NativeMethods.ChromiumHoneypotObjectId),
                        NativeMethods.SmtoAbortIfHung,
                        200,
                        out _);
                    Log.Info($"无障碍握手 hwnd={target.ToInt64()} 类={NativeMethods.GetWindowClass(target)}");
                }
                catch
                {
                    // 宿主可能已退出；下次进前台会重新尝试。
                    owner.Forget(target);
                }
            },
            (hwnd, this),
            preferLocal: false);
    }

    private void Forget(IntPtr hwnd)
    {
        lock (_gate)
        {
            _answered.Remove(hwnd);
        }
    }

    public void Dispose()
    {
        if (_alertHook != IntPtr.Zero)
        {
            NativeMethods.UnhookWinEvent(_alertHook);
        }
    }
}
