using System.Runtime.InteropServices;
using T9Pane.Native;

namespace T9Pane.Services;

/// <summary>
/// Raymond Chen / MSDN：按住手指时系统先等，确认不是 press-and-hold
/// 才发左键；按住则改成 <c>WM_RBUTTONDOWN</c>。
/// 触摸键盘按键必须关掉这个手势，否则长按退格会变成右键菜单。
/// </summary>
internal static class TabletGesturePolicy
{
    public static int QueryStatus() =>
        NativeMethods.TabletDisablePressAndHold
        | NativeMethods.TabletDisablePenTapFeedback
        | NativeMethods.TabletDisablePenBarrelFeedback
        | NativeMethods.TabletDisableFlicks;

    public static void DisablePressAndHold(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        GestureConfig[] config =
        [
            new()
            {
                Id = 0,
                Want = 0,
                Block = NativeMethods.GcAllGestures
            }
        ];
        NativeMethods.SetGestureConfig(
            hwnd,
            0,
            (uint)config.Length,
            config,
            (uint)Marshal.SizeOf<GestureConfig>());
        NativeMethods.SetProp(
            hwnd,
            NativeMethods.TabletPenServiceProperty,
            new IntPtr(QueryStatus()));
    }
}
