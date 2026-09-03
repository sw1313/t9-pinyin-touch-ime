#requires -Version 7.0
#requires -RunAsAdministrator

param(
    [Parameter(Mandatory = $true)]
    [string]$Source,
    [Parameter(Mandatory = $true)]
    [string]$Manifest,
    [int]$WaitForPid = 0
)

$ErrorActionPreference = "Stop"
if (-not [Environment]::Is64BitOperatingSystem -or -not [Environment]::Is64BitProcess) {
    throw "请使用 64 位 pwsh 运行安装脚本，以便同时部署和注册 x64/x86 DLL"
}

$dest = Join-Path ${env:ProgramFiles} "T9Pane"
$exe = Join-Path $dest "T9Pane.exe"
$logDir = Join-Path $env:APPDATA "T9Pane"
New-Item -ItemType Directory -Force -Path $logDir | Out-Null
$log = Join-Path $logDir "install-uiaccess.log"

function Write-Log([string]$message) {
    $line = "{0:yyyy-MM-dd HH:mm:ss.fff} {1}" -f (Get-Date), $message
    Add-Content -LiteralPath $log -Value $line
}

trap {
    Write-Log "安装失败: $($_.Exception.Message)"
    exit 1
}

function Grant-AppContainerReadAndExecute([string]$path) {
    $acl = Get-Acl -LiteralPath $path
    $inheritanceFlags =
        if (Test-Path -LiteralPath $path -PathType Container) {
            [Security.AccessControl.InheritanceFlags]"ContainerInherit, ObjectInherit"
        }
        else {
            [Security.AccessControl.InheritanceFlags]::None
        }
    foreach ($sidValue in "S-1-15-2-1", "S-1-15-2-2") {
        $sid = [Security.Principal.SecurityIdentifier]::new($sidValue)
        $rule = [Security.AccessControl.FileSystemAccessRule]::new(
            $sid,
            [Security.AccessControl.FileSystemRights]::ReadAndExecute,
            $inheritanceFlags,
            [Security.AccessControl.PropagationFlags]::None,
            [Security.AccessControl.AccessControlType]::Allow)
        $acl.SetAccessRule($rule)
    }
    Set-Acl -LiteralPath $path -AclObject $acl
}

function Test-AppContainerReadAndExecute([string]$path) {
    $acl = Get-Acl -LiteralPath $path
    $sddl = $acl.GetSecurityDescriptorSddlForm(
        [Security.AccessControl.AccessControlSections]::Access)
    $descriptor = [Security.AccessControl.RawSecurityDescriptor]::new($sddl)
    $requiredMask = [int][Security.AccessControl.FileSystemRights]::ReadAndExecute
    foreach ($sidValue in "S-1-15-2-1", "S-1-15-2-2") {
        $found = $false
        foreach ($ace in $descriptor.DiscretionaryAcl) {
            if ($ace -isnot [Security.AccessControl.QualifiedAce]) {
                continue
            }

            $hasReadAndExecute =
                (($ace.AccessMask -band $requiredMask) -eq $requiredMask)
            if ($ace.SecurityIdentifier.Value -eq $sidValue -and
                $ace.AceQualifier -eq
                    [Security.AccessControl.AceQualifier]::AccessAllowed -and
                $hasReadAndExecute) {
                $found = $true
                break
            }
        }
        if (-not $found) {
            return $false
        }
    }
    return $true
}

function Get-SignedVersionedIme([string]$arch) {
    $sourceArch = Join-Path $Source $arch
    if (-not (Test-Path -LiteralPath $sourceArch -PathType Container)) {
        throw "缺少 $arch 源目录: $sourceArch"
    }

    $candidates = @(
        Get-ChildItem -LiteralPath $sourceArch -File -Filter "T9Ime.*.dll" |
            Sort-Object LastWriteTimeUtc -Descending
    )
    foreach ($candidate in $candidates) {
        $signature = Get-AuthenticodeSignature -LiteralPath $candidate.FullName
        if ($signature.Status -eq
            [Management.Automation.SignatureStatus]::Valid) {
            return $candidate
        }
        Write-Log "忽略未通过签名验证的 $arch DLL: $($candidate.FullName) $($signature.Status)"
    }

    throw "未找到 Authenticode 有效的版本化 $arch T9Ime DLL: $sourceArch"
}

function Get-InprocServer32(
    [Microsoft.Win32.RegistryView]$view,
    [Microsoft.Win32.RegistryHive]$hive =
        [Microsoft.Win32.RegistryHive]::LocalMachine) {
    $baseKey = [Microsoft.Win32.RegistryKey]::OpenBaseKey(
        $hive,
        $view)
    try {
        $key = $baseKey.OpenSubKey(
            "Software\Classes\CLSID\{A7E91C20-4B3D-4F18-9C2A-1B8E6D0A1001}\InprocServer32")
        if (-not $key) {
            return $null
        }
        try {
            return [string]$key.GetValue($null)
        }
        finally {
            $key.Dispose()
        }
    }
    finally {
        $baseKey.Dispose()
    }
}

