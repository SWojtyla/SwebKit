@echo off
rem stop-dev.cmd - stops the hidden dev tiers started by run-dev.cmd
rem Double-clickable wrapper for stop-dev.ps1 (the actual implementation).

setlocal
set "SCRIPT=%~dp0stop-dev.ps1"

where pwsh >nul 2>&1
if not errorlevel 1 (
  pwsh -NoProfile -File "%SCRIPT%" %*
) else (
  powershell -NoProfile -ExecutionPolicy Bypass -File "%SCRIPT%" %*
)

if errorlevel 1 (
  echo.
  echo [stop-dev] failed - see the errors above.
  pause
)
endlocal
