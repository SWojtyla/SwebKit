# AKS Feature — Migration Bug Fixes

## Summary

The AKS (Kubernetes) feature was ported from MAUI Blazor (`src/SwebKit.App/Components/Pages/AksPage.razor`
+ partials) to React (`web/src/components/aks/*`) talking to a new `.NET` sidecar
(`src-sidecar/Endpoints/AksEndpoints.cs`). Code review of the uncommitted diff found several
features that are visibly present in the UI but not actually wired to a working backend — this is
why AKS "still has bugs" per the user's report. Most fixes here are either a missing sidecar route
registration or a frontend URL/port mistake, not a design problem.

**Jira:** not linked

**Depends on:** [tauri-security-hardening](../tauri-security-hardening/index.md) landing first
(CORS/token changes touch how every sidecar call is made).

## Scope

Fix the concrete bugs found in the AKS React components and their sidecar endpoints, restoring
parity with the MAUI original where it regressed, and either wiring or removing dead/orphaned UI.

## Non-Goals

- No new AKS capabilities beyond what MAUI had or what's already half-built (Gateway API/HPA panels
  stay as already-correct).
- Not redesigning the AKS page layout/tabs.

## Tasks, in priority order

### 1. Fix pod log streaming (Critical)

Two independent bugs both break "view live logs," the flagship AKS feature:

- **`web/src/components/aks/MultiPodLogView.tsx:25-27`** opens a `WebSocket` against
  `ws://localhost:5000/api/aks/{ns}/pods/{pod}/logs/stream`, but
  **`src-sidecar/Endpoints/AksEndpoints.cs:283-318`** implements this route as Server-Sent Events
  (`Content-Type: text/event-stream`, `data: ...\n\n` frames over a plain `GET`), not a WebSocket
  upgrade. Port is also hardcoded to `5000` instead of the sidecar's actual port. **Fix:** rewrite
  `MultiPodLogView.tsx` to use `EventSource` like `PodLogView.tsx` already does correctly, and get
  the base URL the same way (see next bullet).
- **`web/src/components/aks/PodLogView.tsx:57`** builds the `EventSource` URL as a bare relative
  path (`/api/aks/${ns}/pods/${podName}/logs/stream?...`) instead of prefixing it with
  `SIDECAR_BASE_URL` like every other call in `web/src/lib/hooks.ts`/`api.ts` does. In the Tauri
  webview this resolves against the webview's own origin, not the sidecar, so it 404s/fails
  silently. **Fix:** prefix with the sidecar base URL (respect the dynamic port from
  `getSidecarPort()` in `tauri-bridge.ts`, not a hardcoded value).
- Also fix **`MultiPodLogView.tsx:22-34`**, which only ever subscribes to `selectedPods[0]` despite
  a multi-select checkbox UI — either fan out one `EventSource`/subscription per selected pod (like
  the MAUI `MultiPodLogView.razor` did) or, if that's too large a scope-add right now, disable the
  multi-select UI until it's implemented rather than leaving it silently broken.
- Delete or fix `useAksPodLogs` in `web/src/lib/hooks.ts:530-545` — it's now orphaned (superseded by
  `PodLogView`) and shares the same relative-URL bug; leaving it around risks a future caller
  re-introducing the same issue.
- **Test:** open a pod's logs in the demo-mode AKS view and confirm a live stream renders; select 2+
  pods in the multi-pod view and confirm each streams independently (or confirm the UI is disabled
  if not yet implemented).

### 2. Register missing sidecar routes (Critical)

**File:** `web/src/components/aks/AksPage.tsx` (~294-296 deployment "view logs", ~350-399
StatefulSet restart/scale, ingress delete). These call:
- `POST /api/aks/{ns}/statefulsets/{name}/restart`
- `POST /api/aks/{ns}/statefulsets/{name}/scale?replicas=`
- `DELETE /api/aks/{ns}/ingresses/{name}`

None exist in `src-sidecar/Endpoints/AksEndpoints.cs`. The underlying capability is already
implemented in `src/SwebKit.Kubernetes/AksClient/*.cs`'s `IAksClient` (`DeleteIngressAsync`,
`RestartStatefulSetAsync`, `ScaleStatefulSetAsync` — confirmed present, just never exposed via the
sidecar). **Fix:** add the three route registrations in `AksEndpoints.cs` following the same
pattern as the existing Deployment restart/scale routes, calling the existing `IAksClient` methods
and honoring demo mode the same way every other endpoint does (`GetClient()` gating).

**Test:** trigger each action from the context menu against demo mode and confirm success (no
404); confirm the demo client returns a sane response.

### 3. Fix "view logs"/"container details" using the wrong pod (Major)

