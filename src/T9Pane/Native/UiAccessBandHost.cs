using System.Runtime.InteropServices;
using T9Pane.Services;

namespace T9Pane.Native;

internal sealed class UiAccessBandHost : IDisposable
{
    public static UiAccessBandHost Shared { get; } = new();

    private const string ClassName = "T9Pane.UiAccessBand";
    private static readonly NativeMethods.WndProcFn WndProc = NativeMethods.DefWindowProc;
    private static bool _registered;
    private static NativeMethods.CreateWindowInBandFn? _createInBand;

    private IntPtr _hwnd;
    private IntPtr _owner;
    private IntPtr _content;

    public IntPtr Handle => _hwnd;
    public IntPtr LogicalOwner => _owner;

    public bool TryOwnAndRaise(IntPtr window, IntPtr owner = default)
    {
        if (window == IntPtr.Zero)
        {
            return false;
        }

        if (owner == window || !NativeMethods.IsWindow(owner))
        {
            owner = IntPtr.Zero;
        }
        // WPF does not support changing its top-level HWND into a child or
        // attaching it to a transient cross-process owner. Both operations
        // can destroy and recreate its only window. System flyouts use the
        // native TSF-owned renderer; this local path stays a stable popup.
        _owner = owner;
        _content = window;
        return NativeMethods.SetWindowPos(
            window,
            NativeMethods.HwndTopmost,
            0, 0, 0, 0,
            NativeMethods.SwpNoMove
            | NativeMethods.SwpNoSize
            | NativeMethods.SwpNoActivate);
    }

    public bool TryPlace(IntPtr window, IntPtr owner, NativeRect rect)
    {
        if (rect.IsEmpty || !TryOwnAndRaise(window, owner))
        {
            return false;
        }

        NativeMethods.SetWindowPos(
            window,
            NativeMethods.HwndTopmost,
            rect.Left,
            rect.Top,
            rect.Width,
            rect.Height,
            NativeMethods.SwpNoActivate | NativeMethods.SwpShowWindow);
        NativeMethods.ShowWindow(window, NativeMethods.SwShowNoActivate);
        return true;
    }

    public bool TryEnsure()
    {
        if (_hwnd != IntPtr.Zero && NativeMethods.IsWindow(_hwnd))
        {
            return true;
        }

        if (!UiAccessToken.Has())
        {
            return false;
        }

        Resolve();
        if (_createInBand is null || !Register())
        {
            return false;
        }

        var instance = NativeMethods.GetModuleHandle(null);
        _hwnd = _createInBand(
            NativeMethods.WsExNoActivate | NativeMethods.WsExToolWindow | NativeMethods.WsExTopmost,
            ClassName,
            "T9PaneBand",
            NativeMethods.WsPopup,
            0, 0, 0, 0,
            IntPtr.Zero,
            IntPtr.Zero,
            instance,
            IntPtr.Zero,
            NativeMethods.ZbidUiAccess);

        if (_hwnd == IntPtr.Zero)
        {
            Log.Warn($"CreateWindowInBand 失败 err={Marshal.GetLastWin32Error()}");
            return false;
        }

        Log.Info($"已创建 uiAccess 高层宿主 0x{_hwnd:X}");
        return true;
    }

    public void Move(NativeRect rect)
    {
        if (_hwnd == IntPtr.Zero)
        {
            return;
        }

        NativeMethods.SetWindowPos(
            _hwnd,
            NativeMethods.HwndTop,
            rect.Left,
            rect.Top,
            rect.Width,
            rect.Height,
            NativeMethods.SwpShowWindow | NativeMethods.SwpNoActivate);
        NativeMethods.ShowWindow(_hwnd, NativeMethods.SwShowNoActivate);
    }

    public void Hide()
    {
        if (_content != IntPtr.Zero && NativeMethods.IsWindow(_content))
        {
            NativeMethods.ShowWindow(_content, NativeMethods.SwHide);
        }
    }

    public void Dispose()
    {
        if (_hwnd != IntPtr.Zero)
        {
            NativeMethods.DestroyWindow(_hwnd);
            _hwnd = IntPtr.Zero;
            _owner = IntPtr.Zero;
            _content = IntPtr.Zero;
        }
    }

    private static bool Register()
    {
        if (_registered)
        {
            return true;
        }

        var cls = new WndClassEx
        {
            Size = Marshal.SizeOf<WndClassEx>(),
            WndProc = WndProc,
            Instance = NativeMethods.GetModuleHandle(null),
            ClassName = ClassName
        };
        if (NativeMethods.RegisterClassEx(ref cls) == 0 && Marshal.GetLastWin32Error() != 1410)
        {
            Log.Warn($"RegisterClassEx 失败 err={Marshal.GetLastWin32Error()}");
            return false;
        }

        _registered = true;
        return true;
    }

    private static void Resolve()
    {
        if (_createInBand is not null)
        {
            return;
        }

        var user32 = NativeMethods.GetModuleHandle("user32.dll");
        var proc = NativeMethods.GetProcAddress(user32, "CreateWindowInBand");
        if (proc != IntPtr.Zero)
        {
            _createInBand = Marshal.GetDelegateForFunctionPointer<NativeMethods.CreateWindowInBandFn>(proc);
        }
    }
}
