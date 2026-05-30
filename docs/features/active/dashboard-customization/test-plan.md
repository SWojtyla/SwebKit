# Dashboard Customization Test Plan

## Scope

Validate that dashboard tile selection, ordering, default behavior, and tile refresh states work without regressing the existing dashboard metrics.

## Component Tests

- Renders the default dashboard layout when no dashboard preferences exist.
- Hides a tile when the persisted preference marks it invisible.
- Preserves tile ordering from persisted preferences.
- Falls back safely when preferences contain an unknown tile ID.
- Shows configured, unconfigured, loading, empty, error, and stale states for registry-driven tiles.
- Opens the correct destination when a tile drill-through action is invoked.
- Adds, removes, resizes, hides, and reorders custom tile instances from the dashboard builder.
- Renders Service Bus entity watch tiles with active, dead-letter, and scheduled message counts.
- Renders AKS namespace watch tiles with pod, unhealthy pod, and restart counts without requiring deployment-list permissions.

## Persistence Tests

- Loads dashboard preferences from `ui-state.json` with sensible defaults for missing fields.
- Saves tile order, visibility, size, and per-tile settings without disturbing existing UI state fields.
- Preserves known custom template instances such as `service-bus.entity-watch:<instance>` while still dropping unrelated unknown tile IDs.
- Recovers safely from malformed or older dashboard preference payloads.

## Manual Checks

- The dashboard builder is usable with keyboard and pointer input.
- Tile labels and metric values fit at supported desktop window sizes.
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
- Focused Core test execution is currently blocked before the new tests run by existing `DeploymentValidationServiceTests.FakeAksClient` compile errors for missing `IAksClient.DeleteIngressAsync` and `IAksClient.DeleteHttpRouteAsync` implementations.
- Manual dashboard builder smoke validation is still pending.
- Manual visual review of the redesigned dashboard is still pending.
