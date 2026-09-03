#requires -Version 7.0
$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $PSScriptRoot
$version = "0.1.0"
$csproj = Join-Path $root "src\T9Pane\T9Pane.csproj"
$match = Select-String -LiteralPath $csproj -Pattern "<Version>([^<]+)</Version>" | Select-Object -First 1
if ($match) {
    $version = $match.Matches[0].Groups[1].Value
}

$name = "T9-Pinyin-Touch-IME-$version"
$release = Join-Path $root "src\T9Pane\bin\Release\net8.0-windows"
$dist = Join-Path $root "dist"
$stage = Join-Path $dist "payload"
$zip = Join-Path $dist "payload.zip"
$setup = Join-Path $dist "$name-Setup.exe"

Write-Host "构建托管程序 Release $version"
dotnet build $csproj -c Release --nologo
if ($LASTEXITCODE -ne 0) {
    throw "dotnet build 失败"
}

Write-Host "构建 TSF DLL x64 / x86"
foreach ($arch in "x64", "x86") {
    pwsh -NoLogo -NoProfile -File (Join-Path $root "src\T9Ime\build.ps1") -Arch $arch
    if ($LASTEXITCODE -ne 0) {
        throw "T9Ime $arch 失败"
    }
}

if (Test-Path -LiteralPath $stage) {
    Remove-Item -LiteralPath $stage -Recurse -Force
}
New-Item -ItemType Directory -Force -Path $dist, $stage | Out-Null

Get-ChildItem -LiteralPath $release | ForEach-Object {
    if ($_.PSIsContainer -and $_.Name -in @("x64", "x86")) {
        return
    }
    if ($_.Extension -in ".pdb", ".xml") {
        return
    }
    Copy-Item -LiteralPath $_.FullName -Destination (Join-Path $stage $_.Name) -Recurse -Force
}

Get-ChildItem -LiteralPath $stage -Recurse -Include *.pdb, *.xml | Remove-Item -Force -ErrorAction SilentlyContinue
Remove-Item -LiteralPath (Join-Path $stage "Tools\Test-UwpIme.ps1") -Force -ErrorAction SilentlyContinue
Remove-Item -LiteralPath (Join-Path $stage "Tools\Install-UiAccess.ps1") -Force -ErrorAction SilentlyContinue

foreach ($arch in "x64", "x86") {
    $imeDir = Join-Path $release $arch
    $signed = Get-ChildItem -LiteralPath $imeDir -File -Filter "T9Ime.*.dll" |
        Sort-Object LastWriteTimeUtc -Descending |
        Where-Object { (Get-AuthenticodeSignature -LiteralPath $_.FullName).Status -eq "Valid" } |
        Select-Object -First 1
    if (-not $signed) {
        throw "缺少已签名的 $arch T9Ime DLL"
    }

    $destArch = Join-Path $stage $arch
    New-Item -ItemType Directory -Force -Path $destArch | Out-Null
    Copy-Item -LiteralPath $signed.FullName -Destination $destArch -Force
}

$cer = Join-Path $release "T9Ime-Development.cer"
if (-not (Test-Path -LiteralPath $cer)) {
    throw "缺少 T9Ime-Development.cer"
}
Copy-Item -LiteralPath $cer -Destination $stage -Force
Copy-Item -LiteralPath (Join-Path $root "LICENSE") -Destination $stage -Force
Copy-Item -LiteralPath (Join-Path $root "THIRD_PARTY.md") -Destination $stage -Force
Copy-Item -LiteralPath (Join-Path $root "README.md") -Destination $stage -Force

if (Test-Path -LiteralPath $zip) {
    Remove-Item -LiteralPath $zip -Force
}
Compress-Archive -Path (Join-Path $stage "*") -DestinationPath $zip -CompressionLevel Optimal

Write-Host "构建 Win32 安装程序"
pwsh -NoLogo -NoProfile -File (Join-Path $root "src\T9Setup\build.ps1") -PayloadZip $zip -OutDir $dist
if ($LASTEXITCODE -ne 0) {
    throw "T9Setup 编译失败"
}

Copy-Item -LiteralPath (Join-Path $dist "T9Setup.exe") -Destination $setup -Force
Remove-Item -LiteralPath (Join-Path $root "src\T9Setup\payload.zip") -Force -ErrorAction SilentlyContinue
$size = [math]::Round((Get-Item -LiteralPath $setup).Length / 1MB, 2)
Write-Host "已生成 $setup ($size MB)"
Write-Host "双击这个 Setup.exe 即可安装，不需要 PowerShell 或 bat。"
