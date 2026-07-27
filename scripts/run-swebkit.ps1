# run-swebkit.ps1 - one-click SwebKit dev launcher (PowerShell)
#
# Starts the three dev tiers in separate windows:
#   1. .NET sidecar  (http://127.0.0.1:5199)
#   2. Vite frontend (http://localhost:1420)
#   3. Tauri window  (opens the desktop app)
#
# If a tier is already running it is skipped. Close the spawned console
# windows to stop. Logs: scripts\sidecar.log, scripts\vite.log, scripts\tauri.log
#
# Run with:  powershell -ExecutionPolicy Bypass -File .\run-swebkit.ps1
#            (or right-click the file -> "Run with PowerShell")

$repo        = Resolve-Path (Join-Path $PSScriptRoot '..')
$sidecarDir  = Join-Path $repo 'src-sidecar'
$viteDir     = Join-Path $repo 'web'
$tauriBin    = Join-Path $repo 'web\node_modules\.bin\tauri.cmd'
$logDir      = Join-Path $repo 'scripts'
$sidecarLog  = Join-Path $logDir 'sidecar.log'
$viteLog     = Join-Path $logDir 'vite.log'
$tauriLog    = Join-Path $logDir 'tauri.log'

# Ensure the bundle-sidecar glob placeholder exists (gitignored; required by
# tauri dev's build script even though dev mode runs the sidecar externally).
$binDir = Join-Path $repo 'src-tauri\binaries\sidecar'
if (-not (Test-Path $binDir))  { New-Item -ItemType Directory -Path $binDir -Force | Out-Null }
$gitkeep = Join-Path $binDir '.gitkeep'
if (-not (Test-Path $gitkeep)) { New-Item -ItemType File    -Path $gitkeep -Force | Out-Null }

function Wait-ForUrl($url, $name, $timeoutSec = 180) {
    $elapsed = 0
    while ($elapsed -lt $timeoutSec) {
        try {
            $r = Invoke-WebRequest -Uri $url -TimeoutSec 2 -UseBasicParsing -ErrorAction Stop
            if ($r.StatusCode -eq 200) {
                Write-Host "[ok]   $name is up ($url)" -ForegroundColor Green
                return $true
            }
        } catch { }
        Start-Sleep -Seconds 2
        $elapsed += 2
    }
    Write-Warning "[warn] $name did not come up within ${timeoutSec}s ($url)"
    return $false
}

Write-Host "[launch] SwebKit dev environment`n" -ForegroundColor Cyan

# 1. Sidecar
if (Wait-ForUrl "http://127.0.0.1:5199/health" "Sidecar" 2) {
    Write-Host "[skip]  sidecar already running" -ForegroundColor Yellow
} else {
    Write-Host "[launch] sidecar..." -ForegroundColor Cyan
    Start-Process powershell -WorkingDirectory $sidecarDir `
        -ArgumentList "-NoExit", "-Command", "dotnet run -c Debug --urls 'http://127.0.0.1:5199' *> '$sidecarLog'" `
        -WindowStyle Normal
}

# 2. Vite
if (Wait-ForUrl "http://localhost:1420/" "Vite" 2) {
    Write-Host "[skip]  vite already running" -ForegroundColor Yellow
} else {
    Write-Host "[launch] vite..." -ForegroundColor Cyan
    Start-Process powershell -WorkingDirectory $viteDir `
        -ArgumentList "-NoExit", "-Command", "npm run dev *> '$viteLog'" `
        -WindowStyle Normal
}

# Wait (bounded) for both prerequisites before opening the window.
Wait-ForUrl "http://127.0.0.1:5199/health" "Sidecar" 180
Wait-ForUrl "http://localhost:1420/" "Vite" 180

# 3. Tauri window.
# IMPORTANT: tauri dev MUST run from the repo root (or src-tauri), NOT from
# web/ -- the Tauri CLI only finds tauri.conf.json in the current dir or its
# subfolders, and the config lives in src-tauri/. Running it from web/ panics
# with "Couldn't recognize the current folder as a Tauri project".
Write-Host "[launch] starting Tauri window..." -ForegroundColor Cyan
Start-Process powershell -WorkingDirectory $repo `
    -ArgumentList "-NoExit", "-Command", "& '$tauriBin' dev *> '$tauriLog'" `
    -WindowStyle Normal

Write-Host "`n[done]  SwebKit launching. Close the three console windows to stop." -ForegroundColor Cyan
Write-Host "        Logs: scripts\sidecar.log, scripts\vite.log, scripts\tauri.log" -ForegroundColor Cyan
