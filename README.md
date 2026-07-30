# SwebKit

A .NET MAUI Blazor Hybrid desktop "Swiss army knife" debugging tool for .NET developers working
with Azure. Combines Azure Service Bus tooling, Application Insights / OpenTelemetry observability,
and AKS (Kubernetes) debugging helpers in a single developer-centric desktop app.

## Key Features

- **Service Bus** — inspect queue/topic/subscription messages, fix DLQ messages, send/replay test messages
- **Observability** — query Application Insights logs, explore distributed traces, view metrics dashboards
- **AKS** — workload overview, live pod log tailing, port-forwarding, pod shell
- **AI Agent** — intelligent assistant with Mistral AI integration, tool-based architecture for DevOps operations
- **Project + Environment** — everything scoped to a project (e.g. "OrderPlatform") and environment (Dev / Test / Acc / Prod)

## Tech Stack

- .NET MAUI Blazor Hybrid (Windows desktop, cross-platform ready)
- Microsoft Fluent UI Blazor for components
- Azure SDK (Service Bus, Monitor Query, Identity)
- KubernetesClient for AKS integration
- Monaco Editor + xterm.js via JSInterop

## Documentation

- [Documentation Entry Point](docs/README.md)
- [Feature Catalog](docs/features/README.md)
- [Architecture Overview](docs/ARCHITECTURE.md)
- [Full Design Document](docs/DESIGN.md)
- [Docs Rework Traceability Plan](docs/plans/docs-rework-traceability/index.md)
- [Documentation Migration Notes](docs/MIGRATION-NOTES.md)
- [Packaging & Install Details](docs/packaging-and-install.md)

## Install SwebKit (Windows)

SwebKit isn't published anywhere yet — it's a self-signed local install. One command
does everything: generates a signing certificate, builds the Release package, trusts
it, installs it, and launches it.

```powershell
git clone <this-repo-url>
cd SwebKit
pwsh -File scripts/maui/install.ps1
```

That's it. You'll get a single UAC prompt (to trust the certificate for sideloading)
the first time only. SwebKit will appear in the Start Menu afterwards, and you can
re-run the same command any time to rebuild and update it.

This script is also safe for an AI coding agent to run on your behalf — every step is
non-interactive except that one UAC prompt, and re-running it is a no-op wherever
nothing changed. See [docs/packaging-and-install.md](docs/packaging-and-install.md)
for what the script does under the hood and how to troubleshoot it.

## Getting Started (Contributors)

### Prerequisites

- .NET 10 SDK
- MAUI workload: `dotnet workload install maui`
- Windows 10/11 (primary target)

### Build

```bash
dotnet build
```

### Run

```bash
dotnet run --project src/SwebKit.App
```

### Test

```bash
dotnet test
```

## Project Structure

```
src/
  SwebKit.App/          # MAUI Blazor Hybrid app (Razor components)
  SwebKit.Core/         # Domain models, interfaces, configuration
  SwebKit.Agents/       # AI Agent with Mistral AI integration
  SwebKit.Azure/        # Azure Service Bus + App Insights implementations
  SwebKit.Kubernetes/   # AKS / Kubernetes implementation
  SwebKit.OpenTelemetry/ # OTLP observability provider
src-sidecar/            # .NET minimal API sidecar for the Tauri/React rewrite
web/                    # Vite + React + Tailwind frontend
web/e2e/                # Playwright E2E tests
tests/
  SwebKit.Agents.Tests/
  SwebKit.Core.Tests/
  SwebKit.Azure.Tests/
  SwebKit.Kubernetes.Tests/
```

## Web + .NET Sidecar (Tauri/React rewrite)

The `feat/tauri-react-rewrite` branch uses a .NET minimal API sidecar and a Vite/React/Tailwind frontend.

Every step below is also scripted under [scripts/tauri/](scripts/tauri/) — one command each
for the dev loop, a browser test run against the real production artifacts, and a fresh
installer. See [scripts/README.md](scripts/README.md); the manual recipes here document
what those scripts do.

### Prerequisites

- .NET 10 SDK
- Node.js 20+
- (optional) Tauri CLI for the desktop shell

### Run the sidecar (backend)

```powershell
cd src-sidecar
dotnet run --urls http://127.0.0.1:5199
```

