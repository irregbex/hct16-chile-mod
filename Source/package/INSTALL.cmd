@echo off
rem HCT 1.6 - installs the mod into your Steam copy of Horizon Chase Turbo (double-click)
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0tools\install.ps1" %*
echo.
pause
