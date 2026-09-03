using T9Pane.Native;

namespace T9Pane.Services;

internal sealed class WindowFitter
{
    private IntPtr _hwnd;
    private NativeRect _original;
    private bool _wasZoomed;

    public void Apply(IntPtr hwnd, NativeRect keyboard)
    {
        if (hwnd == IntPtr.Zero || keyboard.IsEmpty || !NativeMethods.IsWindow(hwnd))
        {
            Restore();
            return;
        }

        if (!NativeMethods.GetWindowRect(hwnd, out var current) || current.IsEmpty)
        {
            return;
        }

        if (_hwnd != hwnd)
        {
            Restore();
            _hwnd = hwnd;
            _original = current;
            _wasZoomed = NativeMethods.IsZoomed(hwnd);
        }
        else if (!NativeMethods.GetWindowRect(hwnd, out current))
        {
            return;
        }

        var className = NativeMethods.GetWindowClass(hwnd);
        if (className is "Shell_TrayWnd" or "Progman" or "WorkerW" or "T9Pane")
        {
            return;
        }

        NativeMethods.GetWindowThreadProcessId(hwnd, out var pid);
        var process = System.IO.Path.GetFileNameWithoutExtension(NativeMethods.GetProcessPath(pid));
        if (ShellAvoider.IsShellProcess(process))
        {
            return;
        }

        var newBottom = keyboard.Top;
        if (current.Bottom <= newBottom + 8)
        {
            return;
        }

        var height = Math.Max(160, newBottom - current.Top);
        NativeMethods.SetWindowPos(
            hwnd,
            IntPtr.Zero,
            current.Left,
            current.Top,
            current.Width,
            height,
            NativeMethods.SwpNoZOrder | NativeMethods.SwpNoActivate);
    }

    public void Restore()
    {
        if (_hwnd == IntPtr.Zero || !NativeMethods.IsWindow(_hwnd))
        {
            _hwnd = IntPtr.Zero;
            return;
        }

        NativeMethods.SetWindowPos(
            _hwnd,
            IntPtr.Zero,
            _original.Left,
            _original.Top,
            _original.Width,
            _original.Height,
            NativeMethods.SwpNoZOrder | NativeMethods.SwpNoActivate);
        _hwnd = IntPtr.Zero;
        _wasZoomed = false;
    }
}
