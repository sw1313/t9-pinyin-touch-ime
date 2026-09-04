using System.IO;
using T9Pane.Native;

namespace T9Pane.Services;

internal static class SurfaceRelationPolicy
{
    public static int Score(
        bool sameRoot,
        bool sameRootOwner,
        bool sameProcess,
        bool applicationFrame,
        bool viewVisible,
        bool substantiallyOverlaps)
    {
        if (sameRoot)
        {
            return 400;
        }
        if (!viewVisible || !substantiallyOverlaps)
        {
            return 0;
        }
        if (sameRootOwner)
        {
            return 300;
        }
        if (applicationFrame)
        {
            return 250;
        }
        return sameProcess ? 200 : 0;
    }
}

internal static class ShellProcess
{
    private static readonly HashSet<string> Search =
    [
        "searchhost", "searchapp", "searchui", "searchapp.desktop"
    ];

    private static readonly HashSet<string> TrayClasses =
    [
        "shell_traywnd", "shell_secondarytraywnd", "notifyiconoverflowwindow",
        "tasklistthumbnailwnd", "progman", "workerw"
    ];

    private static readonly HashSet<string> SystemFlyouts =
    [
        "searchhost", "searchapp", "searchui", "searchapp.desktop",
        "startmenuexperiencehost", "shellexperiencehost"
    ];

    private static readonly HashSet<string> SystemTsfClients =
    [
        "systemsettings", "winstore.app", "applicationframehost", "textinputhost"
    ];

    public static bool IsSearch(IntPtr hwnd) => Search.Contains(Name(hwnd));

    /// <summary>
    /// 这些外壳宿主自己不承载文本框，点进搜索后由 SearchHost 接管输入焦点，
    /// 但前台窗口仍然留在宿主上。要读光标就必须先转到 SearchHost 窗口，
    /// 否则会去错误的 UIA 树里找输入框。任务栏搜索(explorer)一直是这么处理的，
    /// 开始菜单(startmenuexperiencehost)走的是同一套交接。
    /// </summary>
    internal static bool HandsOffToSearchHost(string processName) =>
        processName is "explorer" or "startmenuexperiencehost";

    /// <summary>
    /// 开始菜单 / 任务栏 / SearchHost 是同一次搜索会话的三个窗口。
    /// 从别的程序按 Win 再点搜索框时，前台会从 StartMenuExperienceHost 交到
    /// SearchHost：这不是换了应用，不能拆掉刚授的权，否则键盘闪一下就没。
    /// explorer 只有任务栏那一份算在会话里，普通文件窗口不算。
    /// </summary>
    internal static bool IsSearchSessionSurfaceName(
        string processName,
        bool trayChrome,
        bool searchFlyoutVisible = false) =>
        Search.Contains(processName)
        || processName == "startmenuexperiencehost"
        || (processName == "explorer" && (trayChrome || searchFlyoutVisible));

    internal static bool IsSearchHandoffName(
        string fromName,
        bool fromTray,
        string toName,
        bool toTray,
        bool searchFlyoutVisible = false) =>
        IsSearchSessionSurfaceName(fromName, fromTray, searchFlyoutVisible)
        && IsSearchSessionSurfaceName(toName, toTray, searchFlyoutVisible);

    internal static bool IsSearchHandoff(IntPtr from, IntPtr to)
    {
        if (from == IntPtr.Zero || to == IntPtr.Zero || from == to)
        {
            return false;
        }

        var fromRoot = NativeMethods.GetAncestor(from, NativeMethods.GaRoot);
        var toRoot = NativeMethods.GetAncestor(to, NativeMethods.GaRoot);
        if (fromRoot == IntPtr.Zero)
        {
            fromRoot = from;
        }

        if (toRoot == IntPtr.Zero)
        {
            toRoot = to;
        }

        return IsSearchHandoffName(
            Name(fromRoot),
            IsTrayChrome(fromRoot),
            Name(toRoot),
            IsTrayChrome(toRoot),
            HasVisibleSearchFlyout());
    }

    public static bool IsSystemFlyout(IntPtr hwnd) => SystemFlyouts.Contains(Name(hwnd));

    public static bool IsSystemTextSurface(IntPtr hwnd) =>
        hwnd != IntPtr.Zero
        && IsSystemTextSurfaceName(
            Name(hwnd),
            IsSystemFlyout(hwnd) || IsSearch(hwnd),
            IsTrayChrome(hwnd));

