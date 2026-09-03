using System.Runtime.InteropServices;

namespace T9Pane.Native;

internal static class InputPaneInterop
{
    public static bool TryGetLocation(out NativeRect rect)
    {
        rect = default;
        object? instance = null;
        try
        {
            instance = new FrameworkInputPane();
            if (instance is not IFrameworkInputPane pane)
            {
                return false;
            }

            var hr = pane.Location(out rect);
            return hr == 0 && !rect.IsEmpty;
        }
        catch
        {
            rect = default;
            return false;
        }
        finally
        {
            if (instance is not null && Marshal.IsComObject(instance))
            {
                Marshal.FinalReleaseComObject(instance);
            }
        }
    }
}

[ComImport]
[Guid("D5120AA3-46BA-44C5-822D-CA8092C1FC72")]
internal class FrameworkInputPane;

[ComImport]
[Guid("5752238B-24F0-495A-82F1-2FD593056796")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IFrameworkInputPane
{
    [PreserveSig]
    int Advise(IntPtr pWindow, IFrameworkInputPaneHandler pHandler, out uint pdwCookie);

    [PreserveSig]
    int AdviseWithHWND(IntPtr hwnd, IFrameworkInputPaneHandler pHandler, out uint pdwCookie);

    [PreserveSig]
    int Unadvise(uint dwCookie);

    [PreserveSig]
    int Location(out NativeRect prcInputPaneScreenLocation);
}

[ComImport]
[Guid("226C537B-1E76-4D9E-A760-33DB29922F18")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IFrameworkInputPaneHandler
{
    [PreserveSig]
    int Showing(ref NativeRect prcInputPaneScreenLocation, [MarshalAs(UnmanagedType.Bool)] bool fEnsureFocusedElementInView);

    [PreserveSig]
    int Hiding([MarshalAs(UnmanagedType.Bool)] bool fEnsureFocusedElementInView);
}
