# Test Plan — Tester Feedback UX Polish

Verification per item. Most items are UI/interaction or Windows-platform behavior, so several
require manual verification in the running MAUI app on Windows (noted). Add focused automated tests
where the logic is unit-testable (namespace ordering, permission filtering, credential-diagnostic
secret scrubbing).

Build/test baseline commands (from repo root):

- Build app: task `build-maui-windows`, or
  `dotnet build src/SwebKit.App/SwebKit.App.csproj -f net10.0-windows10.0.19041.0`
- Tests: `dotnet test` (focus: `tests/SwebKit.App.Tests`, `tests/SwebKit.Kubernetes.Tests`,
  `tests/SwebKit.Azure.Tests`)

---

## A — App shell & lifecycle

| #    | Scenario                                       | Method       | Expected                                                            |
| ---- | ---------------------------------------------- | ------------ | ------------------------------------------------------------------- |
| A1-1 | Click × while running                          | Manual (Win) | App hides to tray; one-time hint shown on first occurrence          |
| A1-2 | Restore from tray                              | Manual (Win) | Window restores focused, state intact                               |
| A1-3 | Native minimize                                | Manual (Win) | Hides to tray consistently with ×                                   |
| A1-4 | Tray → Exit                                    | Manual (Win) | App fully quits; process gone; tray icon removed; monitors disposed |
| A2-1 | Launch app while an instance is hidden in tray | Manual (Win) | No second instance; existing instance restores+focuses              |
| A2-2 | Launch app while an instance is visible        | Manual (Win) | Existing window focused; no duplicate process                       |
| A2-3 | Exit, then relaunch                            | Manual (Win) | Mutex released on exit; fresh instance starts normally              |
| A3-1 | Demo mode banner in light theme                | Manual       | Text clearly readable; contrast ≥ WCAG AA                           |
| A3-2 | Demo mode banner in dark theme                 | Manual       | Text clearly readable; contrast ≥ WCAG AA                           |
| A3-3 | "Disable" action visible                       | Manual       | Higher-contrast button discoverable and works                       |

---

## B — Notification reliability

| #    | Scenario                                               | Method       | Expected                                                              |
| ---- | ------------------------------------------------------ | ------------ | --------------------------------------------------------------------- |
| B1-1 | Alert fires with system toasts enabled                 | Manual (Win) | Toast shown AND in-app notification recorded                          |
| B1-2 | Alert fires with app notifications disabled in Windows | Manual (Win) | No toast, but in-app notification always present; one-time hint shown |
| B1-3 | Alert fires under Focus Assist / DND                   | Manual (Win) | In-app notification present; no lost alert                            |
| B1-4 | Toast throws internally                                | Unit/Manual  | Fallback path invoked; alert not lost; no crash to monitor loop       |

---

## C — AKS ergonomics

| #    | Scenario                                 | Method                     | Expected                                                                        |
| ---- | ---------------------------------------- | -------------------------- | ------------------------------------------------------------------------------- |
| C1-1 | Live logs streaming, scroll up           | Manual                     | Footer shows `Paused at line N`; Tail button becomes call-to-action             |
| C1-2 | Click "Go to live / Tail"                | Manual                     | Resumes live, scrolls to bottom, footer `Live • tailing`                        |
| C1-3 | Load older history                       | Manual                     | Footer shows historical state; no unintended jump-to-bottom                     |
| C1-4 | Toolbar button grouping                  | Manual                     | Buttons grouped (nav / state / data); Close top-right; no crowding              |
| C1-5 | Render load while tailing                | Manual                     | Smooth; throttled render loop preserved (no per-line render)                    |
| C2-1 | `OrderNamespaces` hoists selected first  | Unit (`SwebKit.App.Tests`) | Selected (pending) namespaces sort before unselected; stable within group       |
| C2-2 | Toggle a namespace in the picker         | Manual                     | List reorders live so selected stay grouped at top                              |
| C3-1 | Tab through AKS page                     | Manual                     | Focus flows: connection bar → resource tabs → grid → detail panel               |
| C3-2 | Namespace dropdown keyboard-only         | Manual                     | Open, arrow, space-toggle, Enter apply, Esc cancel all work                     |
| C3-3 | AKS-scoped shortcuts                     | Manual                     | New chords act only on AKS page; unregister on leave; no global-chord collision |
| C4-1 | User lacking `gateways` RBAC             | Unit + Manual              | `gateways`/`gatewayclasses` excluded from permission warning                    |
| C4-2 | User lacking a core resource (e.g. pods) | Unit + Manual              | Core denial still warned (regression guard)                                     |
| C4-3 | Gateway view when denied                 | Manual                     | Gateway section degrades gracefully; no core-permission warning                 |

---

## D — Redis

| #    | Scenario                  | Method | Expected                                                                    |
| ---- | ------------------------- | ------ | --------------------------------------------------------------------------- |
| D1-1 | Enter multi-select mode   | Manual | Single scoped "select all" header checkbox present; duplicate removed       |
| D1-2 | Header checkbox tri-state | Manual | none/some/all reflects row selection; toggling selects/clears in-scope keys |
| D1-3 | Selection count sync      | Manual | Count and per-row checkboxes stay in sync with header                       |

---

## E — Service Bus

| #    | Scenario                                 | Method                       | Expected                                                                            |
| ---- | ---------------------------------------- | ---------------------------- | ----------------------------------------------------------------------------------- |
| E1-1 | Connect with wrong SAS key               | Manual                       | Error shows endpoint host + SAS key **name** + credential source; NOT the key value |
| E1-2 | Connect with unresolved secret reference | Manual                       | Error names which secret-reference/config key was used                              |
| E1-3 | Diagnostic never leaks secrets           | Unit (`SwebKit.Azure.Tests`) | Payload contains key name + endpoint; asserts secret value/connection string absent |
| E1-4 | Auth vs transport failure                | Manual                       | Auth failures read as credential problems with source label                         |

---

## F — Shared UX

| #    | Scenario                                | Method | Expected                                                    |
| ---- | --------------------------------------- | ------ | ----------------------------------------------------------- |
| F1-1 | Drag AKS detail splitter                | Manual | Pane tracks cursor 1:1; no easing/lag during drag           |
| F1-2 | Drag Redis / Service Bus splitters      | Manual | Same fix applies (shared splitter); no regression           |
| F1-3 | Drag agent panel resizer (`uiState.js`) | Manual | No lag; settle transition (if any) only after mouseup       |
| F1-4 | Width persistence after drag            | Manual | Final width persists via existing `notifyWidthChanged` path |

---

## Cross-cutting gates

- [ ] `dotnet build` clean (app + touched libraries)
- [ ] `dotnet test` green (App, Kubernetes, Azure suites at baseline)
- [ ] Aikido full scan on all new/modified files (MCP server may be unavailable — run manually
      before merge; flag prominently in status)
- [ ] Manual Windows smoke of all lifecycle (A), notification (B), and splitter (F) items
- [ ] Docs updated: `functionalities/aks.md`, `functionalities/redis.md`,
      `functionalities/service-bus.md`, `functionalities/monitoring.md`, and any shell/lifecycle
      notes in `architecture.md`
