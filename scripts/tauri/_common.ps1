# _common.ps1 - shared helpers for the Tauri scripts in this folder.
#
# Dot-source it from a sibling script:
#     . (Join-Path $PSScriptRoot '_common.ps1')
#
# Nothing here has side effects on import; it only defines functions. The two
# build steps live here (rather than being copy-pasted per script) because
# build-msi.ps1 and test-frontend.ps1 must produce *identical* artifacts — the
# whole point of test-frontend.ps1 is that what you test is what the MSI ships.

# No Set-StrictMode here on purpose: dot-sourcing runs in the *caller's* scope, so
# setting it would silently change the rules of an interactive session too. Each
# script in this folder sets its own.

# Repo root, resolved from this file's location (scripts\tauri\ -> two levels up).
function Get-RepoRoot {
    (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
}

function Write-Step {
    param([Parameter(Mandatory)][string]$Message)

    Write-Host ''
    Write-Host "==> $Message" -ForegroundColor Cyan
}

function Write-Info {
    param([Parameter(Mandatory)][string]$Message)

    Write-Host "    $Message" -ForegroundColor DarkGray
}

# Runs a native command and throws on a non-zero exit code. PowerShell does not
# do this on its own: `dotnet publish` failing leaves $LASTEXITCODE set but the
# script happily continues, which is how you end up bundling a stale sidecar.
function Invoke-Native {
    param(
        [Parameter(Mandatory)][string]$FilePath,
        [string[]]$Arguments = @(),
        [string]$WorkingDirectory,
        [hashtable]$Environment = @{}
    )

    $previousEnvironment = @{}
    foreach ($key in $Environment.Keys) {
        $previousEnvironment[$key] = [Environment]::GetEnvironmentVariable($key)
        [Environment]::SetEnvironmentVariable($key, $Environment[$key])
    }

    if ($WorkingDirectory) { Push-Location $WorkingDirectory }
    try {
        Write-Info "$FilePath $($Arguments -join ' ')"
        # Out-Host, not bare invocation: a native command's stdout would otherwise
        # land in the *pipeline* of whichever function called this, so a helper
        # that returns a path (Publish-Sidecar) would return the build log too.
        & $FilePath @Arguments | Out-Host
        if ($LASTEXITCODE -ne 0) {
            throw "$FilePath $($Arguments -join ' ') failed with exit code $LASTEXITCODE."
        }
    }
    finally {
        if ($WorkingDirectory) { Pop-Location }
        foreach ($key in $previousEnvironment.Keys) {
            [Environment]::SetEnvironmentVariable($key, $previousEnvironment[$key])
        }
    }
}

# Fails early with an actionable message instead of a confusing mid-build error.
function Assert-Tool {
    param(
        [Parameter(Mandatory)][string]$Name,
        [Parameter(Mandatory)][string]$InstallHint
    )

    if (-not (Get-Command $Name -ErrorAction SilentlyContinue)) {
        throw "'$Name' was not found on PATH. $InstallHint"
    }
}

function Remove-DirectoryIfPresent {
    param([Parameter(Mandatory)][string]$Path)

    if (Test-Path $Path) {
        Write-Info "removing $Path"
        Remove-Item -LiteralPath $Path -Recurse -Force
    }
}

function Get-LogDirectory {
    $logDir = Join-Path (Get-RepoRoot) 'scripts\logs'
    if (-not (Test-Path $logDir)) { New-Item -ItemType Directory -Path $logDir -Force | Out-Null }
    $logDir
}

# Path to the locally installed Tauri CLI entry point. Deliberately invoked as
# `node <path>` rather than through the .bin shim so the same call works from
# pwsh, cmd and CI (this mirrors .github/workflows/release.yml).
function Get-TauriCli {
    $cli = Join-Path (Get-RepoRoot) 'web\node_modules\@tauri-apps\cli\tauri.js'
    if (-not (Test-Path $cli)) {
        throw "Tauri CLI not found at $cli - run 'npm install' in web\ first."
    }
    $cli
}

<#
.SYNOPSIS
    Builds the production frontend bundle into web\dist.

.PARAMETER SidecarUrl
    Baked into the bundle as VITE_SIDECAR_URL. Vite inlines env vars at build
    time, so this cannot be changed after the fact by setting a variable at run
    time. Leave empty for the MSI build: inside Tauri the real (OS-assigned)
    port is resolved over IPC at startup, and hardcoding a URL would override it.
#>
function Build-Frontend {
    param(
        [string]$SidecarUrl,
        [switch]$SkipNpmInstall,
        [switch]$Clean
    )

    $repoRoot = Get-RepoRoot
    $webDir = Join-Path $repoRoot 'web'

    if ($Clean) {
        Remove-DirectoryIfPresent (Join-Path $webDir 'dist')
    }

    if (-not $SkipNpmInstall) {
        Write-Step 'Installing frontend dependencies (npm install)...'
        Invoke-Native -FilePath 'npm' -Arguments @('install') -WorkingDirectory $webDir
    }

    Write-Step 'Building the frontend (tsc + vite build)...'
    $buildEnv = @{}
    if ($SidecarUrl) {
        Write-Info "VITE_SIDECAR_URL=$SidecarUrl (inlined into the bundle)"
        $buildEnv['VITE_SIDECAR_URL'] = $SidecarUrl
    }
    Invoke-Native -FilePath 'npm' -Arguments @('run', 'build') -WorkingDirectory $webDir -Environment $buildEnv

    $indexHtml = Join-Path $webDir 'dist\index.html'
    if (-not (Test-Path $indexHtml)) {
        throw "Frontend build reported success but $indexHtml is missing."
    }

    Join-Path $webDir 'dist'
}

<#
.SYNOPSIS
    Publishes the .NET sidecar into src-tauri\binaries\sidecar and returns the
    path to SwebKit.Sidecar.exe.

.DESCRIPTION
    That folder is gitignored and starts empty. tauri.conf.json bundles it as a
    resource, so if it is stale or empty the installer still builds fine and the
    installed app dies at startup with "Sidecar binary not found" — hence the
    explicit existence check at the end.

    Self-contained by default: end users are not expected to have the .NET
    runtime installed.
#>
function Publish-Sidecar {
    param(
        [switch]$FrameworkDependent,
        [switch]$Clean
    )

    $repoRoot = Get-RepoRoot
    $project = Join-Path $repoRoot 'src-sidecar\SwebKit.Sidecar.csproj'
    $outputDir = Join-Path $repoRoot 'src-tauri\binaries\sidecar'

    if ($Clean) {
        Remove-DirectoryIfPresent $outputDir
    }

    $selfContained = if ($FrameworkDependent) { 'false' } else { 'true' }
    Write-Step "Publishing the sidecar (Release, win-x64, self-contained=$selfContained)..."
    Invoke-Native -FilePath 'dotnet' -Arguments @(
        'publish', $project,
        '-c', 'Release',
        '-r', 'win-x64',
        "--self-contained", $selfContained,
        '-o', $outputDir
    )

    # tauri dev's build script needs this glob to match at least one file even
    # when the sidecar is run externally, and .gitkeep is what run-dev.* creates.
    $gitkeep = Join-Path $outputDir '.gitkeep'
    if (-not (Test-Path $gitkeep)) { New-Item -ItemType File -Path $gitkeep -Force | Out-Null }

    $exe = Join-Path $outputDir 'SwebKit.Sidecar.exe'
    if (-not (Test-Path $exe)) {
        throw "Sidecar publish reported success but $exe is missing - the bundle would ship without a sidecar."
    }

    $exe
}

# Polls an HTTP endpoint until it answers 200 or the timeout expires.
function Wait-ForHttp {
    param(
        [Parameter(Mandatory)][string]$Url,
        [Parameter(Mandatory)][string]$Name,
        [int]$TimeoutSeconds = 60
    )

    $elapsed = 0
    while ($elapsed -lt $TimeoutSeconds) {
        try {
            $response = Invoke-WebRequest -Uri $Url -TimeoutSec 2 -UseBasicParsing -ErrorAction Stop
            if ($response.StatusCode -eq 200) {
                Write-Host "[ok]    $Name is up ($Url)" -ForegroundColor Green
                return $true
            }
        }
        catch { }
        Start-Sleep -Seconds 1
        $elapsed += 1
    }

    Write-Warning "$Name did not answer on $Url within ${TimeoutSeconds}s."
    return $false
}
