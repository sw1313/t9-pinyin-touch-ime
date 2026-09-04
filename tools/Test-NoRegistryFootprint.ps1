#requires -Version 7.0
# 验证切到 T9 再切回微软拼音，不会在触摸键盘设置里留下任何痕迹。
#
# 旧版在切到 T9 时把 TouchKeyboardTapInvoke 写成 0（从不），只在优雅退出时写回；
# 进程一被杀就残留，表现为切回微软拼音也不自动弹键盘、只能手动点图标。0.1.19 起
# 改由 T9Ime.dll 在宿主进程内取消面板显示请求，注册表应当全程保持不变。
$ErrorActionPreference = "Stop"

$paths = @(
    "HKCU:\Software\Microsoft\TabletTip\1.7",
    "HKCU:\Software\Microsoft\Input\Settings"
)
$names = @("TouchKeyboardTapInvoke", "EnableDesktopModeAutoInvoke", "TouchKeyboardInvocationPolicy")

function Get-Snapshot {
    $snap = [ordered]@{}
    foreach ($path in $paths) {
        foreach ($name in $names) {
            $v = (Get-ItemProperty -Path $path -Name $name -ErrorAction SilentlyContinue).$name
            $snap["$path\$name"] = if ($null -eq $v) { "<缺失>" } else { "$v" }
        }
        $count = 0
        if (Test-Path $path) {
            $count = @((Get-Item $path).Property | Where-Object { $_ -like "T9Pane.Backup.*" }).Count
        }
        $snap["$path\备份项"] = $count
    }
    return $snap
}

Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;

[ComImport]
[Guid("71C6E74C-0F28-11D8-A82A-00065B84435C")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface ITfInputProcessorProfileMgr
{
    // 只声明 vtable 首个方法，后面的用不到；多声明反而容易把顺序写错。
    [PreserveSig] int ActivateProfile(uint type, ushort langid, ref Guid clsid,
        ref Guid profile, IntPtr hkl, uint flags);
}

public static class TsfSwitch
{
    // TF_IPPMF_FORSESSION | TF_IPPMF_ENABLEPROFILE | TF_IPPMF_DONTCARECURRENTINPUTLANGUAGE
    const uint Flags = 0x20000000 | 0x00000001 | 0x00000004;
    static readonly Guid ClsidProfiles = new Guid("33C53A50-F456-4884-B049-85FD643ECFED");

    public static readonly Guid T9Clsid = new Guid("A7E91C20-4B3D-4F18-9C2A-1B8E6D0A1001");
    public static readonly Guid T9Profile = new Guid("A7E91C20-4B3D-4F18-9C2A-1B8E6D0A1002");
    public static readonly Guid Mspy = new Guid("81D4E9C9-1D3B-41BC-9E6C-4B40BF79E35E");
    public static readonly Guid MspyProfile = new Guid("FA550B04-5AD7-411F-A5AC-CA038EC515D7");

    [DllImport("ole32.dll")]
    static extern int CoCreateInstance(ref Guid clsid, IntPtr outer, uint context,
        ref Guid iid, out IntPtr result);

    public static int Activate(Guid clsid, Guid profile)
    {
        Guid c = ClsidProfiles;
        Guid iid = new Guid("71C6E74C-0F28-11D8-A82A-00065B84435C");
        IntPtr ptr;
        int hr = CoCreateInstance(ref c, IntPtr.Zero, 1, ref iid, out ptr);
        if (hr != 0 || ptr == IntPtr.Zero) return hr == 0 ? -1 : hr;
        var mgr = (ITfInputProcessorProfileMgr)Marshal.GetObjectForIUnknown(ptr);
        try
        {
            // 1 = TF_PROFILETYPE_INPUTPROCESSOR, 0x0804 = zh-CN
            return mgr.ActivateProfile(1, 0x0804, ref clsid, ref profile, IntPtr.Zero, Flags);
        }
        finally { Marshal.Release(ptr); }
    }
}
'@

$log = "$env:APPDATA\T9Pane\t9pane.log"
function Get-LogLines { if (Test-Path $log) { @(Get-Content $log).Count } else { 0 } }

$baseLine = Get-LogLines
$before = Get-Snapshot

$hr = [TsfSwitch]::Activate([TsfSwitch]::T9Clsid, [TsfSwitch]::T9Profile)
Write-Host "切到 T9: hr=0x$($hr.ToString('X8'))"
Start-Sleep -Seconds 4
$during = Get-Snapshot

$newLines = if (Test-Path $log) { @(Get-Content $log) | Select-Object -Skip $baseLine } else { @() }
$activated = $newLines | Select-String -Pattern "T9 九键已激活" | Select-Object -Last 1
if ($activated) { Write-Host "日志确认已激活: $($activated.Line.Trim())" }
else { Write-Host "警告：日志里没看到激活记录，切换可能没生效" -ForegroundColor Yellow }

$hr2 = [TsfSwitch]::Activate([TsfSwitch]::Mspy, [TsfSwitch]::MspyProfile)
Write-Host "切回微软拼音: hr=0x$($hr2.ToString('X8'))"
Start-Sleep -Seconds 4
$after = Get-Snapshot

Write-Host ""
$rows = foreach ($key in $before.Keys) {
    [pscustomobject]@{
        项目     = ($key -replace "HKCU:\\Software\\Microsoft\\", "")
        切换前   = $before[$key]
        T9激活中 = $during[$key]
        切回后   = $after[$key]
        结论     = if ("$($before[$key])" -eq "$($during[$key])" -and
                       "$($before[$key])" -eq "$($after[$key])") { "未改动" } else { "有改动" }
    }
}
$rows | Format-Table -AutoSize

if (@($rows | Where-Object { $_.结论 -eq "有改动" }).Count -gt 0) {
    Write-Host "失败：仍有注册表写入" -ForegroundColor Red
    exit 1
}
if (-not $activated) {
    Write-Host "不确定：注册表没被改，但没能确认 T9 真的激活过" -ForegroundColor Yellow
    exit 2
}
Write-Host "通过：T9 激活期间及切回后，触摸键盘设置全程未被改动" -ForegroundColor Green
