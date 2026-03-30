# Packaging & Local Installation (MSIX)

SwebKit is distributed as an MSIX package for Windows. This document covers the one-time setup already done, what you need to reproduce it on a new machine, and how to build and install.

---

## What was configured

### 1. Application icon

`src/SwebKit.App/SwebKit.App.csproj` — `MauiIcon` now points to the custom icon:

```xml
<MauiIcon Include="Resources\AppIcon\swebkit_icon.svg" />
```

### 2. MSIX packaging enabled

`WindowsPackageType` was removed (it was previously set to `None`, which disabled packaging).
The following was added to `SwebKit.App.csproj`:

```xml
<WindowsPackagePublisherName>CN=SwebKit</WindowsPackagePublisherName>
<AppxPackageSigningEnabled>true</AppxPackageSigningEnabled>
<PackageCertificateThumbprint>284DD5251E7471870A273AE03290B7114034F3C4</PackageCertificateThumbprint>
```

### 3. Package manifest updated

`src/SwebKit.App/Platforms/Windows/Package.appxmanifest` — Identity and publisher updated:

```xml
<Identity Name="com.swebkit.app" Publisher="CN=SwebKit" Version="0.0.0.0" />
<PublisherDisplayName>SwebKit</PublisherDisplayName>
```

### 4. Self-signed certificate created (your machine)

A self-signed certificate was generated and trusted in `CurrentUser\Root` on your machine:

- **Subject:** `CN=SwebKit`
- **Thumbprint:** `284DD5251E7471870A273AE03290B7114034F3C4`
- **Store:** `Cert:\CurrentUser\My` + trusted in `Cert:\CurrentUser\Root`

---

## Building the MSIX

```bash
dotnet publish src/SwebKit.App/SwebKit.App.csproj -c Release -f net10.0-windows10.0.19041.0
```

Output lands in:

```
src/SwebKit.App/bin/Release/net10.0-windows10.0.19041.0/win-x64/AppPackages/
```

Double-click the `.msix` file to install. SwebKit will appear in the Start Menu and can be pinned to the taskbar. It can be uninstalled from Windows Settings → Apps.

---

## Installing on your machine (after setup is done)

Prerequisites are already met on the developer machine. Just:

1. Build the MSIX (see above)
2. Double-click the `.msix` to install

---

## Setting up on a new machine

The certificate and trust chain do not transfer automatically. Follow these steps on each new machine:

### Step 1 — Generate a new self-signed certificate

Run in PowerShell (no admin needed):

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

Copy the thumbprint output.

### Step 2 — Trust the certificate

Run in an **elevated PowerShell (Run as Administrator)**:

```powershell
$cert = Get-Item "Cert:\CurrentUser\My\<THUMBPRINT>"
$store = New-Object System.Security.Cryptography.X509Certificates.X509Store("TrustedPeople", "LocalMachine")
$store.Open("ReadWrite")
$store.Add($cert)
$store.Close()
```

> `LocalMachine\TrustedPeople` is required for MSIX sideloading. `CurrentUser\Root` is not sufficient and will produce error `0x800B0109`.

### Step 3 — Update the csproj thumbprint

In `src/SwebKit.App/SwebKit.App.csproj`, replace the thumbprint:

```xml
<PackageCertificateThumbprint>YOUR_NEW_THUMBPRINT_HERE</PackageCertificateThumbprint>
```

### Step 4 — Build and install

```bash
dotnet publish src/SwebKit.App/SwebKit.App.csproj -c Release -f net10.0-windows10.0.19041.0
```

Double-click the generated `.msix` to install.

---

## Notes

- The self-signed certificate is only valid for sideloading on machines that explicitly trust it. It is not suitable for Microsoft Store distribution.
- The certificate expires in 1 year by default. Regenerate and repeat the setup steps when it expires.
- If you publish a new version, increment `ApplicationDisplayVersion` / `ApplicationVersion` in `SwebKit.App.csproj` so Windows recognises it as an update rather than a conflicting install.
