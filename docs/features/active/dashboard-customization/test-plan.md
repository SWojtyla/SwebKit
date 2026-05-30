# Dashboard Customization Test Plan

## Scope

Validate that dashboard widget selection, ordering, sizing, configuration, default behavior, responsive layout, and refresh states work without regressing the existing dashboard metrics.

## Component Tests

- Renders the default dashboard layout when no dashboard preferences exist.
- Hides a tile when the persisted preference marks it invisible.
- Preserves tile ordering from persisted preferences.
- Falls back safely when preferences contain an unknown tile ID.
- Shows configured, unconfigured, loading, empty, error, and stale states for registry-driven tiles.
- Opens the correct destination when a tile drill-through action is invoked.
- Adds, removes, resizes, hides, and reorders custom tile instances from the dashboard builder.
- Maps existing persisted `small`, `medium`, and `wide` sizes to the new widget footprint model.
- Renders each MVP tile in its supported footprints without overflowing labels, metrics, or action buttons.
- Renders Service Bus entity watch tiles with active, dead-letter, and scheduled message counts.
- Renders AKS namespace watch tiles with pod, unhealthy pod, and restart counts without requiring deployment-list permissions.

## Persistence Tests

- Loads dashboard preferences from `ui-state.json` with sensible defaults for missing fields.
- Saves tile order, visibility, size, and per-tile settings without disturbing existing UI state fields.
- Preserves known custom template instances such as `service-bus.entity-watch:<instance>` while still dropping unrelated unknown tile IDs.
- Recovers safely from malformed or older dashboard preference payloads.

## Manual Checks

- The widget board is usable with keyboard and pointer input.
- Tile labels, metric values, targets, timestamps, and action buttons fit in every supported footprint.
- The board behaves like a home screen: widgets align predictably, sparse dashboards stay compact, and list widgets use larger footprints without stretching unrelated tiles.
- The board remains usable at desktop widths, half-width snapped windows, and narrow mobile-like widths.
- The board feels pleasant during normal use: spacing is calm, text hierarchy is clear, action buttons are discoverable without shouting, and tile colors support area identity without turning the page into a patchwork.
- Hover, focus, loading, error, stale, and editing states feel polished and intentional rather than abrupt or noisy.
- Configuration affordances are consistent across built-in health widgets, Service Bus entity widgets, and AKS namespace widgets.
- Environment readiness prompts do not render on the dashboard; users review setup state from Settings.
- Health signal tiles stay compact when adjacent workspace panels contain long lists.
- Adding a Service Bus entity tile refreshes that entity without changing the global Service Bus summary tile.
- Adding an AKS namespace tile refreshes that namespace without changing the configured default namespace.
- Recent Resources renders as a compact scannable list with resource name, area/kind metadata, and access time.
- Sparse dashboards that combine an AKS namespace tile with Recent Resources give the Recent Resources panel enough width and row height to remain readable.
- Stale `deployments.apps is forbidden` messages do not hide AKS namespace tiles because deployment permissions are not required for the pod-only summary.
- Refreshing the dashboard does not trigger duplicate concurrent network calls.
- Navigation away from the dashboard cancels or ignores in-flight tile refreshes.
- Demo mode renders meaningful sample tile data.
- A dashboard with only one visible custom tile stays compact at the top of the page, with no stretched overview cards or large blank rows.

## Responsive Widget Checks

- `1x1` widgets show one primary value, short label, status, and a compact action affordance without wrapping awkwardly.
- `2x1` widgets add target/context and secondary values while keeping a stable height.
- `2x2` widgets can show short lists or recent events without internal content pushing the board row taller.
- `3x2` widgets can host richer lists such as Recent Resources or Favorites while remaining scannable without dominating the board.
- Narrow widths collapse widgets to one column with stable vertical rhythm and no horizontal overflow.

## Visual Quality Checks

- The default dashboard has a clear first glance: the most important health signals are obvious without oversized hero treatment.
- Repeated widgets share a consistent frame, but tile contents do not feel monotonous.
- Empty dashboards, sparse dashboards, and busy dashboards all feel intentionally composed.
- The configuration surface is compact enough for repeated use but still gives enough room for target selection and preview.
- No tile relies on visible instructional copy to explain basic actions that should be represented by familiar controls and tooltips.

## Initial Validation Command

Use the existing Windows MAUI build task after implementation changes:

```powershell
dotnet build src/SwebKit.App/SwebKit.App.csproj -f net10.0-windows10.0.19041.0 -p:Configuration=Debug -p:RuntimeIdentifier=win-x64 -p:WindowsPackageType=None -p:WindowsAppSDKSelfContained=true
```

## Current Validation Status

- App build passed with existing warnings after the first implementation slice.
- UX overhaul build passed with existing warnings using alternate output path `artifacts/copilot-build/ux-overhaul/`; the standard build output was locked by a running `SwebKit.App.exe` process.
- Full custom-dashboard overhaul build passed with existing warnings using alternate output path `artifacts/copilot-build/dashboard-custom/`; generated output was removed after validation.
- Sparse-dashboard layout fix build passed with existing warnings using alternate output path `artifacts/copilot-build/dashboard-custom/`; generated output was removed after validation.
- AKS namespace tile RBAC hardening and Recent Resources spacing build passed with existing warnings using alternate output path `artifacts/copilot-build/dashboard-custom/`; generated output was removed after validation.
- Validation-gate recheck passed for UI-state hydration safety and persisted tile-size rendering.
- Added persistence tests for defaults, unknown tile IDs, preference round-tripping, and custom template-instance preservation.
- Updated persistence tests to assert legacy dashboard sizes migrate from `small`, `medium`, and `wide` to `1x1`, `2x1`, and `3x2` footprints.
- Focused Core test execution is currently blocked before the new tests run by existing `DeploymentValidationServiceTests.FakeAksClient` compile errors for missing `IAksClient.DeleteIngressAsync` and `IAksClient.DeleteHttpRouteAsync` implementations.
- Widget-board implementation build passed through `build-maui-windows` with existing warnings.
- Widget row stretching and top-bar favorites popover fixes passed through `build-maui-windows` with existing warnings.
- `3x2` footprint and list-row spacing changes passed through `build-maui-windows` with existing warnings.
- Focused Core test execution remains blocked before the dashboard preference tests run by the existing `DeploymentValidationServiceTests.FakeAksClient` compile errors.
- Manual dashboard builder smoke validation is still pending.
- Manual visual review of the redesigned dashboard is still pending.
