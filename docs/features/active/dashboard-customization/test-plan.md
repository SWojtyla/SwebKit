# Dashboard Customization Test Plan

## Scope

Validate that dashboard tile selection, ordering, default behavior, and tile refresh states work without regressing the existing dashboard metrics.

## Component Tests

- Renders the default dashboard layout when no dashboard preferences exist.
- Hides a tile when the persisted preference marks it invisible.
- Preserves tile ordering from persisted preferences.
- Falls back safely when preferences contain an unknown tile ID.
- Shows configured, unconfigured, loading, empty, error, and stale states for registry-driven tiles.
- Keeps setup attention visible when configuration health requires action, even if other optional tiles are hidden.
- Opens the correct destination when a tile drill-through action is invoked.

## Persistence Tests

- Loads dashboard preferences from `ui-state.json` with sensible defaults for missing fields.
- Saves tile order, visibility, size, and per-tile settings without disturbing existing UI state fields.
- Recovers safely from malformed or older dashboard preference payloads.

## Manual Checks

- The tile picker is usable with keyboard and pointer input.
- Tile labels and metric values fit at supported desktop window sizes.
- Refreshing the dashboard does not trigger duplicate concurrent network calls.
- Navigation away from the dashboard cancels or ignores in-flight tile refreshes.
- Demo mode renders meaningful sample tile data.

## Initial Validation Command

Use the existing Windows MAUI build task after implementation changes:

```powershell
dotnet build src/SwebKit.App/SwebKit.App.csproj -f net10.0-windows10.0.19041.0 -p:Configuration=Debug -p:RuntimeIdentifier=win-x64 -p:WindowsPackageType=None -p:WindowsAppSDKSelfContained=true
```
