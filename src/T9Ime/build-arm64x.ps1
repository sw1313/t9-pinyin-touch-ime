#requires -Version 7.0
$ErrorActionPreference = "Stop"

$vswhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
$vs = & $vswhere -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
if (-not $vs) { throw "VS C++ Build Tools not found" }

$src = $PSScriptRoot
$out = Join-Path $src "..\T9Pane\bin\Release\net8.0-windows\arm64x"
New-Item -ItemType Directory -Force -Path $out | Out-Null
$dllName = "T9Ime.arm64x.dll"

$lib = Get-ChildItem -LiteralPath (Join-Path $vs "VC\Tools\MSVC") -Recurse -Filter "libcpmt.lib" -ErrorAction SilentlyContinue |
    Where-Object { $_.FullName -match '\\lib\\arm64\\libcpmt\.lib$' } |
    Select-Object -First 1
if (-not $lib) {
    throw "缺少 ARM64 C++ 库。请勾选「MSVC v143 - VS 2022 C++ ARM64 生成工具」。"
}

$vcvars = Join-Path $vs "VC\Auxiliary\Build\vcvarsall.bat"
$vcArch = if ([Environment]::Is64BitOperatingSystem -and [System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture -eq [System.Runtime.InteropServices.Architecture]::Arm64) {
    "arm64"
} else {
    "x64_arm64"
}

$cmd = @"
call `"$vcvars`" $vcArch
cd /d `"$out`"
cl /nologo /c /Foempty_arm64.obj `"$src\empty.cpp`"
if errorlevel 1 exit /b 1
cl /nologo /c /arm64EC /Foempty_x64.obj `"$src\empty.cpp`"
if errorlevel 1 exit /b 1
lib /nologo /machine:x64 /def:`"$src\T9Ime_x64.def`" /out:T9Ime_x64.lib
if errorlevel 1 exit /b 1
lib /nologo /machine:arm64 /def:`"$src\T9Ime_arm64.def`" /out:T9Ime_arm64.lib
if errorlevel 1 exit /b 1
link /nologo /dll /noentry /machine:arm64x /defArm64Native:`"$src\T9Ime_arm64.def`" /def:`"$src\T9Ime_x64.def`" empty_arm64.obj empty_x64.obj /out:`"$dllName`" T9Ime_arm64.lib T9Ime_x64.lib
if errorlevel 1 exit /b 1
exit /b 0
"@

$temp = Join-Path $env:TEMP "t9ime-build-arm64x.cmd"
Set-Content -LiteralPath $temp -Value $cmd -Encoding ASCII
& cmd.exe /c $temp
if ($LASTEXITCODE -ne 0) {
    throw "Arm64X 转发 DLL 编译失败"
}

$mutex = [Threading.Mutex]::new($false, "Local\T9Ime.DevSigning")
$locked = $mutex.WaitOne([TimeSpan]::FromSeconds(30))
if (-not $locked) { throw "Timed out waiting for the T9 IME signing lock" }
try {
    $subject = "CN=T9 IME Local Development"
    $certificate = Get-ChildItem -Path Cert:\CurrentUser\My -CodeSigningCert |
        Where-Object { $_.Subject -eq $subject -and $_.HasPrivateKey -and $_.NotAfter -gt (Get-Date).AddDays(30) } |
        Sort-Object NotAfter -Descending |
        Select-Object -First 1
    if (-not $certificate) {
        $certificate = New-SelfSignedCertificate `
            -Subject $subject `
            -Type CodeSigningCert `
            -CertStoreLocation Cert:\CurrentUser\My `
            -HashAlgorithm SHA256 `
            -KeyAlgorithm RSA `
            -KeyLength 2048 `
            -NotAfter (Get-Date).AddYears(5)
    }

    foreach ($storeName in "Root", "TrustedPublisher") {
        $store = [Security.Cryptography.X509Certificates.X509Store]::new(
            $storeName,
            [Security.Cryptography.X509Certificates.StoreLocation]::CurrentUser)
        try {
            $store.Open([Security.Cryptography.X509Certificates.OpenFlags]::ReadWrite)
            if (-not $store.Certificates.Find(
                    [Security.Cryptography.X509Certificates.X509FindType]::FindByThumbprint,
                    $certificate.Thumbprint,
                    $false).Count) {
                $store.Add($certificate)
            }
        }
        finally {
            $store.Close()
        }
    }

    Export-Certificate `
        -Cert $certificate `
        -FilePath (Join-Path (Split-Path $out -Parent) "T9Ime-Development.cer") `
        -Force | Out-Null

    $versionedDll = Join-Path $out $dllName
    $fileAcl = Get-Acl -LiteralPath $versionedDll
    foreach ($sidValue in "S-1-15-2-1", "S-1-15-2-2") {
        $sid = [Security.Principal.SecurityIdentifier]::new($sidValue)
        $rule = [Security.AccessControl.FileSystemAccessRule]::new(
            $sid,
            [Security.AccessControl.FileSystemRights]::ReadAndExecute,
            [Security.AccessControl.InheritanceFlags]::None,
            [Security.AccessControl.PropagationFlags]::None,
            [Security.AccessControl.AccessControlType]::Allow)
        $fileAcl.SetAccessRule($rule)
    }
    Set-Acl -LiteralPath $versionedDll -AclObject $fileAcl

    $signature = Set-AuthenticodeSignature `
        -LiteralPath $versionedDll `
        -Certificate $certificate `
        -HashAlgorithm SHA256
    if ($signature.Status -ne [Management.Automation.SignatureStatus]::Valid) {
        throw "Authenticode signing failed: $($signature.Status) $($signature.StatusMessage)"
    }
    Write-Output "Built and signed $versionedDll"
}
finally {
    if ($locked) { $mutex.ReleaseMutex() }
    $mutex.Dispose()
}
