# SwebKit

A .NET MAUI Blazor Hybrid desktop "Swiss army knife" debugging tool for .NET developers working
with Azure. Combines Azure Service Bus tooling, Application Insights / OpenTelemetry observability,
and AKS (Kubernetes) debugging helpers in a single developer-centric desktop app.

## Key Features

- **Service Bus** — inspect queue/topic/subscription messages, fix DLQ messages, send/replay test messages
- **Observability** — query Application Insights logs, explore distributed traces, view metrics dashboards
- **AKS** — workload overview, live pod log tailing, port-forwarding, pod shell
- **Project + Environment** — everything scoped to a project (e.g. "OrderPlatform") and environment (Dev / Test / Acc / Prod)

## Tech Stack

- .NET MAUI Blazor Hybrid (Windows desktop, cross-platform ready)
- Microsoft Fluent UI Blazor for components
- Azure SDK (Service Bus, Monitor Query, Identity)
- KubernetesClient for AKS integration
- Monaco Editor + xterm.js via JSInterop

## Documentation

- [Architecture Overview](docs/ARCHITECTURE.md)
- [Full Design Document](docs/DESIGN.md)
- [Roadmap](docs/ROADMAP.md)

## Getting Started

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
  SwebKit.Azure/        # Azure Service Bus + App Insights implementations
  SwebKit.Kubernetes/   # AKS / Kubernetes implementation
  SwebKit.OpenTelemetry/ # OTLP observability provider
tests/
  SwebKit.Core.Tests/
  SwebKit.Azure.Tests/
  SwebKit.Kubernetes.Tests/
```
