@echo off
rem run-dev.cmd - one-click SwebKit (Tauri) dev launcher, hot reload (CMD)
rem
rem Starts the three dev tiers in separate windows:
rem   1. .NET sidecar  (http://127.0.0.1:5199)
rem   2. Vite frontend (http://localhost:1420)
rem   3. Tauri window  (opens the desktop app)
rem
rem Double-click this file. If a tier is already running it is skipped.
rem Close the spawned console windows to stop. Logs: scripts\logs\*.log
rem
rem NOTE: the repo path must have no spaces for `start`'s quoting to hold.

setlocal
rem Normalize REPO to a full path (no trailing "..") so cd /d always resolves.
for %%I in ("%~dp0..\..") do set "REPO=%%~fI"
set "SIDECAR=%REPO%\src-sidecar"
set "WEB=%REPO%\web"
set "TAURI=%REPO%\web\node_modules\.bin\tauri.cmd"
set "LOGDIR=%REPO%\scripts\logs"
if not exist "%LOGDIR%" mkdir "%LOGDIR%"

rem Ensure the bundle-sidecar glob placeholder exists (gitignored; required by
rem tauri dev's build script even though dev mode runs the sidecar externally).
if not exist "%REPO%\src-tauri\binaries\sidecar" mkdir "%REPO%\src-tauri\binaries\sidecar"
if not exist "%REPO%\src-tauri\binaries\sidecar\.gitkeep" type nul > "%REPO%\src-tauri\binaries\sidecar\.gitkeep"

echo [launch] SwebKit dev environment

rem 1. Sidecar (run from src-sidecar; log to scripts/sidecar.log)
curl -s -o nul -w "%%{http_code}" --max-time 2 http://127.0.0.1:5199/health | findstr "200" >nul
if not errorlevel 1 (
  echo [skip]  sidecar already running
) else (
  echo [launch] sidecar...
  start "SwebKit Sidecar" /D "%SIDECAR%" cmd /k "dotnet run -c Debug --urls http://127.0.0.1:5199 > "%LOGDIR%\sidecar.log" 2>&1"
)

rem 2. Vite (run from web; log to scripts/vite.log)
curl -s -o nul -w "%%{http_code}" --max-time 2 http://localhost:1420/ | findstr "200" >nul
if not errorlevel 1 (
  echo [skip]  vite already running
) else (
  echo [launch] vite...
  start "SwebKit Vite" /D "%WEB%" cmd /k "npm run dev > "%LOGDIR%\vite.log" 2>&1"
)

rem Wait (bounded) for both prerequisites before opening the Tauri window.
echo [wait]  waiting for sidecar + vite (up to 180s)...
set "ELAPSED=0"
:wait_loop
curl -s -o nul -w "%%{http_code}" --max-time 2 http://127.0.0.1:5199/health | findstr "200" >nul
set "SIDECAR_UP=0"
if not errorlevel 1 set "SIDECAR_UP=1"
curl -s -o nul -w "%%{http_code}" --max-time 2 http://localhost:1420/ | findstr "200" >nul
set "VITE_UP=0"
if not errorlevel 1 set "VITE_UP=1"
if "%SIDECAR_UP%"=="1" if "%VITE_UP%"=="1" goto ready
set /a ELAPSED+=2
if %ELAPSED% GEQ 180 (
  echo [warn]  sidecar/vite not ready after 180s; opening Tauri anyway.
  goto ready
)
timeout /t 2 >nul
goto wait_loop
:ready

rem 3. Tauri window.
rem IMPORTANT: tauri dev MUST run from the repo root (or src-tauri), NOT from
rem web/ -- the Tauri CLI only finds tauri.conf.json in the current dir or its
rem subfolders, and the config lives in src-tauri/. Running it from web/ panics
rem with "Couldn't recognize the current folder as a Tauri project".
echo [launch] starting Tauri window...
start "SwebKit Tauri" /D "%REPO%" cmd /k "call "%TAURI%" dev > "%LOGDIR%\tauri.log" 2>&1"
echo [done]  SwebKit launching. Close the three console windows to stop.
echo         Logs: scripts\logs\sidecar.log, scripts\logs\vite.log, scripts\logs\tauri.log
endlocal
