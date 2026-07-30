<#
.SYNOPSIS
    Builds a fresh SwebKit (Tauri) Windows installer from a clean slate.

.DESCRIPTION
    Runs the full bundling recipe in the only order that works, with a guard after
    each step so a half-built bundle never reaches you:

      1. Clean the previous outputs (web\dist, the published sidecar, the bundle folder).
      2. npm install + npm run build          -> web\dist
      3. dotnet publish (self-contained)      -> src-tauri\binaries\sidecar
      4. tauri build --bundles <targets>      -> src-tauri\target\release\bundle\...

    Step 3 matters more than it looks: src-tauri\binaries\sidecar is gitignored and
    bundled as a Tauri resource, so an empty or stale folder still produces a
    perfectly valid installer whose installed app dies at startup with
    "Sidecar binary not found". This script always republishes it and verifies the
    .exe landed.

    The Rust target directory is left alone by default (an incremental release
    build takes minutes, a cold one much longer). Use -FullClean to force it.

    A full transcript is written to scripts\logs\build-msi.log.

.PARAMETER Bundles
    Installer formats to produce: msi (default), nsis, or all (both).

.PARAMETER SkipNpmInstall
    Skip `npm install` and build with node_modules as-is. Faster; only safe when
    web\package-lock.json has not changed since the last install.

.PARAMETER NoClean
    Keep the previous web\dist, published sidecar and bundle output instead of
    deleting them first. Turns this into an incremental build.

.PARAMETER FullClean
    Also wipe the Rust target directory (`cargo clean`) for a true from-scratch
    build. Adds several minutes.

.PARAMETER Install
    Launch the produced MSI with msiexec when the build succeeds.

.EXAMPLE
    pwsh -File scripts/tauri/build-msi.ps1

.EXAMPLE
    pwsh -File scripts/tauri/build-msi.ps1 -Bundles all -FullClean
#>
[CmdletBinding()]
param(
    [ValidateSet('msi', 'nsis', 'all')]
    [string]$Bundles = 'msi',
    [switch]$SkipNpmInstall,
    [switch]$NoClean,
    [switch]$FullClean,
    [switch]$Install
)

Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'

. (Join-Path $PSScriptRoot '_common.ps1')

$repoRoot = Get-RepoRoot
$tauriDir = Join-Path $repoRoot 'src-tauri'
$bundleDir = Join-Path $tauriDir 'target\release\bundle'
$logFile = Join-Path (Get-LogDirectory) 'build-msi.log'

$startedAt = Get-Date
Start-Transcript -Path $logFile -Force | Out-Null
try {
    Write-Host 'SwebKit - Tauri installer build' -ForegroundColor Cyan
    Write-Host '===============================' -ForegroundColor Cyan
    Write-Info "repo:    $repoRoot"
    Write-Info "bundles: $Bundles"
    Write-Info "log:     $logFile"

    # -----------------------------------------------------------------------
    # 0. Prerequisites
    # -----------------------------------------------------------------------
    Write-Step 'Checking prerequisites...'
    Assert-Tool -Name 'node' -InstallHint 'Install Node.js 20+ from https://nodejs.org/.'
    Assert-Tool -Name 'npm' -InstallHint 'Install Node.js 20+ from https://nodejs.org/.'
    Assert-Tool -Name 'dotnet' -InstallHint 'Install the .NET 10 SDK (see global.json).'
    Assert-Tool -Name 'cargo' -InstallHint 'Install Rust (stable) from https://rustup.rs/, plus VS Build Tools 2022 with the "Desktop development with C++" workload.'
    Write-Host '[ok]    node, npm, dotnet, cargo found' -ForegroundColor Green

    # -----------------------------------------------------------------------
    # 1. Clean
    # -----------------------------------------------------------------------
    if ($NoClean) {
        Write-Step 'Skipping clean (-NoClean): this is an incremental build.'
    }
    else {
        Write-Step 'Cleaning previous build output...'
        Remove-DirectoryIfPresent $bundleDir

        if ($FullClean) {
            Write-Info 'cargo clean (this makes the Rust build start from scratch)'
            Invoke-Native -FilePath 'cargo' -Arguments @('clean') -WorkingDirectory $tauriDir
        }
    }

    # -----------------------------------------------------------------------
    # 2. Frontend (must exist before Tauri bundles it: frontendDist=../web/dist)
    # -----------------------------------------------------------------------
    # No VITE_SIDECAR_URL here on purpose: in the packaged app Tauri assigns the
    # sidecar a free port at startup and the frontend resolves it over IPC.
    # Baking a URL in would pin the app to a port nothing is listening on.
    Build-Frontend -SkipNpmInstall:$SkipNpmInstall -Clean:(-not $NoClean) | Out-Null

    # -----------------------------------------------------------------------
    # 3. Sidecar (bundled as a Tauri resource)
    # -----------------------------------------------------------------------
    $sidecarExe = Publish-Sidecar -Clean:(-not $NoClean)
    Write-Host "[ok]    sidecar published: $sidecarExe" -ForegroundColor Green

    # -----------------------------------------------------------------------
    # 4. Bundle
    # -----------------------------------------------------------------------
    # tauri build MUST run from src-tauri (or the repo root) - the CLI only finds
    # tauri.conf.json in the current directory or below, never in a parent.
    Write-Step "Building the Tauri bundle ($Bundles)... this takes a few minutes."
    $tauriArgs = @((Get-TauriCli), 'build')
    if ($Bundles -ne 'all') { $tauriArgs += @('--bundles', $Bundles) }
    Invoke-Native -FilePath 'node' -Arguments $tauriArgs -WorkingDirectory $tauriDir

    # -----------------------------------------------------------------------
    # 5. Report
    # -----------------------------------------------------------------------
    Write-Step 'Build output'

    $artifacts = @(
        Get-ChildItem -Path $bundleDir -Recurse -Include '*.msi', '*-setup.exe' -File -ErrorAction SilentlyContinue |
            Sort-Object LastWriteTime -Descending
    )

    if ($artifacts.Count -eq 0) {
        throw "tauri build succeeded but no installer was found under $bundleDir."
    }

    # A stale artifact from an earlier run would be misleading here, so surface
    # anything the current run did not actually rewrite.
    foreach ($artifact in $artifacts) {
        $isFresh = $artifact.LastWriteTime -ge $startedAt
        $sizeMb = [math]::Round($artifact.Length / 1MB, 1)
        $marker = if ($isFresh) { 'new  ' } else { 'stale' }
        $color = if ($isFresh) { 'Green' } else { 'Yellow' }
        Write-Host ("[{0}] {1} ({2} MB)" -f $marker, $artifact.FullName, $sizeMb) -ForegroundColor $color
    }

    $msi = $artifacts | Where-Object { $_.Extension -eq '.msi' } | Select-Object -First 1

    Write-Host ''
    Write-Host ("Done in {0:mm\:ss}." -f ((Get-Date) - $startedAt)) -ForegroundColor Green

    if ($Install) {
        if (-not $msi) { throw 'No .msi was produced, so -Install has nothing to run.' }
        Write-Step "Launching the installer for $($msi.Name)..."
        Start-Process 'msiexec.exe' -ArgumentList @('/i', "`"$($msi.FullName)`"") -Wait
    }
    elseif ($msi) {
        Write-Host "Install it with:  msiexec /i `"$($msi.FullName)`"" -ForegroundColor Cyan
    }
}
finally {
    Stop-Transcript | Out-Null
}
