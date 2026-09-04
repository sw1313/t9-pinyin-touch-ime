using System.Runtime.InteropServices;
using T9Pane.Native;

namespace T9Pane.Services;

internal static class SipLayer
{
    private const uint BandIhm = 6;
    private static NativeMethods.GetWindowBandFn? _getBand;
    private static NativeMethods.SetWindowBandFn? _setBand;
    private static bool _resolved;
    private static IntPtr _lastLoggedKeyboard;
    private static uint _lastLoggedBand;

    public static void Prepare(IntPtr overlay, IntPtr keyboard)
    {
        if (overlay == IntPtr.Zero)
        {
            return;
        }

        if (keyboard == IntPtr.Zero || !NativeMethods.IsWindow(keyboard))
        {
            Detach(overlay);
            return;
        }

        var parent = NativeMethods.GetParent(overlay);
        if (parent != IntPtr.Zero && parent != keyboard && !IsChildOf(overlay, keyboard))
        {
            Detach(overlay);
        }

        TrySetOwner(overlay, keyboard);
        TryMatchKeyboardBand(overlay, keyboard);
        TryAttach(overlay, keyboard);
        Raise(overlay, keyboard);
    }

    public static bool TryAttach(IntPtr overlay, IntPtr keyboard)
    {
        if (overlay == IntPtr.Zero || keyboard == IntPtr.Zero || !NativeMethods.IsWindow(keyboard))
        {
            return false;
        }

        foreach (var target in AttachCandidates(keyboard))
        {
            if (NativeMethods.GetParent(overlay) == target)
            {
                return true;
            }

            var style = NativeMethods.GetWindowStyle(overlay);
            NativeMethods.SetWindowStyle(overlay, (style | NativeMethods.WsChild) & ~NativeMethods.WsPopup);
            NativeMethods.SetParent(overlay, target);
            if (NativeMethods.GetParent(overlay) == target)
            {
                NativeMethods.SetWindowPos(
                    overlay,
                    NativeMethods.HwndTop,
                    0, 0, 0, 0,
                    NativeMethods.SwpNoMove | NativeMethods.SwpNoSize | NativeMethods.SwpNoActivate | NativeMethods.SwpFrameChanged);
                Log.Info($"九键已挂到键盘窗口 0x{target:X}");
                return true;
            }

            NativeMethods.SetParent(overlay, IntPtr.Zero);
            NativeMethods.SetWindowStyle(overlay, style);
        }

        Log.Warn("无法把九键挂到触摸键盘窗口上，改升窗口层");
        return false;
    }

    public static void Detach(IntPtr overlay)
    {
        if (overlay == IntPtr.Zero)
        {
            return;
        }

        if (NativeMethods.GetParent(overlay) != IntPtr.Zero)
        {
            NativeMethods.SetParent(overlay, IntPtr.Zero);
            var style = NativeMethods.GetWindowStyle(overlay);
            NativeMethods.SetWindowStyle(overlay, (style | NativeMethods.WsPopup) & ~NativeMethods.WsChild);
        }

        TrySetOwner(overlay, IntPtr.Zero);
        NativeMethods.SetWindowPos(
            overlay,
            NativeMethods.HwndTopmost,
            0, 0, 0, 0,
            NativeMethods.SwpNoMove | NativeMethods.SwpNoSize | NativeMethods.SwpNoActivate | NativeMethods.SwpFrameChanged);
    }

    /// <summary>
    /// 官方触摸键盘在 IHM 层，高于普通 Topmost。平板上若没藏干净，
    /// 九键必须升到同一层才压得住。
    /// </summary>
    public static void RaiseToIhmBand(IntPtr overlay)
    {
        if (overlay == IntPtr.Zero)
        {
            return;
        }

        TryMatchKeyboardBand(overlay, IntPtr.Zero);
        Raise(overlay, IntPtr.Zero);
    }

    public static bool TrySetOwner(IntPtr overlay, IntPtr keyboard)
    {
        if (overlay == IntPtr.Zero)
        {
            return false;
        }

        var previous = NativeMethods.SetWindowLongPtr(overlay, NativeMethods.GwlHwndParent, keyboard);
        return previous != IntPtr.Zero || keyboard == IntPtr.Zero;
    }

