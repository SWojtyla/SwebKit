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
pwsh -File scripts/install.ps1
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
