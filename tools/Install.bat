@echo off
setlocal
cd /d "%~dp0"
where pwsh >nul 2>&1
if errorlevel 1 (
  echo 需要 PowerShell 7。请先安装 https://aka.ms/powershell
  pause
  exit /b 1
)
pwsh -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%~dp0Tools\Install-User.ps1"
exit /b %ERRORLEVEL%
