# AKS & Storage UX improvements

State: Review

## Goal

Four user-requested improvements:

1. Rename the **HPA** tab to **Autoscaling** and make it cover both HPAs and KEDA
   resources (ScaledObjects already surface via their generated HPAs; KEDA
   **ScaledJobs** are currently invisible — add them).
2. CronJobs: UI-friendly schedule editor instead of editing YAML cron notation,
   plus a "next run" column.
3. Storage accounts: browse **Azure Files file shares**, not just blob containers.
4. Timezone clarity: all displayed times are local — make that visible in the UI
   and centralise the formatting.
5. (Added during implementation) Service Bus **purge** fix — the endpoint bound
   `deadLetter` from the query string while the UI sends a JSON body, so every
   real purge returned 400.
6. (Added on request) AKS network depth: richer **HTTPRoute** detail and a new
   **Envoy** tab covering the `gateway.envoyproxy.io` CRDs (traffic policies,
   security, backends, proxy config).

## Scope

- `web/` AKS tab rename (`hpa` → `autoscaling`; legacy `?tab=hpa` still resolves),
  merged HPA + ScaledJob table, CronJob schedule dialog + next-run column,
  storage file-share browsing (list, directories/files, file detail, content
  preview, download-as-text, SAS URL).
- `src/SwebKit.Core`: `ScaledJobInfo`, `CronJobInfo.TimeZone`, share models,
  `IAksClient`/`IStorageClient` additions (default-throw where optional),
  demo clients (scaledjob, schedule override, demo shares).
- `src/SwebKit.Kubernetes`: scaledjob list/pause/scale/delete via CustomObjects,
  cronjob schedule patch, scaledjob YAML read.
- `src/SwebKit.Azure`: `Azure.Storage.Files.Shares` client usage.
- `src-sidecar`: new endpoints (`scaledjobs`, `cronjobs/{name}/schedule`,
  `shares`, share items/file content/properties/SAS).
- `web/src/lib/cron.ts` + `datetime.ts` with vitest coverage.
- Status-bar timezone chip; sweep display-time call sites onto the new helper.
- Service Bus purge request body binding (`PurgeRequest` record) + e2e
  regression that actually verifies the queue empties.
- Envoy: `EnvoyResourceInfo` + per-kind highlight parsing for 8 CRD kinds
  (`backendtrafficpolicies`, `clienttrafficpolicies`, `securitypolicies`,
  `backends`, `envoyproxies`, `envoyextensionpolicies`, `envoypatchpolicies`,
  `httproutefilters`), `GET /api/aks/{ns}/envoy/{plural}` with a kind
  whitelist, `EnvoyTab` kind-picker UI, HTTPRoute rule/match/filter/timeout
  models + `HttpRouteDetailPanel`.

## Non-goals

- File-share mutations (upload/delete/metadata) — read + download + SAS only.
- ScaledObject/ScaledJob creation forms; CronJob `timeZone` editing.
- The legacy MAUI/Blazor app (`src/SwebKit.App`).

## Tasks

- [x] Core models + interface additions (IAksClient, IStorageClient)
- [x] Kubernetes client: scaledjobs, cron schedule patch, timezone mapping
- [x] Azure storage client: file shares
- [x] Demo clients: scaledjob, cron schedule override, demo shares
- [x] Sidecar endpoints
- [x] Web: cron.ts + datetime.ts (+ tests), hooks, types
- [x] Web: Autoscaling tab (rename + scaledjobs), CronJobs schedule dialog + next run
- [x] Web: storage file-share browsing + detail panel
- [x] Web: status-bar timezone chip + display-time sweep
- [x] e2e spec updates (`aks-tab-hpa` → `aks-tab-autoscaling`, new coverage)
- [x] Service Bus purge fix + regression coverage
- [x] Envoy: models, k8s custom-object listing, demo fixtures, sidecar endpoint, EnvoyTab, HTTPRoute detail
- [x] Validation: web build, vitest, dotnet build, sidecar/kubernetes tests, aikido scan

## Implementation notes

- Tab id stays `"hpa"` (deep links keep working); the label and route
  rendering moved to `AutoscalingTab`, which shows an HPA section plus a KEDA
  ScaledJobs table. KEDA-managed HPAs still show their ScaledObject badge and
  route scale/pause ops to the owning ScaledObject.
- `KubernetesAksClient.ScaledJobs.cs` does list/scale/pause/delete via
  CustomObjects on `keda.sh/v1alpha1 scaledjobs`.
- CronJob schedule updates go through `POST .../cronjobs/{name}/schedule`
  (server-side 5-field/macro validation) → strategic merge patch on
  `spec.schedule`. The dialog computes the next run in `spec.timeZone` when set.
- File shares are read-only: list, directory browse, file properties, text
  preview (binary flagged), SAS URL. `Azure.Storage.Files.Shares` added to
  `Directory.Packages.props` / `SwebKit.Azure.csproj`.
- All timestamp rendering goes through `web/src/lib/datetime.ts`; the status
  bar shows the resolved local timezone name + abbrev.
- Aikido scan findings were all pre-existing or demo fixtures (kubeconfig
  path read, fake base64 secrets in demo YAML, sanitized `highlightYaml`
  dangerouslySetInnerHTML, client-side sidecar fetch).
- Service Bus purge: the handler now takes `PurgeRequest { deadLetter }` —
  a bare `bool` binds from the query string, never the JSON body, which is
  why real purges 400'd while direct-handler tests passed.
- Envoy: `KubernetesAksClient.Envoy.cs` lists the 8 `gateway.envoyproxy.io`
  kinds via CustomObjects and flattens each spec into label/value highlights
  (max connections, retries, rate limit, TLS, JWT issuers, …). `EnvoyProxy`
  is cluster-scoped; the rest are namespaced. HTTPRoutes got `rules` (matches,
  filters, backendRefs, request/backend timeouts) and per-parent status
  parsing in `MapHttpRoutes`.

## Test plan

- `web/src/lib/cron.test.ts` — parse/describe/next-run edge cases.
- `web/src/lib/datetime.test.ts` — helpers.
- `tests/SwebKit.Sidecar.Tests` — schedule endpoint validation + scaledjob passthrough.
- `web/e2e/aks.spec.ts` — autoscaling tab (KEDA ScaledJob row), cronjob schedule dialog in demo mode.
- `web/e2e/storage*.spec.ts` — shares section visible, browse demo share.
