# Packaging & Local Installation (MSIX)

**This page covers the legacy MAUI app only** (`src/SwebKit.App`, MSIX, self-signed
sideload). For the Tauri rewrite's MSI/NSIS installer see
[scripts/README.md](../scripts/README.md) — `pwsh -File scripts/tauri/build-msi.ps1`.

SwebKit is distributed as a self-signed MSIX package for Windows — there is no public
distribution yet, so every machine (yours, a teammate's, or an AI agent acting on your
behalf) signs its own local build with its own certificate.

## Quick start (recommended)

From a full clone of the repo, in PowerShell:

```powershell
pwsh -File scripts/maui/install.ps1
```

This single command:

1. Generates a local `CN=SwebKit` self-signed code-signing certificate (or reuses one
   you already have, if it's still valid).
2. Updates `SwebKit.App.csproj`'s `PackageCertificateThumbprint` to match it.
3. Bumps `Package.appxmanifest`'s `Identity Version` to a value newer than any
   previous install, so Windows treats this as an **upgrade** (see
   [Config persistence across installs](#config-persistence-across-installs) below).
4. Runs `dotnet publish -c Release` to produce the MSIX.
5. Trusts that certificate for local sideloading — this needs admin rights, so expect
   **one UAC prompt** the first time you ever run it on a machine.
6. Installs the MSIX with `Add-AppxPackage`.
7. Launches SwebKit.

Re-run the exact same command any time (e.g. after pulling new changes) to rebuild and
update the installed app — every step is idempotent, so nothing happens twice
unnecessarily (an already-trusted certificate isn't re-trusted, an up-to-date csproj
isn't rewritten, etc.).

Useful flags:

| Flag           | Effect                                                             |
| -------------- | ------------------------------------------------------------------ |
| `-SkipInstall` | Build and sign the package only; leaves the `.msix` for you to run |
| `-NoLaunch`    | Install but don't open the app afterwards                          |

**This script is safe for an AI coding agent to run unattended.** The only interactive
moment is the OS-level UAC elevation prompt for trusting the certificate (a real human
must click "Yes" on that dialog once per machine — an agent cannot and should not try
to bypass it). Everything else — certificate generation, csproj updates, build, install,
launch — is fully scripted with no prompts.

---

## How it works (what the script automates)

### 1. Application icon

`src/SwebKit.App/SwebKit.App.csproj` — `MauiIcon` points to the custom icon:

```xml
<MauiIcon Include="Resources\AppIcon\swebkit_icon.svg" />
```

### 2. MSIX packaging

`SwebKit.App.csproj` has packaging enabled by default (no `WindowsPackageType` override):

```xml
<WindowsPackagePublisherName>CN=SwebKit</WindowsPackagePublisherName>
<AppxPackageSigningEnabled>true</AppxPackageSigningEnabled>
<PackageCertificateThumbprint>...</PackageCertificateThumbprint>
```

`scripts/maui/install.ps1` keeps `PackageCertificateThumbprint` in sync with whatever
certificate exists on the current machine — you should never need to edit this by hand.

### 3. Package manifest

`src/SwebKit.App/Platforms/Windows/Package.appxmanifest` — Identity and publisher:

```xml
<Identity Name="com.swebkit.app" Publisher="CN=SwebKit" Version="0.0.0.0" />
<PublisherDisplayName>SwebKit</PublisherDisplayName>
```

`Publisher` must always match `WindowsPackagePublisherName`/the certificate's
`Subject` (`CN=SwebKit`) — this is why the script always generates certificates with
that exact subject, so any machine's self-signed cert satisfies the manifest.

The `Version="0.0.0.0"` you see checked in is just a placeholder — `install.ps1`
overwrites it before every publish. See the next section for why that matters.

<a id="config-persistence-across-installs"></a>

### 4. Config persistence across installs

SwebKit is a packaged (MSIX, full-trust) app, so Windows redirects its `%AppData%`
reads/writes to a per-package virtualized folder tied to the package's identity —
and **deletes that folder when the package is uninstalled**. Upgrading a package
in place (installing a strictly newer `Identity Version`) preserves it; only a full
uninstall does not.

Before the version-bump step existed, `Package.appxmanifest` carried the same
hardcoded `Version="0.0.0.0"` on every rebuild. Publishing again with unchanged
identity/version hits Windows' same-identity rejection (`0x80073CFB`), and the
script's only recourse was to `Remove-AppxPackage` (uninstall) and reinstall —
which is exactly the operation that wipes saved config. So on this script's old
behavior, **every rebuild silently reset your settings**, because "install a new
version" was actually "uninstall, then install."

`install.ps1` now derives `Identity Version`'s `Build.Revision` from the current
time before every publish, so each run is always strictly newer than the last and
Windows performs a real in-place upgrade — config now survives normal use of this
script. The old uninstall/reinstall fallback still exists for the rare case of two
runs within the same minute, or an install that predates this change, but it's now
the exception rather than the rule.

### 5. The self-signed certificate

Generated with:

```powershell
New-SelfSignedCertificate `
  -Type Custom `
  -Subject "CN=SwebKit" `
  -KeyUsage DigitalSignature `
  -FriendlyName "SwebKit" `
  -CertStoreLocation "Cert:\CurrentUser\My" `
  -TextExtension @("2.5.29.37={text}1.3.6.1.5.5.7.3.3", "2.5.29.19={text}")
```

Stored in `Cert:\CurrentUser\My` (needed to _sign_ the package at publish time) and its
public `.cer` is imported into `Cert:\LocalMachine\TrustedPeople` (needed for Windows to
_trust_ the signature at install time — `CurrentUser\Root` is not sufficient and
produces error `0x800B0109`).

---

## Manual steps (if you don't want to run the script)

<details>
<summary>Expand for the fully manual equivalent</summary>

### Step 1 — Generate a certificate

```powershell
$cert = New-SelfSignedCertificate `
  -Type Custom `
  -Subject "CN=SwebKit" `
  -KeyUsage DigitalSignature `
  -FriendlyName "SwebKit" `
  -CertStoreLocation "Cert:\CurrentUser\My" `
  -TextExtension @("2.5.29.37={text}1.3.6.1.5.5.7.3.3", "2.5.29.19={text}")

Write-Output $cert.Thumbprint
```

### Step 2 — Update the csproj thumbprint

In `src/SwebKit.App/SwebKit.App.csproj`:

```xml
<PackageCertificateThumbprint>YOUR_THUMBPRINT_HERE</PackageCertificateThumbprint>
```

### Step 2.5 — Bump the package version

In `src/SwebKit.App/Platforms/Windows/Package.appxmanifest`, set `Identity`'s
`Version` to something strictly greater than whatever is currently installed
(check with `Get-AppxPackage -Name "*SwebKit*"`). Skipping this step means Windows
may reject the install as a duplicate identity, or — if you work around that with
`Remove-AppxPackage` first — wipe your saved SwebKit configuration; see
[Config persistence across installs](#config-persistence-across-installs) above.

### Step 3 — Publish

```bash
dotnet publish src/SwebKit.App/SwebKit.App.csproj -c Release -f net10.0-windows10.0.19041.0
```

Output lands under `src/SwebKit.App/bin/Release/net10.0-windows10.0.19041.0/win-x64/`,
in an `AppPackages/` folder containing the `.msix` and a matching `.cer`.

### Step 4 — Trust the certificate (elevated PowerShell)

```powershell
Import-Certificate -FilePath "path\to\the.cer" -CertStoreLocation Cert:\LocalMachine\TrustedPeople
```

### Step 5 — Install

```powershell
Add-AppxPackage -Path "path\to\the.msix"
```

Or just double-click the `.msix` file.

</details>

---

## Notes

- The self-signed certificate is only valid for sideloading on machines that explicitly
  trust it. It is **not** suitable for Microsoft Store distribution — see
  [MICROSOFT_STORE_SUBMISSION_GUIDE.md](../MICROSOFT_STORE_SUBMISSION_GUIDE.md) for that
  path when SwebKit is ready to publish for real.
- Certificates expire after 1 year. `scripts/maui/install.ps1` detects an expired/missing
  certificate and generates a fresh one automatically — nothing to remember.
- The package version bump (previous section) touches the tracked
  `Package.appxmanifest` file, the same way the certificate thumbprint sync touches
  the tracked `.csproj` — expect a local working-tree diff on that file after
  running the script. That's expected; it's local machine/build state, not meant
  to be committed.
