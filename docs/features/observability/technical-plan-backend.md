# Technical Plan — Observability: Backend

## Status

- Current: Pending

## Architecture

```
IObservabilityProvider
  QueryLogsAsync(LogQuery) → IReadOnlyList<LogEntry>
  GetTraceAsync(traceId)   → TraceResult
  GetMetricsAsync(query)   → MetricsResult

AppInsightsObservabilityProvider  — Azure Monitor Query SDK
OtlpObservabilityProvider         — SwebKit.OpenTelemetry
```

## Implementation Sequence

1. Implement trace result mapping in `AppInsightsObservabilityProvider`.
2. Implement metrics query mapping.
3. Implement saved query domain model and `ProfileRepository` CRUD.
4. Implement `OtlpObservabilityProvider` adapter and connection test.
5. Define cross-link parameter contract for incoming navigation.

## Detailed Tasks

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

## Acceptance Checks

- [ ] Log queries return correctly mapped `LogEntry` records.
- [ ] Trace query returns span hierarchy in the correct order.
- [ ] Saved queries persist and reload without data loss.
- [ ] OTLP provider can be configured and connection-tested.
- [ ] Cross-link parameters are accepted and applied to the active query.

## Traceability Backlinks

- `docs/features/observability/index.md`
- `docs/features/observability/technical-plan-ui.md`
- `docs/features/observability/test-plan.md`
