# SwebKit

A Windows desktop "Swiss army knife" debugging tool for .NET developers working
with Azure. Tauri shell + React frontend + .NET sidecar — combining Azure Service
Bus tooling, Application Insights / OpenTelemetry observability, AKS debugging
helpers, and more in a single developer-centric app.

## Key Features

- **Service Bus** — inspect queue/topic/subscription messages, fix and resubmit DLQ messages, batch resend with fresh MessageIds, compose/schedule messages, message templates
- **Observability** — query Application Insights logs, explore distributed traces, view metrics dashboards
- **AKS** — workload overview, live pod log tailing, port-forwarding, pod shell
- **API Client** — HTTP request workspace with environments, variables, collections, and JSONPath helpers
- **Storage** — browse blobs, metadata, versions, upload/download, soft-delete recovery
- **Redis** — browse keys, inspect values, manage caches
- **SQL** — read-only database explorer: schema tree, query editor, saved queries, history, schema compare
- **AI Agent** — intelligent assistant with tool-based architecture for DevOps operations
- **Project + Environment** — everything scoped to a project (e.g. "OrderPlatform") and environment (Dev / Test / Acc / Prod)
- **Demo mode** — explore every feature with seeded demo data, no Azure credentials needed

## Tech Stack

- **Shell**: Tauri (Rust) — native Windows desktop, MSI/NSIS installer
- **Frontend**: React 19 + TypeScript + Vite + Tailwind, TanStack Query, CodeMirror, xterm.js
- **Backend**: .NET 10 minimal-API sidecar (`src-sidecar/`), spawned by the shell
- **SDKs**: Azure SDK (Service Bus, Monitor Query, Identity), KubernetesClient
- **Tests**: Playwright e2e (demo mode) · Vitest unit · xUnit sidecar/domain tests

## Documentation

- [Documentation Entry Point](docs/README.md)
- [Global Context — stack, layout, conventions, commands](docs/context.md)
- [Feature Catalog](docs/features/README.md)
- [Architecture Router](docs/architecture/index.md)
- [Pitfalls](docs/pitfalls/index.md)
- [Scripts & Packaging](scripts/README.md)
- [Documentation Migration Notes](docs/MIGRATION-NOTES.md)

## Install SwebKit (Windows)

SwebKit isn't published anywhere yet — you build your own installer. One command
produces a signed MSI:

```powershell
git clone <this-repo-url>
cd SwebKit
pwsh -File scripts/tauri/build-msi.ps1 -Install
```

This builds the frontend, publishes the self-contained .NET sidecar, bundles the
Tauri app, and launches the produced MSI. Output lands at
`src-tauri/target/release/bundle/msi/SwebKit_<version>_x64_en-US.msi` — distribute
that file to install elsewhere.

See [scripts/README.md](scripts/README.md) for flags (`-Bundles all`, `-FullClean`,
`-NoClean`, …) and troubleshooting.

### Prerequisites for building

- **.NET 10 SDK**
- **Node.js 20+**
- **Rust** (stable) — https://rustup.rs/
- **Visual Studio Build Tools 2022** with the *Desktop development with C++* workload
  (MSVC + Windows SDK + `link.exe`; VS Code alone is not sufficient)

## Development

Every step is scripted under [scripts/tauri/](scripts/tauri/) — one command each
for the dev loop, a browser test run against real production artifacts, and a fresh
installer. The manual recipes below document what those scripts do.

### Dev loop (hot reload)

```powershell
pwsh -File scripts/tauri/run-dev.ps1     # or double-click scripts/tauri/run-dev.cmd
```

Starts the sidecar (`dotnet run`, port 5199), the Vite dev server (port 1420) and
the Tauri window, each in its own console, skipping any tier already running.

The manual equivalent:

```powershell
cd src-sidecar && dotnet run --urls http://127.0.0.1:5199   # backend
cd web && npm install && npm run dev                       # http://localhost:1420
cd src-tauri && node ../web/node_modules/@tauri-apps/cli/tauri.js dev
```

The frontend expects the sidecar at `http://127.0.0.1:5199`; override with
`VITE_SIDECAR_URL` if needed.

### Test the production bundle without an installer

```powershell
pwsh -File scripts/tauri/test-frontend.ps1
```

Rebuilds the frontend bundle and published sidecar from scratch, runs them together
at `http://127.0.0.1:1421`, and opens a browser. Same artifacts the MSI ships, in
seconds — and the sidecar's config is redirected to a throwaway folder so your real
profiles and templates are untouched.

### Build pieces individually

```powershell
cd web && npm run build          # frontend -> web/dist
dotnet publish src-sidecar\SwebKit.Sidecar.csproj -c Release -r win-x64 --self-contained true -o src-tauri\binaries\sidecar
cd src-tauri && node ../web/node_modules/@tauri-apps/cli/tauri.js build
```

The bundle ships the sidecar as a resource (`src-tauri/binaries/sidecar/` is
gitignored and starts empty). If you bundle without publishing it first, the
installer builds fine but the installed app dies with "Sidecar binary not found".

## Tests

```powershell
cd web && npx playwright test    # e2e — starts sidecar + vite itself, isolated ports
cd web && npm run test:unit      # vitest
dotnet test                      # .NET test projects under tests/
```

## Project Structure

```
src-tauri/              # Tauri (Rust) desktop shell
web/                    # Vite + React + Tailwind frontend
web/e2e/                # Playwright e2e tests (demo mode)
src-sidecar/            # .NET minimal API sidecar (REST under /api/*)
src/
  SwebKit.Core/         # Domain models, abstractions, demo clients
  SwebKit.Agents/       # AI Agent
  SwebKit.Azure/        # Azure Service Bus + App Insights implementations
  SwebKit.Kubernetes/   # AKS / Kubernetes implementation
  SwebKit.Redis/        # Redis implementation
  SwebKit.Sql/          # SQL Server implementation
  SwebKit.DevOps/       # Azure DevOps integration
  SwebKit.Observability/ # Observability wiring
  SwebKit.OpenTelemetry/ # OTLP provider
tests/
  SwebKit.<Area>.Tests/ # xUnit per area
scripts/                # tauri/ (current) + maui/ (legacy) build scripts
docs/                   # context.md, features/, architecture/, pitfalls/
```

## Legacy (reference only)

`src/SwebKit.App/` (the original .NET MAUI Blazor Hybrid app), `src/SwebKit.WinUI/`,
`src/SwebKit.Agent.PocConsole/`, and their test projects are kept as reference.
They are not built or shipped — the MAUI install script still lives at
`scripts/maui/install.ps1` for anyone who needs the old MSIX, and
[docs/packaging-and-install.md](docs/packaging-and-install.md) documents that flow.
