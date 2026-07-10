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
shipping/merging. Next up: Phase 3 (optional request tabs).

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

- [ ] Add `ApiClientRequestTabs` setting (default off)
- [ ] Settings UI toggle
- [ ] Open-tabs model in `ApiClientState`
- [ ] Tab strip component + open/focus/close behaviour
- [ ] Per-tab dirty / cancellation / editor lifecycle
- [ ] Shortcut routing to active tab (Ctrl+S / Send / Ctrl+P; Ctrl+W / Ctrl+Tab)
- [ ] Focused tests (off-path unchanged; on-path behaviours)

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
