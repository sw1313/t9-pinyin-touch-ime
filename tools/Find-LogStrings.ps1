#requires -Version 7.0
# 在源码里找日志里出现过的措辞，用来判断运行中的二进制是否与当前源码一致。
$root = Split-Path -Parent $PSScriptRoot
$needles = @("取字框成功", "取字框失败", "重定位到框", "取输入框", "重定位键盘", "已借壳 SIP", "平板触摸焦点")

foreach ($needle in $needles) {
    $hits = Get-ChildItem -Path (Join-Path $root "src") -Recurse -Include *.cs, *.cpp, *.h |
        Select-String -Pattern ([regex]::Escape($needle)) -Encoding UTF8
    Write-Host "『$needle』 命中 $($hits.Count) 处"
    $hits | Select-Object -First 3 | ForEach-Object {
        Write-Host "   $($_.Filename):$($_.LineNumber)  $($_.Line.Trim())"
    }
}

Write-Host "`n=== 已安装二进制 ==="
foreach ($p in @("C:\Program Files\T9Pane\T9Pane.exe", "C:\Program Files\T9Pane\T9Pane.dll")) {
    if (Test-Path -LiteralPath $p) {
        $i = Get-Item -LiteralPath $p
        Write-Host ("{0}`n   版本={1}  修改={2}" -f $i.Name, $i.VersionInfo.FileVersion, $i.LastWriteTime)
    }
}

Write-Host "`n=== 源码构建产物 ==="
$bin = Join-Path $root "src\T9Pane\bin\Release\net8.0-windows"
foreach ($p in @("T9Pane.exe", "T9Pane.dll")) {
    $f = Join-Path $bin $p
    if (Test-Path -LiteralPath $f) {
        $i = Get-Item -LiteralPath $f
        Write-Host ("{0}`n   版本={1}  修改={2}" -f $i.Name, $i.VersionInfo.FileVersion, $i.LastWriteTime)
    }
}

Write-Host "`n=== 正在运行的 T9Pane ==="
Get-Process T9Pane -ErrorAction SilentlyContinue | ForEach-Object {
    Write-Host ("pid={0} 启动={1}" -f $_.Id, $_.StartTime)
    try { Write-Host ("   映像={0}" -f $_.MainModule.FileName) } catch { Write-Host "   映像=拿不到（uiAccess 进程）" }
}
