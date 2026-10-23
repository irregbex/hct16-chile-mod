@echo off
rem HCT 1.6 - step 1: copies from your Horizon Chase Turbo the DLLs the mod is compiled against (double-click)
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0Prepare-References.ps1" %*
echo.
pause
