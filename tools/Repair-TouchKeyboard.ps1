#requires -Version 7.0
# 清掉 0.1.18 之前版本留下的触摸键盘设置残留，并让任务栏托盘区恢复正常。
#
# 旧版在切到 T9 时会把「显示触摸键盘」写成「从不」，只在优雅退出时写回；一旦
# 进程被杀就留在机器上，表现为切回微软拼音也不自动弹键盘、只能手动点图标。
# 它还会 cloak 官方键盘窗口，把托盘区一起弄乱，得重启资源管理器才好。
$ErrorActionPreference = "Stop"

$tip = "HKCU:\Software\Microsoft\TabletTip\1.7"
$input = "HKCU:\Software\Microsoft\Input\Settings"

Get-Process T9Pane -ErrorAction SilentlyContinue | ForEach-Object {
    Write-Host "先停掉旧版 T9Pane（pid=$($_.Id)），否则它会把设置再写回去"
    $_ | Stop-Process -Force
}
Start-Sleep -Milliseconds 500

function Get-Dword([string]$path, [string]$name) {
    (Get-ItemProperty -Path $path -Name $name -ErrorAction SilentlyContinue).$name
}

$held = Get-Dword $tip "T9Pane.Backup.Active"
if ($held -ne 1) {
    Write-Host "没有检测到 T9 备份标记，只做一次兜底还原"
}

# 备份里 Had*=0 表示这个值原本不存在，还原时应当删掉而不是写 0。
$restore = @(
    @{ Name = "TouchKeyboardTapInvoke";
       Had  = (Get-Dword $tip "T9Pane.Backup.HadTouchKeyboardTapInvoke");
       Val  = (Get-Dword $tip "T9Pane.Backup.TouchKeyboardTapInvoke") },
    @{ Name = "EnableDesktopModeAutoInvoke";
       Had  = (Get-Dword $tip "T9Pane.Backup.HadEnableDesktopModeAutoInvoke");
       Val  = (Get-Dword $tip "T9Pane.Backup.EnableDesktopModeAutoInvoke") },
    @{ Name = "TouchKeyboardInvocationPolicy";
       Had  = (Get-Dword $tip "T9Pane.Backup.HadTouchKeyboardInvocationPolicy");
       Val  = (Get-Dword $tip "T9Pane.Backup.TouchKeyboardInvocationPolicy") }
)

foreach ($path in @($tip, $input)) {
    if (-not (Test-Path $path)) { continue }
    foreach ($item in $restore) {
        if ($item.Had -eq 1) {
            Set-ItemProperty -Path $path -Name $item.Name -Value $item.Val -Type DWord
            Write-Host "$path\$($item.Name) = $($item.Val)"
        }
        else {
            Remove-ItemProperty -Path $path -Name $item.Name -ErrorAction SilentlyContinue
            Write-Host "$path\$($item.Name) 已删除（原本不存在）"
        }
    }
}

Get-Item $tip | Select-Object -ExpandProperty Property |
    Where-Object { $_ -like "T9Pane.Backup.*" } |
    ForEach-Object {
        Remove-ItemProperty -Path $tip -Name $_ -ErrorAction SilentlyContinue
        Write-Host "已清除备份项 $_"
    }

Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class Broadcast
{
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern IntPtr SendMessageTimeout(IntPtr hwnd, uint msg, IntPtr wparam,
        string lparam, uint flags, uint timeout, out IntPtr result);

    public static void SettingChange()
    {
        IntPtr r;
        SendMessageTimeout((IntPtr)0xffff, 0x001A, IntPtr.Zero, null, 0x2, 200, out r);
        SendMessageTimeout((IntPtr)0xffff, 0x001A, IntPtr.Zero,
            @"Software\Microsoft\Input\Settings", 0x2, 200, out r);
    }
}
'@
[Broadcast]::SettingChange()
Write-Host "`n已广播设置变更通知"

Get-Process TextInputHost, TabTip -ErrorAction SilentlyContinue |
    Stop-Process -Force -ErrorAction SilentlyContinue

# 托盘区是被 cloak 弄乱的，只有重建 explorer 才会重新布局。
Write-Host "正在重启资源管理器以修复任务栏托盘区…"
Get-Process explorer -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Seconds 3
if (-not (Get-Process explorer -ErrorAction SilentlyContinue)) {
    Start-Process explorer.exe
    Start-Sleep -Seconds 2
}

Write-Host "`n=== 现在的设置 ==="
Get-ItemProperty $tip | Select-Object * -Exclude PS* | Format-List
Get-ItemProperty $input -ErrorAction SilentlyContinue | Select-Object * -Exclude PS* | Format-List
