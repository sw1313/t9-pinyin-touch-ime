#requires -Version 7.0
# 带 T9PANE_PERF=1 重启 T9Pane，把按键热路径的耗时打进日志。
#
# T9Pane 有 uiAccess，普通权限杀不掉，所以停进程要提权；但启动必须回到非提权，
# 否则 uiAccess 令牌拿不到、覆盖层会盖不住别的窗口。
$ErrorActionPreference = "Stop"
$exe = "C:\Program Files\T9Pane\T9Pane.exe"

if (Get-Process T9Pane -ErrorAction SilentlyContinue) {
    Write-Host "提权停掉现有 T9Pane…"
    Start-Process powershell -Verb RunAs -Wait -ArgumentList @(
        "-NoProfile", "-Command",
        "Get-Process T9Pane -ErrorAction SilentlyContinue | Stop-Process -Force"
    )
    Start-Sleep -Seconds 2
}
if (Get-Process T9Pane -ErrorAction SilentlyContinue) { throw "T9Pane 还在运行，没停掉" }

# 通过 explorer 启动，拿到普通用户令牌；环境变量得先写进注册表级别拿不到，
# 所以这里直接用带环境变量的子进程启动。
$env:T9PANE_PERF = "1"
Start-Process -FilePath $exe
Start-Sleep -Seconds 5

$proc = Get-Process T9Pane -ErrorAction SilentlyContinue
if (-not $proc) { throw "T9Pane 没起来" }
Write-Host "T9Pane 已带采样启动 pid=$($proc.Id)"
Write-Host "现在请在任意输入框里用触摸连续打几个字，然后运行 Analyze-Log.ps1"
