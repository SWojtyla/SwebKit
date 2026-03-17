# Status — App Insights Log Viewer

---

title: "Status - App Insights Log Viewer"
owner: ""
state: "Planned"
branch: ""
started: ""
last_updated: "2026-03-17"

---

## Quick summary

Current state: **Planned** — feature plan created, no implementation started.

**Current focus:** Review and approve the plan then begin backend changes.

## Progress checklist

- [x] Planning complete
- [ ] Design reviewed
- [ ] `Azure.ResourceManager.ResourceGraph` package added to `SwebKit.Azure.csproj`
- [ ] `AppInsightsResourceInfo` model created
- [ ] `IAppInsightsDiscoveryService` interface created
- [ ] `AzureAppInsightsDiscoveryService` implemented (Resource Graph)
- [ ] Discovery service registered in DI (`MauiProgram.cs`)
- [ ] `ObservabilityConfig` model extended (`AppInsightsResourceId`)
- [ ] `AppInsightsObservabilityProvider` resource-query path implemented
- [ ] Auth error surfaced (`LastAuthError`, `CredentialIdentity`)
- [ ] `ObservabilityConfigForm` resource picker (browse combobox) implemented
- [ ] Log viewer UI: table + time range + severity filter
- [ ] KQL editor mode integrated
- [ ] Auth status indicator wired up
- [ ] Unit tests passing (UT-1 to UT-12)
- [ ] Manual smoke test against real App Insights resource
- [ ] Docs aligned

## Completed

- Feature scope, backend, frontend, and test-plan documents drafted.

## Remaining

- All implementation tasks. See `backend.md` and `frontend.md`.

## Blockers

- None recorded.

## Validation

See [test-plan.md](test-plan.md).