function Set-UserInprocServer32(
    [Microsoft.Win32.RegistryView]$view,
    [string]$dll) {
    $baseKey = [Microsoft.Win32.RegistryKey]::OpenBaseKey(
        [Microsoft.Win32.RegistryHive]::CurrentUser,
        $view)
    try {
        $key = $baseKey.CreateSubKey(
            "Software\Classes\CLSID\{A7E91C20-4B3D-4F18-9C2A-1B8E6D0A1001}\InprocServer32")
        try {
            $key.SetValue($null, $dll, [Microsoft.Win32.RegistryValueKind]::String)
            $key.SetValue(
                "ThreadingModel",
                "Apartment",
                [Microsoft.Win32.RegistryValueKind]::String)
        }
        finally {
            $key.Dispose()
        }
    }
    finally {
        $baseKey.Dispose()
    }
}

Write-Log "开始安装 Source=$Source Dest=$dest"

if (-not (Test-Path -LiteralPath $Source)) {
    throw "源目录不存在: $Source"
}
if (-not (Test-Path -LiteralPath $Manifest)) {
    throw "清单不存在: $Manifest"
}

New-Item -ItemType Directory -Force -Path $dest | Out-Null
foreach ($item in Get-ChildItem -LiteralPath $Source) {
    if ($item.PSIsContainer -and $item.Name -in @("x64", "x86")) {
        continue
    }
    Copy-Item -LiteralPath $item.FullName -Destination $dest -Recurse -Force
}
Write-Log "已复制到 $dest"

$imeCertificate = Join-Path $dest "T9Ime-Development.cer"
if (-not (Test-Path -LiteralPath $imeCertificate -PathType Leaf)) {
    throw "缺少 T9Ime 签名证书: $imeCertificate"
}
Import-Certificate -FilePath $imeCertificate -CertStoreLocation "Cert:\LocalMachine\Root" | Out-Null
Import-Certificate -FilePath $imeCertificate -CertStoreLocation "Cert:\LocalMachine\TrustedPublisher" | Out-Null
Write-Log "已信任 T9 IME 开发签名证书"

$deployedIme = @{}
foreach ($arch in "x64", "x86") {
    $sourceDll = Get-SignedVersionedIme $arch
    $destArch = Join-Path $dest $arch
    New-Item -ItemType Directory -Force -Path $destArch | Out-Null

    do {
        $deploymentVersion =
            "{0}.{1}" -f
                [DateTime]::UtcNow.ToString("yyyyMMddHHmmssfff"),
                [Guid]::NewGuid().ToString("N").Substring(0, 8)
        $deployedDll = Join-Path $destArch "T9Ime.$deploymentVersion.dll"
    } while (Test-Path -LiteralPath $deployedDll)

    Copy-Item -LiteralPath $sourceDll.FullName -Destination $deployedDll
    [IO.File]::SetLastWriteTimeUtc($deployedDll, [DateTime]::UtcNow)
    $deployedIme[$arch] = $deployedDll
    Write-Log "已部署签名 $arch DLL: $deployedDll"
}

Grant-AppContainerReadAndExecute $dest
Grant-AppContainerReadAndExecute $exe
foreach ($arch in "x64", "x86") {
    Grant-AppContainerReadAndExecute (Join-Path $dest $arch)
    Grant-AppContainerReadAndExecute $deployedIme[$arch]
}
Write-Log "已设置 AppContainer 读取和执行 ACL"

Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class ManifestPatch {
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern IntPtr BeginUpdateResource(string file, bool deleteExisting);
    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool UpdateResource(IntPtr update, IntPtr type, IntPtr name, ushort language, byte[] data, uint size);
    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool EndUpdateResource(IntPtr update, bool discard);
    public static void Apply(string exe, byte[] xml) {
        var update = BeginUpdateResource(exe, false);
        if (update == IntPtr.Zero) throw new System.ComponentModel.Win32Exception();
        if (!UpdateResource(update, (IntPtr)24, (IntPtr)1, 0, xml, (uint)xml.Length)) {
            EndUpdateResource(update, true);
            throw new System.ComponentModel.Win32Exception();
        }
        if (!EndUpdateResource(update, false)) throw new System.ComponentModel.Win32Exception();
    }
}
'@

[ManifestPatch]::Apply($exe, [System.IO.File]::ReadAllBytes($Manifest))
Write-Log "已写入 uiAccess 清单"

