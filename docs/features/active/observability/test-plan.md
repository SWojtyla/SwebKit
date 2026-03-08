# Test Plan - Observability

---

title: "Test Plan - Observability"
owner: ""
status: "Planned"
created: ""
updated: "2026-03-08"

---

## Status

- Current: Planned

## Scope

- Validate trace, log, and metric workflows for fast triage and deep diagnostics.
- Validate saved query behavior scoped by project and environment context.
- Validate cross-navigation contracts from Service Bus and AKS into observability views.
- Keep feature-first traceability explicit for future regression tracking.

## Test Levels

- Unit tests (`tests/SwebKit.Azure.Tests/`, `tests/SwebKit.Core.Tests/`): query mapping, normalization, and saved-query state.
- Component tests (`tests/SwebKit.App.Tests/`): timeline, dashboard tiles, and query UX behavior.
- Integration tests (provider-mocked): logs and metrics query flows with correlation metadata.
- Smoke tests (manual): cross-link hops from Service Bus and AKS to prefiltered observability views.

## Key Scenarios

- [ ] OBS-001: Trace timeline renders ordered spans and preserves correlation identifiers.
- [ ] OBS-002: Saved queries persist by project and environment and restore expected filters.
- [ ] OBS-003: Metrics dashboard tiles load and refresh with expected label and range behavior.
- [ ] OBS-004: Log query execution handles empty results and errors with actionable feedback.
- [ ] OBS-005: Service Bus deep-link opens observability with compatible preselected context.
- [ ] OBS-006: AKS deep-link opens observability with compatible preselected context.

## Command Placeholders

```
dotnet test tests/SwebKit.Azure.Tests/SwebKit.Azure.Tests.csproj -p:Configuration=Debug
dotnet test tests/SwebKit.Core.Tests/SwebKit.Core.Tests.csproj -p:Configuration=Debug
dotnet test tests/SwebKit.App.Tests/SwebKit.App.Tests.csproj -p:Configuration=Debug
dotnet test SwebKit.slnx
```

## Traceability Backlinks

- `docs/features/active/observability/index.md`
- `docs/features/active/observability/technical-plan-backend.md`
- `docs/features/active/observability/technical-plan-ui.md`
