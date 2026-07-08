<#
.SYNOPSIS
    One-command setup, build, and install for SwebKit (self-signed MSIX sideload).

.DESCRIPTION
    Safe to re-run any time (e.g. after pulling new changes). It will:
      1. Generate (or reuse) a local "CN=SwebKit" self-signed code-signing certificate.
      2. Point SwebKit.App.csproj at that certificate's thumbprint.
      3. Publish the Release MSIX package.
      4. Trust the package's certificate for local sideloading (one UAC prompt).
      5. Install the MSIX with Add-AppxPackage.
      6. Launch SwebKit.

    No manual thumbprint copy/paste, no double-clicking files. Designed to be run by a
    human or driven non-interactively by an automation/agent — every step is scripted,
    and re-running it is a no-op where nothing changed.

.PARAMETER SkipInstall
    Build and sign the package but don't trust/install/launch it.

.PARAMETER NoLaunch
    Install the app but don't launch it afterwards.

.EXAMPLE
    pwsh -File scripts/install.ps1
#>
[CmdletBinding()]
param(
    [switch]$SkipInstall,
    [switch]$NoLaunch
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$csprojPath = Join-Path $repoRoot "src/SwebKit.App/SwebKit.App.csproj"
$targetFramework = "net10.0-windows10.0.19041.0"
$certSubject = "CN=SwebKit"

function Write-Step {
    param([string]$Message)
    Write-Host ""
    Write-Host "==> $Message" -ForegroundColor Cyan
}

if (-not (Test-Path $csprojPath)) {
    throw "Could not find $csprojPath — run this script from a full clone of the SwebKit repo."
}

# ---------------------------------------------------------------------------
# 1. Find or create the local signing certificate
# ---------------------------------------------------------------------------
Write-Step "Checking for a local '$certSubject' signing certificate..."

$cert = Get-ChildItem Cert:\CurrentUser\My |
Where-Object { $_.Subject -eq $certSubject -and $_.NotAfter -gt (Get-Date) } |
Sort-Object NotAfter -Descending |
Select-Object -First 1

if ($cert) {
    Write-Host "Found existing certificate (thumbprint $($cert.Thumbprint), expires $($cert.NotAfter))."
}
else {
    Write-Host "None found — generating a new one-year self-signed certificate..."
    $cert = New-SelfSignedCertificate `
        -Type Custom `
        -Subject $certSubject `
        -KeyUsage DigitalSignature `
        -FriendlyName "SwebKit" `
        -CertStoreLocation "Cert:\CurrentUser\My" `
        -TextExtension @("2.5.29.37={text}1.3.6.1.5.5.7.3.3", "2.5.29.19={text}")
    Write-Host "Created certificate with thumbprint $($cert.Thumbprint)."
}

$thumbprint = $cert.Thumbprint

# ---------------------------------------------------------------------------
# 2. Sync the csproj's PackageCertificateThumbprint
# ---------------------------------------------------------------------------
Write-Step "Syncing PackageCertificateThumbprint in SwebKit.App.csproj..."

$csprojContent = Get-Content $csprojPath -Raw
$thumbprintPattern = '<PackageCertificateThumbprint>[^<]*</PackageCertificateThumbprint>'
$thumbprintReplacement = "<PackageCertificateThumbprint>$thumbprint</PackageCertificateThumbprint>"

if ($csprojContent -match $thumbprintPattern) {
    if ($Matches[0] -eq $thumbprintReplacement) {
        Write-Host "csproj already references this certificate — nothing to change."
    }
    else {
        ($csprojContent -replace $thumbprintPattern, $thumbprintReplacement) |
        Set-Content -Path $csprojPath -NoNewline
        Write-Host "Updated csproj to use thumbprint $thumbprint."
    }
}
else {
    throw "Could not find a <PackageCertificateThumbprint> element in $csprojPath to update."
}

# ---------------------------------------------------------------------------
# 3. Publish the Release MSIX
# ---------------------------------------------------------------------------
Write-Step "Publishing SwebKit (Release, $targetFramework)... this can take a few minutes."

dotnet publish $csprojPath -c Release -f $targetFramework
if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE."
}

# ---------------------------------------------------------------------------
# 4. Locate the produced .msix + its sibling .cer
# ---------------------------------------------------------------------------
Write-Step "Locating the published package..."