$subject = "CN=T9Pane Local"
$cert = Get-ChildItem -LiteralPath "Cert:\CurrentUser\My" |
    Where-Object { $_.Subject -eq $subject -and $_.HasPrivateKey } |
    Select-Object -First 1
if (-not $cert) {
    $cert = New-SelfSignedCertificate -Type CodeSigningCert -Subject $subject -CertStoreLocation "Cert:\CurrentUser\My" -KeyExportPolicy Exportable -NotAfter (Get-Date).AddYears(10)
    Write-Log "已创建代码签名证书 $($cert.Thumbprint)"
}

$cer = Join-Path $env:TEMP "t9pane-local.cer"
Export-Certificate -Cert $cert -FilePath $cer | Out-Null
Import-Certificate -FilePath $cer -CertStoreLocation "Cert:\LocalMachine\Root" | Out-Null
Import-Certificate -FilePath $cer -CertStoreLocation "Cert:\LocalMachine\TrustedPublisher" | Out-Null
Write-Log "证书已导入本机受信任根/发布者"

$sign = Set-AuthenticodeSignature -FilePath $exe -Certificate $cert
Write-Log "签名状态 $($sign.Status) $($sign.StatusMessage)"
if ($sign.Status -ne "Valid") {
    throw "签名失败: $($sign.Status) $($sign.StatusMessage)"
}
if (-not (Test-AppContainerReadAndExecute $exe)) {
    throw "T9Pane.exe 缺少 AppContainer 读取和执行 ACL: $exe"
}

$regsvr32 = @{
    x64 = Join-Path $env:WINDIR "System32\regsvr32.exe"
    x86 = Join-Path $env:WINDIR "SysWOW64\regsvr32.exe"
}
$registryViews = @{
    x64 = [Microsoft.Win32.RegistryView]::Registry64
    x86 = [Microsoft.Win32.RegistryView]::Registry32
}
foreach ($arch in "x64", "x86") {
    $registrar = $regsvr32[$arch]
    if (-not (Test-Path -LiteralPath $registrar -PathType Leaf)) {
        throw "缺少 $arch regsvr32: $registrar"
    }

    $registration = Start-Process `
        -FilePath $registrar `
        -ArgumentList @("/s", ('"{0}"' -f $deployedIme[$arch])) `
        -Wait `
        -PassThru
    if ($registration.ExitCode -ne 0) {
        throw "$arch T9Ime 注册失败，regsvr32 退出码 $($registration.ExitCode)"
    }
    Set-UserInprocServer32 $registryViews[$arch] $deployedIme[$arch]
    Write-Log "已注册 $arch T9Ime: $($deployedIme[$arch])"
}

$programFilesPrefix = [IO.Path]::GetFullPath($dest).TrimEnd("\") + "\"
foreach ($arch in "x64", "x86") {
    $dll = [IO.Path]::GetFullPath([string]$deployedIme[$arch])
    if (-not $dll.StartsWith(
            $programFilesPrefix,
            [StringComparison]::OrdinalIgnoreCase)) {
        throw "$arch DLL 不在 Program Files 安装目录中: $dll"
    }
    if (-not (Test-Path -LiteralPath $dll -PathType Leaf)) {
        throw "$arch DLL 不存在: $dll"
    }

    $signature = Get-AuthenticodeSignature -LiteralPath $dll
    if ($signature.Status -ne
        [Management.Automation.SignatureStatus]::Valid) {
        throw "$arch DLL Authenticode 验证失败: $($signature.Status) $dll"
    }
    if (-not (Test-AppContainerReadAndExecute $dll)) {
        throw "$arch DLL 缺少 AppContainer 读取和执行 ACL: $dll"
    }

    foreach ($hive in @(
            [Microsoft.Win32.RegistryHive]::LocalMachine,
            [Microsoft.Win32.RegistryHive]::CurrentUser)) {
        $registeredDll = Get-InprocServer32 $registryViews[$arch] $hive
        if (-not [string]::Equals(
                $registeredDll,
                $dll,
                [StringComparison]::OrdinalIgnoreCase)) {
            throw "$arch $hive InprocServer32 校验失败。期望 $dll，实际 $registeredDll"
        }
        if (-not (Test-Path -LiteralPath $registeredDll -PathType Leaf)) {
            throw "$arch $hive InprocServer32 指向不存在的文件: $registeredDll"
        }
    }
    Write-Log "已验证 $arch Authenticode、ACL 和 HKLM/HKCU InprocServer32: $dll"
}

if ($WaitForPid -gt 0) {
    $proc = Get-Process -Id $WaitForPid -ErrorAction SilentlyContinue
    if ($proc) {
        Write-Log "等待进程 $WaitForPid 退出"
        [void]$proc.WaitForExit(20000)
    }
}

Write-Log "安装完成；高层副本须由普通桌面会话启动"
