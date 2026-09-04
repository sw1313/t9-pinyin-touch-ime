#requires -Version 7.0
# 原样列出官方触摸键盘相关进程的全部顶层窗口，用来判断它到底有没有真的显示。
$ErrorActionPreference = "Stop"

Add-Type -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

public static class SipDump
{
    delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr param);

    [DllImport("user32.dll")] static extern bool EnumWindows(EnumWindowsProc cb, IntPtr param);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);
    [DllImport("user32.dll")] static extern int GetClassName(IntPtr hwnd, StringBuilder buf, int max);
    [DllImport("user32.dll")] static extern int GetWindowText(IntPtr hwnd, StringBuilder buf, int max);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
    [DllImport("dwmapi.dll")] static extern int DwmGetWindowAttribute(IntPtr hwnd, int attr, out int val, int size);

    [StructLayout(LayoutKind.Sequential)]
    struct RECT { public int Left, Top, Right, Bottom; }

    public static string[] All()
    {
        var rows = new List<string>();
        EnumWindows((hwnd, param) =>
        {
            uint pid;
            GetWindowThreadProcessId(hwnd, out pid);
            string proc;
            try { proc = Process.GetProcessById((int)pid).ProcessName.ToLowerInvariant(); }
            catch { return true; }

            if (proc != "textinputhost" && proc != "tabtip" && proc != "osk"
                && !proc.Contains("textinput")) return true;

            int cloaked = -1;
            DwmGetWindowAttribute(hwnd, 14, out cloaked, 4);

            RECT r;
            GetWindowRect(hwnd, out r);
            var cls = new StringBuilder(256);
            GetClassName(hwnd, cls, cls.Capacity);
            var title = new StringBuilder(256);
            GetWindowText(hwnd, title, title.Capacity);

            rows.Add(string.Format("{0,-16} vis={1,-5} cloaked={2,-3} {3,4}x{4,-4} @({5},{6}) cls={7} title='{8}'",
                proc, IsWindowVisible(hwnd), cloaked,
                r.Right - r.Left, r.Bottom - r.Top, r.Left, r.Top,
                cls, title));
            return true;
        }, IntPtr.Zero);
        return rows.ToArray();
    }
}
'@

$procs = Get-Process TextInputHost, TabTip, osk -ErrorAction SilentlyContinue
if ($procs) {
    Write-Host "进程：" -NoNewline
    Write-Host (($procs | ForEach-Object { "$($_.ProcessName)($($_.Id))" }) -join ", ")
}
else {
    Write-Host "没有触摸键盘相关进程在跑"
}

$rows = [SipDump]::All()
if ($rows.Count -eq 0) {
    Write-Host "未枚举到任何相关窗口"
}
else {
    $rows | ForEach-Object { Write-Host $_ }
}