    internal static bool IsSystemTextSurfaceName(
        string processName,
        bool systemFlyout,
        bool trayChrome) =>
        systemFlyout
        || (processName == "explorer" && trayChrome);

    /// <summary>
    /// 开始菜单 / SearchHost 浮层还在时，explorer 上的搜索框也必须走宿主位图。
    /// 官方 SIP 在 IHM 层；WPF 顶层窗会画在菜单上面，点却落到菜单按钮上。
    /// </summary>
    internal static bool RequiresHostRenderName(
        string processName,
        bool systemFlyout,
        bool trayChrome,
        bool searchFlyoutVisible) =>
        IsSystemTextSurfaceName(processName, systemFlyout, trayChrome)
        || (processName == "explorer" && searchFlyoutVisible);

    public static bool RequiresHostRender(IntPtr hwnd) =>
        RequiresHostRenderName(
            Name(hwnd),
            IsSystemFlyout(hwnd) || IsSearch(hwnd),
            IsTrayChrome(hwnd),
            HasVisibleSearchFlyout());

    public static bool HasVisibleSearchFlyout() =>
        TryFindVisibleSearch(out _, out _) || TryFindVisibleStartMenu(out _, out _);

    public static bool IsActiveSearchSession(IntPtr hwnd, bool hasTaskbarSearch = false) =>
        hasTaskbarSearch
        || (hwnd != IntPtr.Zero
            && IsSearchSessionSurfaceName(
                Name(hwnd),
                IsTrayChrome(hwnd),
                HasVisibleSearchFlyout()));

    public static bool IsForegroundFlyout()
    {
        var fg = NativeMethods.GetForegroundWindow();
        if (fg == IntPtr.Zero)
        {
            return false;
        }

        var top = NativeMethods.GetAncestor(fg, NativeMethods.GaRoot);
        return IsSystemFlyout(top)
            || IsSearch(top)
            || IsSystemFlyout(fg)
            || (Name(top) == "explorer" && HasVisibleSearchFlyout());
    }

    public static bool IsForegroundSystemTextHost()
    {
        var fg = NativeMethods.GetForegroundWindow();
        if (fg == IntPtr.Zero)
        {
            return false;
        }

        var top = NativeMethods.GetAncestor(fg, NativeMethods.GaRoot);
        return IsForegroundFlyout();
    }

    public static bool IsForegroundBrokeredSurface()
    {
        var foreground = NativeMethods.GetForegroundWindow();
        var top = NativeMethods.GetAncestor(foreground, NativeMethods.GaRoot);
        return top != IntPtr.Zero
            && (IsForegroundSystemTextHost()
                || IsApplicationFrameWindow(top));
    }

    public static IntPtr ResolveForegroundSurface(IntPtr foreground)
    {
        var root = NativeMethods.GetAncestor(foreground, NativeMethods.GaRoot);
        if (root == IntPtr.Zero)
        {
            return foreground;
        }

        var name = Name(root);
        // explorer 只有任务栏那一份会把输入交给 SearchHost，普通文件窗口不能重定向。
        var handsOff = name == "explorer"
            ? IsTrayChrome(root)
            : HandsOffToSearchHost(name);
        if (handsOff && TryFindVisibleSearch(out var search, out _))
        {
            return search;
        }

        return foreground;
    }

    public static bool BelongsToForegroundSurface(
        IntPtr foreground,
        IntPtr contextView) =>
        ForegroundSurfaceScore(foreground, contextView) > 0;

    public static int ForegroundSurfaceScore(
        IntPtr foreground,
        IntPtr contextView)
    {
        if (foreground == IntPtr.Zero || contextView == IntPtr.Zero)
        {
            return 0;
        }

        var foregroundRoot = NativeMethods.GetAncestor(
            foreground,
            NativeMethods.GaRoot);
        var viewRoot = NativeMethods.GetAncestor(
            contextView,
            NativeMethods.GaRoot);
        if (foregroundRoot == IntPtr.Zero || viewRoot == IntPtr.Zero)
        {
            return 0;
        }

        var foregroundOwner = NativeMethods.GetAncestor(
            foregroundRoot,
            NativeMethods.GaRootOwner);
        var viewOwner = NativeMethods.GetAncestor(
            viewRoot,
            NativeMethods.GaRootOwner);
        NativeMethods.GetWindowThreadProcessId(foregroundRoot, out var foregroundPid);
        NativeMethods.GetWindowThreadProcessId(viewRoot, out var viewPid);
        var visible = NativeMethods.IsWindowVisible(viewRoot)
            && !NativeMethods.IsCloaked(viewRoot);
        var overlaps = NativeMethods.GetWindowRect(foregroundRoot, out var frame)
            && NativeMethods.GetWindowRect(viewRoot, out var view)
            && SubstantiallyOverlaps(frame, view);
        return SurfaceRelationPolicy.Score(
            foregroundRoot == viewRoot,
            foregroundOwner != IntPtr.Zero && foregroundOwner == viewOwner,
            foregroundPid != 0 && foregroundPid == viewPid,
            IsApplicationFrameWindow(foregroundRoot),
            visible,
            overlaps);
    }

