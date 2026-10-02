<#
.SYNOPSIS
    One-click SwebKit (Tauri) dev launcher with hot reload.

.DESCRIPTION
    Starts the three dev tiers - .NET sidecar (http://127.0.0.1:5199), Vite
    (http://localhost:1420) and the Tauri window - as hidden background
    processes, then streams their startup output into this console so the run
    is visibly progressing.

    Port collisions are handled, not just detected: a tier only counts as
    "already running" when the port answers with a SwebKit-shaped response (a
    foreign app listening on 5199/1420 no longer fools the health check). When
    the default port is held by something else, a free OS-assigned port is
    picked and the choice is wired everywhere it has to agree - vite --port,
    the sidecar's --urls, VITE_SIDECAR_URL for the browser build, tauri dev's
    devUrl override, and SWEBKIT_DEV_SIDECAR_PORT for the desktop app.

    Nothing else stays open: the tier processes have no console window at all,
    and this launcher exits once the app is up. Everything a tier prints also
    lands in scripts\logs\{sidecar,vite,tauri}.log (+ .err.log), each tier's
    PID goes to scripts\logs\<name>.pid, and the chosen ports are recorded in
    scripts\logs\dev-ports.json - so scripts\tauri\stop-dev.cmd can shut the
    whole stack down regardless of which ports ended up in use.

    This is the *debug* path: the sidecar runs from source via `dotnet run` and
    the frontend is served by Vite with HMR. To exercise the same artifacts the
    installer ships (published sidecar + production frontend bundle) use
    scripts\tauri\test-frontend.ps1 instead.

.PARAMETER SidecarPort
    Preferred sidecar port (default 5199). If a foreign app holds it, a free
    port is chosen automatically.

.PARAMETER VitePort
    Preferred Vite dev port (default 1420). Same auto-relocation applies.

.PARAMETER NoBrowser
    Don't open the Vite URL in the default browser once it is up.

.PARAMETER NoTauri
    Don't build/launch the desktop app - sidecar + Vite only. Useful for pure
    frontend work (no Rust toolchain wait).

.EXAMPLE
    pwsh -File scripts/tauri/run-dev.ps1
    (double-clicking run-dev.cmd does the same thing)
#>
[CmdletBinding()]
param(
    [int]$SidecarPort = 5199,
    [int]$VitePort = 1420,
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
$portsFile = Join-Path $logDir 'dev-ports.json'

# Per-file read cursors so Write-LogDelta only prints what a tier logged since
# the last poll - the launcher console becomes the live view of all three tiers.
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

# Identity probes - "something answers on the port" is not enough. A foreign app
# returning HTTP 200 on 1420 used to count as "vite already running", and the
# Tauri window then loaded the wrong app. Each tier must prove it is SwebKit.
function Test-SwebKitSidecar {
    param([Parameter(Mandatory)][int]$Port)

    try {
        $r = Invoke-RestMethod -Uri "http://127.0.0.1:$Port/health" -TimeoutSec 2 -ErrorAction Stop
        return ($r.status -eq 'ok' -and "$($r.version)" -match '^\d+\.\d+\.\d+')
    }
    catch { return $false }
}

function Test-SwebKitVite {
    param([Parameter(Mandatory)][int]$Port)

    try {
        $r = Invoke-WebRequest -Uri "http://localhost:$Port/" -TimeoutSec 2 -UseBasicParsing -ErrorAction Stop
        return $r.Content -match '<title>SwebKit</title>'
    }
    catch { return $false }
}

function Test-PortFree {
    param([Parameter(Mandatory)][int]$Port)

    -not (Get-NetTCPConnection -LocalPort $Port -State Listen -ErrorAction SilentlyContinue)
}

function Get-FreePort {
    $listener = [System.Net.Sockets.TcpListener]::new([System.Net.IPAddress]::Loopback, 0)
    try {
        $listener.Start()
        return $listener.LocalEndpoint.Port
    }
    finally { $listener.Stop() }
}

<#
Decides where a tier goes:
  - already up and ours (on the preferred port, or the one dev-ports.json
    recorded from a previous relocated run) -> reuse it
  - preferred port free                    -> use it
  - preferred port held by a foreign app   -> OS-assigned free port
#>
function Resolve-TierPort {
    param(
        [Parameter(Mandatory)][string]$Name,
        [Parameter(Mandatory)][int]$Preferred,
        [Parameter(Mandatory)][scriptblock]$IdentityTest,
        [int]$PreviouslyUsed = 0
    )

    foreach ($candidate in @($Preferred, $PreviouslyUsed) | Where-Object { $_ -gt 0 } | Select-Object -Unique) {
        if (& $IdentityTest $candidate) {
            if ($candidate -ne $Preferred) {
                Write-Info "$Name already running on relocated port $candidate"
            }
            return @{ Port = $candidate; Running = $true }
        }
    }

    if (Test-PortFree $Preferred) {
        return @{ Port = $Preferred; Running = $false }
    }

    $free = Get-FreePort
    Write-Warning "$Name port $Preferred is held by another application - using $free instead"
    return @{ Port = $free; Running = $false }
}

function Start-Tier {
    param(
        [Parameter(Mandatory)][string]$Name,
        [Parameter(Mandatory)][string]$FilePath,
        [Parameter(Mandatory)][string[]]$Arguments,
        [Parameter(Mandatory)][string]$WorkingDirectory,
        [Parameter(Mandatory)][string]$Log,
        [Parameter(Mandatory)][string]$ErrLog,
        [hashtable]$Environment = @{}
    )

    # Fresh logs each run - stale output from a previous session reads like the
    # current one and is the classic "looks like nothing happens" red herring.
    foreach ($file in @($Log, $ErrLog)) {
        if (Test-Path $file) { Remove-Item $file -Force }
    }

    # Start-Process has no -Environment parameter, so the variables are set on
    # this process (children inherit them) and restored right after the spawn.
    $previousEnvironment = @{}
    foreach ($key in $Environment.Keys) {
        $previousEnvironment[$key] = [Environment]::GetEnvironmentVariable($key)
        [Environment]::SetEnvironmentVariable($key, $Environment[$key])
    }
    try {
        $process = Start-Process -FilePath $FilePath -ArgumentList $Arguments -WorkingDirectory $WorkingDirectory `
            -RedirectStandardOutput $Log -RedirectStandardError $ErrLog -WindowStyle Hidden -PassThru
    }
    finally {
        foreach ($key in $previousEnvironment.Keys) {
            [Environment]::SetEnvironmentVariable($key, $previousEnvironment[$key])
        }
    }

    Set-Content -Path (Join-Path $logDir "$Name.pid") -Value $process.Id
    Write-Info "$Name launched (pid $($process.Id)) -> $Log"
    $process
}

# Polls the tier's identity check while streaming its log into this console, so
# a long `dotnet run` build or vite warmup is visible instead of a silent wait.
# Returns $false (never throws) if the process died or the timeout expired -
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
        if (& $Tier.IdentityTest $Tier.Port) {
            Write-Host "[ok]    $($Tier.Name) is up ($($Tier.Url))" -ForegroundColor Green
            return $true
        }
        if ($Tier.Process.HasExited) {
            Write-Host "[fail]  $($Tier.Name) exited early (code $($Tier.Process.ExitCode)) - see $($Tier.Log) / $($Tier.ErrLog)" -ForegroundColor Red
            return $false
        }
        if (((Get-Date) - $lastBeat).TotalSeconds -ge 15) {
            Write-Info "$($Tier.Name) still starting..."
            $lastBeat = Get-Date
        }
        Start-Sleep -Milliseconds 800
    }
    Write-Warning "$($Tier.Name) did not come up within ${TimeoutSec}s - see $($Tier.Log)"
    return $false
}

Write-Host 'SwebKit dev environment' -ForegroundColor Cyan
Write-Host '=======================' -ForegroundColor Cyan

Write-Step 'Checking prerequisites...'
Assert-Tool -Name 'dotnet' -InstallHint 'Install the .NET 10 SDK (see global.json).'
Assert-Tool -Name 'node'   -InstallHint 'Install Node.js 20+ from https://nodejs.org/.'
Assert-Tool -Name 'npm'    -InstallHint 'Install Node.js 20+ from https://nodejs.org/.'

# `dotnet` on PATH is not enough - global.json can still reject every installed
# SDK ("A compatible .NET SDK was not found"), which otherwise only surfaces as
# an opaque 'sidecar exited early'. Resolve the SDK now, from the repo root, so
# the real error prints before anything is spawned.
Push-Location $repoRoot
try {
    # Stop lifted around the call: in 5.1 `2>&1` under Stop throws on the first
    # stderr line, which would replace the readable SDK error with a stack trace.
    $ErrorActionPreference = 'Continue'
    $sdkVersion = (dotnet --version 2>&1 | ForEach-Object { "$_" }) -join "`n"
    $ErrorActionPreference = 'Stop'
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

# Ports recorded by a previous (possibly relocated) run - lets tiers started
# then be detected as "already running" now.
$previousPorts = @{ sidecar = 0; vite = 0 }
if (Test-Path $portsFile) {
    try {
        $saved = Get-Content $portsFile -Raw | ConvertFrom-Json
        if ($saved.sidecar) { $previousPorts.sidecar = [int]$saved.sidecar }
        if ($saved.vite) { $previousPorts.vite = [int]$saved.vite }
    }
    catch { }
}

# 1. Sidecar
$sidecarTier = @{
    Name         = 'sidecar'
    IdentityTest = ${function:Test-SwebKitSidecar}
    Log          = Join-Path $logDir 'sidecar.log'
    ErrLog       = Join-Path $logDir 'sidecar.err.log'
    Process      = $null
    Port         = 0
    Url          = ''
}
$resolved = Resolve-TierPort -Name 'sidecar' -Preferred $SidecarPort `
    -IdentityTest ${function:Test-SwebKitSidecar} -PreviouslyUsed $previousPorts.sidecar
$sidecarTier.Port = $resolved.Port
$sidecarTier.Url = "http://127.0.0.1:$($resolved.Port)/health"
if ($resolved.Running) {
    Write-Host '[skip]  sidecar already running' -ForegroundColor Yellow
}
else {
    Write-Step "Starting sidecar (dotnet run) on 127.0.0.1:$($resolved.Port)..."
    $sidecarTier.Process = Start-Tier -Name 'sidecar' -FilePath 'dotnet' `
        -Arguments @('run', '-c', 'Debug', '--urls', "http://127.0.0.1:$($resolved.Port)") `
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
    try {
        Invoke-Native -FilePath 'npm' -Arguments @('install') -WorkingDirectory $webDir
    }
    catch {
        Write-Warning "$($_.Exception.Message) - the frontend may not start"
    }
}

# 2. Vite - VITE_SIDECAR_URL is baked into the bundle vite serves, so it must
#    reflect the sidecar port that actually got chosen above (not the 5199
#    default baked into transport.ts).
$viteTier = @{
    Name         = 'vite'
    IdentityTest = ${function:Test-SwebKitVite}
    Log          = Join-Path $logDir 'vite.log'
    ErrLog       = Join-Path $logDir 'vite.err.log'
    Process      = $null
    Port         = 0
    Url          = ''
}
$resolved = Resolve-TierPort -Name 'vite' -Preferred $VitePort `
    -IdentityTest ${function:Test-SwebKitVite} -PreviouslyUsed $previousPorts.vite
$viteTier.Port = $resolved.Port
$viteTier.Url = "http://localhost:$($resolved.Port)/"
if ($resolved.Running) {
    Write-Host '[skip]  vite already running' -ForegroundColor Yellow
}
else {
    Write-Step "Starting Vite dev server on localhost:$($resolved.Port)..."
    $viteTier.Process = Start-Tier -Name 'vite' -FilePath 'node' `
        -Arguments @((Join-Path $webDir 'node_modules\vite\bin\vite.js'), '--port', "$($resolved.Port)", '--strictPort') `
        -WorkingDirectory $webDir -Log $viteTier.Log -ErrLog $viteTier.ErrLog `
        -Environment @{ VITE_SIDECAR_URL = "http://127.0.0.1:$($sidecarTier.Port)" }
}

# Wait (bounded) for both prerequisites before opening the window; both tiers
# were launched above, so their logs interleave here as they come up.
$sidecarUp = if ($sidecarTier.Process) { Wait-Tier -Tier $sidecarTier } else { $true }
$viteUp = if ($viteTier.Process) { Wait-Tier -Tier $viteTier } else { $true }

# Record the ports that ended up live, for stop-dev's fallback and for the
# reuse check on the next run.
@{ sidecar = $sidecarTier.Port; vite = $viteTier.Port } |
ConvertTo-Json | Set-Content -Path $portsFile

if (-not $sidecarUp -or -not $viteUp) {
    Write-Warning 'One or more tiers failed to start - fix the errors above and rerun.'
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

    $tauriArgs = @((Get-TauriCli), 'dev')
    # devUrl is hardcoded to :1420 in tauri.conf.json - point it at the port
    # Vite actually got. --config accepts a JSON file, which sidesteps quoting
    # a JSON blob as a process argument.
    $tauriConfig = Join-Path $logDir 'tauri-dev-config.json'
    @{ build = @{ devUrl = $viteTier.Url } } | ConvertTo-Json | Set-Content -Path $tauriConfig
    $tauriArgs += @('--config', $tauriConfig)

    # The webview asks the Rust side for the sidecar port (get_sidecar_port);
    # in dev that defaults to 5199 unless this env var says otherwise.
    $tauriProcess = Start-Tier -Name 'tauri' -FilePath 'node' `
        -Arguments $tauriArgs `
        -WorkingDirectory $repoRoot -Log $tauriLog -ErrLog $tauriErrLog `
        -Environment @{ SWEBKIT_DEV_SIDECAR_PORT = "$($sidecarTier.Port)" }

    # The Rust build is the long pole on a cold cache (minutes). Stream the
    # build output until the app exe actually exists as a running process -
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
            Write-Host "[fail]  tauri dev exited (code $($tauriProcess.ExitCode)) - see $tauriLog / $tauriErrLog" -ForegroundColor Red
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
        Write-Warning "tauri dev still building after 10min - it will keep going; follow $tauriLog"
        $tauriOk = $true
    }
}

if (-not $NoBrowser) { Start-Process $viteTier.Url }

Write-Host ''
if ($tauriOk -or $NoTauri) {
    Write-Host '[done]  SwebKit is up. This window can close itself now.' -ForegroundColor Green
}
else {
    Write-Host '[done]  sidecar + Vite are up; the Tauri tier needs attention (above).' -ForegroundColor Yellow
}
Write-Host "        sidecar:  http://127.0.0.1:$($sidecarTier.Port)" -ForegroundColor DarkGray
Write-Host "        frontend: $($viteTier.Url)" -ForegroundColor DarkGray
Write-Host "        logs:     $logDir" -ForegroundColor DarkGray
Write-Host '        stop:     scripts\tauri\stop-dev.cmd' -ForegroundColor DarkGray
