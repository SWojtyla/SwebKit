# Test Plan — API Client UX Refactor

## Strategy

- Phase 1 (icons) and Phase 2 (refactor) are behaviour-preserving → rely on the existing API Client
  test suite passing unchanged, plus visual/manual smoke.
- Phase 3 (tabs) and Phase 4 (cookie jar) add behaviour → add focused component/service tests.
- Run focused API Client tests after each phase; do not batch phases before validating.

Build/test commands per `AGENTS.md`: `dotnet build` then `dotnet test` (focused projects
`SwebKit.Core.Tests`, `SwebKit.App.Tests`).

---

## Phase 1 — Iconography

| #   | Scenario                  | Expected                                                                                    |
| --- | ------------------------- | ------------------------------------------------------------------------------------------- |
| 1   | Load API Client page      | No emoji or literal ASCII glyphs render in toolbar, env picker, response viewer, quick-nav  |
| 2   | Toolbar dropdown chevrons | Render as `FluentIcon` chevrons, not `v`/`▾`                                                |
| 3   | Response status bar       | Timing/size/subscription/warning use `FluentIcon`, aligned to prior sizes (no layout shift) |
| 4   | Empty state               | Icon renders via FluentIcon path, not an emoji string                                       |
| 5   | Build                     | No new `RZ10012` warnings; MAUI Windows build succeeds                                      |

---

## Phase 2 — Refactor (behaviour-preserving)

| #   | Scenario                              | Expected                                                                 |
| --- | ------------------------------------- | ------------------------------------------------------------------------ |
| 1   | Existing API Client test suite        | All pre-refactor tests still pass unchanged                              |
| 2   | Select request in tree                | Request loads into builder; response panel resets — same as before       |
| 3   | Dirty + switch request (autosave off) | Save/discard prompt still fires                                          |
| 4   | Splitter init on collection switch    | No stale/zero-width splitter (the previously fixed bug does not regress) |
| 5   | Linked-save conflict                  | Reload / Keep mine / Save as copy banner still works                     |
| 6   | Env picker + worksheet toggles        | State survives child re-render (no BL-4 reset)                           |
| 7   | Navigate away and back                | Streams cancelled on dispose (BL-7); no stale updates                    |

---

## Phase 3 — Optional request tabs

### Toggle OFF (default)

| #   | Scenario             | Expected                                                  |
| --- | -------------------- | --------------------------------------------------------- |
| 1   | Setting default      | `ApiClientRequestTabs` defaults to `false` on fresh state |
| 2   | Open requests        | Single-request model identical to today; no tab strip     |
| 3   | Ctrl+P / tree switch | Works as before                                           |

### Toggle ON

| #   | Scenario                              | Expected                                                           |
| --- | ------------------------------------- | ------------------------------------------------------------------ |
| 4   | Enable setting                        | Tab strip appears above the request builder                        |
| 5   | Open second request                   | New tab added; first tab preserved; no implicit replace            |
| 6   | Edit tab A, switch to B, back to A    | Tab A dirty state and edits preserved                              |
| 7   | Send in tab A, switch to B            | Tab B usable; tab A send continues; no cross-tab cancel (DEC-UX-4) |
| 8   | Close dirty tab                       | Save/discard prompt fires                                          |
| 9   | Close tab with active WS/subscription | Session cancelled and disposed (BL-7)                              |
| 10  | Ctrl+S / Send / Ctrl+P under tabs     | Route to the active tab; focused shortcut test                     |
| 11  | Ctrl+W / Ctrl+Tab                     | Close/cycle tabs (if shortcuts adopted)                            |

---

## Phase 4 — Cookie jar (deferrable)

| #   | Scenario                        | Expected                                                              |
| --- | ------------------------------- | --------------------------------------------------------------------- |
| 1   | Capture off (default)           | No cookies stored or sent                                             |
| 2   | Capture on: login sets cookie   | Response `Set-Cookie` stored in jar for the domain                    |
| 3   | Follow-up same-domain request   | Stored cookie replayed automatically                                  |
| 4   | Different domain                | Cookie NOT sent cross-domain (CookieContainer semantics)              |
| 5   | Copy as cURL                    | Cookie values scrubbed, not embedded                                  |
| 6   | Export collection / linked save | No cookie values written to JSON / `.swebkit-api/` / diffs / examples |
| 7   | Malformed `Set-Cookie`          | Request still succeeds; jar degrades gracefully                       |
| 8   | Clear per-domain / clear-all    | Jar cleared; subsequent requests send no cookies                      |

---

## Security Validation

- Run the Aikido full scan on new/modified first-party code per repo rules
  (`.github/instructions/aikido_rules.instructions.md`) once implementation lands.
- Confirm no secret/cookie values appear in any export, cURL, example, diff, or linked file.
