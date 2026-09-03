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
$x86Out = Join-Path $dist "publish-win-x86"
$armOut = Join-Path $dist "publish-win-arm64"
$zip = Join-Path $dist "payload.zip"
$setup = Join-Path $dist "$name-Setup.exe"

Write-Host "发布 x64 / x86 / ARM64 托管程序 $version"
if (Test-Path -LiteralPath $stage) {
    Remove-Item -LiteralPath $stage -Recurse -Force
}

dotnet publish $csproj -c Release -r win-x64 --self-contained false -o $stage --nologo
if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish win-x64 失败"
}

if (Test-Path -LiteralPath $x86Out) {
    Remove-Item -LiteralPath $x86Out -Recurse -Force
}

dotnet publish $csproj -c Release -r win-x86 --self-contained false -o $x86Out --nologo
if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish win-x86 失败"
}

if (Test-Path -LiteralPath $armOut) {
    Remove-Item -LiteralPath $armOut -Recurse -Force
}

dotnet publish $csproj -c Release -r win-arm64 --self-contained false -o $armOut --nologo
if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish win-arm64 失败"
}

Write-Host "构建 TSF DLL x64 / x86 / ARM64"
foreach ($arch in "x64", "x86", "arm64") {
    pwsh -NoLogo -NoProfile -File (Join-Path $root "src\T9Ime\build.ps1") -Arch $arch
    if ($LASTEXITCODE -ne 0) {
        throw "T9Ime $arch 失败"
    }
}

New-Item -ItemType Directory -Force -Path $dist | Out-Null
Get-ChildItem -LiteralPath $stage -Recurse -Include *.pdb, *.xml | Remove-Item -Force -ErrorAction SilentlyContinue
Remove-Item -LiteralPath (Join-Path $stage "Tools\Test-UwpIme.ps1") -Force -ErrorAction SilentlyContinue
Remove-Item -LiteralPath (Join-Path $stage "Tools\Install-UiAccess.ps1") -Force -ErrorAction SilentlyContinue

$host64 = Join-Path $stage "hosts\win-x64"
$host86 = Join-Path $stage "hosts\win-x86"
$hostArm = Join-Path $stage "hosts\win-arm64"
New-Item -ItemType Directory -Force -Path $host64, $host86, $hostArm | Out-Null
Copy-Item -LiteralPath (Join-Path $stage "T9Pane.exe") -Destination (Join-Path $host64 "T9Pane.exe") -Force
Copy-Item -LiteralPath (Join-Path $x86Out "T9Pane.exe") -Destination (Join-Path $host86 "T9Pane.exe") -Force
Copy-Item -LiteralPath (Join-Path $armOut "T9Pane.exe") -Destination (Join-Path $hostArm "T9Pane.exe") -Force
if (-not (Test-Path -LiteralPath (Join-Path $host86 "T9Pane.exe"))) {
    throw "缺少 32 位 T9Pane.exe"
}
if (-not (Test-Path -LiteralPath (Join-Path $hostArm "T9Pane.exe"))) {
    throw "缺少 ARM64 T9Pane.exe"
}

foreach ($arch in "x64", "x86", "arm64") {
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
