# Backend Plan - incident-timeline-workbench

---

title: "Backend Plan - incident-timeline-workbench"
owner: "GitHub Copilot"
status: "Planned"

---

## Goal

Add a cancellation-aware, performance-bounded backend aggregation layer that transforms App Insights, AKS, Service Bus, and DevOps signals into one normalized incident timeline contract for the new workbench UI.

## Impacted areas

- Existing projects and likely touchpoints:
- src/SwebKit.Core/Models
- src/SwebKit.Core/Abstractions
- src/SwebKit.Core/Services
- src/SwebKit.Observability/AzureAppInsightsProvider.cs
- src/SwebKit.Kubernetes/AksClient/KubernetesAksClient.cs
- src/SwebKit.Azure/ServiceBus/AzureServiceBusClient.cs
- src/SwebKit.DevOps/DevOpsClient.cs
- Planned new files:
- src/SwebKit.Core/Models/IncidentTimelineModels.cs
- src/SwebKit.Core/Abstractions/IIncidentTimelineService.cs
- src/SwebKit.Core/Abstractions/IIncidentTimelineSignalSource.cs
- src/SwebKit.Core/Services/IncidentTimelineService.cs
- src/SwebKit.Observability/IncidentTimeline/AppInsightsTimelineSignalSource.cs
- src/SwebKit.Kubernetes/IncidentTimeline/AksTimelineSignalSource.cs
- src/SwebKit.Azure/ServiceBus/IncidentTimeline/ServiceBusDlqTimelineSignalSource.cs
- src/SwebKit.DevOps/IncidentTimeline/DevOpsReleaseTimelineSignalSource.cs

## Design

The backend uses a source-adapter pattern to avoid coupling the new workbench to specific SDK clients:

1. Each source adapter projects source-specific payloads into a normalized timeline row model at the project boundary.
2. IncidentTimelineService fans out source queries in parallel using a linked CancellationToken.
3. Aggregation merges rows by UTC timestamp, applies deterministic tie-break ordering, deduplicates stable keys, and returns capped results.
4. Service returns source-level health metadata (duration, failed, canceled, timeout) so UI can render partial-result status.

This design keeps existing feature clients reusable and prevents broad breaking changes in existing page components.

## API / Contracts

- Core query contracts (new):
- IncidentTimelineQuery with StartUtc, EndUtc, SelectedSources, MaxItems, Cursor.
- IncidentTimelineSource enum with Observability, Aks, ServiceBusDlq, Releases.
- Core result contracts (new):
- IncidentTimelineItem with ItemId, TimestampUtc, Source, Severity, Title, Summary, CorrelationKey, Metadata.
- IncidentTimelinePage with Items, NextCursor, SourceStatuses, IsPartial.
- IncidentTimelineSourceStatus with Source, Outcome, DurationMs, ErrorMessage.
- Service contracts (new):
- IIncidentTimelineSignalSource: FetchAsync(query, ct) for one source.
- IIncidentTimelineService: GetTimelineAsync(query, ct) for merged cross-source output.
- Backward compatibility notes:
- Existing interfaces (IObservabilityProvider, IAksClient, IServiceBusClient, IDevOpsClient) remain additive.
- No contract removals or behavior changes for existing pages.

## Tasks

### Wave 1 - Core contracts and orchestration [dotnet-expert] (sequential root)

- [ ] Create IncidentTimeline models in src/SwebKit.Core/Models.
- [ ] Create timeline abstractions in src/SwebKit.Core/Abstractions.
- [ ] Implement IncidentTimelineService in src/SwebKit.Core/Services.
- [ ] Add merge-order deterministic comparator and duplicate-key policy.
- [ ] Add source-level timeout budget support and per-source health telemetry.

### Wave 2 - Source adapters [dotnet-expert] (parallel after Wave 1)

- [ ] Implement App Insights adapter in src/SwebKit.Observability using existing query capabilities from AzureAppInsightsProvider.
- [ ] Implement AKS adapter in src/SwebKit.Kubernetes using event and restart data from IAksClient methods.
- [ ] Implement Service Bus adapter in src/SwebKit.Azure using DLQ stats and message peek metadata from IServiceBusClient.
- [ ] Implement DevOps adapter in src/SwebKit.DevOps using recent run/release trigger metadata from IDevOpsClient.
- [ ] Ensure all adapters normalize timestamps to UTC at source boundary.

### Wave 3 - Performance and cancellation hardening [dotnet-expert] (depends on Waves 1-2)

- [ ] Implement cancellation-first strategy: every new load cancels prior aggregate request.
- [ ] Guarantee OperationCanceledException passthrough and avoid generic catch swallowing.
- [ ] Add bounded result limits and source-specific top-N caps to prevent memory spikes.
- [ ] Add lightweight in-memory short-lived cache for identical query parameters (optional, feature flagged).

### Wave 4 - Test implementation [dotnet-expert] (depends on Waves 1-3)

- [ ] Add unit tests in tests/SwebKit.Core.Tests for merge, dedup, pagination/cursor, and cancellation.
- [ ] Add adapter mapping tests in tests/SwebKit.Azure.Tests, tests/SwebKit.Kubernetes.Tests, and tests/SwebKit.DevOps.Tests.
- [ ] Add failure-injection tests for timeout, auth failure, and partial-result behavior.
- [ ] Record notable tradeoffs and deviations in decisions.md.

## Migration and runtime changes

- No schema or persistent data migration required.
- No infrastructure change required.
- Runtime configuration additions, if any, should be optional with safe defaults (for example max items and per-source timeout budget).

## Validation

- Unit tests: Not started
- Integration tests: Not started
- Manual checks:
- Verify query cancellation under rapid refresh loops.
- Verify partial source failure still returns usable timeline.
- Verify 24-hour query stays within target latency envelope.

## Notes

- Cancellation and timeout handling are correctness requirements, not optional optimizations.
- Apply dotnet-csharp CS-2 guidance: never swallow OperationCanceledException.
- Apply azure-sdk guidance when enumerating SDK pageable sequences and handling scoped Service Bus connection strings.
