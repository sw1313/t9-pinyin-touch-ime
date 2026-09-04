#requires -Version 7.0
# 用鼠标点一下输入框，触发 SyncCore，量出它（含内部 UIA 探测）的真实耗时。
# 触摸和鼠标进的是同一个 SyncCore 路径，所以鼠标足够测出 UIA 的开销。
$ErrorActionPreference = "Stop"
$log = "$env:APPDATA\T9Pane\t9pane.log"
$out = Join-Path $PSScriptRoot "sync-cost.out"

if (-not (Get-Process T9Pane -ErrorAction SilentlyContinue)) { throw "T9Pane 没在运行" }

Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class M
{
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] public static extern void mouse_event(uint f, uint x, uint y, uint d, IntPtr e);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
    public const uint LeftDown = 0x0002, LeftUp = 0x0004;
    public static void Click(int x, int y)
    {
        SetCursorPos(x, y);
        System.Threading.Thread.Sleep(120);
        mouse_event(LeftDown, 0, 0, 0, IntPtr.Zero);
        System.Threading.Thread.Sleep(60);
        mouse_event(LeftUp, 0, 0, 0, IntPtr.Zero);
    }
}
'@

$before = @(Get-Content $log -Encoding UTF8).Count

$np = Get-Process notepad -ErrorAction SilentlyContinue | Where-Object { $_.MainWindowHandle -ne 0 } | Select-Object -First 1
if (-not $np) {
    Start-Process notepad
    Start-Sleep -Seconds 3
    $np = Get-Process notepad -ErrorAction SilentlyContinue | Where-Object { $_.MainWindowHandle -ne 0 } | Select-Object -First 1
}
if (-not $np) { throw "记事本没起来" }

[M]::SetForegroundWindow($np.MainWindowHandle) | Out-Null
Start-Sleep -Milliseconds 900
$r = New-Object 'M+RECT'
[M]::GetWindowRect($np.MainWindowHandle, [ref]$r) | Out-Null
$cx = [int](($r.Left + $r.Right) / 2)
$cy = [int](($r.Top + $r.Bottom) / 2)

# 点几次，让 SyncCore 反复跑，能看出均值和峰值。
foreach ($i in 1..5) {
    [M]::Click($cx, ($cy + $i * 6))
    Start-Sleep -Milliseconds 700
}
Start-Sleep -Seconds 2

$new = @(Get-Content $log -Encoding UTF8) | Select-Object -Skip $before
$lines = @("=== 新增日志 $($new.Count) 行 ===")
$lines += "`n--- 耗时采样 ---"
$cost = $new | Where-Object { $_ -match '耗时' }
if ($cost) { $lines += $cost } else { $lines += "（没有采样数据：T9Pane 可能不是带 T9PANE_PERF=1 启动的）" }
$lines += "`n--- 全部新增日志 ---"
$lines += $new
$lines | Out-File -FilePath $out -Encoding UTF8
Write-Host "结果写入 $out"
