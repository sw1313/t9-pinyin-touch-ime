using System.Runtime.InteropServices;
using T9Pane.Native;

namespace T9Pane.Services;

internal static class SipSuppressor
{
    private static readonly SipWindowLocator Locator = new();
    private static DateTime _nextHideUtc = DateTime.MinValue;

    public static void HideOfficial()
    {
        if (DateTime.UtcNow < _nextHideUtc)
        {
            return;
        }

        _nextHideUtc = DateTime.UtcNow.AddMilliseconds(400);
        if (InputPaneInterop.TryGetLocation(out _))
        {
            InputPaneController.TryHideWinRt(NativeMethods.GetForegroundWindow());
        }

        // Toggle 是开关键。只在系统仍认为触摸键盘可见时才按，避免刚藏掉又被拉起来。
        if (InputPaneInterop.TryGetLocation(out _))
        {
            ToggleOff();
        }

        foreach (var (hwnd, _) in Locator.FindKeyboardWindows())
        {
            if (hwnd == IntPtr.Zero || !NativeMethods.IsWindow(hwnd))
            {
                continue;
            }

            NativeMethods.PostMessage(
                hwnd,
                NativeMethods.WmSysCommand,
                (IntPtr)NativeMethods.ScClose,
                IntPtr.Zero);
            NativeMethods.ShowWindow(hwnd, NativeMethods.SwHide);
        }
    }

    private static void ToggleOff()
    {
        object? host = null;
        try
        {
            host = new UiHostNoLaunch();
            if (host is ITipInvocation tip)
            {
                tip.Toggle(NativeMethods.GetDesktopWindow());
            }
        }
        catch
        {
            // 无触摸键盘服务时忽略
        }
        finally
        {
            if (host is not null && Marshal.IsComObject(host))
            {
                Marshal.FinalReleaseComObject(host);
            }
        }
    }
}