    public static bool IsSystemTextClient(uint pid)
    {
        return IsSystemTextClientName(Name(pid));
    }

    internal static bool IsSystemTextClientName(string name) =>
        SystemTsfClients.Contains(name)
            || SystemFlyouts.Contains(name)
            || name == "explorer";

    internal static bool IsApplicationFrameClass(string className) =>
        className.Equals(
            "ApplicationFrameWindow",
            StringComparison.OrdinalIgnoreCase);

    public static bool IsApplicationFrameWindow(IntPtr hwnd) =>
        hwnd != IntPtr.Zero && IsApplicationFrameClass(NativeMethods.GetWindowClass(hwnd));

    public static bool IsTrayChrome(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
        {
            return false;
        }

        hwnd = NativeMethods.GetAncestor(hwnd, NativeMethods.GaRoot);
        var className = NativeMethods.GetWindowClass(hwnd).ToLowerInvariant();
        return TrayClasses.Contains(className);
    }

    public static bool TryFindVisibleSearch(out IntPtr hwnd, out NativeRect rect)
    {
        hwnd = IntPtr.Zero;
        rect = default;
        IntPtr found = IntPtr.Zero;
        NativeRect foundRect = default;
        NativeMethods.EnumWindows((h, _) =>
        {
            if (!NativeMethods.IsWindowVisible(h) || NativeMethods.IsCloaked(h) || IsTrayChrome(h) || !IsSearch(h))
            {
                return true;
            }

            if (!NativeMethods.GetWindowRect(h, out var r) || r.Width < 200 || r.Height < 60)
            {
                return true;
            }

            found = h;
            foundRect = r;
            return false;
        }, IntPtr.Zero);

        if (found == IntPtr.Zero)
        {
            return false;
        }

        hwnd = found;
        rect = foundRect;
        return true;
    }

    public static bool TryFindVisibleStartMenu(out IntPtr hwnd, out NativeRect rect)
    {
        hwnd = IntPtr.Zero;
        rect = default;
        IntPtr found = IntPtr.Zero;
        NativeRect foundRect = default;
        NativeMethods.EnumWindows((h, _) =>
        {
            if (!NativeMethods.IsWindowVisible(h)
                || NativeMethods.IsCloaked(h)
                || Name(h) != "startmenuexperiencehost")
            {
                return true;
            }

            if (!NativeMethods.GetWindowRect(h, out var r) || r.Width < 200 || r.Height < 200)
            {
                return true;
            }

            found = h;
            foundRect = r;
            return false;
        }, IntPtr.Zero);

        if (found == IntPtr.Zero)
        {
            return false;
        }

        hwnd = found;
        rect = foundRect;
        return true;
    }

    public static string Name(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
        {
            return "";
        }

        NativeMethods.GetWindowThreadProcessId(hwnd, out var pid);
        return Name(pid);
    }

    internal static bool SubstantiallyOverlaps(NativeRect first, NativeRect second)
    {
        var left = Math.Max(first.Left, second.Left);
        var top = Math.Max(first.Top, second.Top);
        var right = Math.Min(first.Right, second.Right);
        var bottom = Math.Min(first.Bottom, second.Bottom);
        if (right <= left || bottom <= top)
        {
            return false;
        }

        var intersection = (long)(right - left) * (bottom - top);
        var smaller = Math.Min(
            (long)first.Width * first.Height,
            (long)second.Width * second.Height);
        return smaller > 0 && intersection * 2 >= smaller;
    }

    public static string Name(uint pid)
    {
        if (pid == 0)
        {
            return "";
        }

        var path = NativeMethods.GetProcessPath(pid);
        return string.IsNullOrEmpty(path)
            ? ""
            : Path.GetFileNameWithoutExtension(path).ToLowerInvariant();
    }

}
