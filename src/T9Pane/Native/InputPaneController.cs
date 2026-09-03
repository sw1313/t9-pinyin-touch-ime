using System.Runtime.InteropServices;
using T9Pane.Services;

namespace T9Pane.Native;

internal static class InputPaneController
{
    private static readonly Guid IidInterop = new("75CF2C57-9195-4931-8332-F0B409E916AF");
    private static readonly Guid IidInputPane2 = new("23B8D7D0-5C27-4466-985B-7E0C85FB3D93");
    private static DateTime _nextShowUtc = DateTime.MinValue;

    public static bool TryShowFor(IntPtr hwnd)
    {
        if (InputPaneInterop.TryGetLocation(out _))
        {
            return true;
        }

        if (DateTime.UtcNow < _nextShowUtc)
        {
            return false;
        }

        _nextShowUtc = DateTime.UtcNow.AddMilliseconds(700);
        if (hwnd != IntPtr.Zero && TryShowWinRt(hwnd))
        {
            return InputPaneInterop.TryGetLocation(out _);
        }

        SipInvoker.TryShowOnce();
        return InputPaneInterop.TryGetLocation(out _);
    }

    public static void TryHide()
    {
        SipInvoker.Reset();
        SipSuppressor.HideOfficial();
    }

    public static bool TryHideWinRt(IntPtr hwnd) => TryWinRt(hwnd, hide: true);

    private static bool TryShowWinRt(IntPtr hwnd) => TryWinRt(hwnd, hide: false);

    private static bool TryWinRt(IntPtr hwnd, bool hide)
    {
        if (hwnd == IntPtr.Zero)
        {
            hwnd = NativeMethods.GetForegroundWindow();
        }

        if (hwnd == IntPtr.Zero)
        {
            return false;
        }

        var hstr = IntPtr.Zero;
        var factoryPtr = IntPtr.Zero;
        var panePtr = IntPtr.Zero;
        try
        {
            var name = "Windows.UI.ViewManagement.InputPane";
            if (WindowsCreateString(name, name.Length, out hstr) != 0)
            {
                return false;
            }

            var iid = IidInterop;
            if (RoGetActivationFactory(hstr, ref iid, out factoryPtr) != 0 || factoryPtr == IntPtr.Zero)
            {
                return false;
            }

            var interop = (IInputPaneInterop)Marshal.GetObjectForIUnknown(factoryPtr);
            var paneIid = IidInputPane2;
            if (interop.GetForWindow(hwnd, ref paneIid, out panePtr) != 0 || panePtr == IntPtr.Zero)
            {
                return false;
            }

            var pane = (IInputPane2)Marshal.GetObjectForIUnknown(panePtr);
            var hr = hide ? pane.TryHide(out var done) : pane.TryShow(out done);
            return hr == 0 && done != 0;
        }
        catch
        {
            return false;
        }
        finally
        {
            if (panePtr != IntPtr.Zero)
            {
                Marshal.Release(panePtr);
            }

            if (factoryPtr != IntPtr.Zero)
            {
                Marshal.Release(factoryPtr);
            }

            if (hstr != IntPtr.Zero)
            {
                WindowsDeleteString(hstr);
            }
        }
    }

    [DllImport("combase.dll", CharSet = CharSet.Unicode)]
    private static extern int WindowsCreateString(string sourceString, int length, out IntPtr hstring);

    [DllImport("combase.dll")]
    private static extern int WindowsDeleteString(IntPtr hstring);

    [DllImport("combase.dll")]
    private static extern int RoGetActivationFactory(IntPtr activatableClassId, [In] ref Guid iid, out IntPtr factory);

    [ComImport]
    [Guid("75CF2C57-9195-4931-8332-F0B409E916AF")]
    [InterfaceType(ComInterfaceType.InterfaceIsIInspectable)]
    private interface IInputPaneInterop
    {
        [PreserveSig]
        int GetForWindow(IntPtr appWindow, [In] ref Guid riid, out IntPtr inputPane);
    }

    [ComImport]
    [Guid("23B8D7D0-5C27-4466-985B-7E0C85FB3D93")]
    [InterfaceType(ComInterfaceType.InterfaceIsIInspectable)]
    private interface IInputPane2
    {
        [PreserveSig]
        int TryShow(out byte result);

        [PreserveSig]
        int TryHide(out byte result);
    }
}
