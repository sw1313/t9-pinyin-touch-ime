using System.Runtime.InteropServices;
using T9Pane.Native;

namespace T9Pane.Services;

internal static class SipInvoker
{
    private static DateTime _nextAllowedUtc = DateTime.MinValue;

    public static void Reset() => _nextAllowedUtc = DateTime.MinValue;

    public static void TryShowOnce()
    {
        if (InputPaneInterop.TryGetLocation(out _))
        {
            return;
        }

        if (DateTime.UtcNow < _nextAllowedUtc)
        {
            return;
        }

        _nextAllowedUtc = DateTime.UtcNow.AddMilliseconds(900);
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

[ComImport]
[Guid("4ce576fa-83dc-4F88-951c-9d0782b4e376")]
internal class UiHostNoLaunch;

[ComImport]
[Guid("37c994e7-432b-4834-a2f7-dce1f13b834b")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface ITipInvocation
{
    void Toggle(IntPtr hwndDesktop);
}
