using System.IO;
using T9Pane.Native;

namespace T9Pane.Services;

/// <summary>
/// 只负责识别官方触摸键盘的进程和窗口几何，用来判断触摸点是否落在它上面。
///
/// 不再压制它：改注册表会留下全机生效的残留（切回微软拼音也弹不出键盘），
/// cloak 它的窗口会连带弄坏任务栏托盘区，而把这些放在触摸的同步路径上会拖慢
/// 连打。阻止弹出改由 T9Ime.dll 在宿主进程内取消系统输入面板的显示请求完成。
/// </summary>
internal static class SipSuppressionPolicy
{
    public static readonly string[] ProcessNames =
    [
        "textinputhost",
        "tabtip",
        "tabtip32",
        "osk",
        "windowsinternal.composableshell.experiences.textinput.inputapp"
    ];

    public static bool ShouldEnablePointerHookForProfile(
        bool canCommitForeground,
        bool hasForegroundProfileLease,
        bool hasSystemProfileLease,
        bool officialT9Selected) =>
        PointerIntentTrackingPolicy.ShouldEnableForSession(
            canCommitForeground,
            hasForegroundProfileLease,
            hasSystemProfileLease,
            officialT9Selected);

    /// <summary>
    /// 拦官方触摸键盘只在语言栏就是 T9 时生效。
    /// 残留 TSF 客户端、搜索框租约都不能继续拦。
    /// </summary>
    public static bool ShouldSuppressOfficialSip(bool officialT9Selected) =>
        officialT9Selected;

    public static bool IsOfficialSipProcess(string? processName)
    {
        if (string.IsNullOrWhiteSpace(processName))
        {
            return false;
        }

        var name = Path.GetFileNameWithoutExtension(processName).ToLowerInvariant();
        return ProcessNames.Contains(name)
            || name.Contains("textinput", StringComparison.Ordinal);
    }

    public static bool IsFullscreenSipHost(NativeRect rect, NativeRect work) =>
        !rect.IsEmpty
        && !work.IsEmpty
        && (rect.Height > work.Height * 0.70 || rect.Area > work.Area * 0.70);

    public static bool LooksLikeTouchKeyboard(NativeRect rect, NativeRect work)
    {
        if (rect.IsEmpty || work.IsEmpty)
        {
            return false;
        }

        var width = rect.Width;
        var height = rect.Height;
        if (width < 280 || height < 140)
        {
            return false;
        }

        if (IsFullscreenSipHost(rect, work))
        {
            return false;
        }

        var docked = width >= work.Width * 0.55
                     && height <= work.Height * 0.68
                     && height >= 160
                     && Math.Abs(rect.Bottom - work.Bottom) <= 96;

        var floating = width >= 360
                       && width <= work.Width * 0.92
                       && height is >= 160 and <= 640
                       && width >= height * 1.05
                       && width * height < work.Area * 0.50;

        return docked || floating;
    }

    public static bool LooksLikeSuppressibleSip(NativeRect rect, NativeRect work)
    {
        if (LooksLikeTouchKeyboard(rect, work))
        {
            return true;
        }

        if (rect.IsEmpty || work.IsEmpty || IsFullscreenSipHost(rect, work))
        {
            return false;
        }

        return rect.Width >= 280 && rect.Height >= 140 && rect.Area < work.Area * 0.72;
    }

    public static bool IsOfficialSipSurface(bool sipProcess, NativeRect window, NativeRect work) =>
        sipProcess && LooksLikeSipHitSurface(window, work);

    /// <summary>
    /// 只有盘面或盘面上的小键才挡命中。TextInputHost 常有一块很大但不到全屏的
    /// 宿主，不能把中间输入区的点击一律收成 Unavailable。
    /// </summary>
    public static bool LooksLikeSipHitSurface(NativeRect rect, NativeRect work)
    {
        if (LooksLikeTouchKeyboard(rect, work))
        {
            return true;
        }

        if (rect.IsEmpty || work.IsEmpty || IsFullscreenSipHost(rect, work))
        {
            return false;
        }

        var inDockBand = rect.Height <= 120
            && rect.Width <= work.Width
            && rect.Bottom >= work.Bottom - (int)(work.Height * 0.68)
            && rect.Bottom <= work.Bottom + 16;
        return inDockBand;
    }
}

internal static class OfficialSipHit
{
    public static bool IsKeyboardSurface(int x, int y)
    {
        var hwnd = NativeMethods.WindowFromPoint(new NativePoint { X = x, Y = y });
        if (hwnd == IntPtr.Zero)
        {
            return false;
        }

        NativeMethods.GetWindowThreadProcessId(hwnd, out var pid);
        if (!SipSuppressionPolicy.IsOfficialSipProcess(NativeMethods.GetProcessPath(pid)))
        {
            return false;
        }

        if (!NativeMethods.GetWindowRect(hwnd, out var rect) || rect.IsEmpty)
        {
            return false;
        }

        if (!NativeMethods.TryGetMonitorWork(rect, out var work) || work.IsEmpty)
        {
            return true;
        }

        return SipSuppressionPolicy.IsOfficialSipSurface(true, rect, work);
    }
}
