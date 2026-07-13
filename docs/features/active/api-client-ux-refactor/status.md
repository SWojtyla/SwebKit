# Status — API Client UX Refactor

## Current State

`Review`

## Quick Summary

Plan created from a post-review of the archived API Client foundation. Four workstreams: FluentIcon
cleanup, a behaviour-preserving `ApiClientPage.razor` refactor, optional request tabs behind a
default-off toggle, and a deferrable secret-scrubbed cookie jar.

**Jira:** not linked

**Current focus:** Phase 1 (iconography) implemented and build-clean; pending a manual in-app
visual smoke pass and an Aikido security scan (MCP server unavailable in this session — run
manually before merge). Phase 2 (`ApiClientPage.razor` refactor, Tasks 1-9) is complete: build
clean, both test suites at baseline (no new ApiClient failures), and all 7 Phase 2 test-plan
scenarios code-traced clean across the split component tree — see test-plan.md Phase 2 table.
**Caveat:** the Phase 2 "manual smoke" checklist item was verified via code-trace only in this
session (no interactive MAUI app run) — an interactive UI smoke pass is still recommended before
shipping/merging.

Phase 3 (optional request tabs, Tasks 1-9) is implemented: the `ApiClientRequestTabs` setting
(default off) is wired end-to-end — settings toggle, a session-only open-tabs model (DEC-UX-7),
the tab strip UI, open/focus/close with a hand-rolled 3-button dirty-close confirm dialog, one
`RequestBuilderPanel`/`ResponseViewerPanel` pair per open tab kept alive via CSS visibility (per
BL-4, not `@if`) so background-tab sends/subscriptions survive tab switches, per-tab splitter
drag-resize (each open tab gets its own JS handle, initialized lazily and torn down when the tab
closes), and Ctrl+S / Send / Ctrl+P / Ctrl+Shift+W (close active tab) / Ctrl+PageUp / Ctrl+PageDown
(cycle tabs) shortcut routing to the active tab. Ctrl+W / Ctrl+Tab / Ctrl+Shift+Tab were **not**
reused — see DEC-UX-8 — new chords were chosen to mirror existing browser tab conventions so no
new mental model is required. Build is clean and focused tests were added for the setting default,
the `ApiClientOpenTab` POCO, the tab strip component, and the Ctrl+S/Ctrl+P command contracts.
**Caveat:** the tab-close/cycle shortcut _routing_ (`GetNextOpenTabId`, the `OnApiClientShortcut`
cases) is verified via code-trace only — `ApiClientPage` cannot be bUnit-rendered (see below), so
there is no automated contract test for these three cases the way there is for Ctrl+S/Ctrl+P.

**Phase 4 decision:** left deferred per maintainer call — Phases 1-3 already deliver standalone
value (tabs, iconography, the refactor foundation) and the cookie jar is a separate, security-
sensitive workstream (session-secret handling, scrubbing across every export path) that deserves
its own dedicated implementation pass rather than being rushed to close out this feature. Tracking
moved to a **Future work** note below; Phase 4 checklist items remain unchecked and out of scope
for this feature's "done" state.
(c) `tests/SwebKit.App.Tests` cannot bUnit-render `ApiClientPage` / `ApiClientWorkspace` /
`ApiClientRequestWorkspace` (they transitively pull in the MAUI-only `FilePicker` API via
`RequestBuilderPanel` / `CollectionExportDialog` — a build-time reference issue, not a mocking
gap), so several ON-path scenarios are verified via code-trace only, not automated — see
test-plan.md Phase 3 verification note. (d) the Aikido MCP server was unavailable in all 8
implementation sessions for this phase — a manual Aikido full scan across all Phase 3 changed
files is still required before merge; flag prominently, do not let it get buried.
Next up: none — Phase 4 stays deferred (see decision above); remaining work before ship is the
manual Aikido scan and the maintainer's own manual/visual smoke pass.

## Sequencing

1. Phase 1 — Iconography (independent, low risk)
2. Phase 2 — `ApiClientPage.razor` refactor (foundation, behaviour-preserving)
3. Phase 3 — Optional request tabs (built on Phase 2)
4. Phase 4 — Cookie jar (deferrable; can ship later)

