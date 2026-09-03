using System.IO;
using T9Pane.Native;

namespace T9Pane.Services;

internal readonly record struct SipWindowHit(
    IntPtr Hwnd,
    NativeRect Rect,
    bool Cloaked,
    string ClassName,
    string ProcessName,
    uint Band);

internal sealed class SipWindowLocator
{
    private static readonly HashSet<string> SipProcessNames =
    [
        "textinputhost",
        "tabtip",
        "tabtip32",
        "windowsinternal.composableshell.experiences.textinput.inputapp"
    ];

    private bool _dumped;

    public IReadOnlyList<(IntPtr Hwnd, NativeRect Rect)> FindKeyboardWindows()
    {
        return Enumerate(includeChildren: true)
            .Where(x => LooksLikeTouchKeyboard(x.Rect) && !x.Cloaked)
            .OrderByDescending(ScoreVisual)
            .Select(x => (x.Hwnd, x.Rect))
            .ToList();
    }

    public IReadOnlyList<IntPtr> FindSipWindows(NativeRect? targetRect)
    {
        var hits = Enumerate(includeChildren: true);
        if (targetRect is { } target && !target.IsEmpty)
        {
            return hits
                .Where(x => target.IntersectionArea(x.Rect) >= target.Area * 0.35 || Contains(x.Rect, target))
                .Select(x => x.Hwnd)
                .ToList();
        }

        return hits.Select(x => x.Hwnd).ToList();
    }

    public bool TryFindSipRect(NativeRect? hint, out NativeRect rect)
    {
        return TryFindKeyboard(hint, out _, out rect);
    }

    public bool TryFindKeyboard(NativeRect? hint, out IntPtr hwnd, out NativeRect rect)
    {
        if (TryFindTarget(hint, out var target))
        {
            hwnd = target.Hwnd;
            rect = target.Visual;
            return true;
        }

        hwnd = IntPtr.Zero;
        rect = default;
        return false;
    }

    public IntPtr FindHwndNear(NativeRect pane) => TryFindTarget(pane, out var target) ? target.Hwnd : IntPtr.Zero;

    public bool TryFindTarget(NativeRect? visualHint, out (IntPtr Hwnd, NativeRect Visual) target)
    {
        var hits = Enumerate(includeChildren: true);
        DumpOnce(hits);

        var visual = visualHint is { } hint && !hint.IsEmpty && LooksLikeTouchKeyboard(hint)
            ? hint
            : hits.Where(x => !x.Cloaked && LooksLikeTouchKeyboard(x.Rect))
                .OrderByDescending(ScoreVisual)
                .Select(x => (NativeRect?)x.Rect)
                .FirstOrDefault() ?? default;

        if (visual.IsEmpty)
        {
            target = default;
            return false;
        }

        var hwnd = PickHost(hits, visual);
        target = (hwnd, visual);
        return hwnd != IntPtr.Zero || LooksLikeTouchKeyboard(visual);
    }

    public IReadOnlyList<IntPtr> FindWindowsContaining(NativeRect area)
    {
        if (area.IsEmpty)
        {
            return [];
        }

        return Enumerate(includeChildren: true)
            .Where(x => Contains(x.Rect, area) || x.Rect.IntersectionArea(area) >= area.Area * 0.6)
            .OrderBy(x => x.Rect.Area)
            .Select(x => x.Hwnd)
            .Distinct()
            .ToList();
    }

    public static bool LooksLikeTouchKeyboard(NativeRect rect)
    {
        if (rect.IsEmpty || !NativeMethods.TryGetMonitorWork(rect, out var work))
        {
            return false;
        }

        var width = rect.Width;
        var height = rect.Height;
        if (width < 360 || height < 160)
        {
            return false;
        }

        // TextInputHost 常驻一个接近全屏的宿主窗口，不能拿来当键盘可视区域。
        if (height > work.Height * 0.50 || width * height > work.Area * 0.55)
        {
            return false;
        }

        var docked = width >= work.Width * 0.55
                     && height <= work.Height * 0.48
                     && height >= 180
                     && Math.Abs(rect.Bottom - work.Bottom) <= 48;

        var floating = width >= 360
                       && width <= work.Width * 0.92
                       && height is >= 180 and <= 580
                       && width >= height * 1.25
                       && width * height < work.Area * 0.42;

        return docked || floating;
    }