$appDir = Split-Path -Parent $csprojPath
$msix = Get-ChildItem -Path $appDir -Recurse -Filter "*.msix" -ErrorAction SilentlyContinue |
Sort-Object LastWriteTime -Descending |
Select-Object -First 1

if (-not $msix) {
    throw "No .msix package found under $appDir after publish. Check the dotnet publish output above for errors."
}

$cerPath = Join-Path $msix.DirectoryName ($msix.BaseName + ".cer")
Write-Host "Package: $($msix.FullName)"

if ($SkipInstall) {
    Write-Host ""
    Write-Host "Skipped install (-SkipInstall). Double-click the .msix above, or re-run without -SkipInstall." -ForegroundColor Yellow
    return
}

# ---------------------------------------------------------------------------
# 5. Trust the package certificate for local sideloading (needs admin, once)
# ---------------------------------------------------------------------------
Write-Step "Trusting the package certificate for local sideloading..."

$alreadyTrusted = Get-ChildItem Cert:\LocalMachine\TrustedPeople -ErrorAction SilentlyContinue |
Where-Object { $_.Thumbprint -eq $thumbprint }

if ($alreadyTrusted) {
    Write-Host "Certificate is already trusted on this machine."
}
elseif (Test-Path $cerPath) {
    $isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).
    IsInRole([Security.Principal.WindowsBuiltinRole]::Administrator)

    if ($isAdmin) {
        Import-Certificate -FilePath $cerPath -CertStoreLocation Cert:\LocalMachine\TrustedPeople | Out-Null
        Write-Host "Certificate trusted."
    }
    else {
        Write-Host "This one-time step needs admin rights — expect a UAC prompt." -ForegroundColor Yellow
        $importCommand = "Import-Certificate -FilePath '$cerPath' -CertStoreLocation Cert:\LocalMachine\TrustedPeople"
        Start-Process pwsh -Verb RunAs -Wait -ArgumentList @("-NoProfile", "-Command", $importCommand)
        Write-Host "Certificate trusted."
    }
}
else {
    Write-Host "No .cer file found next to the package — skipping trust step (it may already be trusted)." -ForegroundColor Yellow
}

# ---------------------------------------------------------------------------
# 6. Install
# ---------------------------------------------------------------------------
Write-Step "Installing SwebKit..."

try {
    Add-AppxPackage -Path $msix.FullName -ForceApplicationShutdown
}
catch {
    # This script always publishes the same fixed package version, so a rebuild with code
    # changes but no version bump has the same identity as the already-installed package —
    # Windows blocks that (HRESULT 0x80073CFB) instead of silently upgrading it. Remove the
    # stale package and retry once instead of making the user do it by hand; this is what
    # "safe to re-run any time" above actually promises.
    $isSameIdentityConflict = $_.Exception.Message -match '0x80073CFB' -or
    $_.Exception.Message -match 'same identity as an already-installed package'

    if (-not $isSameIdentityConflict) {
        throw
    }

    Write-Host "An older build with the same package version is already installed — removing it and retrying..." -ForegroundColor Yellow

    Get-AppxPackage -Name "*SwebKit*" | ForEach-Object {
        Remove-AppxPackage -Package $_.PackageFullName -ErrorAction Stop
    }

    Add-AppxPackage -Path $msix.FullName -ForceApplicationShutdown
}

Write-Host "SwebKit installed." -ForegroundColor Green

# ---------------------------------------------------------------------------
# 7. Launch
# ---------------------------------------------------------------------------
if (-not $NoLaunch) {
    Write-Step "Launching SwebKit..."
    $installedApp = Get-AppxPackage -Name "*SwebKit*" | Select-Object -First 1
    if ($installedApp) {
        $appId = (Get-AppxPackageManifest $installedApp).Package.Applications.Application.Id
        Start-Process "shell:AppsFolder\$($installedApp.PackageFamilyName)!$appId"
    }
    else {
        Write-Host "Could not resolve the installed package automatically — open SwebKit from the Start Menu." -ForegroundColor Yellow
    }
}

Write-Host ""
Write-Host "Done. SwebKit is installed and pinnable from the Start Menu." -ForegroundColor Green
