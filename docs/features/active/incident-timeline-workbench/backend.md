# Backend Plan - incident-timeline-workbench

---

title: "Backend Plan - incident-timeline-workbench"
owner: "GitHub Copilot"
status: "Planned"

---

## Goal

Add a cancellation-aware, performance-bounded backend aggregation layer that produces one workload-scoped incident evidence timeline for the new cockpit UI. The backend should gather evidence from AKS, App Insights, Service Bus, and deployment or release activity for one workload and one incident window, without introducing causal claims.

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
- src/SwebKit.Azure/ServiceBus/IncidentTimeline/ServiceBusEvidenceSignalSource.cs
- src/SwebKit.DevOps/IncidentTimeline/DevOpsReleaseTimelineSignalSource.cs

## Design

The backend uses a source-adapter pattern so the new cockpit can stay additive and avoid coupling directly to individual SDK clients:

1. The UI submits one workload-scoped query containing profile, cluster, namespace, workload selector, incident window, selected sources, and a hard max item cap.
2. The AKS adapter resolves the anchor workload and produces direct evidence items from pod lifecycle and event data.
3. Other adapters query only the assets that are already mapped to that workload, its known topology, or an existing correlation ID inside the same time window.
4. IncidentTimelineService fans out eligible source queries in parallel using a linked CancellationToken, merges all evidence by UTC timestamp, and returns per-source coverage metadata.
5. Every returned item includes one or more link reasons explaining why it is present. The service does not compute or expose root-cause scores.

This design keeps existing feature clients reusable, respects the SwebKit.Core contract boundary, and avoids broad breaking changes in existing pages.

## API / Contracts

- Core query contracts (new):
- IncidentTimelineQuery with Scope, StartUtc, EndUtc, SelectedSources, and MaxItems.
- IncidentWorkloadScope with profile context, cluster context, namespace, workload reference, and optional pod hint.
- IncidentTimelineSource enum with Observability, Aks, ServiceBus, and Releases.
- Core result contracts (new):
- IncidentTimelineItem with ItemId, TimestampUtc, Source, Severity, Title, Summary, ResourceRef, LinkReasons, and Metadata.
- IncidentLinkReason with Type, Relevance, and Explanation.
- IncidentTimelinePage with Items, SourceStatuses, IsPartial, and WasTruncated.
- IncidentTimelineSourceStatus with Source, Outcome, DurationMs, CoverageState, and ErrorMessage.
- Service contracts (new):
- IIncidentTimelineSignalSource: FetchAsync(query, ct) for one source.
- IIncidentTimelineService: GetTimelineAsync(query, ct) for merged cross-source evidence.
- Backward compatibility notes:
- Existing interfaces remain additive.
- No contract removals or behavior changes for existing pages.

## Inclusion rules by source

- AKS:
- Required: namespace and workload resolution.
- Include pod events, restart counts, warnings, scheduling failures, and owner-chain activity for the selected workload.
- Mark these items as Direct when the owner chain resolves cleanly.
- App Insights:
- Required: explicit app or component mapping to the workload, or an existing correlation ID discovered from already-scoped evidence.
- Include failures, exceptions, and targeted request or dependency evidence inside the incident window.
- Do not sweep all telemetry from the namespace or subscription.
- Service Bus:
- Required: explicit queue, topic, or subscription mapping to the scoped workload or existing correlation ID.
- Include symptoms such as DLQ growth, repeated receive failures, or send failures within the window.
- Do not infer ownership from name similarity alone.
- Deployments or releases:
- Required: explicit environment, app, pipeline, or namespace mapping.
- Include deployment, rollout, or release activity that overlaps the selected incident window.
- Treat these as Contextual unless a stronger mapping exists.

## Confidence and explanation model

Backend contracts should carry a safe relevance model that the UI can render directly:

- Direct: explicit workload ownership or topology match.
- Corroborating: existing correlation ID, or explicit mapping plus time overlap.
- Contextual: already-scoped platform or release activity that happened in the same window.

Each IncidentLinkReason explanation should be human-readable, for example:

- "Linked because ReplicaSet owner resolves to deployment phonotif-api in namespace prd-phonotif."
- "Linked because queue phonotif-outbound is mapped to the selected workload and showed DLQ growth in the incident window."
- "Linked because release 2026.04.11 targeted namespace prd-phonotif during the selected window."

The backend must not emit fields or enums that suggest cause, blame, or root-cause probability.

## Tasks

### Wave 1 - Core scope and evidence contracts [dotnet-expert] (sequential root)

- [ ] Define IncidentWorkloadScope and workload-scoped query contracts in src/SwebKit.Core/Models.
- [ ] Define normalized evidence item, link reason, and source coverage contracts in src/SwebKit.Core/Models.
- [ ] Create IIncidentTimelineService and IIncidentTimelineSignalSource abstractions in src/SwebKit.Core/Abstractions.
- [ ] Implement IncidentTimelineService in src/SwebKit.Core/Services.
- [ ] Add deterministic merge ordering, duplicate-key policy, and transparent truncation support.

### Wave 2 - Source adapters [dotnet-expert] (parallel after Wave 1)

- [ ] Implement AKS adapter as the anchor evidence source for workload and namespace activity.
- [ ] Implement App Insights adapter using existing query capabilities and explicit workload mapping rules.
- [ ] Implement Service Bus adapter using topology mapping or existing correlation IDs.
- [ ] Implement DevOps adapter for recent deployment or release activity tied to the same workload or namespace.
- [ ] Normalize all timestamps to UTC at the source boundary.

### Wave 3 - Orchestration hardening [dotnet-expert] (depends on Waves 1-2)

- [ ] Implement cancellation-first request handling so every new load cancels the prior aggregate request.
- [ ] Guarantee OperationCanceledException passthrough and avoid generic catch swallowing.
- [ ] Add per-source timeout budgets and coverage-state reporting.
- [ ] Apply bounded result limits and source-specific top-N caps for the 15 minute, 1 hour, and 6 hour windows.

### Wave 4 - Test implementation [dotnet-expert] (depends on Waves 1-3)

- [ ] Add unit tests in tests/SwebKit.Core.Tests for inclusion rules, merge ordering, truncation, and cancellation.
- [ ] Add adapter mapping tests in tests/SwebKit.Azure.Tests, tests/SwebKit.Kubernetes.Tests, and tests/SwebKit.DevOps.Tests.
- [ ] Add failure-injection tests for timeout, auth failure, unmapped source coverage, and partial-result behavior.
- [ ] Record notable tradeoffs and deviations in decisions.md.

## Migration and runtime changes

- No schema or persistent data migration required.
- No infrastructure change required.
- Runtime configuration additions, if any, should be optional with safe defaults.
- Any non-AKS workload mappings used by adapters should be additive configuration or existing metadata, not new mandatory infrastructure.

## Validation

- Unit tests: Not started
- Integration tests: Not started
- Manual checks:
- Verify the `prd-phonotif` pod-down scenario returns only workload-scoped evidence.
- Verify unmapped sources are surfaced as unavailable or unmapped instead of guessed.
- Verify partial source failure still returns usable evidence.
- Verify the 6 hour query stays within the target latency envelope.

## Notes

- Cancellation and timeout handling are correctness requirements, not optional optimizations.
- Apply dotnet-csharp CS-2 guidance: never swallow OperationCanceledException.
- Apply azure-sdk guidance when enumerating SDK pageable sequences and handling scoped Service Bus connection strings.
