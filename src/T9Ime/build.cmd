@echo off
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0build.ps1" -Arch %1
exit /b %ERRORLEVEL%
