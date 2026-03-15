# Backend Plan — Observability

---

title: "Backend Plan - Observability"
owner: ""
status: "Planned"
created: "2026-03-08"
updated: ""

---

## Goal

Implement provider-agnostic observability: trace mapping, metrics querying, saved query persistence, OTLP provider support, and cross-link parameter contracts.

## Impacted areas

- `src/SwebKit.Azure/Observability/AppInsightsObservabilityProvider.cs`
- `src/SwebKit.OpenTelemetry/OtlpObservabilityProvider.cs`
- `src/SwebKit.Core/Abstractions/IObservabilityProvider.cs`
- `src/SwebKit.Core/Domain/SavedQuery.cs`
- `src/SwebKit.Core/Configuration/ProfileRepository.cs`

## Design

```
IObservabilityProvider
  QueryLogsAsync(LogQuery) → IReadOnlyList<LogEntry>
  GetTraceAsync(traceId)   → TraceResult
  GetMetricsAsync(query)   → MetricsResult

AppInsightsObservabilityProvider  — Azure Monitor Query SDK
OtlpObservabilityProvider         — SwebKit.OpenTelemetry
```

## Implementation sequence

1. Implement trace result mapping in `AppInsightsObservabilityProvider`.
2. Implement metrics query mapping.
3. Implement saved query domain model and `ProfileRepository` CRUD.
4. Implement `OtlpObservabilityProvider` adapter and connection test.
5. Define cross-link parameter contract for incoming navigation.

## Tasks

- [ ] Add App Insights trace mapping to `TraceResult` model.
  - Files: `src/SwebKit.Azure/Observability/AppInsightsObservabilityProvider.cs`
- [ ] Add metrics query mapping.
  - Files: `src/SwebKit.Azure/Observability/AppInsightsObservabilityProvider.cs`
- [ ] Add saved query domain model and persistence.
  - Files: `src/SwebKit.Core/Domain/SavedQuery.cs`, `src/SwebKit.Core/Configuration/ProfileRepository.cs`
- [ ] Implement `OtlpObservabilityProvider` and config test path.
  - Files: `src/SwebKit.OpenTelemetry/OtlpObservabilityProvider.cs`
- [ ] Define cross-link parameter contract.
  - Files: `src/SwebKit.Core/Abstractions/IObservabilityProvider.cs`

## Validation

- Unit tests: Not started
- Integration tests: Not started
- Manual checks: See `test-plan.md`

## Acceptance checks

- [ ] Log queries return correctly mapped `LogEntry` records.
- [ ] Trace query returns span hierarchy in the correct order.
- [ ] Saved queries persist and reload without data loss.
- [ ] OTLP provider can be configured and connection-tested.
- [ ] Cross-link parameters are accepted and applied to the active query.
