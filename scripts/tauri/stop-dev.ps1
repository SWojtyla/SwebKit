<#
.SYNOPSIS
    Stops the dev tiers started by run-dev.ps1.

.DESCRIPTION
    run-dev.ps1 launches the sidecar, Vite and tauri dev as hidden background
    processes and records each PID in scripts\logs\<name>.pid. This script
    kills each recorded process tree (taskkill /T, so `dotnet run`'s child
    process and tauri dev's cargo/app children go too).

    When a PID file is missing or stale the tier is matched by its listen port
    instead (sidecar 5199, Vite 1420), so manually started instances are
    covered as well.

.EXAMPLE
    pwsh -File scripts/tauri/stop-dev.ps1
    (double-clicking stop-dev.cmd does the same thing)
#>
[CmdletBinding()]
param()

Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Continue'

. (Join-Path $PSScriptRoot '_common.ps1')

$logDir = Join-Path (Get-RepoRoot) 'scripts\logs'

function Stop-Tree {
    param([Parameter(Mandatory)][int]$ProcessId, [Parameter(Mandatory)][string]$Name)

    $process = Get-Process -Id $ProcessId -ErrorAction SilentlyContinue
    if (-not $process) { return $false }
    Write-Host "[stop]  $Name (pid $ProcessId)" -ForegroundColor Cyan
    & taskkill /PID $ProcessId /T /F 2>&1 | Out-Null
    return $true
}

$stoppedAny = $false

# run-dev records the ports it actually bound (possibly relocated off the
# defaults when 5199/1420 were taken) in dev-ports.json - prefer those for the
# port fallback, keeping the defaults as a final guess.
$savedPorts = @{ sidecar = 5199; vite = 1420 }
$portsFile = Join-Path $logDir 'dev-ports.json'
if (Test-Path $portsFile) {
    try {
        $saved = Get-Content $portsFile -Raw | ConvertFrom-Json
        if ($saved.sidecar) { $savedPorts.sidecar = [int]$saved.sidecar }
        if ($saved.vite) { $savedPorts.vite = [int]$saved.vite }
        Remove-Item $portsFile -Force -ErrorAction SilentlyContinue
    }
    catch { }
}

# Tier name -> listen port used as the fallback lookup when no live PID exists.
$tiers = @(
    @{ Name = 'sidecar'; Port = $savedPorts.sidecar },
    @{ Name = 'vite'; Port = $savedPorts.vite },
    @{ Name = 'tauri'; Port = 0 }   # no port; PID file or the built app exe only
)

foreach ($tier in $tiers) {
    $pidFile = Join-Path $logDir "$($tier.Name).pid"
    $stopped = $false

    if (Test-Path $pidFile) {
        $pidText = (Get-Content $pidFile -Raw).Trim()
        if ($pidText -match '^\d+$') {
            $stopped = Stop-Tree -ProcessId ([int]$pidText) -Name $tier.Name
        }
        Remove-Item $pidFile -Force -ErrorAction SilentlyContinue
    }

    if (-not $stopped -and $tier.Port -gt 0) {
        $listeners = Get-NetTCPConnection -LocalPort $tier.Port -State Listen -ErrorAction SilentlyContinue
        foreach ($listener in $listeners) {
            $stopped = (Stop-Tree -ProcessId $listener.OwningProcess -Name $tier.Name) -or $stopped
        }
    }

    $stoppedAny = $stoppedAny -or $stopped
}

# The Tauri app exe itself: tauri dev's process tree normally covers it, but a
# reparented orphan would hold the webview (and the next run's file locks).
$appExe = Join-Path (Get-RepoRoot) 'src-tauri\target\debug\swebkit.exe'
$appRunning = Get-Process -Name 'swebkit' -ErrorAction SilentlyContinue |
Where-Object { try { $_.Path -eq $appExe } catch { $false } }
foreach ($app in $appRunning) {
    $stoppedAny = (Stop-Tree -ProcessId $app.Id -Name 'tauri app') -or $stoppedAny
}

if ($stoppedAny) {
    Write-Host '[done]  dev tiers stopped.' -ForegroundColor Green
}
else {
    Write-Host '[done]  nothing was running.' -ForegroundColor DarkGray
}
