#requires -Version 7.0
# 实机验证：切到 T9 之后，宿主进程里是否成功接住系统输入面板的显示请求，
# 以及官方触摸键盘有没有真的露头。
$ErrorActionPreference = "Stop"

Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
using System.Threading;

[ComImport]
[Guid("71C6E74C-0F28-11D8-A82A-00065B84435C")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface ITfInputProcessorProfileMgr
{
    [PreserveSig] int ActivateProfile(uint type, ushort langid, ref Guid clsid, ref Guid profile, IntPtr hkl, uint flags);
}

public static class TsfSwitch
{
    const uint Flags = 0x20000000 | 0x00000001 | 0x00000004;
    static readonly Guid ClsidProfiles = new Guid("33C53A50-F456-4884-B049-85FD643ECFED");

    public static readonly Guid T9Clsid = new Guid("A7E91C20-4B3D-4F18-9C2A-1B8E6D0A1001");
    public static readonly Guid T9Profile = new Guid("A7E91C20-4B3D-4F18-9C2A-1B8E6D0A2001");
    public static readonly Guid Mspy = new Guid("81D4E9C9-1D3B-41BC-9E6C-4B40BF79E35E");
    public static readonly Guid MspyProfile = new Guid("FA550B04-5AD7-411F-A5AC-CA038EC515D7");

    public static int Activate(Guid clsid, Guid profile)
    {
        int hr = unchecked((int)0x80004005);
        var done = new ManualResetEventSlim(false);
        var thread = new Thread(() =>
        {
            try
            {
                var type = Type.GetTypeFromCLSID(ClsidProfiles, true);
                var mgr = (ITfInputProcessorProfileMgr)Activator.CreateInstance(type);
                var c = clsid; var p = profile;
                hr = mgr.ActivateProfile(1, 0x0804, ref c, ref p, IntPtr.Zero, Flags);
            }
            finally { done.Set(); }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        done.Wait();
        thread.Join();
        return hr;
    }
}
'@

$log = "$env:APPDATA\T9Pane\t9pane.log"

Write-Host "切换到 T9 九键…"
$hr = [TsfSwitch]::Activate([TsfSwitch]::T9Clsid, [TsfSwitch]::T9Profile)
Write-Host ("ActivateProfile hr = 0x{0:X8}" -f $hr)
Start-Sleep -Seconds 2

Write-Host "`n启动记事本作为宿主…"
Start-Process notepad
Start-Sleep -Seconds 4

$np = Get-Process notepad -ErrorAction SilentlyContinue |
    Where-Object { $_.MainWindowHandle -ne 0 } | Select-Object -First 1
if ($np) {
    Add-Type -TypeDefinition 'using System;using System.Runtime.InteropServices;public static class W{[DllImport("user32.dll")]public static extern bool GetWindowRect(IntPtr h,out R r);[DllImport("user32.dll")]public static extern bool SetForegroundWindow(IntPtr h);[StructLayout(LayoutKind.Sequential)]public struct R{public int L,T,Rr,B;}}'
    [W]::SetForegroundWindow($np.MainWindowHandle) | Out-Null
    Start-Sleep -Milliseconds 800
    $r = New-Object 'W+R'
    [W]::GetWindowRect($np.MainWindowHandle, [ref]$r) | Out-Null
    $x = [int](($r.L + $r.Rr)/2); $y = [int]($r.T + ($r.B - $r.T)*0.4)
    Write-Host "记事本 pid=$($np.Id) 注入触摸 ($x,$y)"
    & "D:\T9-ime-temp\dist\probe\TouchTap.exe" $x $y | Out-Host
    Start-Sleep -Seconds 3
}
else {
    Write-Host "没拿到记事本窗口"
}

Write-Host "`n=== 官方键盘窗口状态 ==="
pwsh -NoProfile -ExecutionPolicy Bypass -File "D:\T9-ime-temp\tools\Dump-SipWindows.ps1"

Write-Host "`n=== T9 日志中的激活记录 ==="
if (Test-Path $log) {
    Select-String -Path $log -Pattern "sipCancel|已激活" | Select-Object -Last 15 |
        ForEach-Object { Write-Host $_.Line }
}
else {
    Write-Host "日志不存在：$log"
}
