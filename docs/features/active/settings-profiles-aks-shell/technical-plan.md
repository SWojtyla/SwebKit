# Technical Plan — Settings Profile Lists + AKS Shell/Port-Forward

## Module 1 — Shared profile list layout

New `web/src/components/settings/ProfileListLayout.tsx`:

- Props: `title`, `addLabel`, `onAdd`, `items: { id, title, subtitle?, isActive? }[]`,
  `selectedId`, `onSelect`, `testIdPrefix`, `emptyMessage`, `headerActions?`, `before?`
  (section-level fields that aren't per-profile, e.g. Redis's namespace separator),
  `children` (the selected entry's editor), `filterPlaceholder?`.
- Renders the title/Add row, then `flex`: a `w-64` scrollable list column and a `min-w-0
  flex-1` detail column.
- List rows are `<button>`s, testid `<prefix>-item-<id>`, `aria-current` on the selected row,
  truncated title + muted subtitle + an `Active` badge when `isActive`.
- A filter input appears once `items.length > 5`, matching on title + subtitle.
- Empty list → `emptyMessage` where the list would be; detail column shows a "select or add"
  placeholder.

Selection is owned by each settings component through a small shared hook
`useSelectedProfileId(persistKey, items)` (same file): persisted via
`loadViewPreference`/`saveViewPreference` (`settings-selected:<key>`), falls back to the first
item, and resolves through the live item list so a deleted selection degrades to the first
remaining entry instead of dangling.

Per-file rework:

- **ServiceBusSettings** — list rows keyed by namespace id; editor keeps every existing field
  and testid (`sb-namespace-<id>` moves onto the detail card; rows get `sb-item-<id>`).
- **RedisSettings** — same; `redis-cache-<id>` on the detail card, `redis-item-<id>` rows;
  the namespace-separator field stays above the split via `before`.
- **StorageSettings** — same pattern (`storage-account-<id>` card, `storage-item-<id>` rows).
- **SqlSettings** — keeps its extras: "Discover in Azure" via `headerActions`, the
  by-server collapsible grouping moves into the list column (group headers + per-group "add
  database" stay), `sql-connection-<id>` on the detail card.
- **AgentSettings** — `agent-profile-<field>-<id>` testids replace index-based ones; the list
  row shows the provider name as subtitle and an `Active` badge for `activeProfileId`. The
  "Active profile" radio stays in the detail card footer.

## Module 2 — Remove the Enable checkbox

- Delete the `isEnabled` checkbox `<section>` in `AgentSettings.tsx`. Replace with a one-line
  note under the heading: the agent is available whenever an active profile is set.
- `AgentConfig.isEnabled` stays in `types.ts` (the MAUI app reads
  `UserSettings.Settings.Agent.IsEnabled`; removing the field would break that contract).

## Module 3 — Tauri capability grant (ACL fix)

New `src-tauri/capabilities/default.json`:

```json
{
  "$schema": "../gen/schemas/desktop-schema.json",
  "identifier": "default",
  "description": "Main window: core defaults including event listen/emit for sidecar and pod-shell streams.",
  "windows": ["*"],
  "permissions": ["core:default"]
}
```

`core:default` includes `core:event:default` (`allow-listen`/`allow-unlisten`/`allow-emit`/
`allow-emit-to`), which is what `plugin:event|listen` needs. This also fixes
`onSidecarLifecycleEvent`, which has been silently failing the same way.

## Module 4 — Pod shell as a docked terminal

- `AksPage.tsx`: the content row becomes `flex min-w-0 flex-1 flex-col` — the tab content
  keeps `flex-1 overflow-auto`, and `PodShellPanel` renders as a bottom sibling inside that
  column (side panels unaffected).
- `PodShellPanel.tsx`: drop the `fixed inset-0` overlay; render `flex flex-col border-t` with
  a `cursor-row-resize` drag handle on the top edge. Height persists via
  `loadViewPreference("aks-pod-shell-height")`. Same testids (`pod-shell-panel`,
  `pod-shell-terminal`, `pod-shell-close`, `pod-shell-status`, `pod-shell-pod-name`); the
  `pod-shell-overlay` testid goes away with the overlay.
- `AksWorkspaceContext.tsx`: `setActiveTab` no longer clears `shellPod` (a docked terminal
  isn't stale when you switch resource tabs); `handleContextChange` still kills it — a shell
  against the previous cluster must never survive. `shellPod` leaves `autoRefreshPaused`:
  the dock doesn't cover the table it's refreshing.
- Wire `kubeconfigPath`: expose it on the workspace context from
  `profile.config.aksConfig.kubeconfigPath`, pass to `PodShellPanel` and `PortForwardPanel`
  (the bridge already accepts the arg; it was never sent — a non-default kubeconfig meant
  kubectl silently used `~/.kube/config`).

## Module 5 — `kubectl port-forward` robustness (`native.rs`)

- Extract `build_port_forward_args(...)` (pure, unit-tested like `build_exec_args`): emits
  `:<remote>` when no local port is requested (kubectl's documented auto-assign form).
- Child wrapped in `Arc<Mutex<Child>>`; a waiter thread polls `try_wait` (same pattern as
  `pod_shell.rs`) and reports early exit — with the collected stderr tail — instead of letting
  the ready-wait run out.
- stderr lines are collected into a small bounded buffer; lines containing "error" still
  short-circuit, and the tail is appended to both the early-exit and the timeout error.
- `FORWARD_READY_TIMEOUT` 10s → 30s (exec-credential plugins like kubelogin can be slow on a
  cold start).
- `PortForwardPanel` passes `kubeconfigPath` through `startPortForward`.

## Validation

- `npm --prefix web run build`, `npm --prefix web run test:unit`, `npm --prefix web run lint`.
- `cargo test --lib` in `src-tauri` (arg-building + parser tests).
- `dotnet build` `src-sidecar` (untouched but cheap to confirm).
- `npx playwright test e2e/settings.spec.ts` for the reworked selectors.
- Manual: pod shell + port-forward against a live cluster in `tauri dev` (the ACL and pty
  paths cannot be exercised by Playwright — `isTauri()` is false in a browser).
