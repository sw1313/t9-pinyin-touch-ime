using T9Pane.Native;

namespace T9Pane.Services;

internal sealed class SipLetterHole : IDisposable
{
    private readonly SipWindowLocator _locator = new();
    private readonly HashSet<IntPtr> _punched = [];

    public int Count => _punched.Count;

    public int Apply(NativeRect letter)
    {
        if (letter.IsEmpty)
        {
            Restore();
            return 0;
        }

        var targets = _locator.FindWindowsContaining(letter);
        var keep = new HashSet<IntPtr>(targets);
        foreach (var old in _punched.ToList())
        {
            if (!keep.Contains(old) || !NativeMethods.IsWindow(old))
            {
                Clear(old);
                _punched.Remove(old);
            }
        }

        foreach (var hwnd in targets)
        {
            if (_punched.Contains(hwnd))
            {
                Punch(hwnd, letter);
                continue;
            }

            if (Punch(hwnd, letter))
            {
                _punched.Add(hwnd);
                Log.Info($"已挖空字母区 0x{hwnd:X} class={NativeMethods.GetWindowClass(hwnd)} {letter.Width}x{letter.Height}");
            }
        }

        return _punched.Count;
    }

    public void Restore()
    {
        foreach (var hwnd in _punched)
        {
            Clear(hwnd);
        }

        _punched.Clear();
    }

    private static bool Punch(IntPtr hwnd, NativeRect letter)
    {
        if (!NativeMethods.IsWindow(hwnd) || !NativeMethods.GetWindowRect(hwnd, out var window) || window.IsEmpty)
        {
            return false;
        }

        var holeLeft = letter.Left - window.Left;
        var holeTop = letter.Top - window.Top;
        var holeRight = letter.Right - window.Left;
        var holeBottom = letter.Bottom - window.Top;
        if (holeRight <= holeLeft || holeBottom <= holeTop)
        {
            return false;
        }

        var full = NativeMethods.CreateRectRgn(0, 0, window.Width, window.Height);
        var hole = NativeMethods.CreateRectRgn(holeLeft, holeTop, holeRight, holeBottom);
        var dest = NativeMethods.CreateRectRgn(0, 0, 0, 0);
        try
        {
            if (full == IntPtr.Zero || hole == IntPtr.Zero || dest == IntPtr.Zero)
            {
                return false;
            }

            NativeMethods.CombineRgn(dest, full, hole, NativeMethods.RgnDiff);
            if (NativeMethods.SetWindowRgn(hwnd, dest, true) == 0)
            {
                Log.Warn($"挖空失败 0x{hwnd:X} err={System.Runtime.InteropServices.Marshal.GetLastWin32Error()}");
                NativeMethods.DeleteObject(dest);
                dest = IntPtr.Zero;
                return false;
            }

            dest = IntPtr.Zero;
            return Verify(hwnd);
        }
        finally
        {
            if (full != IntPtr.Zero)
            {
                NativeMethods.DeleteObject(full);
            }

            if (hole != IntPtr.Zero)
            {
                NativeMethods.DeleteObject(hole);
            }

            if (dest != IntPtr.Zero)
            {
                NativeMethods.DeleteObject(dest);
            }
        }
    }

    private static bool Verify(IntPtr hwnd)
    {
        var probe = NativeMethods.CreateRectRgn(0, 0, 0, 0);
        if (probe == IntPtr.Zero)
        {
            return true;
        }

        try
        {
            var kind = NativeMethods.GetWindowRgn(hwnd, probe);
            if (kind is NativeMethods.RegionSimple or NativeMethods.RegionComplex)
            {
                return true;
            }

            Log.Warn($"挖空未生效 0x{hwnd:X} rgn={kind}");
            return false;
        }
        finally
        {
            NativeMethods.DeleteObject(probe);
        }
    }

    private static void Clear(IntPtr hwnd)
    {
        if (!NativeMethods.IsWindow(hwnd))
        {
            return;
        }

        NativeMethods.SetWindowRgn(hwnd, IntPtr.Zero, true);
    }

    public void Dispose() => Restore();
}
