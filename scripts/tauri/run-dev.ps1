<#
.SYNOPSIS
    One-click SwebKit (Tauri) dev launcher with hot reload.

.DESCRIPTION
    Starts the three dev tiers — .NET sidecar (http://127.0.0.1:5199), Vite
    (http://localhost:1420) and the Tauri window — as hidden background
    processes, then streams their startup output into this console so the run
    is visibly progressing. A tier already answering on its port is skipped.

    Nothing else stays open: the tier processes have no console window at all,
    and this launcher exits once the app is up. Everything a tier prints also
    lands in scripts\logs\{sidecar,vite,tauri}.log (+ .err.log), and each
    tier's PID goes to scripts\logs\<name>.pid so scripts\tauri\stop-dev.cmd
    can shut the whole stack down.

    This is the *debug* path: the sidecar runs from source via `dotnet run` and
    the frontend is served by Vite with HMR. To exercise the same artifacts the
    installer ships (published sidecar + production frontend bundle) use
    scripts\tauri\test-frontend.ps1 instead.

.PARAMETER NoBrowser
    Don't open http://localhost:1420/ in the default browser once Vite is up.

.PARAMETER NoTauri
    Don't build/launch the desktop app — sidecar + Vite only. Useful for pure
    frontend work (no Rust toolchain wait).

.EXAMPLE
    pwsh -File scripts/tauri/run-dev.ps1
    (double-clicking run-dev.cmd does the same thing)
#>
[CmdletBinding()]
param(
    [switch]$NoBrowser,
    [switch]$NoTauri
)

Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'

. (Join-Path $PSScriptRoot '_common.ps1')

$repoRoot = Get-RepoRoot
$sidecarDir = Join-Path $repoRoot 'src-sidecar'
$webDir = Join-Path $repoRoot 'web'
$logDir = Get-LogDirectory

# Per-file read cursors so Write-LogDelta only prints what a tier logged since
# the last poll — the launcher console becomes the live view of all three tiers.
$script:logCursors = @{}

function Write-LogDelta {
    param(
        [Parameter(Mandatory)][string]$File,
        [Parameter(Mandatory)][string]$Tier,
        [ConsoleColor]$Color = [ConsoleColor]::DarkGray
    )

    if (-not (Test-Path $File)) { return }
    if (-not $script:logCursors.ContainsKey($File)) {
        $script:logCursors[$File] = @{ Offset = [long]0; Pending = '' }
    }
    $cursor = $script:logCursors[$File]

    $chunk = $null
    try {
        $reader = [System.IO.StreamReader]::new(
            [System.IO.FileStream]::new($File, [System.IO.FileMode]::Open, [System.IO.FileAccess]::Read,
                [System.IO.FileShare]::ReadWrite -bor [System.IO.FileShare]::Delete))
        try {
            if ($reader.BaseStream.Length -lt $cursor.Offset) { $cursor.Offset = 0; $cursor.Pending = '' }
            $reader.BaseStream.Position = $cursor.Offset
            $chunk = $reader.ReadToEnd()
            $cursor.Offset = $reader.BaseStream.Position
        }
        finally { $reader.Dispose() }
    }
    catch { return }

    if ([string]::IsNullOrEmpty($chunk)) { return }
    $lines = @(($cursor.Pending + $chunk) -split "`r?`n")
    $cursor.Pending = $lines[-1]
    for ($i = 0; $i -lt $lines.Count - 1; $i++) {
        $line = $lines[$i].TrimEnd()
        if ($line.Length -gt 0) { Write-Host "[$Tier] $line" -ForegroundColor $Color }
    }
}

function Test-Url {
    param([Parameter(Mandatory)][string]$Url)

    try {
        $response = Invoke-WebRequest -Uri $Url -TimeoutSec 2 -UseBasicParsing -ErrorAction Stop
        return $response.StatusCode -eq 200
    }
    catch { return $false }
}

function Start-Tier {
    param(
        [Parameter(Mandatory)][string]$Name,
        [Parameter(Mandatory)][string]$FilePath,
        [Parameter(Mandatory)][string[]]$Arguments,
        [Parameter(Mandatory)][string]$WorkingDirectory,
        [Parameter(Mandatory)][string]$Log,
        [Parameter(Mandatory)][string]$ErrLog
    )

    # Fresh logs each run — stale output from a previous session reads like the
    # current one and is the classic "looks like nothing happens" red herring.
    foreach ($file in @($Log, $ErrLog)) {
        if (Test-Path $file) { Remove-Item $file -Force }
    }

    $process = Start-Process -FilePath $FilePath -ArgumentList $Arguments -WorkingDirectory $WorkingDirectory `
        -RedirectStandardOutput $Log -RedirectStandardError $ErrLog -WindowStyle Hidden -PassThru
    Set-Content -Path (Join-Path $logDir "$Name.pid") -Value $process.Id
    Write-Info "$Name launched (pid $($process.Id)) -> $Log"
    $process
}

# Polls the tier's health URL while streaming its log into this console, so a
# long `dotnet run` build or vite warmup is visible instead of a silent wait.
# Returns $false (never throws) if the process died or the timeout expired —
# the caller decides whether that is fatal.
function Wait-Tier {
    param(
        [Parameter(Mandatory)][hashtable]$Tier,
        [int]$TimeoutSec = 180
    )

    $deadline = (Get-Date).AddSeconds($TimeoutSec)
    $lastBeat = Get-Date
    while ((Get-Date) -lt $deadline) {
        Write-LogDelta -File $Tier.Log -Tier $Tier.Name -Color DarkGray
        Write-LogDelta -File $Tier.ErrLog -Tier $Tier.Name -Color DarkYellow
        if (Test-Url $Tier.Url) {
            Write-Host "[ok]    $($Tier.Name) is up ($($Tier.Url))" -ForegroundColor Green
            return $true
        }
        if ($Tier.Process.HasExited) {
            Write-Host "[fail]  $($Tier.Name) exited early (code $($Tier.Process.ExitCode)) — see $($Tier.Log) / $($Tier.ErrLog)" -ForegroundColor Red
            return $false
        }
        if (((Get-Date) - $lastBeat).TotalSeconds -ge 15) {
            Write-Info "$($Tier.Name) still starting..."
            $lastBeat = Get-Date
        }
        Start-Sleep -Milliseconds 800
    }
    Write-Warning "$($Tier.Name) did not come up within ${TimeoutSec}s — see $($Tier.Log)"
    return $false
}

Write-Host 'SwebKit dev environment' -ForegroundColor Cyan
Write-Host '=======================' -ForegroundColor Cyan

Write-Step 'Checking prerequisites...'
Assert-Tool -Name 'dotnet' -InstallHint 'Install the .NET 10 SDK (see global.json).'
Assert-Tool -Name 'node'   -InstallHint 'Install Node.js 20+ from https://nodejs.org/.'
Assert-Tool -Name 'npm'    -InstallHint 'Install Node.js 20+ from https://nodejs.org/.'

# `dotnet` on PATH is not enough — global.json can still reject every installed
# SDK ("A compatible .NET SDK was not found"), which otherwise only surfaces as
# an opaque 'sidecar exited early'. Resolve the SDK now, from the repo root, so
# the real error prints before anything is spawned.
Push-Location $repoRoot
try {
    $sdkVersion = dotnet --version 2>&1
    if ($LASTEXITCODE -ne 0) {
        throw "No compatible .NET SDK for this repo (global.json):`n$sdkVersion"
    }
    Write-Info "dotnet SDK $sdkVersion"
}
finally { Pop-Location }

# Ensure the bundle-sidecar glob placeholder exists (gitignored; required by
# tauri dev's build script even though dev mode runs the sidecar externally).
$binDir = Join-Path $repoRoot 'src-tauri\binaries\sidecar'
if (-not (Test-Path $binDir)) { New-Item -ItemType Directory -Path $binDir -Force | Out-Null }
$gitkeep = Join-Path $binDir '.gitkeep'
if (-not (Test-Path $gitkeep)) { New-Item -ItemType File -Path $gitkeep -Force | Out-Null }

# 1. Sidecar
$sidecarTier = @{
    Name    = 'sidecar'
    Url     = 'http://127.0.0.1:5199/health'
    Log     = Join-Path $logDir 'sidecar.log'
    ErrLog  = Join-Path $logDir 'sidecar.err.log'
    Process = $null
}
if (Test-Url $sidecarTier.Url) {
    Write-Host '[skip]  sidecar already running' -ForegroundColor Yellow
}
else {
    Write-Step 'Starting sidecar (dotnet run)...'
    $sidecarTier.Process = Start-Tier -Name 'sidecar' -FilePath 'dotnet' `
        -Arguments @('run', '-c', 'Debug', '--urls', 'http://127.0.0.1:5199') `
        -WorkingDirectory $sidecarDir -Log $sidecarTier.Log -ErrLog $sidecarTier.ErrLog
}

# Keep frontend deps in sync with the lockfile. npm tracks its own installed
# state in node_modules\.package-lock.json -- if the repo lockfile is newer
# (e.g. after a pull that added a package) or node_modules is missing entirely,
# install before Vite starts or it will crash on unresolved imports.
$nodeModules = Join-Path $webDir 'node_modules'
$installedLock = Join-Path $nodeModules '.package-lock.json'
$repoLock = Join-Path $webDir 'package-lock.json'
$needsInstall = -not (Test-Path $nodeModules)
if (-not $needsInstall -and (Test-Path $repoLock)) {
    $needsInstall = -not (Test-Path $installedLock) -or
    ((Get-Item $repoLock).LastWriteTime -gt (Get-Item $installedLock).LastWriteTime)
}
if ($needsInstall) {
    Write-Step 'Frontend dependencies out of date - running npm install...'
    Push-Location $webDir
    try {
        npm install
        if ($LASTEXITCODE -ne 0) {
            Write-Warning "npm install failed (exit $LASTEXITCODE) - the frontend may not start"
        }
    }
    finally { Pop-Location }
}

# 2. Vite
$viteTier = @{
    Name    = 'vite'
    Url     = 'http://localhost:1420/'
    Log     = Join-Path $logDir 'vite.log'
    ErrLog  = Join-Path $logDir 'vite.err.log'
    Process = $null
}
if (Test-Url $viteTier.Url) {
    Write-Host '[skip]  vite already running' -ForegroundColor Yellow
}
else {
    Write-Step 'Starting Vite dev server...'
    $viteTier.Process = Start-Tier -Name 'vite' -FilePath 'node' `
        -Arguments @((Join-Path $webDir 'node_modules\vite\bin\vite.js'), '--port', '1420') `
        -WorkingDirectory $webDir -Log $viteTier.Log -ErrLog $viteTier.ErrLog
}

# Wait (bounded) for both prerequisites before opening the window; both tiers
# were launched above, so their logs interleave here as they come up.
$sidecarUp = if ($sidecarTier.Process) { Wait-Tier -Tier $sidecarTier } else { $true }
$viteUp = if ($viteTier.Process) { Wait-Tier -Tier $viteTier } else { $true }

if (-not $sidecarUp -or -not $viteUp) {
    Write-Warning 'One or more tiers failed to start — fix the errors above and rerun.'
    Write-Host "        Logs: $logDir" -ForegroundColor DarkGray
    exit 1
}

# 3. Tauri window. `tauri dev` MUST run from the repo root (or src-tauri), NOT
# from web/ -- the Tauri CLI only finds tauri.conf.json in the current dir or
# its subfolders, and the config lives in src-tauri/. Running it from web/
# panics with "Couldn't recognize the current folder as a Tauri project".
$tauriOk = $false
if ($NoTauri) {
    Write-Host '[skip]  Tauri window (-NoTauri)' -ForegroundColor Yellow
}
else {
    Write-Step 'Starting Tauri (cargo build, then the desktop window)...'
    $tauriLog = Join-Path $logDir 'tauri.log'
    $tauriErrLog = Join-Path $logDir 'tauri.err.log'
    $tauriProcess = Start-Tier -Name 'tauri' -FilePath 'node' `
        -Arguments @((Get-TauriCli), 'dev') `
        -WorkingDirectory $repoRoot -Log $tauriLog -ErrLog $tauriErrLog

    # The Rust build is the long pole on a cold cache (minutes). Stream the
    # build output until the app exe actually exists as a running process —
    # that is the moment the window is really up, unlike a wall-clock guess.
    $appExe = Join-Path $repoRoot 'src-tauri\target\debug\swebkit.exe'
    $deadline = (Get-Date).AddMinutes(10)
    $lastBeat = Get-Date
    while ((Get-Date) -lt $deadline) {
        Write-LogDelta -File $tauriLog -Tier 'tauri' -Color DarkGray
        Write-LogDelta -File $tauriErrLog -Tier 'tauri' -Color DarkYellow
        $appRunning = Get-Process -Name 'swebkit' -ErrorAction SilentlyContinue |
        Where-Object { try { $_.Path -eq $appExe } catch { $false } }
        if ($appRunning) { $tauriOk = $true; break }
        if ($tauriProcess.HasExited) {
            Write-Host "[fail]  tauri dev exited (code $($tauriProcess.ExitCode)) — see $tauriLog / $tauriErrLog" -ForegroundColor Red
            break
        }
        if (((Get-Date) - $lastBeat).TotalSeconds -ge 15) {
            Write-Info 'tauri still building (cold Rust builds take a few minutes)...'
            $lastBeat = Get-Date
        }
        Start-Sleep -Seconds 1
    }
    if ($tauriOk) {
        Write-Host '[ok]    Tauri window is running' -ForegroundColor Green
    }
    elseif (-not $tauriProcess.HasExited) {
        Write-Warning "tauri dev still building after 10min — it will keep going; follow $tauriLog"
        $tauriOk = $true
    }
}

if (-not $NoBrowser) { Start-Process 'http://localhost:1420/' }

Write-Host ''
if ($tauriOk -or $NoTauri) {
    Write-Host '[done]  SwebKit is up. This window can close itself now.' -ForegroundColor Green
}
else {
    Write-Host '[done]  sidecar + Vite are up; the Tauri tier needs attention (above).' -ForegroundColor Yellow
}
Write-Host "        Logs: $logDir" -ForegroundColor DarkGray
Write-Host '        Stop everything: scripts\tauri\stop-dev.cmd' -ForegroundColor DarkGray
