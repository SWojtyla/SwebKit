<!-- Copied from technical-plan-backend.md -->

# Backend

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
- [ ] Add metrics query mapping.
- [ ] Add saved query domain model and persistence.
- [ ] Implement `OtlpObservabilityProvider` and config test path.
- [ ] Define cross-link parameter contract.

## Acceptance Checks

- [ ] Log queries return correctly mapped `LogEntry` records.
- [ ] Trace query returns span hierarchy in the correct order.
- [ ] Saved queries persist and reload without data loss.
- [ ] OTLP provider can be configured and connection-tested.
- [ ] Cross-link parameters are accepted and applied to the active query.