    private static IntPtr PickHost(IReadOnlyList<SipWindowHit> hits, NativeRect visual)
    {
        var exact = hits
            .Where(x => Similar(x.Rect, visual))
            .OrderBy(x => x.Cloaked ? 1 : 0)
            .ThenBy(x => x.Rect.Area)
            .FirstOrDefault();
        if (exact.Hwnd != IntPtr.Zero)
        {
            return exact.Hwnd;
        }

        var container = hits
            .Where(x => Contains(x.Rect, visual))
            .OrderBy(x => x.Cloaked ? 1 : 0)
            .ThenBy(x => x.Rect.Area)
            .FirstOrDefault();
        return container.Hwnd;
    }

    private IReadOnlyList<SipWindowHit> Enumerate(bool includeChildren)
    {
        var hits = new List<SipWindowHit>();
        var seen = new HashSet<IntPtr>();

        void Consider(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero || !seen.Add(hwnd) || !TryDescribe(hwnd, out var hit))
            {
                return;
            }

            hits.Add(hit);
            if (!includeChildren)
            {
                return;
            }

            NativeMethods.EnumChildWindows(hwnd, (child, _) =>
            {
                if (seen.Add(child) && TryDescribe(child, out var childHit))
                {
                    hits.Add(childHit);
                }

                return true;
            }, IntPtr.Zero);
        }

        Consider(NativeMethods.FindWindow("IPTip_Main_Window", null));
        NativeMethods.EnumWindows((hwnd, _) =>
        {
            Consider(hwnd);
            return true;
        }, IntPtr.Zero);

        return hits;
    }

    private static bool TryDescribe(IntPtr hwnd, out SipWindowHit hit)
    {
        hit = default;
        if (!NativeMethods.IsWindow(hwnd) || !NativeMethods.GetWindowRect(hwnd, out var rect) || rect.IsEmpty)
        {
            return false;
        }

        NativeMethods.GetWindowThreadProcessId(hwnd, out var pid);
        if (pid == 0)
        {
            return false;
        }

        var processPath = NativeMethods.GetProcessPath(pid);
        var name = Path.GetFileNameWithoutExtension(processPath).ToLowerInvariant();
        var className = NativeMethods.GetWindowClass(hwnd);
        var sipProcess = SipProcessNames.Contains(name) || name.Contains("textinput", StringComparison.Ordinal);
        if (!sipProcess)
        {
            return false;
        }

        SipLayer.TryGetBand(hwnd, out var band);
        hit = new SipWindowHit(
            hwnd,
            rect,
            NativeMethods.IsCloaked(hwnd),
            className,
            name,
            band);
        return true;
    }

    private void DumpOnce(IReadOnlyList<SipWindowHit> hits)
    {
        if (_dumped || hits.Count == 0)
        {
            return;
        }

        _dumped = true;
        foreach (var hit in hits.Take(20))
        {
            Log.Info($"SIP窗口 0x{hit.Hwnd:X} {hit.ProcessName}/{hit.ClassName} {hit.Rect.Width}x{hit.Rect.Height} @{hit.Rect.Left},{hit.Rect.Top} cloak={hit.Cloaked} band={hit.Band} vis={NativeMethods.IsWindowVisible(hit.Hwnd)}");
        }
    }

    private static int ScoreVisual(SipWindowHit hit)
    {
        NativeMethods.TryGetMonitorWork(hit.Rect, out var work);
        var dockedBonus = !work.IsEmpty && Math.Abs(hit.Rect.Bottom - work.Bottom) <= 48 ? 50_000 : 80_000;
        return dockedBonus - Math.Abs(hit.Rect.Width - 900) - Math.Abs(hit.Rect.Height - 360);
    }

    private static bool Contains(NativeRect outer, NativeRect inner)
    {
        return !inner.IsEmpty
               && outer.Left <= inner.Left + 12
               && outer.Top <= inner.Top + 12
               && outer.Right >= inner.Right - 12
               && outer.Bottom >= inner.Bottom - 12;
    }

    private static bool Similar(NativeRect a, NativeRect b)
    {
        return Math.Abs(a.Width - b.Width) <= 48
               && Math.Abs(a.Height - b.Height) <= 48
               && a.IntersectionArea(b) >= Math.Min(a.Area, b.Area) * 0.7;
    }
}
