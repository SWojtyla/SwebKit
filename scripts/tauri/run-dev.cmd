@echo off
rem run-dev.cmd - one-click SwebKit (Tauri) dev launcher, hot reload (CMD)
rem
rem Double-clickable wrapper for run-dev.ps1 (the actual implementation lives
rem there). Starts the three dev tiers as hidden background processes and
rem streams their startup output into this window:
rem   1. .NET sidecar  (http://127.0.0.1:5199)
rem   2. Vite frontend (http://localhost:1420)
rem   3. Tauri window  (opens the desktop app)
rem
rem If a tier is already running it is skipped. This window closes itself once
rem the stack is up (it stays open on failure so the error is readable).
rem Stop everything with scripts\tauri\stop-dev.cmd.
rem
rem Logs + PIDs: scripts\logs\{sidecar,vite,tauri}.log / .err.log / .pid
rem
rem NOTE: the repo path must have no spaces for the spawned tiers' argument
rem quoting to hold.

setlocal
set "SCRIPT=%~dp0run-dev.ps1"

where pwsh >nul 2>&1
if not errorlevel 1 (
  pwsh -NoProfile -File "%SCRIPT%" %*
) else (
  powershell -NoProfile -ExecutionPolicy Bypass -File "%SCRIPT%" %*
)

if errorlevel 1 (
  echo.
  echo [run-dev] failed - see the errors above or scripts\logs\
  pause
)
endlocal
