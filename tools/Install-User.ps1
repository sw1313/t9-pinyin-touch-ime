#requires -Version 7.0
$ErrorActionPreference = "Stop"

function Test-Is64BitPwsh {
    [Environment]::Is64BitOperatingSystem -and [Environment]::Is64BitProcess
}

function Test-IsAdmin {
    $id = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = [Security.Principal.WindowsPrincipal]::new($id)
    $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

$root = Split-Path -Parent $PSScriptRoot
if (-not (Test-Path -LiteralPath (Join-Path $root "T9Pane.exe"))) {
    throw "找不到 T9Pane.exe。请解压完整安装包后再运行 Install.bat。"
}

if (-not (Test-Is64BitPwsh)) {
    throw "请使用 64 位 pwsh 安装，以便同时注册 x64/x86 输入法 DLL。"
}

if (-not (Test-IsAdmin)) {
    $self = $PSCommandPath
    Start-Process pwsh -Verb RunAs -Wait -ArgumentList @(
        "-NoLogo", "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", $self
    )
    exit $LASTEXITCODE
}

$install = Join-Path $PSScriptRoot "Install-UiAccess.ps1"
$manifest = Join-Path $root "app.uia.manifest"
if (-not (Test-Path -LiteralPath $install)) {
    throw "缺少 Tools\Install-UiAccess.ps1"
}

& $install -Source $root -Manifest $manifest -WaitForPid 0
$exe = Join-Path $env:ProgramFiles "T9Pane\T9Pane.exe"
if (Test-Path -LiteralPath $exe) {
    Start-Process explorer.exe -ArgumentList "`"$exe`""
}

Write-Host ""
Write-Host "安装完成。按 Win+空格 切到「T9 九键」，再点一下输入框。"
Write-Host "日志： $env:APPDATA\T9Pane\t9pane.log"
Read-Host "按回车关闭"
