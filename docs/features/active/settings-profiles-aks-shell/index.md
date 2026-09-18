# Settings Profile Lists + AKS Shell/Port-Forward Fixes

## Status

`In Progress` — see `status.md`.

## Goal

Three user-reported problems, one change set:

1. **Settings profile lists don't scale.** Every settings tab that manages a list of
   connections (Service Bus, Redis, SQL, Storage, AI Agent) renders one large editor card per
   entry, stacked vertically. At ~10 Redis caches the page is a scroll marathon. Rework all of
   them onto one shared master-detail layout: a compact, filterable list on the left and the
   selected entry's editor on the right.

2. **The "Enable AI Agent" checkbox is dead state.** Nothing in the React/Tauri app reads
   `agent.isEnabled` — the sidecar resolves the active profile directly
   (`AgentConfig.GetActiveProfile()`). The flag still exists in the persisted model because the
   MAUI shell reads it (`MainLayout.razor`), so the checkbox is removed from the web UI but the
   field stays in the contract.

3. **AKS pod shell and port-forward are broken/hostile.**
   - Opening a shell throws `Command plugin:event|listen not allowed by ACL`: the app has no
     `src-tauri/capabilities/` file at all, so the compiled capability set is `{}` — app-defined
     commands are exempt from ACL (which is why `start_pod_shell` itself works) but
     `plugin:event|listen`, used to stream pty output, is not.
   - The shell renders as a full-screen modal that locks the rest of the app. It becomes a
     bottom-docked, vertically resizable terminal strip (VS Code-style) that survives tab
     switches inside the same cluster context.
   - `kubectl port-forward` fails with "did not start within 10s": the spawn loop only watches
     stdout for `Forwarding from 127.0.0.1:` and stderr for lines containing "error". A kubectl
     that exits early with a non-"error" message (e.g. `Unable to connect to the server:`), or a
     slow exec-credential plugin (kubelogin/Entra), produces the same uninformative timeout.
     Fixes: `:<remote>` port syntax for auto-assign, early-exit detection via `try_wait`
     polling, stderr tail surfaced in the error, 30s ready timeout, and the configured
     kubeconfig path actually passed through (it was silently dropped before).

## Scope

- `web/src/components/settings/` — new `ProfileListLayout.tsx`, reworked
  `ServiceBusSettings`, `RedisSettings`, `StorageSettings`, `SqlSettings`, `AgentSettings`.
- `web/src/components/aks/PodShellPanel.tsx`, `AksPage.tsx`, `AksWorkspaceContext.tsx`,
  `PortForwardPanel.tsx`.
- `src-tauri/capabilities/default.json` (new), `src-tauri/src/native.rs`.
- `web/e2e/settings.spec.ts` — agent profile testids move from index- to id-based.

## Non-goals

- Multiple simultaneous shell sessions / tabbed terminals.
- Removing `AgentConfig.IsEnabled` from the persisted model (MAUI still consumes it).
- Changing the port-forward feature set (still per-pod, started/stopped from the panel).

## Links

- `docs/architecture/functionalities/aks.md`
- `docs/architecture/functionalities/settings-and-configuration.md`
- `docs/pitfalls/react-frontend.md`
