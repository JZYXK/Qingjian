@echo off
set "QJ_UNINSTALL=%~dp0Uninstall.ps1"
cd /d "%TEMP%"
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%QJ_UNINSTALL%"
pause
