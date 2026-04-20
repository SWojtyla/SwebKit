# Test Plan — aks-workspace-polish

---

title: "Test Plan — aks-workspace-polish"
owner: ""
status: "Not started"
created: "2026-04-20"
updated: "2026-04-20"

---

## Goal

Verify that all 11 improvements render correctly, respond to edge cases, do not regress existing AKS page behaviour, and persist state safely.

## Scope

- **In scope:** all items #1–#16 (selected subset), visual correctness, interaction correctness, persistence correctness, graceful degradation
- **Out of scope:** end-to-end cluster connectivity tests (covered by existing E2E suite), Kubernetes API mock fidelity

## Main scenarios (priority)

### #1 — Log level colouring

1. Stream logs from a pod with `ERROR`, `WARN`, `INFO` lines → each line class matches severity
2. Stream structured JSON logs (no plain-text level keyword) → no spurious severity class applied
3. Log line > 120 characters with level keyword buried deep → level keyword beyond 120-char threshold is ignored, line renders as default

### #2 — Status row tinting

1. Pod with status `CrashLoopBackOff` → row has red tint CSS class
2. Pod with status `Running` + `Ready=true` → no tint
3. Pod with status `Pending` → amber tint
4. Pod with status `Terminating` → muted tint
5. Deployment with `ReadyReplicas < Replicas` → row tinted as degraded
6. StatefulSet with `ReadyReplicas == 0` → row tinted as critical

### #3 — Events panel filter + jump

1. Toggle "Warning" filter → only Warning-type events visible
2. Toggle "Normal" filter → only Normal-type events visible
3. Clear filter → all events visible
4. Click "go to resource" on a Deployment event → main grid switches to Deployments, selects matching row
5. Click "go to resource" on a Pod event → main grid switches to Pods, selects matching row
6. Event references a resource that no longer exists → link is disabled with tooltip "Resource not found"

### #4 — Dynamic keyboard hints

1. Select a `CrashLoopBackOff` pod → `s` (shell) hint is dimmed, `l` (logs) hint is active
2. Select a running pod with shell available → `s` hint is active
3. Select a CronJob row → only CronJob-relevant hints shown
4. No row selected → default all-resource hints shown

### #5 — CronJob next-run countdown

1. CronJob with standard cron `0 * * * *` → tooltip shows correct next-run time (within ±1 min)
2. CronJob with non-standard expression (e.g., `@daily`) → tooltip shows raw schedule string with a note that parsing is unavailable
3. CronJob with `Suspended=true` → tooltip shows "Suspended"

### #6 — All-namespaces chip

1. Click `*` chip → namespace selector switches to multi-namespace mode
2. Multi-namespace mode active → chip is highlighted / active state
3. Select a specific namespace → chip deactivates, mode returns to single namespace

### #10 — Port-forward browser open

1. Port-forward becomes `Active` on port 8080 → "Open in browser" button appears
2. Click "Open in browser" → system browser opens `http://localhost:8080`
3. Port-forward on port 5432 (PostgreSQL) → "Open in browser" button is NOT shown
4. Port-forward is `Stopped` → button is hidden

### #11 — Pinned port-forward targets

1. Complete a port-forward and click "Pin this" → entry appears in pinned list on next open of `PortForwardStartDialog`
2. Pinned entry is scoped to the active kubeconfig context → switching context shows a different pinned list
3. Add 21 pinned entries → oldest entry is evicted, list stays at 20
4. Delete a pinned entry → entry removed from `user-settings.json` atomically (no truncation on concurrent save)
5. App restarts → pinned entries are restored from `user-settings.json`

### #13 — Helm diff before rollback

1. Select a Helm release revision and click "Diff" → `HelmDiffPreviewPanel` opens with the diff output
2. `helm-diff` plugin not installed → panel shows a setup notice with install instructions; rollback action remains available
3. Current revision selected for diff → "same version" notice shown; no error
4. Diff content is large (>1000 lines) → panel scrolls, does not lock UI

### #14 — YAML editor pre-validation

1. Remove a required field (e.g., `metadata.name`) from a Deployment YAML → warning banner appears before Apply button is enabled/clicked
2. Invalid YAML syntax (indentation error) → error banner shows parse error details
3. Valid YAML → no banner; Apply proceeds normally
4. Apply with warning acknowledged → Apply still sends to cluster; cluster-side error reported normally

### #16 — Container detail requests/limits vs usage

1. Open container detail for a running pod with metrics available → requests, limits, and current usage shown side by side for CPU and memory
2. Container has no `resources` block defined → "No requests/limits configured" shown; metrics still displayed
3. Metrics server unavailable → usage column shows "Metrics unavailable"; requests/limits still displayed

## Automated coverage

- **Unit tests** (`SwebKit.App.Tests`): log level CSS class assignment, next-run cron calculation, HTTP port detection logic, pinned target eviction at cap
- **Unit tests** (`SwebKit.Kubernetes.Tests`): no new client logic added — no new tests required in this project
- Integration tests: existing AKS bootstrap tests unaffected — no contract changes

## Test data and setup

- A local kubeconfig with at least one cluster context for manual verification
- A pod with known `CrashLoopBackOff` state (or demo data enabled) for item #2
- `helm-diff` plugin installed in test environment (already done — see terminal history)
- `UserSettings` JSON file writable; back up before persistence tests

## Manual checks

- Check: Status row tinting renders correctly across light and dark themes
- Check: Log coloring does not change line layout or cause horizontal scroll on short lines
- Check: "Open in browser" does not open browser from inside the MAUI WebView (must use platform `Launcher.OpenAsync`)
- Check: Pinned port-forward entries saved under correct context key when user has multiple kubeconfig contexts
- Check: Helm diff panel does not affect rollback flow when plugin is absent

## Regression risks & mitigations

- Risk: Log level regex impacts virtualized log line render performance — Mitigation: benchmark with 10 k lines; regex limited to first 120 chars per line
- Risk: Row tinting CSS class conflicts with existing selected-row highlight — Mitigation: tint uses `background-color` on a lower z-index layer; selected state overrides with `!important` only for the highlight colour
- Risk: Cron expression parser produces wrong next-run for UTC vs local time — Mitigation: always display times in UTC with explicit suffix

## Acceptance criteria

- All Wave 1 and Wave 2 manual checks pass
- Wave 3 pinned-target persistence survives app restart
- Item #13 does not break rollback when `helm-diff` is absent
- No regressions on existing AKS page keyboard navigation or panel open/close behaviour
- Unit tests for log level classification and cron next-run pass in CI

## Validation status

- Automated: Not started
- Manual: Not started

## Sign-off

_(pending)_
