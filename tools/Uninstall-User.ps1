#requires -Version 7.0
$ErrorActionPreference = "Stop"

function Test-IsAdmin {
    $id = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = [Security.Principal.WindowsPrincipal]::new($id)
    $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

if (-not (Test-IsAdmin)) {
    Start-Process pwsh -Verb RunAs -Wait -ArgumentList @(
        "-NoLogo", "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", $PSCommandPath
    )
    exit $LASTEXITCODE
}

$exe = Join-Path $env:ProgramFiles "T9Pane\T9Pane.exe"
Stop-Process -Name T9Pane -Force -ErrorAction SilentlyContinue
if (Test-Path -LiteralPath $exe) {
    & $exe /unregister
}

$dest = Join-Path $env:ProgramFiles "T9Pane"
if (Test-Path -LiteralPath $dest) {
    Remove-Item -LiteralPath $dest -Recurse -Force -ErrorAction SilentlyContinue
}

Write-Host "已注销输入法并尝试删除 $dest"
Write-Host "用户词库和日志仍在 $env:APPDATA\T9Pane ，需要可自行删除。"
Read-Host "按回车关闭"
