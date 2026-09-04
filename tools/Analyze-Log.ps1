#requires -Version 7.0
# 从 t9pane.log 里挑出热路径证据：谁在高频重复、重复得多密。
param([int]$Tail = 0)

$OutputEncoding = [System.Text.Encoding]::UTF8
$log = "$env:APPDATA\T9Pane\t9pane.log"
$lines = Get-Content $log -Encoding UTF8
if ($Tail -gt 0) { $lines = $lines | Select-Object -Last $Tail }
Write-Host "分析 $($lines.Count) 行`n"

Write-Host "=== TSF 上下文事件的爆发密度（同一秒内出现几条）==="
$ctx = $lines | Where-Object { $_ -match "TSF 上下文" }
Write-Host "总计 $($ctx.Count) 条"
$bursts = $ctx | ForEach-Object {
    if ($_ -match '^(\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2})') { $matches[1] }
} | Group-Object | Sort-Object Count -Descending
Write-Host "最密集的 10 秒："
$bursts | Select-Object -First 10 | ForEach-Object { "  $($_.Name)  ->  $($_.Count) 条/秒" }
$avg = ($bursts | Measure-Object -Property Count -Average).Average
Write-Host ("有事件的秒数 {0}，平均 {1:N1} 条/秒" -f $bursts.Count, $avg)

Write-Host "`n=== TSF 上下文的 layout 取值分布 ==="
$ctx | ForEach-Object {
    if ($_ -match 'layout=(\d+)') { "layout=$($matches[1])" }
} | Group-Object | Sort-Object Count -Descending | ForEach-Object { "  $($_.Name)  $($_.Count)" }

Write-Host "`n=== 取字框成功的来源分布（定位数据从哪来）==="
$lines | ForEach-Object {
    if ($_ -match '取字框成功.*来源=([^\s]+)') { $matches[1] }
} | Group-Object | Sort-Object Count -Descending | ForEach-Object { "  $($_.Name)  $($_.Count)" }

Write-Host "`n=== 取字框失败的原因分布 ==="
$lines | ForEach-Object {
    if ($_ -match '取字框失败.*原因=(.*)$') { $matches[1].Trim() }
} | Group-Object | Sort-Object Count -Descending | Select-Object -First 10 |
    ForEach-Object { "  $($_.Count)  $($_.Name)" }

Write-Host "`n=== 重定位到框：宿主分布 ==="
$lines | ForEach-Object {
    if ($_ -match '重定位到框 宿主=([^\s]+)') { $matches[1] }
} | Group-Object | Sort-Object Count -Descending | ForEach-Object { "  $($_.Name)  $($_.Count)" }

Write-Host "`n=== 警告与错误 Top10 ==="
$lines | Where-Object { $_ -match '\[(WARN|ERROR)\]' } | ForEach-Object {
    ($_ -replace '^\S+ \S+ ', '') -replace '\d+', 'N'
} | Group-Object | Sort-Object Count -Descending | Select-Object -First 10 |
    ForEach-Object { "  $($_.Count)  $($_.Name)" }

Write-Host "`n=== Perf 采样（若已开启 T9PANE_PERF=1）==="
$perf = $lines | Where-Object { $_ -match '(?i)perf|ms\]|耗时' }
if ($perf) { $perf | Select-Object -Last 25 | ForEach-Object { "  $_" } }
else { Write-Host "  无 Perf 采样数据" }

Write-Host "`n=== 最近 25 行原文 ==="
$lines | Select-Object -Last 25 | ForEach-Object { "  $_" }
