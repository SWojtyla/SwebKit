# UX Follow-up Batch — Settings, Service Bus, AKS, Redis, API Client

**Status:** Review  
**Target branch:** `main`  
**Branch:** `devin/ux-followup-july-27`  

## Scope

Fix the concrete parity/usability gaps raised for the Tauri/React rewrite:

1. **Settings** — restore application settings import/export.
2. **Service Bus** — show real active/DLQ/scheduled message counts against an Azure Service Bus namespace.
3. **AKS** —
   - fix deployment status text/colors,
   - hide completed pods by default with a toggle,
   - allow HPA scale up/down, delete, KEDA pause/resume, and YAML view,
   - allow CronJob suspend,
   - clarify Helm release YAML/notes in the detail panel.
4. **Redis** — collapse the duplicated key list, editable namespace separator, key count, and smarter load-more/load-all.
5. **API Client** — improve the default left/middle/right panel ratio so the request editor fits.

## Outcomes

- Settings can be exported as JSON and imported back, replacing the current no-op UI.
- Service Bus entity tree and message tabs show live stats.
- AKS deployment status is human-readable and color-coded.
- AKS pods default to hiding completed pods; checkbox restores them.
- AKS HPA and CronJobs support the actions exposed by `IAksClient`.
- Helm release detail shows manifest, values, notes, and history with consistent YAML highlighting.
- Redis browser presents one hierarchical list, accepts a custom separator, and loads all matching keys when the namespace is small.
- API Client request editor defaults to a usable width.

## Non-Goals

- Rebuilding full MAUI/Blazor parity outside the listed items.
- New features not mentioned (e.g., new AKS resource kinds, Redis pub/sub redesign, API Client capture rules).

## Verification

- `npm run build`
- `dotnet build src-sidecar/SwebKit.Sidecar.csproj`
- `dotnet test tests/SwebKit.Sidecar.Tests`
- `npx playwright test`

See `technical-plan.md` for the ordered symbol-level implementation plan.
