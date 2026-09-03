using System.Runtime.InteropServices;
using System.Text;

namespace T9Pane.Native;

internal static class NativeMethods
{
    public static readonly IntPtr HwndMessage = new(-3);
    public const int GwlHwndParent = -8;
    public const int GwlStyle = -16;
    public const int GwlExStyle = -20;
    public const int WsChild = 0x40000000;
    public const int WsPopup = unchecked((int)0x80000000);
    public const int SwpNoOwnerZOrder = 0x0200;
    public static readonly IntPtr HwndTop = IntPtr.Zero;
    public const int WsExLayered = 0x00080000;
    public const int WsExTransparent = 0x00000020;
    public const int WsExNoActivate = 0x08000000;
    public const int WsExToolWindow = 0x00000080;
    public const int WsExTopmost = 0x00000008;
    public const int LwaAlpha = 0x2;
    public const int SwpNoActivate = 0x0010;
    public const int SwpShowWindow = 0x0040;
    public const int SwpHideWindow = 0x0080;
    public const int SwpNoMove = 0x0002;
    public const int SwpNoSize = 0x0001;
    public const int SwpNoZOrder = 0x0004;
    public const int SwpFrameChanged = 0x0020;
    public const int SwpAsyncWindowPos = 0x4000;
    public const int DwmwaCloak = 13;
    public const int DwmwaCloaked = 14;
    public const uint MonitorDefaultToNearest = 2;
    public const int RgnDiff = 4;
    public const int RegionNull = 1;
    public const int RegionSimple = 2;
    public const int RegionComplex = 3;
    public const int WmMouseActivate = 0x0021;
    public const int MaNoActivate = 3;
    public const int SwHide = 0;
    public const int SwShowNoActivate = 4;
    public const uint ZbidUiAccess = 2;
    public const int TokenQuery = 0x0008;
    public const int TokenUiAccess = 26;
    public const uint KeyeventfExtendedkey = 0x0001;
    public const uint KeyeventfKeyup = 0x0002;
    public const uint KeyeventfUnicode = 0x0004;
    public const uint KeyeventfScancode = 0x0008;
    public const uint InputKeyboard = 1;
    public const ushort VkBack = 0x08;
    public const ushort VkTab = 0x09;
    public const ushort VkReturn = 0x0D;
    public const ushort VkShift = 0x10;
    public const ushort VkControl = 0x11;
    public const ushort VkMenu = 0x12;
    public const ushort VkEscape = 0x1B;
    public const ushort VkSpace = 0x20;
    public const ushort VkPrior = 0x21;
    public const ushort VkNext = 0x22;
    public const ushort VkEnd = 0x23;
    public const ushort VkHome = 0x24;
    public const ushort VkLeft = 0x25;
    public const ushort VkUp = 0x26;
    public const ushort VkRight = 0x27;
    public const ushort VkDown = 0x28;
    public const ushort VkInsert = 0x2D;
    public const ushort VkDelete = 0x2E;
    public const ushort VkF1 = 0x70;
    public const ushort VkLWin = 0x5B;
    public const ushort VkV = 0x56;
    public const ushort VkNumpad0 = 0x60;
    public const uint GaRoot = 2;
    public const uint GaRootOwner = 3;
    public const uint EventObjectImeShow = 0x8027;
    public const uint EventObjectImeHide = 0x8028;
    public const uint EventObjectImeChange = 0x8029;
    public const int ChildIdSelf = 0;
    public const int WmCopyData = 0x004A;
    public const int WmKeyDown = 0x0100;
    public const int WmKeyUp = 0x0101;
    public const int WmChar = 0x0102;
    public const int WmQuit = 0x0012;
    public const int WmLeftButtonDown = 0x0201;
    public const int WhMouseLowLevel = 14;
    public const int WmSysCommand = 0x0112;
    public const int ScClose = 0xF060;
    public const int ScTaskList = 0xF130;
    public static readonly IntPtr HwndTopmost = new(-1);

    public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")]
    public static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll")]
    public static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern bool IsWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern bool GetWindowRect(IntPtr hWnd, out NativeRect lpRect);

    [DllImport("user32.dll")]
    public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll")]
    public static extern IntPtr GetKeyboardLayout(uint idThread);

    public const uint RegNotifyChangeName = 0x00000001;
    public const uint RegNotifyChangeLastSet = 0x00000004;

