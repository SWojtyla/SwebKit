# Test Plan — Settings Profile Lists + AKS Shell/Port-Forward

## Settings profile lists

- Each list section (Service Bus, Redis, Storage, SQL, Agent) renders one row per profile in
  the left column; clicking a row shows that profile's editor on the right.
- Selection persists across reload (per-section `view-pref` key) and survives profile removal
  (falls back to the first remaining entry).
- Add auto-selects the new profile so its editor is immediately visible.
- Filter input appears only when the list has >5 entries and filters on title + subtitle.
- Remove keeps the existing confirm rule (blank placeholders delete instantly, configured
  entries confirm via `ConfirmBar`).
- Existing e2e flows in `settings.spec.ts` keep passing: `.last()` locators on the detail
  fields still resolve (a single editor is mounted at a time — the selected one), and the
  id-based `*-test-connection-*` / `*-remove-*` testids are unchanged.
- Agent profile testids move to id-based (`agent-profile-base-url-<id>`); `settings.spec.ts`
  is updated to resolve the id from the new profile's list row.
- Empty list shows the empty hint; single item doesn't need the filter input.

## AI Agent checkbox

- The checkbox and its section are gone; a note explains enablement derives from the active
  profile. The agent still works with an active profile (nothing else consumed the flag).

## Pod shell

- `tauri dev` only (browser can't reach Tauri APIs): opening a shell on a running pod connects
  instead of throwing `plugin:event|listen not allowed by ACL`; output streams and keystrokes
  work; the terminal docks at the bottom of the AKS page, resizes via its top handle, and does
  not block the rest of the UI; switching resource tabs keeps the session; switching cluster
  context kills it.
- `pod-shell-*` testids unchanged for the panel pieces that still exist.

## Port-forward

- `start_port_forward` against a live pod binds and reports the actual local port (auto-assign
  uses the `:remote` form).
- A dead cluster / missing pod now errors fast with kubectl's own stderr in the message,
  rather than the generic 10s timeout.
- `cargo test --lib` covers `build_port_forward_args` and the `127.0.0.1:` parser.

## Regression risks

- Playwright specs reading stacked-card markup: the only mounted editor is the selected
  profile — verified via `settings.spec.ts`.
- `AksWorkspaceContext`: `shellPod` removed from `autoRefreshPaused` and from `setActiveTab`'s
  clearing — auto-refresh keeps running with a docked terminal, and the shell survives tab
  switches but not context switches.
