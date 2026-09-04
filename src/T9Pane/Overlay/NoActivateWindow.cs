using System.Windows;
using System.Windows.Interop;
using T9Pane.Native;
using T9Pane.Services;

namespace T9Pane.Overlay;

internal class NoActivateWindow : Window
{
    public NoActivateWindow()
    {
        ShowActivated = false;
        Focusable = false;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var hwnd = new WindowInteropHelper(this).Handle;
        var style = NativeMethods.GetWindowExStyle(hwnd);
        NativeMethods.SetWindowExStyle(
            hwnd,
            style | NativeMethods.WsExNoActivate | NativeMethods.WsExToolWindow | NativeMethods.WsExTopmost);

        if (PresentationSource.FromVisual(this) is HwndSource source)
        {
            source.AddHook(WndProc);
        }

        TabletGesturePolicy.DisablePressAndHold(hwnd);
    }

    private static IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == NativeMethods.WmMouseActivate)
        {
            handled = true;
            return new IntPtr(NativeMethods.MaNoActivate);
        }

        if (msg == NativeMethods.WmTabletQuerySystemGestureStatus)
        {
            handled = true;
            return new IntPtr(TabletGesturePolicy.QueryStatus());
        }

        return IntPtr.Zero;
    }
}