    [DllImport("advapi32.dll", SetLastError = true)]
    public static extern int RegNotifyChangeKeyValue(
        Microsoft.Win32.SafeHandles.SafeRegistryHandle hKey,
        bool watchSubtree,
        uint notifyFilter,
        Microsoft.Win32.SafeHandles.SafeWaitHandle hEvent,
        bool asynchronous);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtr64(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
    private static extern int GetWindowLong32(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern IntPtr SetWindowLongPtr64(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongW")]
    private static extern int SetWindowLong32(IntPtr hWnd, int nIndex, int dwNewLong);

    [DllImport("user32.dll")]
    public static extern bool SetLayeredWindowAttributes(IntPtr hwnd, uint crKey, byte bAlpha, uint dwFlags);

    [DllImport("user32.dll")]
    public static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);

    [DllImport("user32.dll")]
    public static extern bool MoveWindow(IntPtr hWnd, int x, int y, int nWidth, int nHeight, bool bRepaint);

    [DllImport("user32.dll")]
    public static extern IntPtr SetParent(IntPtr hWndChild, IntPtr hWndNewParent);

    [DllImport("user32.dll")]
    public static extern IntPtr GetParent(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern bool ScreenToClient(IntPtr hWnd, ref NativePoint lpPoint);

    [DllImport("user32.dll")]
    public static extern bool EnumChildWindows(IntPtr hWndParent, EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern int SetWindowRgn(IntPtr hWnd, IntPtr hRgn, bool bRedraw);

    [DllImport("user32.dll")]
    public static extern int GetWindowRgn(IntPtr hWnd, IntPtr hRgn);

    [DllImport("gdi32.dll")]
    public static extern IntPtr CreateRectRgn(int x1, int y1, int x2, int y2);

    [DllImport("gdi32.dll")]
    public static extern int CombineRgn(IntPtr hrgnDest, IntPtr hrgnSrc1, IntPtr hrgnSrc2, int fnCombineMode);

    [DllImport("gdi32.dll")]
    public static extern bool DeleteObject(IntPtr ho);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    public static extern IntPtr GetModuleHandle(string? lpModuleName);

    public delegate IntPtr LowLevelMouseDelegate(int code, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern IntPtr SetWindowsHookEx(
        int idHook,
        LowLevelMouseDelegate callback,
        IntPtr module,
        uint threadId);

    [DllImport("user32.dll")]
    public static extern IntPtr CallNextHookEx(
        IntPtr hook,
        int code,
        IntPtr wParam,
        IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool UnhookWindowsHookEx(IntPtr hook);

    [DllImport("user32.dll")]
    public static extern int GetMessage(
        out NativeMessage message,
        IntPtr window,
        uint messageFilterMin,
        uint messageFilterMax);

    [DllImport("user32.dll")]
    public static extern bool PeekMessage(
        out NativeMessage message,
        IntPtr window,
        uint messageFilterMin,
        uint messageFilterMax,
        uint removeMessage);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool PostThreadMessage(
        uint threadId,
        uint message,
        IntPtr wParam,
        IntPtr lParam);

    [DllImport("user32.dll")]
    public static extern IntPtr WindowFromPoint(NativePoint point);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern ushort RegisterClassEx(ref WndClassEx lpwcx);

    [DllImport("user32.dll")]
    public static extern IntPtr DefWindowProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    public static extern bool DestroyWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("kernel32.dll")]
    public static extern IntPtr GetCurrentProcess();

    [DllImport("advapi32.dll", SetLastError = true)]
    public static extern bool OpenProcessToken(IntPtr processHandle, uint desiredAccess, out IntPtr tokenHandle);

    [DllImport("advapi32.dll", SetLastError = true)]
    public static extern bool GetTokenInformation(IntPtr tokenHandle, int tokenInformationClass, out int tokenInformation, int tokenInformationLength, out int returnLength);

    [UnmanagedFunctionPointer(CallingConvention.StdCall, CharSet = CharSet.Unicode)]
    public delegate IntPtr CreateWindowInBandFn(
        int dwExStyle,
        string lpClassName,
        string lpWindowName,
        int dwStyle,
        int x,
        int y,
        int nWidth,
        int nHeight,
        IntPtr hWndParent,
        IntPtr hMenu,
        IntPtr hInstance,
        IntPtr lpParam,
        uint dwBand);

    public delegate IntPtr WndProcFn(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Ansi, ExactSpelling = true, SetLastError = true)]
    public static extern IntPtr GetProcAddress(IntPtr hModule, string procName);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    public delegate int GetWindowBandFn(IntPtr hWnd, out uint pdwBand);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    public delegate int SetWindowBandFn(IntPtr hWnd, IntPtr hwndInsertAfter, uint dwBand);

    public const uint EventSystemForeground = 0x0003;
    public const uint EventSystemAlert = 0x0002;
    public const uint EventObjectFocus = 0x8005;
    public const uint ClsctxInprocServer = 1;
    public const uint CoinitApartmentThreaded = 2;
    public const uint WmGetObject = 0x003D;
    /// <summary>
    /// Chromium 用来探测“是否有辅助技术在运行”的自定义 object id。
    /// 它发出 EVENT_SYSTEM_ALERT 携带这个 id，若随后收到针对该 id 的
    /// WM_GETOBJECT 查询，就认定有 AT 客户端并开启完整无障碍支持。
    /// </summary>
    public const int ChromiumHoneypotObjectId = 1;
    public const uint SmtoAbortIfHung = 0x0002;
    public static readonly IntPtr HwndBroadcast = new(0xFFFF);
    public const uint WmSettingChange = 0x001A;
    public const int ObjidWindow = 0;
    public const int ObjidClient = unchecked((int)0xFFFFFFFC);
    public const int ObjidCaret = unchecked((int)0xFFFFFFF8);
    public const int GuiCaretBlinking = 0x00000001;
    public const uint WineventOutofcontext = 0;
    public const uint WineventSkipownprocess = 0x0002;

    public delegate void WinEventDelegate(
        IntPtr hWinEventHook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint eventThread, uint eventTime);

    [DllImport("user32.dll")]
    public static extern IntPtr SetWinEventHook(
        uint eventMin, uint eventMax, IntPtr hmodWinEventProc, WinEventDelegate lpfnWinEventProc,
        uint idProcess, uint idThread, uint dwFlags);

    [DllImport("user32.dll")]
    public static extern bool UnhookWinEvent(IntPtr hWinEventHook);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern bool SendNotifyMessage(
        IntPtr hWnd,
        uint msg,
        IntPtr wParam,
        string lParam);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern IntPtr SendMessageTimeout(
        IntPtr hWnd,
        uint msg,
        IntPtr wParam,
        IntPtr lParam,
        uint flags,
        uint timeoutMs,
        out IntPtr result);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern IntPtr SendMessageTimeout(
        IntPtr hWnd,
        uint msg,
        IntPtr wParam,
        string lParam,
        uint flags,
        uint timeoutMs,
        out IntPtr result);

    [DllImport("user32.dll")]
    public static extern void NotifyWinEvent(uint eventId, IntPtr hwnd, int idObject, int idChild);

    [DllImport("user32.dll")]
    public static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    public static extern IntPtr GetDesktopWindow();

    [DllImport("user32.dll")]
    public static extern IntPtr GetAncestor(IntPtr hwnd, uint gaFlags);

    [DllImport("user32.dll")]
    public static extern bool PostMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", EntryPoint = "MapVirtualKeyW")]
    public static extern uint MapVirtualKey(uint code, uint mapType);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, ref CopyDataStruct lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "SendMessageW")]
    public static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    public static extern IntPtr GetFocus();

    [DllImport("user32.dll")]
    public static extern bool IsZoomed(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern bool GetGUIThreadInfo(uint idThread, ref GuiThreadInfo pui);

    [DllImport("user32.dll")]
    public static extern bool GetCursorPos(out NativePoint lpPoint);

    [DllImport("user32.dll")]
    public static extern IntPtr SetCapture(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern bool ReleaseCapture();

    [DllImport("user32.dll")]
    public static extern IntPtr GetCapture();

    [DllImport("user32.dll")]
    public static extern bool GetCaretPos(out NativePoint lpPoint);

    [DllImport("user32.dll")]
    public static extern bool ClientToScreen(IntPtr hWnd, ref NativePoint lpPoint);

    [DllImport("user32.dll")]
    public static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);

    [DllImport("kernel32.dll")]
    public static extern uint GetCurrentThreadId();

    [DllImport("user32.dll")]
    public static extern bool GetWindowPlacement(IntPtr hWnd, ref WindowPlacement lpwndpl);

    [DllImport("user32.dll")]
    public static extern bool SetWindowPlacement(IntPtr hWnd, ref WindowPlacement lpwndpl);

    [DllImport("user32.dll")]
    public static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    [DllImport("user32.dll")]
    public static extern bool AllowSetForegroundWindow(uint dwProcessId);

    [DllImport("user32.dll")]
    public static extern IntPtr GetMessageExtraInfo();

    [DllImport("user32.dll")]
    public static extern IntPtr FindWindow(string? lpClassName, string? lpWindowName);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern IntPtr FindWindowEx(
        IntPtr parent,
        IntPtr childAfter,
        string? className,
        string? windowName);

    [DllImport("user32.dll")]
    public static extern uint GetDpiForWindow(IntPtr hwnd);

    [DllImport("dwmapi.dll")]
    public static extern int DwmSetWindowAttribute(IntPtr hwnd, int dwAttribute, ref int pvAttribute, int cbAttribute);

    [DllImport("dwmapi.dll")]
    public static extern int DwmGetWindowAttribute(IntPtr hwnd, int dwAttribute, out int pvAttribute, int cbAttribute);

    [DllImport("user32.dll")]
    public static extern IntPtr MonitorFromRect(ref NativeRect lprc, uint dwFlags);

    [DllImport("user32.dll")]
    public static extern bool GetMonitorInfo(IntPtr hMonitor, ref MonitorInfo lpmi);

    public static bool IsCloaked(IntPtr hwnd)
    {
        return DwmGetWindowAttribute(hwnd, DwmwaCloaked, out var cloaked, sizeof(int)) == 0 && cloaked != 0;
    }

    public static bool TryGetMonitorWork(NativeRect hint, out NativeRect work)
    {
        var monitor = MonitorFromRect(ref hint, MonitorDefaultToNearest);
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (monitor == IntPtr.Zero || !GetMonitorInfo(monitor, ref info))
        {
            work = default;
            return false;
        }

        work = info.Work;
        return !work.IsEmpty;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    public static extern bool QueryFullProcessImageName(IntPtr hProcess, int dwFlags, StringBuilder lpExeName, ref int lpdwSize);

    [DllImport("kernel32.dll")]
    public static extern IntPtr OpenProcess(uint dwDesiredAccess, bool bInheritHandle, uint dwProcessId);

    [DllImport("kernel32.dll")]
    public static extern bool CloseHandle(IntPtr hObject);

    public static IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex)
    {
        return IntPtr.Size == 8
            ? GetWindowLongPtr64(hWnd, nIndex)
            : new IntPtr(GetWindowLong32(hWnd, nIndex));
    }

    public static IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr value)
    {
        return IntPtr.Size == 8
            ? SetWindowLongPtr64(hWnd, nIndex, value)
            : new IntPtr(SetWindowLong32(hWnd, nIndex, value.ToInt32()));
    }

    public static int GetWindowStyle(IntPtr hWnd)
    {
        var value = IntPtr.Size == 8
            ? GetWindowLongPtr64(hWnd, GwlStyle)
            : new IntPtr(GetWindowLong32(hWnd, GwlStyle));
        return unchecked((int)value.ToInt64());
    }

    public static void SetWindowStyle(IntPtr hWnd, int style)
    {
        if (IntPtr.Size == 8)
        {
            SetWindowLongPtr64(hWnd, GwlStyle, new IntPtr(style));
        }
        else
        {
            SetWindowLong32(hWnd, GwlStyle, style);
        }
    }

    public static int GetWindowExStyle(IntPtr hWnd)
    {
        var value = IntPtr.Size == 8
            ? GetWindowLongPtr64(hWnd, GwlExStyle)
            : new IntPtr(GetWindowLong32(hWnd, GwlExStyle));
        return unchecked((int)value.ToInt64());
    }

    public static void SetWindowExStyle(IntPtr hWnd, int exStyle)
    {
        if (IntPtr.Size == 8)
        {
            SetWindowLongPtr64(hWnd, GwlExStyle, new IntPtr(exStyle));
        }
        else
        {
            SetWindowLong32(hWnd, GwlExStyle, exStyle);
        }
    }

    public static string GetWindowClass(IntPtr hWnd)
    {
        var sb = new StringBuilder(256);
        _ = GetClassName(hWnd, sb, sb.Capacity);
        return sb.ToString();
    }

    public static string GetWindowTitle(IntPtr hWnd)
    {
        var sb = new StringBuilder(256);
        _ = GetWindowText(hWnd, sb, sb.Capacity);
        return sb.ToString();
    }

    public static string GetProcessPath(uint processId)
    {
        var handle = OpenProcess(0x1000, false, processId); // PROCESS_QUERY_LIMITED_INFORMATION
        if (handle == IntPtr.Zero)
        {
            return string.Empty;
        }

        try
        {
            var sb = new StringBuilder(1024);
            var size = sb.Capacity;
            return QueryFullProcessImageName(handle, 0, sb, ref size) ? sb.ToString() : string.Empty;
        }
        finally
        {
            CloseHandle(handle);
        }
    }

    [DllImport("ole32.dll")]
    public static extern int CoInitializeEx(IntPtr reserved, uint coInit);

    [DllImport("ole32.dll")]
    public static extern int CoCreateInstance(
        ref Guid rclsid,
        IntPtr outer,
        uint context,
        ref Guid iid,
        out IntPtr ppv);

    [DllImport("msctf.dll")]
    public static extern int TF_CreateThreadMgr(out IntPtr threadMgr);
}

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
internal struct WndClassEx
{
    public int Size;
    public int Style;
    public NativeMethods.WndProcFn WndProc;
    public int ClsExtra;
    public int WndExtra;
    public IntPtr Instance;
    public IntPtr Icon;
    public IntPtr Cursor;
    public IntPtr Background;
    public string? MenuName;
    public string ClassName;
    public IntPtr IconSm;
}

[StructLayout(LayoutKind.Sequential)]
internal struct GuiThreadInfo
{
    public int Size;
    public int Flags;
    public IntPtr Active;
    public IntPtr Focus;
    public IntPtr Capture;
    public IntPtr MenuOwner;
    public IntPtr MoveSize;
    public IntPtr Caret;
    public NativeRect CaretRect;
}

[StructLayout(LayoutKind.Sequential)]
internal struct WindowPlacement
{
    public int Length;
    public int Flags;
    public int ShowCmd;
    public NativePoint MinPosition;
    public NativePoint MaxPosition;
    public NativeRect NormalPosition;
}

[StructLayout(LayoutKind.Sequential)]
internal struct CopyDataStruct
{
    public IntPtr DwData;
    public int CbData;
    public IntPtr LpData;
}

[StructLayout(LayoutKind.Sequential)]
internal struct NativePoint
{
    public int X;
    public int Y;
}

[StructLayout(LayoutKind.Sequential)]
internal struct LowLevelMouseHookData
{
    public NativePoint Point;
    public uint MouseData;
    public uint Flags;
    public uint Time;
    public UIntPtr ExtraInfo;
}

[StructLayout(LayoutKind.Sequential)]
internal struct NativeMessage
{
    public IntPtr Window;
    public uint Message;
    public IntPtr WParam;
    public IntPtr LParam;
    public uint Time;
    public NativePoint Point;
    public uint Private;
}

[StructLayout(LayoutKind.Sequential)]
internal struct NativeRect
{
    public int Left;
    public int Top;
    public int Right;
    public int Bottom;

    public int Width => Right - Left;
    public int Height => Bottom - Top;
    public bool IsEmpty => Width <= 0 || Height <= 0;

    public int Area => Math.Max(0, Width) * Math.Max(0, Height);

    public bool Intersects(NativeRect other)
    {
        return Left < other.Right && Right > other.Left && Top < other.Bottom && Bottom > other.Top;
    }

    public int IntersectionArea(NativeRect other)
    {
        var left = Math.Max(Left, other.Left);
        var top = Math.Max(Top, other.Top);
        var right = Math.Min(Right, other.Right);
        var bottom = Math.Min(Bottom, other.Bottom);
        var w = right - left;
        var h = bottom - top;
        return w > 0 && h > 0 ? w * h : 0;
    }
}

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
internal struct MonitorInfo
{
    public int Size;
    public NativeRect Monitor;
    public NativeRect Work;
    public uint Flags;
}

[StructLayout(LayoutKind.Sequential)]
internal struct INPUT
{
    public uint Type;
    public InputUnion U;
}

/// <summary>
/// x64 上 union 必须按 MOUSEINPUT 对齐，INPUT 才是 40 字节。
/// 只放 KEYBDINPUT 时 SizeOf 会变成 32，SendInput 对 Win/Alt+Tab 无效。
/// 见 pinvoke.net INPUT、StackOverflow 6830651。
/// </summary>
[StructLayout(LayoutKind.Explicit)]
internal struct InputUnion
{
    [FieldOffset(0)]
    public MOUSEINPUT Mi;

    [FieldOffset(0)]
    public KEYBDINPUT Ki;

    [FieldOffset(0)]
    public HARDWAREINPUT Hi;
}

[StructLayout(LayoutKind.Sequential)]
internal struct MOUSEINPUT
{
    public int Dx;
    public int Dy;
    public uint MouseData;
    public uint Flags;
    public uint Time;
    public IntPtr ExtraInfo;
}

[StructLayout(LayoutKind.Sequential)]
internal struct KEYBDINPUT
{
    public ushort Vk;
    public ushort Scan;
    public uint Flags;
    public uint Time;
    public IntPtr ExtraInfo;
}

[StructLayout(LayoutKind.Sequential)]
internal struct HARDWAREINPUT
{
    public uint Msg;
    public ushort ParamL;
    public ushort ParamH;
}
