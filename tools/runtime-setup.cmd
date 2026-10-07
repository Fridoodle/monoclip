@echo off
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0install-runtime.ps1" -Destination "%~dp0"
if errorlevel 1 (
  echo Runtime konnte nicht eingerichtet werden. Bitte Meldung oben pruefen.
  pause
  exit /b 1
)
pause