    public static bool TryMatchKeyboardBand(IntPtr overlay, IntPtr keyboard)
    {
        Resolve();
        if (_setBand is null || overlay == IntPtr.Zero)
        {
            return false;
        }

        var band = BandIhm;
        if (TryGetBand(keyboard, out var keyboardBand) && keyboardBand != 0)
        {
            band = keyboardBand;
        }

        var ok = _setBand(overlay, IntPtr.Zero, band) != 0;
        if (ok)
        {
            if (_lastLoggedKeyboard != keyboard || _lastLoggedBand != band)
            {
                _lastLoggedKeyboard = keyboard;
                _lastLoggedBand = band;
                Log.Info($"已把九键升到键盘同层 band={band}");
            }

            return true;
        }

        if (_lastLoggedKeyboard != keyboard)
        {
            _lastLoggedKeyboard = keyboard;
            Log.Warn($"SetWindowBand 失败 band={band} err={Marshal.GetLastWin32Error()}，无 uiAccess 时这是预期结果");
        }

        return false;
    }

    public static void Raise(IntPtr overlay, IntPtr keyboard)
    {
        if (overlay == IntPtr.Zero)
        {
            return;
        }

        if (keyboard != IntPtr.Zero && NativeMethods.IsWindow(keyboard))
        {
            var parent = NativeMethods.GetParent(overlay);
            if (parent == keyboard || IsChildOf(overlay, keyboard))
            {
                NativeMethods.SetWindowPos(
                    overlay,
                    NativeMethods.HwndTop,
                    0, 0, 0, 0,
                    NativeMethods.SwpNoMove | NativeMethods.SwpNoSize | NativeMethods.SwpNoActivate);
                return;
            }

            TryMatchKeyboardBand(overlay, keyboard);
        }

        NativeMethods.SetWindowPos(
            overlay,
            NativeMethods.HwndTopmost,
            0, 0, 0, 0,
            NativeMethods.SwpNoMove | NativeMethods.SwpNoSize | NativeMethods.SwpNoActivate);
        NativeMethods.SetWindowPos(
            overlay,
            NativeMethods.HwndTop,
            0, 0, 0, 0,
            NativeMethods.SwpNoMove | NativeMethods.SwpNoSize | NativeMethods.SwpNoActivate);
    }

    public static bool IsAttachedTo(IntPtr overlay, IntPtr keyboard)
    {
        return overlay != IntPtr.Zero
               && keyboard != IntPtr.Zero
               && NativeMethods.IsWindow(keyboard)
               && (NativeMethods.GetParent(overlay) == keyboard || IsChildOf(overlay, keyboard));
    }

    public static NativeRect ToClient(IntPtr keyboard, NativeRect screen)
    {
        var topLeft = new NativePoint { X = screen.Left, Y = screen.Top };
        NativeMethods.ScreenToClient(keyboard, ref topLeft);
        return new NativeRect
        {
            Left = topLeft.X,
            Top = topLeft.Y,
            Right = topLeft.X + screen.Width,
            Bottom = topLeft.Y + screen.Height
        };
    }

    public static bool TryGetBand(IntPtr hwnd, out uint band)
    {
        Resolve();
        band = 0;
        if (_getBand is null || hwnd == IntPtr.Zero || !NativeMethods.IsWindow(hwnd))
        {
            return false;
        }

        return _getBand(hwnd, out band) >= 0;
    }

    private static bool IsChildOf(IntPtr overlay, IntPtr keyboard)
    {
        var parent = NativeMethods.GetParent(overlay);
        while (parent != IntPtr.Zero)
        {
            if (parent == keyboard)
            {
                return true;
            }

            parent = NativeMethods.GetParent(parent);
        }

        return false;
    }

    private static IEnumerable<IntPtr> AttachCandidates(IntPtr keyboard)
    {
        yield return keyboard;
        var children = new List<IntPtr>();
        NativeMethods.EnumChildWindows(keyboard, (hwnd, _) =>
        {
            if (NativeMethods.IsWindowVisible(hwnd)
                && NativeMethods.GetWindowRect(hwnd, out var rect)
                && rect.Width >= 200
                && rect.Height >= 120)
            {
                children.Add(hwnd);
            }

            return true;
        }, IntPtr.Zero);

        foreach (var child in children)
        {
            yield return child;
        }
    }

    private static void Resolve()
    {
        if (_resolved)
        {
            return;
        }

        _resolved = true;
        var user32 = NativeMethods.GetModuleHandle("user32.dll");
        if (user32 == IntPtr.Zero)
        {
            return;
        }

        var get = NativeMethods.GetProcAddress(user32, "GetWindowBand");
        var set = NativeMethods.GetProcAddress(user32, "SetWindowBand");
        if (get != IntPtr.Zero)
        {
            _getBand = Marshal.GetDelegateForFunctionPointer<NativeMethods.GetWindowBandFn>(get);
        }

        if (set != IntPtr.Zero)
        {
            _setBand = Marshal.GetDelegateForFunctionPointer<NativeMethods.SetWindowBandFn>(set);
        }
    }
}
