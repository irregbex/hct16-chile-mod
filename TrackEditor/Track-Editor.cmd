@echo off
rem HCT 1.6 Track Editor - opens the browser editor on the mod installed in your Steam copy of Horizon Chase Turbo.
rem Needs Python 3 (https://www.python.org/downloads/ - tick "Add python.exe to PATH" when installing).
rem Keep this window open while you use the editor; close it (or Ctrl+C) to stop it.
rem Options are passed on, e.g.:  Track-Editor.cmd --content "..\Content\HCT15Content"   (edit this repository's content)
setlocal
set "PY="
where python >nul 2>nul && set "PY=python"
if not defined PY where py >nul 2>nul && set "PY=py -3"
if not defined PY (
  echo Python 3 was not found. Install it from https://www.python.org/downloads/
  echo and tick "Add python.exe to PATH", then run this again.
  pause
  exit /b 1
)
%PY% "%~dp0hct15_editor.py" %*
pause
