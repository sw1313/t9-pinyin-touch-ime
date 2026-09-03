using T9Pane.Native;

namespace T9Pane.Services;

internal sealed class CloakRecord
{
    public IntPtr Hwnd { get; init; }
    public int OriginalExStyle { get; init; }
    public bool AppliedLayered { get; set; }
}

internal sealed class SipWindowCloaker : IDisposable
{
    private readonly SipWindowLocator _locator = new();
    private readonly List<CloakRecord> _records = [];

    public int CloakedCount => _records.Count;

    public void Apply(NativeRect sipRect)
    {
        if (sipRect.IsEmpty)
        {
            Restore();
            return;
        }

        var windows = _locator.FindSipWindows(sipRect);
        var keep = new HashSet<IntPtr>(windows);

        for (var i = _records.Count - 1; i >= 0; i--)
        {
            if (!keep.Contains(_records[i].Hwnd) || !NativeMethods.IsWindow(_records[i].Hwnd))
            {
                RestoreOne(_records[i]);
                _records.RemoveAt(i);
            }
        }

        foreach (var hwnd in windows)
        {
            if (_records.Any(x => x.Hwnd == hwnd))
            {
                Reinforce(hwnd);
                continue;
            }

            var original = NativeMethods.GetWindowExStyle(hwnd);
            var record = new CloakRecord { Hwnd = hwnd, OriginalExStyle = original };
            if (TryCloak(hwnd, original))
            {
                record.AppliedLayered = true;
                _records.Add(record);
                Log.Info($"已借壳 SIP 窗口 0x{hwnd:X} class={NativeMethods.GetWindowClass(hwnd)}");
            }
            else
            {
                Log.Warn($"无法隐藏 SIP 窗口 0x{hwnd:X} class={NativeMethods.GetWindowClass(hwnd)}");
            }
        }
    }

    public void Restore()
    {
        foreach (var record in _records)
        {
            RestoreOne(record);
        }

        _records.Clear();
    }

    private static bool TryCloak(IntPtr hwnd, int originalExStyle)
    {
        try
        {
            var next = originalExStyle | NativeMethods.WsExLayered | NativeMethods.WsExTransparent;
            NativeMethods.SetWindowExStyle(hwnd, next);
            NativeMethods.SetLayeredWindowAttributes(hwnd, 0, 0, NativeMethods.LwaAlpha);
            NativeMethods.SetWindowPos(
                hwnd,
                IntPtr.Zero,
                0, 0, 0, 0,
                NativeMethods.SwpNoMove | NativeMethods.SwpNoSize | NativeMethods.SwpNoZOrder |
                NativeMethods.SwpNoActivate | NativeMethods.SwpFrameChanged);
            // 只做透明，不用 DWM Cloak：系统仍认为触摸键盘在，搜索框才会继续避让
            return true;
        }
        catch (Exception ex)
        {
            Log.Error($"Cloak 失败: {ex.Message}");
            return false;
        }
    }

    private static void Reinforce(IntPtr hwnd)
    {
        try
        {
            var style = NativeMethods.GetWindowExStyle(hwnd);
            if ((style & NativeMethods.WsExLayered) == 0 || (style & NativeMethods.WsExTransparent) == 0)
            {
                NativeMethods.SetWindowExStyle(hwnd, style | NativeMethods.WsExLayered | NativeMethods.WsExTransparent);
            }

            NativeMethods.SetLayeredWindowAttributes(hwnd, 0, 0, NativeMethods.LwaAlpha);
        }
        catch
        {
            // keep going
        }
    }

    private static void RestoreOne(CloakRecord record)
    {
        if (!NativeMethods.IsWindow(record.Hwnd))
        {
            return;
        }

        try
        {
            NativeMethods.SetLayeredWindowAttributes(record.Hwnd, 0, 255, NativeMethods.LwaAlpha);
            NativeMethods.SetWindowExStyle(record.Hwnd, record.OriginalExStyle);
            NativeMethods.SetWindowPos(
                record.Hwnd,
                IntPtr.Zero,
                0, 0, 0, 0,
                NativeMethods.SwpNoMove | NativeMethods.SwpNoSize | NativeMethods.SwpNoZOrder |
                NativeMethods.SwpNoActivate | NativeMethods.SwpFrameChanged);
        }
        catch (Exception ex)
        {
            Log.Warn($"恢复 SIP 窗口失败: {ex.Message}");
        }
    }

    public void Dispose() => Restore();
}
