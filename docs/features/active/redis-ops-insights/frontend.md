# Frontend Plan - redis-ops-insights

---

title: "Frontend Plan - redis-ops-insights"
owner: "GitHub Copilot"
status: "Not started"

---

## Goal

Extend the current Redis page so operators can inspect slowlog or hot-key evidence and Pub/Sub activity in the same workspace they already use for key browsing and health analysis.

## Impacted areas

- Existing page and components:
- `src/SwebKit.App/Components/Pages/RedisPage.razor`
- `src/SwebKit.App/Components/Pages/RedisPage.razor.css`
- `src/SwebKit.App/Components/Redis/RedisKeyspaceHealthExplorer.razor`
- `src/SwebKit.App/Components/Redis/RedisPrefixMemory.razor`
- `src/SwebKit.App/Components/Redis/RedisServerInfo.razor`
- Likely new page-local components:
- `src/SwebKit.App/Components/Redis/RedisOpsInsightsPanel.razor`
- `src/SwebKit.App/Components/Redis/RedisSlowLogPanel.razor`
- `src/SwebKit.App/Components/Redis/RedisPubSubPanel.razor`
- Likely impacted tests:
- `tests/SwebKit.App.Tests/RedisKeyspaceHealthExplorerTests.cs`
- `tests/SwebKit.App.Tests/RedisToolbarTests.cs`
- Likely new tests such as `RedisOpsInsightsPanelTests.cs` and `RedisPubSubPanelTests.cs`.

## UX notes

- The Redis page should remain one route and one cache-scoped workspace.
- The new diagnostics should not push key detail off-screen by default. Prefer a consolidated lower-right diagnostics surface with tabs such as `Health`, `Slowlog`, `Pub/Sub`, and `Prefix Memory`.
- Each diagnostics tab should inherit the active cache entry, database, and current scan context automatically.
- Each diagnostics tab should remain manual-refresh or manual-analyze only.

### User flows

- Slowlog or hot-key:
- Operator opens the `Slowlog` tab, refreshes the snapshot, and sees the most recent slow commands with grouped signals and likely related keys or prefixes.
- Clicking a related key should reuse the existing key detail pane rather than opening a second inspector.
- Pub/Sub:
- Operator opens the `Pub/Sub` tab, refreshes the snapshot, and sees channel counts and subscriber summaries.
- The UI remains read-only. No `Subscribe`, `Publish`, or `Listen` action is exposed.

### Component states

- Loading: per-tab loading state so the rest of the Redis page remains interactive.
- Loaded: coverage-aware summary plus tab-specific table or chart.
- Partial: one signal source is unavailable but others still render.
- Unsupported: explicit informational state for slowlog or Pub/Sub commands not exposed by the environment.
- Empty: no slowlog entries or no active channels render as neutral empty states, not errors.

### Accessibility

- The tab strip must be keyboard reachable and expose readable labels.
- Severity cards must include text, not color only.
- Hot-key findings need plain-language signal explanations that are understandable without hovering a tooltip.

## API / contract changes

- `RedisPage.razor` should remain an orchestration shell; new projection components should accept already-shaped DTOs from Core models.
- New UI components should not re-implement hot-key scoring logic.
- Existing selection and drill-through behavior (`SelectKeyAsync`, `OpenFindingKeyAsync`) should stay the canonical navigation path into key detail.

## Tasks

### Wave 1 - Slowlog and hot-key surface [blazor-expert]

- [ ] Add a slowlog summary view that stays readable at bounded entry counts.
- [ ] Render hot-key findings with explicit signal provenance and drill-through to key detail or prefix context.
- [ ] Handle unsupported-command and permission-limited states without page-level error callouts.
- [ ] Add bUnit coverage for loading, unsupported, empty, and drill-through behavior.

### Wave 2 - Pub/Sub visibility and polish [blazor-expert]

- [ ] Add Pub/Sub channel and subscriber summaries with manual refresh.
- [ ] Allow lightweight channel filtering based on the current key prefix or a user-entered pattern.
- [ ] Ensure the page layout still works on the current desktop resolution without crowding the key-detail pane.
- [ ] Update component tests and final page copy once the combined diagnostics surface is stable.

## Validation

- Component tests: Not started
- Manual UX checks:
- Verify no blank-render issues when adding new Redis component namespaces to `_Imports.razor`.
- Verify all post-await renders use `InvokeAsync(StateHasChanged)` where needed.
- Verify switching cache entry or database while a diagnostics tab is loading does not leave stale results visible.
- Verify the new diagnostics surface still keeps key browsing and mutation affordances understandable.

## Notes

- Apply `docs/pitfalls/blazor-maui.md` guidance directly: set parameter guards before awaits, dispatch UI updates through `InvokeAsync`, and keep any new folder namespace imports explicit.
- Avoid adding another always-open vertical panel; the Redis page already has a dense right column.
- The UI should keep reminding the operator that hot-key insights describe the loaded scan, not the entire cache, unless later scope adds a deliberate full-analysis mode.
