# Status — Settings Profile Lists + AKS Shell/Port-Forward

**Status:** `Review`

## Done

- Diagnosis: `plugin:event|listen` fails because no `src-tauri/capabilities/` file exists —
  the compiled capability set is `{}`; app commands are ACL-exempt but core/plugin commands
  (event listen) are not.
- Diagnosis: port-forward timeout — ready-wait only recognizes `Forwarding from 127.0.0.1:` on
  stdout or stderr lines containing "error"; early exit or slow exec-auth both collapse into
  the same 10s timeout, and the configured kubeconfig path was never passed.
- `src-tauri/capabilities/default.json` grants `core:default` (event listen/unlisten/emit).
- `native.rs` port-forward hardened: `:remote` auto-assign arg, `--context`/`--kubeconfig`
  flags, stdout+stderr reader threads, bounded stderr tail in error messages, early-exit
  `try_wait` detector, 30s ready timeout, IPv4/IPv6/`[::]` forwarding-line parsing, kill+wait
  cleanup. New unit tests for arg building and the parser.
- Pod shell reworked from modal overlay to bottom-docked, drag-resizable terminal (height
  persisted via `view-pref:`); survives resource-tab switches, still torn down on cluster
  context change. Configured kubeconfig path wired through `AksWorkspaceContext` to both
  `startPodShell` and `startPortForward`.
- `ProfileListLayout` master-detail component + `profile-list-utils.ts` (selection, add
  auto-select, filter, group — unit-tested). Applied to Service Bus, Redis, Storage, SQL
  (with server grouping + discovery preserved) and Agent settings.
- "Enable AI Agent" checkbox removed; `isEnabled` is now derived — set true whenever a
  profile becomes active, false when none remains.
- Pitfall entry added: `core:` plugin commands need an explicit capability grant
  (`plugin:event|listen not allowed by ACL`).
- Editor test ids switched from array-index to profile-id based
  (`agent-profile-*-{p.id}` etc.); e2e specs derive the id from the `*-item-{id}`
  list row after `aria-current="true"` lands, matching the master-detail layout.
- `runTest` merges uncommitted `DraftInput` text (via `onDraftChange`) so "Test
  connection" sends the currently-typed values instead of a stale saved copy.

## Validation

- `cargo test --lib`: 64 pass (incl. 7 new port-forward tests).
- `tsc -b`: clean.
- `vitest run`: 442 pass (incl. 11 new profile-list-utils tests).
- ESLint on touched files: clean (warnings only, pre-existing patterns).
- `npm run build` (web): clean; `dotnet build` (sidecar): 0 warnings/0 errors.
- Playwright `settings.spec.ts` + `aks*.spec.ts`: 40/40 pass (one blank-page
  startup flake on "all tabs are visible" — `settings-title` renders before any
  of this work; passes in isolation).
- Aikido `scan_paths` on all modified files: one finding
  (`AIK_Rust-command-injection` on `hidden_command`) reviewed — false positive:
  `program` is only ever a fixed literal (`"kubectl"`/`"git"`/bundled sidecar),
  `Command` invokes no shell, and args are passed via structured `.arg()` calls.
