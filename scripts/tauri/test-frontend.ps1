<#
.SYNOPSIS
    Freshly builds the frontend and the sidecar, then runs them together in your
    browser so you can test the production pair without building an installer.

.DESCRIPTION
    This is the middle ground between run-dev.ps1 (source + HMR + Tauri window)
    and build-msi.ps1 (full installer, minutes per iteration): it exercises the
    exact two artifacts the MSI ships —

      * the production frontend bundle (web\dist, built by vite, not served by
        the dev server), and
      * the published sidecar executable (src-tauri\binaries\sidecar\SwebKit.Sidecar.exe,
        self-contained, Release)

    — wired to each other and served at http://127.0.0.1:<Port>. Both are rebuilt
    from scratch on every run; there is no "reuse what's there" path by default,
    because a stale bundle silently testing yesterday's code is the failure mode
    this script exists to prevent.

    Two things differ from the packaged app by necessity: the sidecar port is
    fixed (Tauri normally assigns a free one over IPC, which needs no browser
    equivalent — so it is inlined into the bundle as VITE_SIDECAR_URL instead),
    and Tauri-native features (secret storage, native dialogs, shell) are
    unavailable outside the webview. Everything that talks to the sidecar over
    HTTP behaves identically.

    Press Ctrl+C to stop; both processes are terminated on the way out.
    Logs: scripts\logs\test-sidecar.log, scripts\logs\test-preview.log.

.PARAMETER Port
    Port for the static preview server. Defaults to 1421 so it never collides
    with run-dev.ps1's Vite dev server on 1420.

.PARAMETER SidecarPort
    Port the published sidecar listens on. Defaults to 5199 (the dev convention).

.PARAMETER UseRealAppData
    Let the sidecar read and write your real %AppData%\SwebKit configuration.
    By default it is redirected to a throwaway folder (scripts\.test-appdata) so a
    test run cannot damage your saved profiles, templates or monitoring rules.

.PARAMETER FrameworkDependent
    Publish the sidecar framework-dependent instead of self-contained. Noticeably
    faster to publish, but no longer byte-for-byte what the installer ships.

.PARAMETER SkipNpmInstall
    Skip `npm install` and build with node_modules as-is.

.PARAMETER NoBrowser
    Don't open a browser window; just print the URL.

.EXAMPLE
    pwsh -File scripts/tauri/test-frontend.ps1

.EXAMPLE
    pwsh -File scripts/tauri/test-frontend.ps1 -UseRealAppData -SidecarPort 5197
#>
[CmdletBinding()]
param(
    [int]$Port = 1421,
    [int]$SidecarPort = 5199,
    [switch]$UseRealAppData,
    [switch]$FrameworkDependent,
    [switch]$SkipNpmInstall,
    [switch]$NoBrowser
)

Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'

. (Join-Path $PSScriptRoot '_common.ps1')

$repoRoot = Get-RepoRoot
$webDir = Join-Path $repoRoot 'web'
$logDir = Get-LogDirectory
$sidecarLog = Join-Path $logDir 'test-sidecar.log'
$sidecarErrLog = Join-Path $logDir 'test-sidecar.err.log'
$previewLog = Join-Path $logDir 'test-preview.log'
$previewErrLog = Join-Path $logDir 'test-preview.err.log'
$appDataRoot = Join-Path $repoRoot 'scripts\.test-appdata'
$sidecarUrl = "http://127.0.0.1:$SidecarPort"
$previewUrl = "http://127.0.0.1:$Port"

$viteCli = Join-Path $webDir 'node_modules\vite\bin\vite.js'

$sidecarProcess = $null
$previewProcess = $null