**File:** `AksPage.tsx` `showDeploymentMenu` (~294-296) and `showStatefulSetMenu` (~385-386) do
`if (selectedPod) openLogs(selectedPod)` — using whatever pod happens to be globally selected
elsewhere on the page, instead of a pod that belongs to the right-clicked Deployment/StatefulSet.
**Fix:** resolve the actual pod(s) for the clicked resource (via its label selector, same as the
MAUI `AksPage.razor.ContextMenuActions.cs` did) before opening logs; if there are multiple pods,
either pick the first or prompt — but never silently show an unrelated pod's logs or no-op.

### 4. Restore the production delete/restart confirmation guard (Major)

**File:** `AksPage.tsx` `handleKillPod`/`handleRestartDeployment`/`handleScaleDeployment`
(~266-282) currently use raw browser `confirm()`/`prompt()`. The MAUI original
(`AksPage.razor.ContextMenuActions.cs:292-296`) required **typing the resource name** to confirm
when the environment is flagged production — that safety net is gone. The replacement component,
**`web/src/components/aks/AksConfirmBar.tsx`, already exists but is never imported/used anywhere**.
**Fix:** wire `AksConfirmBar` into these three handlers (and any other destructive action —
ingress/statefulset delete once Task 2 lands), gated on the same "is this a production environment"
flag the MAUI version used, replacing the raw `confirm()`/`prompt()` calls.

**Test:** in a profile flagged as production, attempt to delete a pod — confirm the type-to-confirm
bar appears and blocks the action until the name matches; confirm demo/non-prod environments still
use a lighter-weight confirmation.

### 5. Fix the Secrets detail panel (Major)

**File:** `web/src/components/aks/SecretDetailPanel.tsx:51-61`.
- The "reveal" (eye icon) toggle only renders a static string
  (`"(base64 encoded — value not fetched from sidecar)"`) instead of calling the existing
  `GET /api/aks/{ns}/secrets/{name}/values` endpoint (`AksEndpoints.cs:149-154`, already
  implemented). **Fix:** call it on reveal, base64-decode client-side for display, and keep values
  out of any client-side cache/log.
- The "Copy" button copies the **key name**, not the decoded value
  (`copyValue(key)` → `clipboard.writeText(key)`). **Fix:** copy the actual secret value fetched
  above.

### 6. Remove or wire up non-functional stubs (Major)

- **`web/src/components/aks/YamlViewer.tsx`**'s "Apply" button and
  **`HelmDetailPanel.tsx:707-728`**'s per-revision "Rollback" both just set a local status message
  ("needs a sidecar endpoint") and do nothing. The header-level Rollback button
  (`HelmDetailPanel.tsx` ~692-698) only clears local state. **Decision needed:** either (a) implement
  the missing sidecar mutating endpoints (YAML apply via `kubectl apply`-equivalent through
  `KubernetesClient`; Helm rollback via the Helm SDK/CLI already used elsewhere in
  `SwebKit.Kubernetes`) if these are in scope for this pass, or (b) hide/disable these buttons
  behind a "coming soon" state instead of presenting a button that silently does nothing. Given the
  size of the rest of this plan, recommend (b) for now and file the real implementation as a
  separate, smaller follow-up feature.
- Remove the dead `resourceFilter` state/input in `AksPage.tsx:73,375` (never applied to any tab's
  data) or actually wire it into each tab's filtering — pick one; a visible but non-functional
  filter box is worse than no filter box.

### 7. Wire up or delete orphaned components (Minor)

`AksConfirmBar.tsx` gets wired up by Task 4. After that, re-check:
`NetworkPolicyAnalysisPanel.tsx` and `PodDisruptionBudgetPanel.tsx` are still never imported by any
tab (no "Network Policy"/"PDB" entry in `AksPage.tsx`'s `tabs` array). **Decision needed:** add them
as real tabs if they're meant to ship in this pass, or delete them if they were speculative/unused
scaffolding — don't leave working-looking components with no route to reach them.

## Dependencies

- [tauri-security-hardening](../tauri-security-hardening/index.md) (sidecar call auth changes).
- `src/SwebKit.Kubernetes/AksClient/*.cs` (`IAksClient`, `KubernetesAksClient`, `DemoAksClient`) —
  already has the capabilities Task 2 needs; no Core-layer changes expected, just new endpoint
  registrations.

## Risks

| Risk | Mitigation |
|---|---|
| Adding sidecar routes for StatefulSet/Ingress mutation without the prod confirm guard (Task 4) ships new destructive actions unsafely | Land Task 4 (confirm guard) before or together with Task 2 (new routes), not after |
| Fixing pod-log streaming reveals further contract mismatches once actually exercised end-to-end | Test against both demo mode and a real AKS cluster if available before calling this done |
| Scope creep implementing YAML apply / Helm rollback "properly" | Default to hiding the buttons (Task 6, option b) unless the user explicitly wants full implementation in this pass |
