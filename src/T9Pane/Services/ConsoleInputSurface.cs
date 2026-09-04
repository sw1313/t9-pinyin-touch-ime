using System.Windows.Automation;
using T9Pane.Native;

namespace T9Pane.Services;

/// <summary>
/// 官方 InputPane / TabTip 把控制台视口本身当成编辑框：
/// <c>ConsoleWindowClass</c>、<c>CASCADIA_HOSTING_WINDOW_CLASS</c>。
/// 整页 Document 在这里是输入面，不是 Chromium 页面离开。
/// </summary>
internal static class ConsoleInputSurface
{
    public static bool IsClass(string className) =>
        className.Equals("ConsoleWindowClass", StringComparison.OrdinalIgnoreCase)
        || className.Equals("Console_2_Window", StringComparison.OrdinalIgnoreCase)
        || className.Equals("CASCADIA_HOSTING_WINDOW_CLASS", StringComparison.OrdinalIgnoreCase);

    public static bool IsWindow(IntPtr hwnd)
    {
        for (var i = 0; hwnd != IntPtr.Zero && i < 6; i++)
        {
            if (IsClass(NativeMethods.GetWindowClass(hwnd)))
            {
                return true;
            }

            var root = NativeMethods.GetAncestor(hwnd, NativeMethods.GaRoot);
            if (root != IntPtr.Zero && root != hwnd && IsClass(NativeMethods.GetWindowClass(root)))
            {
                return true;
            }

            hwnd = NativeMethods.GetParent(hwnd);
        }

        return false;
    }

    public static bool IsForeground()
    {
        var fg = NativeMethods.GetForegroundWindow();
        return fg != IntPtr.Zero
            && IsWindow(NativeMethods.GetAncestor(fg, NativeMethods.GaRoot));
    }

    public static bool IsViewport(ControlType type) =>
        type == ControlType.Document
        || type == ControlType.Pane
        || type == ControlType.Custom
        || type == ControlType.Window;

    /// <summary>
    /// VS Code / Cursor 集成终端：AutomationId 或名称带 Terminal，
    /// 不是网页标题里偶尔出现的 PowerShell。
    /// </summary>
    public static bool LooksLikeTerminalPane(string? name, string? automationId)
    {
        if (HasTerminalToken(automationId))
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        return HasTerminalToken(name)
            || name.StartsWith("Terminal", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsEditable(
        ControlType type,
        string? name,
        string? automationId,
        IntPtr elementHwnd,
        IntPtr foregroundHwnd)
    {
        if (IsWindow(elementHwnd) || IsWindow(foregroundHwnd))
        {
            return IsViewport(type) || InputInvocationProbe.IsTextField(type);
        }

        return IsViewport(type) && LooksLikeTerminalPane(name, automationId);
    }

    private static bool HasTerminalToken(string? text) =>
        !string.IsNullOrWhiteSpace(text)
        && (text.Contains("Terminal", StringComparison.OrdinalIgnoreCase)
            || text.Contains("xterm", StringComparison.OrdinalIgnoreCase));
}
