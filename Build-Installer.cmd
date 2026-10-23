@echo off
rem HCT 1.6 - step 2: builds the mod and puts together Installer\ (double-click)
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0Build-Installer.ps1" %*
echo.
pause