The sidecar port can be overridden with `--urls` or `ASPNETCORE_URLS`.

### Run the frontend (dev)

```powershell
cd web
npm install          # first time only
npm run dev          # http://localhost:1420
```

The frontend expects the sidecar at `http://127.0.0.1:5199` unless you override it:

```powershell
cd web
$env:VITE_SIDECAR_URL="http://127.0.0.1:5198"; npm run dev
```

### Build

```powershell
# Frontend
cd web
npm run build

# Sidecar
cd src-sidecar
dotnet build
```

### Build the Tauri desktop app (installer)

The Tauri shell wraps the React frontend into a native Windows desktop app with an MSI/NSIS installer.

#### Quick start

```powershell
pwsh -File scripts/tauri/build-msi.ps1
```

Cleans the previous output and runs all four steps below in order, verifying each one —
including that the sidecar actually landed in the bundle, which is the easiest thing to
get wrong. Output: `src-tauri/target/release/bundle/msi/SwebKit_<version>_x64_en-US.msi`.
Flags (`-Bundles all`, `-FullClean`, `-NoClean`, `-Install`, …) are documented in
[scripts/README.md](scripts/README.md).

#### Prerequisites

- **Rust** (stable): install from https://rustup.rs/
- **Visual Studio Build Tools 2022** with the **Desktop development with C++** workload
  - Download from https://visualstudio.microsoft.com/visual-cpp-build-tools/
  - In the installer, check "Desktop development with C++" (includes MSVC, Windows SDK, and `link.exe`)
  - VS Code alone is NOT sufficient — you need the Build Tools
- Node.js 20+ (already required for the frontend)
- .NET 10 SDK (already required for the sidecar)

#### Build the frontend first

```powershell
cd web
npm install
npm run build
```

This produces `web/dist/` which Tauri bundles into the app.

#### Publish the sidecar second

The bundle ships the .NET sidecar as a resource (`src-tauri/binaries/sidecar/`). That
folder is gitignored and starts empty, so it must be populated before bundling — otherwise
the installer builds fine but the installed app dies at startup with
"Sidecar binary not found".

```powershell
dotnet publish src-sidecar\SwebKit.Sidecar.csproj -c Release -r win-x64 --self-contained true -o src-tauri\binaries\sidecar
```

Self-contained is required: end users are not expected to have the .NET runtime installed.

#### Build the installer

```powershell
cd src-tauri
node ../web/node_modules/@tauri-apps/cli/tauri.js build
```

Or if you have the Tauri CLI globally installed:

```powershell
cd src-tauri
tauri build
```

#### Output

The installers are generated at:

- **MSI**: `src-tauri/target/release/bundle/msi/SwebKit_0.1.0_x64_en-US.msi`
- **NSIS**: `src-tauri/target/release/bundle/nsis/SwebKit_0.1.0_x64-setup.exe`

Either can be distributed for installation. The MSI is recommended for enterprise deployment (supports silent install via `msiexec`).

#### Dev mode (hot reload)

```powershell
pwsh -File scripts/tauri/run-dev.ps1     # or double-click scripts/tauri/run-dev.cmd
```

Starts the sidecar, the Vite dev server and the Tauri window, each in its own console,
skipping any tier already running. The manual equivalent — start the sidecar and Vite dev
server first, then:

```powershell
cd src-tauri
node ../web/node_modules/@tauri-apps/cli/tauri.js dev
```

This opens the native desktop window pointing at `http://localhost:1420` with hot module replacement.

#### Test the production bundle without building an installer

```powershell
pwsh -File scripts/tauri/test-frontend.ps1
```

Rebuilds the production frontend bundle and the published sidecar from scratch, runs them
together at `http://127.0.0.1:1421`, and opens a browser. Same artifacts the MSI ships,
seconds instead of minutes, and the sidecar's config is redirected to a throwaway folder
so your real profiles and templates are untouched.

### Run E2E tests

Playwright starts the sidecar and Vite automatically on isolated ports, so you don't need to run them manually.

```powershell
cd web
npx playwright test
```

Useful variations:

```powershell
npx playwright test --ui            # interactive UI mode
npx playwright test --headed        # visible browser
npm run test:e2e                    # same as `playwright test`
npm run test:e2e:ui
npm run test:e2e:headed
```

### Run .NET unit tests

```powershell
dotnet test
```
