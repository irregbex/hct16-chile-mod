@echo off
rem HCT 1.6 - removes the mod and leaves the game as Steam installed it (double-click)
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0tools\install.ps1" -Uninstall %*
echo.
pause
