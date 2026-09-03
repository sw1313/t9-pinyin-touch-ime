#requires -Version 7.0

param(
    [string]$PayloadZip = "",
    [string]$OutDir = ""
)

$ErrorActionPreference = "Stop"
$src = $PSScriptRoot
if (-not $OutDir) {
    $OutDir = Join-Path (Split-Path -Parent (Split-Path -Parent $src)) "dist"
}

New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
$payload = Join-Path $src "payload.zip"
if ($PayloadZip -and (Test-Path -LiteralPath $PayloadZip)) {
    Copy-Item -LiteralPath $PayloadZip -Destination $payload -Force
}
elseif (-not (Test-Path -LiteralPath $payload)) {
    Add-Type -AssemblyName System.IO.Compression
    $empty = [IO.File]::Open($payload, [IO.FileMode]::Create)
    $zip = New-Object IO.Compression.ZipArchive($empty, [IO.Compression.ZipArchiveMode]::Create)
    $zip.Dispose()
    $empty.Dispose()
}

$vswhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
$vs = & $vswhere -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
if (-not $vs) {
    throw "未找到 Visual Studio C++ 生成工具"
}

$vcvars = Join-Path $vs "VC\Auxiliary\Build\vcvarsall.bat"
$outExe = Join-Path $OutDir "T9Setup.exe"
$cmd = @"
call `"$vcvars`" x86
cd /d `"$src`"
rc /nologo /c 65001 /fo T9Setup.res T9Setup.rc
if errorlevel 1 exit /b 1
cl /nologo /O2 /EHsc /utf-8 /std:c++17 /W3 /DUNICODE /D_UNICODE /DWIN32 /D_WINDOWS T9Setup.cpp Install.cpp CertSign.cpp /Fe:`"$outExe`" /link T9Setup.res /SUBSYSTEM:WINDOWS /MANIFEST:NO ole32.lib oleaut32.lib uuid.lib advapi32.lib user32.lib shell32.lib shlwapi.lib gdi32.lib comctl32.lib crypt32.lib wintrust.lib urlmon.lib winhttp.lib
if errorlevel 1 exit /b 1
exit /b 0
"@

$temp = Join-Path $env:TEMP "t9setup-build.cmd"
Set-Content -LiteralPath $temp -Value $cmd -Encoding ASCII
& cmd.exe /c $temp
if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

Write-Output "Built $outExe"