## Progress Checklist

### Planning

- [x] Scope captured
- [x] Architecture touchpoints identified
- [x] Frontend module drafted
- [x] Backend module drafted
- [x] Decisions captured
- [x] Test plan drafted
- [ ] Maintainer confirms scope and phase order

### Phase 1 — Iconography

- [x] Audit all emoji / ASCII glyphs across API Client components
- [x] Map each to a FluentIcon
- [x] Replace glyphs; align sizes to avoid layout shift
- [x] Extend `EmptyState` for FluentIcon if needed
- [x] Build clean (no new RZ10012)
- [ ] Visual smoke pass (manual, in running app)

### Phase 2 — Refactor

- [x] Define `ApiClientState` container (page-scoped)
- [x] Extract `ApiClientToolbar.razor`
- [x] Extract `ApiClientWorkspace.razor` (split host)
- [x] Re-parent tree / builder / response / worksheet panels
- [x] Preserve dispose / cancellation / conflict banners
- [x] Full API Client test suite green; manual smoke (caveat: "manual smoke" verified via
      code-trace of all 7 test-plan.md Phase 2 scenarios, not an interactive UI run — see
      Quick Summary)

### Phase 3 — Optional request tabs

- [x] Add `ApiClientRequestTabs` setting (default off)
- [x] Settings UI toggle
- [x] Open-tabs model in `ApiClientState`
- [x] Tab strip component + open/focus/close behaviour
- [x] Per-tab dirty / cancellation / editor lifecycle (per-tab splitter drag-resize now wired —
      each open tab gets its own JS handle, lazily initialized and disposed on tab close)
- [x] Shortcut routing to active tab (Ctrl+S / Send / Ctrl+P; close/cycle via Ctrl+Shift+W /
      Ctrl+PageUp / Ctrl+PageDown — Ctrl+W / Ctrl+Tab / Ctrl+Shift+Tab intentionally not reused,
      see DEC-UX-8)
- [x] Focused tests (off-path unchanged; on-path behaviours) (caveat: several on-path scenarios,
      including the new tab-shortcut routing, are verified via code-trace only, not bUnit-
      automated, due to a test-project MAUI reference limitation — see Quick Summary and
      test-plan.md Phase 3 verification note)

### Phase 4 — Cookie jar (deferred — see Quick Summary decision)

Not in scope for this feature's completion. Checklist retained for whoever picks this up next.

- [ ] `IApiCookieJar` / `ApiCookieJar` over `CookieContainer`
- [ ] Opt-in flag; execution-path integration (no global UseCookies)
- [ ] Secret scrubbing across exports / cURL / examples / diffs / linked files
- [ ] Cookie panel UI (view / clear per-domain / clear-all)
- [ ] Graceful degradation on malformed `Set-Cookie`
- [ ] Focused tests

### Validation

- [x] `dotnet build` clean (SwebKit.App, net10.0-windows, 0 errors)
- [x] `dotnet test` (App) \u2014 all `ApiClient*` tests green (9/9). Full-suite run shows ~10 unrelated
      failures (`RedisKeyDetail`, `ShellFoundation`/`TopBar`, `AlertMonitor`, `AksPageBatch`,
      `ObservabilityPage`, `MessageListView`, `ServiceBusConfigForm`) that are pre-existing bUnit
      test-runner flakiness under parallel execution \u2014 confirmed by re-running the same tests in
      isolation, where they pass cleanly. None touch API Client code.
- [ ] Aikido full scan on new/modified code — MCP server unavailable this session; run manually
      before merge
- [x] Docs updated (architecture `functionalities/api-client.md`)

## Future work (not required for this feature)

- Phase 4 (cookie jar) — deferred per maintainer decision; scope unchanged in `backend.md` /
  `frontend.md` / `index.md` for whoever picks it up.
- Automated (bUnit) coverage for `ApiClientPage`/`ApiClientWorkspace`/`ApiClientRequestWorkspace`
  is blocked on the MAUI `FilePicker` test-project reference issue; revisit if that's resolved.
