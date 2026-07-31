---
name: Testing SwebKit AKS UI
description: How to end-to-end test the SwebKit AKS page, log viewer, and network resources in the Vite + .NET sidecar dev environment.
---

# Testing SwebKit AKS UI

## Devin secrets needed

None.

## One-time environment

- .NET SDK 10 is installed at `~/.dotnet`. Add it to PATH each session:
  ```bash
  export PATH="$HOME/.dotnet:$PATH"
  export DOTNET_CLI_TELEMETRY_OPTOUT=1
  ```
- Node version must satisfy Vite / `@vitejs/plugin-react` engines (`^20.19.0 || >=22.12.0`). The machine has `v22.12.0` under nvm:
  ```bash
  source /home/ubuntu/.nvm/nvm.sh && nvm use 22
  ```

## Starting the app

1. Sidecar:
   ```bash
   cd /home/ubuntu/repos/SwebKit/src-sidecar
   dotnet run --project SwebKit.Sidecar.csproj
   ```
   Dev server listens on `http://127.0.0.1:5199`.

2. Web:
   ```bash
   cd /home/ubuntu/repos/SwebKit/web
   npm run dev
   ```
   Vite serves on `http://localhost:1420`.

## Demo mode

The app needs demo mode for AKS to work without a kubeconfig. The toggle is the top-right header button, `title="Toggle demo mode"`, text `Live`. If native mouse clicks do not register (coordinate scaling issues), the button can be triggered from the browser console:

```js
document.querySelector('[title="Toggle demo mode"]').click();
```

After enabling, the AKS page shows `Connected` and loads deployments, pods, services, etc.

## Coordinate scaling for computer-use clicks

The tool coordinate space is `1024x768`, but the actual screen is `1600x1200` with `window.devicePixelRatio === 1` in Chrome. To convert a DOM `getBoundingClientRect()` value to tool screen coordinates:

```js
const screenY = rect.y + (window.outerHeight - window.innerHeight); // ~87px chrome
const toolX = rect.x * 1024 / 1600;
const toolY = screenY * 768 / 1200;
```

If precise clicking is unreliable (native `<select>` dropdowns, small toolbar buttons), dispatching `change` or `click` events through `browser_console` is a pragmatic fallback; verify the resulting UI state via screenshots.

## Useful data-testids

- `aks-tab-network`, `aks-network-submenu`, `aks-tab-services`, `aks-tab-gatewayclasses`, `aks-tab-gateways`, `aks-tab-httproutes`
- `services-table-body`, `gatewayclasses-table-body`, `gateways-table-body`, `pods-table-body`
- `pod-detail-panel`, `pod-log-view`, `log-output`
- `log-pause-btn`, `log-go-live-btn`, `log-latest-btn`, `log-filter-input`
- `log-range-select`, `log-container-select`
- `log-copy-visible-btn`, `log-export-btn`, `log-clear-btn`, `log-line-count`

## Known gotchas

- `PodLogView` builds the stream URL. The sidecar endpoint requires `previousContainer` as a query parameter. If it is not supplied, the request returns `400` and the log panel shows `Log stream closed unexpectedly.`
- The `pod-log-view` `VISIBLE` window is `200` lines. The demo log source is 29 initial lines plus ~1 line per second, so `Older` / `Newer` pagination cannot be exercised in a short run.
- Log color classes live in `web/src/lib/logLevel.ts` and `web/src/styles/globals.css`. `[DBG]` should map to `log-level-debug` (`color: var(--info)`).
- Service Bus subscription stats are looked up by entity path. The UI encodes slashes (`user-events%2Fsubscriptions%2Fconsumer-a`) and the sidecar now decodes them, so subscription message tab counts match the entity-tree badge.
- HPA scale/disable/enable/delete actions now refresh the table immediately. The disabled badge renders as a separate `Disabled` pill next to the `HPA`/`KEDA` type label.
- `window.confirm` blocks CDP JS evaluation. For destructive actions (HPA disable/delete, CronJob suspend/resume, Settings import), override `window.confirm = () => true` in the browser console first.
- The Redis demo seed contains only ~22 keys, which is below the 100-key page size. To exercise `Load more` / `Load all`, seed extra keys via the sidecar `POST /api/redis/{cacheId}/keys/{key}/value` endpoint.
- Settings import is driven by a hidden `<input type="file">`. Because the native file picker is not automatable, serve the modified JSON from the same origin (e.g. `/import.json` under `web/`) and construct a `DataTransfer`/`File` from the fetched text to dispatch the `change` event.
- Settings page route is `/settings`; `/settings/general` does not match any route. General is the default tab when visiting `/settings`.
- Controlled React number inputs (e.g. HPA **Max replicas**) do not reliably update state when only `.value` is set and an `input` event is dispatched from `browser_console`. Use a native `type` action or `triple_click` + `type` with the `computer` tool for those fields.
- **Deployment Scale in demo mode** updates the in-memory `DemoDeployments` list in `DemoAksClient.ScaleDeploymentAsync`, so the `Ready` column should change after a successful scale. Verify the success toast and the updated `Ready` count.
