# Status — API Client UX Refactor

## Current State

`In Progress`

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
BL-4, not `@if`) so background-tab sends/subscriptions survive tab switches, and Ctrl+S / Send /
Ctrl+P shortcut routing to the active tab. Build is clean and focused tests were added for the
setting default, the `ApiClientOpenTab` POCO, the tab strip component, and the Ctrl+S/Ctrl+P
command contracts.
**Caveats (open items before Phase 3 can be considered fully done):** (a) per-tab splitter
drag-resize is **not** wired when tabs are ON — each tab renders a static, non-draggable divider;
only the OFF path retains working JS drag-resize. (b) Ctrl+W / Ctrl+Tab close/cycle shortcuts were
explicitly **not** implemented — those chords are already globally bound to app-level page-tab
navigation, and reusing them would silently override existing behaviour; this needs a maintainer
decision (e.g. rebind to different chords) before test-plan Phase 3 ON scenario #11 can land.
(c) `tests/SwebKit.App.Tests` cannot bUnit-render `ApiClientPage` / `ApiClientWorkspace` /
`ApiClientRequestWorkspace` (they transitively pull in the MAUI-only `FilePicker` API via
`RequestBuilderPanel` / `CollectionExportDialog` — a build-time reference issue, not a mocking
gap), so several ON-path scenarios are verified via code-trace only, not automated — see
test-plan.md Phase 3 verification note. (d) the Aikido MCP server was unavailable in all 8
implementation sessions for this phase — a manual Aikido full scan across all Phase 3 changed
files is still required before merge; flag prominently, do not let it get buried.
Next up: Phase 4 (cookie jar), gated on resolving the Phase 3 caveats above (Aikido scan at
minimum).

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
- [x] Per-tab dirty / cancellation / editor lifecycle (caveat: per-tab splitter drag-resize is
      not wired when tabs are ON — known gap, see Quick Summary)
- [ ] Shortcut routing to active tab (Ctrl+S / Send / Ctrl+P; Ctrl+W / Ctrl+Tab) — Ctrl+S / Send /
      Ctrl+P routed and tested; Ctrl+W / Ctrl+Tab deferred pending a maintainer decision on chord
      rebinding (see Quick Summary)
- [x] Focused tests (off-path unchanged; on-path behaviours) (caveat: several on-path scenarios
      are verified via code-trace only, not bUnit-automated, due to a test-project MAUI reference
      limitation — see Quick Summary and test-plan.md Phase 3 verification note)

### Phase 4 — Cookie jar (deferrable)

- [ ] `IApiCookieJar` / `ApiCookieJar` over `CookieContainer`
- [ ] Opt-in flag; execution-path integration (no global UseCookies)
- [ ] Secret scrubbing across exports / cURL / examples / diffs / linked files
- [ ] Cookie panel UI (view / clear per-domain / clear-all)
- [ ] Graceful degradation on malformed `Set-Cookie`
- [ ] Focused tests

### Validation

- [ ] `dotnet build` clean
- [ ] `dotnet test` (Core + App) green
- [ ] Aikido full scan on new/modified code
- [ ] Docs updated (architecture `functionalities/api-client.md`)
