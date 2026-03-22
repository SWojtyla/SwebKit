# Status — Application Insights Viewer

**Status:** Planned

## Current Focus

Planning phase — no implementation started.

## Remaining Work

- [ ] Create `SwebKit.Observability` project and wire into solution
- [ ] Define `IAppInsightsClient` interface in `SwebKit.Core`
- [ ] Implement resource discovery (`AppInsightsDiscoveryService`)
- [ ] Implement `AzureAppInsightsClient` in `SwebKit.Observability`
- [ ] Add `AppInsightsConfig` to `AppConfig` / `profiles.json`
- [ ] Build `AppInsightsPage.razor` with tab layout
- [ ] Build Overview tab (summary cards + trend chart)
- [ ] Build Failures tab (exception groups + detail pane)
- [ ] Build Performance tab (operation latency table + P-chart)
- [ ] Build Logs tab (Monaco KQL editor + preset library + results grid)
- [ ] Time range picker component
- [ ] Resource selector / switcher
- [ ] Register in nav, keyboard shortcuts, command palette
- [ ] Update `docs/architecture/architecture.md` and add functionality deep-dive

## Blockers

None.

## Validation State

Not started.
