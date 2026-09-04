#requires -Version 7.0
# 自己切 T9 / 微软拼音，核对本机 T9Pane 是否跟上语言栏的切换。
#
# 0.1.19 起不再改「显示触摸键盘」，所以这里只验收语言栏跟随；
# 「不写注册表」由 Test-NoRegistryFootprint.ps1 单独验收。
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$csproj = Join-Path $root 'src\T9Pane\T9Pane.csproj'
$version = (Select-String -LiteralPath $csproj -Pattern '<Version>([^<]+)</Version>').Matches[0].Groups[1].Value
$setup = Join-Path $root "dist\T9-Pinyin-Touch-IME-$version-Setup.exe"
$log = Join-Path $env:APPDATA 'T9Pane\t9pane.log'
Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
using System.Threading;
public static class LayoutHoldTsf {
  const uint Flags = 0x20000000 | 0x00000001 | 0x00000004;
  static readonly Guid Clsid = new Guid("33C53A50-F456-4884-B049-85FD643ECFED");
  static readonly Guid Iid = new Guid("71C6E74C-0F28-11D8-A82A-00065B84435C");
  static readonly Guid Tip = new Guid("34745C63-B2F0-4784-8B67-5E12C8701A31");
  public static readonly Guid T9 = new Guid("A7E91C20-4B3D-4F18-9C2A-1B8E6D0A1001");
  public static readonly Guid T9Profile = new Guid("A7E91C20-4B3D-4F18-9C2A-1B8E6D0A1002");
  public static readonly Guid Mspy = new Guid("81D4E9C9-1D3B-41BC-9E6C-4B40BF79E35E");
  public static readonly Guid MspyProfile = new Guid("FA550B04-5AD7-411F-A5AC-CA038EC515D7");
  [DllImport("ole32.dll")] static extern int CoCreateInstance(ref Guid c, IntPtr o, uint ctx, ref Guid i, out IntPtr p);
  [DllImport("ole32.dll")] static extern int CoInitializeEx(IntPtr r, uint f);
  [StructLayout(LayoutKind.Sequential)]
  struct Tf { public uint t; public ushort lang; public Guid clsid; public Guid profile; public Guid cat; public IntPtr sub; public uint caps; public IntPtr hkl; public uint flags; }
  [ComImport, Guid("71C6E74C-0F28-11D8-A82A-00065B84435C"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
  interface IMgr {
    [PreserveSig] int ActivateProfile(uint type, ushort langid, ref Guid clsid, ref Guid profile, IntPtr hkl, uint flags);
    [PreserveSig] int DeactivateProfile(uint type, ushort langid, ref Guid clsid, ref Guid profile, IntPtr hkl, uint flags);
    [PreserveSig] int GetProfile(uint type, ushort langid, ref Guid clsid, ref Guid profile, IntPtr hkl, out Tf p);
    [PreserveSig] int EnumProfiles(ushort langid, out IntPtr e);
    [PreserveSig] int ReleaseInputProcessor(ref Guid clsid, uint flags);
    [PreserveSig] int RegisterProfile(ref Guid clsid, ushort langid, ref Guid profile, [MarshalAs(UnmanagedType.LPWStr)] string d, uint cd, [MarshalAs(UnmanagedType.LPWStr)] string ic, uint ci, uint idx, IntPtr h, uint pref, int en, uint flags);
    [PreserveSig] int UnregisterProfile(ref Guid clsid, ushort langid, ref Guid profile, uint flags);
    [PreserveSig] int GetActiveProfile(ref Guid cat, out Tf p);
  }
  static int OnSta(Func<IMgr, int> body) {
    var hr = unchecked((int)0x80004005);
    var done = new ManualResetEvent(false);
    var t = new Thread(() => {
      try {
        CoInitializeEx(IntPtr.Zero, 2);
        var c = Clsid; var i = Iid;
        if (CoCreateInstance(ref c, IntPtr.Zero, 1, ref i, out var p) != 0 || p == IntPtr.Zero) return;
        hr = body((IMgr)Marshal.GetObjectForIUnknown(p));
        Marshal.Release(p);
      } finally { done.Set(); }
    });
    t.SetApartmentState(ApartmentState.STA);
    t.Start();
    done.WaitOne();
    t.Join();
    return hr;
  }
  public static string ActiveName() {
    var name = "fail";
    OnSta(mgr => {
      var cat = Tip;
      var hr = mgr.GetActiveProfile(ref cat, out var a);
      name = a.clsid == T9 ? "T9" : (a.clsid == Mspy ? "MSPY" : a.clsid.ToString("D"));
      return hr;
    });
    return name;
  }
  public static int ActivateT9() => OnSta(mgr => { var c = T9; var p = T9Profile; return mgr.ActivateProfile(1, 0x0804, ref c, ref p, IntPtr.Zero, Flags); });
  public static int ActivateMspy() => OnSta(mgr => { var c = Mspy; var p = MspyProfile; return mgr.ActivateProfile(1, 0x0804, ref c, ref p, IntPtr.Zero, Flags); });
}
'@

function Get-InstalledVersion {
    $exe = 'C:\Program Files\T9Pane\T9Pane.exe'
    if (-not (Test-Path -LiteralPath $exe)) { return $null }
    return (Get-Item -LiteralPath $exe).VersionInfo.FileVersion
}

function Wait-T9Pane([string]$want) {
    $end = [datetime]::UtcNow.AddSeconds(40)
    while ([datetime]::UtcNow -lt $end) {
        $proc = Get-Process -Name T9Pane -ErrorAction SilentlyContinue
        $ver = Get-InstalledVersion
        if ($proc -and $ver -and $ver.StartsWith($want)) { return }
        Start-Sleep -Milliseconds 400
    }
    throw "T9Pane $want 没有在 40 秒内起来（当前 $(Get-InstalledVersion)）"
}

function Wait-Log([string]$pattern, [datetime]$after, [int]$seconds) {
    $end = [datetime]::UtcNow.AddSeconds($seconds)
    while ([datetime]::UtcNow -lt $end) {
        if (Test-Path -LiteralPath $log) {
            foreach ($line in Get-Content -LiteralPath $log -Encoding UTF8 -Tail 80) {
                if ($line -notmatch $pattern) { continue }
                if ($line -match '^(\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2})') {
                    $ts = [datetime]::ParseExact($Matches[1], 'yyyy-MM-dd HH:mm:ss', $null)
                    if ($ts -ge $after.AddSeconds(-1)) { return $line }
                }
            }
        }
        Start-Sleep -Milliseconds 300
    }
    return $null
}

Write-Host "验收语言栏 Hold  目标版本 $version"
$installed = Get-InstalledVersion
if (-not $installed -or -not $installed.StartsWith($version)) {
    if (-not (Test-Path -LiteralPath $setup)) { throw "缺少安装包 $setup" }
    Write-Host "静默安装 $setup （已装 $installed）"
    $p = Start-Process -FilePath $setup -ArgumentList '/quiet' -Wait -PassThru
    if ($p.ExitCode -ne 0) { throw "静默安装失败 exit=$($p.ExitCode)" }
}

if (-not (Get-Process -Name T9Pane -ErrorAction SilentlyContinue)) {
    $explorer = Join-Path $env:WINDIR 'explorer.exe'
    Start-Process -FilePath $explorer -ArgumentList '"C:\Program Files\T9Pane\T9Pane.exe"'
}

Wait-T9Pane $version
$restore = [LayoutHoldTsf]::ActiveName()
Write-Host "当前 GetActiveProfile=$restore"
$failed = $false
try {
    # 起点必须是「不在 T9」，否则下面切到 T9 是空操作、根本不会产生切换日志。
    if ($restore -eq 'T9') {
        Write-Host '当前已在 T9，先切到微软拼音建立基线'
        if ([LayoutHoldTsf]::ActivateMspy() -ne 0) { throw '建立基线失败' }
        Start-Sleep -Seconds 2
    }

    $t9At = Get-Date
    $hr = [LayoutHoldTsf]::ActivateT9()
    Write-Host ("ActivateT9 hr=0x{0:X8} active={1}" -f $hr, [LayoutHoldTsf]::ActiveName())
    if ($hr -ne 0) { throw "ActivateT9 失败" }
    $line = Wait-Log '语言栏布局 T9 九键' $t9At 10
    if ($line) {
        Write-Host "T9 日志=$line"
    }
    else {
        Write-Host "FAIL: 切到 T9 后 T9Pane 没跟上语言栏"
        $failed = $true
    }

    $msAt = Get-Date
    $hr = [LayoutHoldTsf]::ActivateMspy()
    Write-Host ("ActivateMspy hr=0x{0:X8} active={1}" -f $hr, [LayoutHoldTsf]::ActiveName())
    if ($hr -ne 0) { throw "ActivateMspy 失败" }
    $line = Wait-Log '语言栏布局 其他输入法' $msAt 10
    if ($line) {
        Write-Host "MSPY 日志=$line"
    }
    else {
        Write-Host "FAIL: 切走 T9 后 T9Pane 没跟上语言栏"
        $failed = $true
    }
} finally {
    if ($restore -eq 'T9') { [void][LayoutHoldTsf]::ActivateT9() } else { [void][LayoutHoldTsf]::ActivateMspy() }
}

if ($failed) { throw '语言栏 Hold 验收失败' }
Write-Host '语言栏 Hold 验收通过'
