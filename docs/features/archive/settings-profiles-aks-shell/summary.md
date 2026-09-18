# Summary — Settings Profile Lists + AKS Shell/Port-Forward (archived 2026-09-18)

## Goal

Fix three user-reported problems in one change set: settings profile lists that
don't scale beyond a handful of entries, a dead "Enable AI Agent" checkbox, and
broken/hostile AKS pod shell + port-forward.

## Delivered

- **`ProfileListLayout` master-detail component** + `profile-list-utils.ts`
  (selection, add auto-select, filter, group — unit-tested). Applied to
  Service Bus, Redis, Storage, SQL (with server grouping + discovery preserved)
  and Agent settings.
- **Derived `agent.isEnabled`** — the checkbox is removed from the web UI; the
  flag is set true whenever a profile becomes active, false when none remains.
  The field stays in the persisted model because the legacy MAUI shell reads it.
- **Tauri capability grant** — new `src-tauri/capabilities/default.json` grants
  `core:default` so `plugin:event|listen` works (pty streaming for the shell).
- **Port-forward hardening** (`src-tauri/src/native.rs`) — `:remote` auto-assign
  arg, `--context`/`--kubeconfig` flags, stdout+stderr reader threads, bounded
  stderr tail in errors, early-exit `try_wait` detector, 30s ready timeout,
  IPv4/IPv6/`[::]` forwarding-line parsing, kill+wait cleanup.
- **Pod shell rework** — modal overlay replaced by a bottom-docked,
  drag-resizable terminal (height persisted via `view-pref:`); survives
  resource-tab switches, torn down on cluster context change. Configured
  kubeconfig path wired through `AksWorkspaceContext` to `startPodShell` and
  `startPortForward`.
- **Test-connection fix** — `runTest` merges uncommitted `DraftInput` text so
  "Test connection" sends the currently-typed values instead of a stale saved
  copy.
- Editor test ids switched from array-index to profile-id based; e2e specs
  derive the id from the `*-item-{id}` list row.

## Key decisions

- `AgentConfig.IsEnabled` stays in the persisted contract — the legacy MAUI
  `MainLayout.razor` still consumes it. Removing it is deferred to whenever the
  MAUI shell is retired.
- `core:` plugin commands need an explicit capability grant — the compiled
  capability set without a file is `{}`, and app-defined commands being
  ACL-exempt masked this for a long time. Recorded in `docs/pitfalls/`.
- A spawn ready-wait must detect early process exit (`try_wait` polling), not
  just scan output — a fast-failing kubectl collapses into the same timeout as
  a slow exec-credential plugin otherwise.

## Validation

- `cargo test --lib`: 64 pass (7 new port-forward tests).
- `tsc -b` clean; `vitest run` 442 pass (11 new profile-list-utils tests).
- `npm run build` (web) clean; `dotnet build` (sidecar) 0 warnings/errors.
- Playwright `settings.spec.ts` + `aks*.spec.ts`: 40/40 pass.
- Aikido `scan_paths`: one finding (`AIK_Rust-command-injection` on
  `hidden_command`) reviewed — false positive (fixed literal program, no shell,
  structured `.arg()` calls).

## Lessons learned

- Master-detail list layouts should be built once as a shared component —
  rolling it out to five settings tabs was mechanical after the first.
- Id-based test ids survive list reordering; index-based ones don't.
- "Ready" detection for spawned processes needs three paths: success line,
  error output, and early exit — any two alone produce misleading timeouts.

## Follow-up

- Multiple simultaneous shell sessions / tabbed terminals — deliberately out
  of scope.
- `AgentConfig.IsEnabled` removal blocked on MAUI shell retirement.
