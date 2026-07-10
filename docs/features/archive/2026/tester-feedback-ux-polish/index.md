# Tester Feedback — UX Polish Batch

## Goal

Resolve 11 concrete usability and correctness findings from a colleague's hands-on test pass so
the app feels like a trustworthy daily-driver desktop tool rather than a capable prototype. The
findings span the app shell (lifecycle, single-instance, demo banner), notification reliability,
and per-feature ergonomics in AKS, Redis, and Service Bus.

## Value

These are the friction points a real operator hit in the first sitting: an "Exit" button that
does not exit, no single-instance guard (so a hidden-to-tray app spawns duplicates), a resize
handle that lags the cursor, and alerting that silently works on one machine but not another.
Fixing them removes the "why did it do that?" moments that erode trust before the deep diagnostics
value is ever seen.

## Scope

Grouped into six workstreams. Each item traces to a specific source touchpoint (see
`frontend.md` / `backend.md`).

### A — App shell & lifecycle

- **A1 (#5) Minimize vs Exit** — the window close (×) is intercepted to hide-to-tray, and native
  minimize also hides to tray, so there is no visible "really exit" affordance. Make exit
  semantics explicit and predictable.
- **A2 (#7) Single instance** — no startup guard exists. Launching again while an instance is
  hidden in the tray must focus/restore the existing instance instead of starting a fresh one.
- **A3 (#8) Demo banner contrast** — improve readability/contrast of the demo-mode banner.

### B — Notification reliability

- **B1 (#6) Alerting inconsistency** — Windows toast fires on one machine but not another.
  Add a capability probe + a visible fallback (in-app notification) and a one-time diagnostic so
  the failure is never silent.

### C — AKS ergonomics

- **C1 (#2) Logs "go to tail" + sidepanel buttons** — a one-click jump-to-live control with clear
  live/paused/historical state, and a tidier button layout in the AKS detail sidepanel.
- **C2 (#9) Namespace dropdown ordering** — show currently selected namespaces first so they are
  easy to deselect.
- **C3 (#10) Keyboard friendliness** — logical tab order and AKS-scoped shortcuts across the page
  and panels.
- **C4 (#11) Gateways permission warning** — exclude `gateways` / `gatewayclasses` from the
  lacking-permission warning; their absence is expected, not a missing-core-access signal.

### D — Redis

- **D1 (#3) "Select all keys" position** — the multi-select "select all" control placement is not
  intuitive; reposition/relabel so its scope (loaded/visible) is obvious.

### E — Service Bus

- **E1 (#1) Show credential used on error** — when a namespace connection/credential check fails,
  surface _which_ credential reference / endpoint / SAS key **name** was used, to make
  misconfiguration diagnosable. **Never** render the secret value itself.

### F — Shared UX

- **F1 (#4) Sidepanel resize lag** — the splitter resizes slower than the mouse moves. Remove the
  lag (CSS transition on width during drag / repaint queuing).

## Non-Goals

- No redesign of any feature's information architecture — these are targeted fixes, not rewrites.
- E1 does **not** display secret values (SAS keys, connection-string secrets, tokens). Only
  non-secret identifiers (endpoint host, SAS key name, secret-reference name).
- No cross-platform work beyond what already exists; lifecycle/single-instance/toast items are
  Windows-desktop only (they already live under `Platforms/Windows`).
- A2 does not add multi-window support; it enforces exactly one running instance.
- C3 does not introduce a global keybinding remap system; it adds AKS-scoped bindings + tab order.

## Dependencies

- Windows tray lifecycle: `src/SwebKit.App/Platforms/Windows/WindowsTrayLifecycleService.cs`
- Windows toast: `src/SwebKit.App/Platforms/Windows/WindowsToastNotificationService.cs`
- Alert engine: `src/SwebKit.App/Services/AlertMonitorService.cs`
- AKS client + RBAC denials: `src/SwebKit.Kubernetes/AksClient/KubernetesAksClient.cs`
- Splitter interop: `src/SwebKit.App/wwwroot/js/splitter.js`, `wwwroot/js/uiState.js`

## Risks

- **Lifecycle regressions (A1/A2)** — the close/minimize interception is subtle; a wrong change
  can make the app impossible to close or trap it hidden in the tray. Guard with explicit tray
  state and manual verification on Windows.
- **Toast probe false-negatives (B1)** — a capability check that is too strict could disable
  working toasts. Prefer "attempt + observe + fallback" over hard gating.
- **Credential display leakage (E1)** — the single highest-risk item; a careless change could
  print a SAS key. Enforce a "names only" rule and add a focused test asserting no secret material
  appears.
- **Splitter change scope (F1)** — the splitter is shared across multiple workspaces; a CSS/JS
  change affects all of them. Verify Redis, AKS, Service Bus, and agent panels.

## Quick links

- Frontend module: `frontend.md`
- Backend / platform module: `backend.md`
- Decisions: `decisions.md`
- Test plan: `test-plan.md`
- Status: `status.md`

**Jira:** not linked