try {
    Write-Host 'SwebKit - production frontend + sidecar test run' -ForegroundColor Cyan
    Write-Host '===============================================' -ForegroundColor Cyan

    Write-Step 'Checking prerequisites...'
    Assert-Tool -Name 'node' -InstallHint 'Install Node.js 20+ from https://nodejs.org/.'
    Assert-Tool -Name 'npm' -InstallHint 'Install Node.js 20+ from https://nodejs.org/.'
    Assert-Tool -Name 'dotnet' -InstallHint 'Install the .NET 10 SDK (see global.json).'

    # Refuse to run against someone else's sidecar - the whole point is to test
    # the one this script is about to build.
    if (Wait-ForHttp -Url "$sidecarUrl/health" -Name 'An existing sidecar' -TimeoutSeconds 1) {
        throw "Something is already listening on $sidecarUrl. Stop it (or pass -SidecarPort <other>) so this run tests the sidecar it just built."
    }

    # 1. Sidecar first: the frontend build needs its URL baked in (below).
    $sidecarExe = Publish-Sidecar -FrameworkDependent:$FrameworkDependent -Clean

    # 2. Production frontend bundle. Vite inlines VITE_SIDECAR_URL at build time,
    #    so the port has to be known now, not at launch.
    Build-Frontend -SidecarUrl $sidecarUrl -SkipNpmInstall:$SkipNpmInstall -Clean | Out-Null

    # 3. Start the published sidecar.
    Write-Step "Starting the published sidecar on $sidecarUrl..."
    $sidecarEnvironment = @{}
    if ($UseRealAppData) {
        Write-Host '[warn]  using your real %AppData%\SwebKit configuration (-UseRealAppData).' -ForegroundColor Yellow
    }
    else {
        if (-not (Test-Path $appDataRoot)) { New-Item -ItemType Directory -Path $appDataRoot -Force | Out-Null }
        Write-Info "SWEBKIT_APPDATA_ROOT=$appDataRoot (throwaway; your real config is untouched)"
        $sidecarEnvironment['SWEBKIT_APPDATA_ROOT'] = $appDataRoot
    }

    $sidecarStartInfo = @{
        FilePath               = $sidecarExe
        ArgumentList           = @('--urls', $sidecarUrl)
        WorkingDirectory       = Split-Path -Parent $sidecarExe
        RedirectStandardOutput = $sidecarLog
        RedirectStandardError  = $sidecarErrLog
        PassThru               = $true
        WindowStyle            = 'Hidden'
    }

    # Start-Process has no -Environment parameter, so the variable is set on this
    # process (children inherit it) and restored right after the spawn.
    $previousAppDataRoot = [Environment]::GetEnvironmentVariable('SWEBKIT_APPDATA_ROOT')
    try {
        foreach ($key in $sidecarEnvironment.Keys) {
            [Environment]::SetEnvironmentVariable($key, $sidecarEnvironment[$key])
        }
        $sidecarProcess = Start-Process @sidecarStartInfo
    }
    finally {
        [Environment]::SetEnvironmentVariable('SWEBKIT_APPDATA_ROOT', $previousAppDataRoot)
    }

    if (-not (Wait-ForHttp -Url "$sidecarUrl/health" -Name 'Sidecar' -TimeoutSeconds 60)) {
        throw "The sidecar did not become healthy. See $sidecarLog and $sidecarErrLog."
    }

    # 4. Serve web\dist statically (vite preview - no dev server, no HMR, no
    #    on-the-fly transforms: this is the built bundle exactly as shipped).
    Write-Step "Serving the production bundle on $previewUrl..."
    $previewProcess = Start-Process -FilePath 'node' `
        -ArgumentList @($viteCli, 'preview', '--port', $Port, '--strictPort', '--host', '127.0.0.1') `
        -WorkingDirectory $webDir `
        -RedirectStandardOutput $previewLog `
        -RedirectStandardError $previewErrLog `
        -PassThru -WindowStyle Hidden

    if (-not (Wait-ForHttp -Url $previewUrl -Name 'Preview server' -TimeoutSeconds 30)) {
        throw "The preview server did not start. See $previewLog and $previewErrLog."
    }

    if (-not $NoBrowser) { Start-Process $previewUrl }

    Write-Host ''
    Write-Host "SwebKit is running at $previewUrl (sidecar: $sidecarUrl)" -ForegroundColor Green
    Write-Host "Logs: $sidecarLog, $previewLog" -ForegroundColor DarkGray
    Write-Host 'Press Ctrl+C to stop both.' -ForegroundColor Cyan

    # Block until interrupted, or until either process dies on its own (a sidecar
    # crash mid-session should end the run instead of leaving a dead-backend page).
    while ($true) {
        Start-Sleep -Seconds 1
        if ($sidecarProcess.HasExited) {
            Write-Warning "The sidecar exited with code $($sidecarProcess.ExitCode). See $sidecarErrLog."
            break
        }
        if ($previewProcess.HasExited) {
            Write-Warning "The preview server exited with code $($previewProcess.ExitCode). See $previewErrLog."
            break
        }
    }
}
finally {
    Write-Host ''
    Write-Host '[stop]  shutting down...' -ForegroundColor Cyan
    foreach ($process in @($previewProcess, $sidecarProcess)) {
        if ($process -and -not $process.HasExited) {
            Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue
        }
    }
}
