# Status — API Client UX Refactor

## Current State

`Planned`

## Quick Summary

Plan created from a post-review of the archived API Client foundation. Four workstreams: FluentIcon
cleanup, a behaviour-preserving `ApiClientPage.razor` refactor, optional request tabs behind a
default-off toggle, and a deferrable secret-scrubbed cookie jar.

**Jira:** not linked

**Current focus:** Confirm scope and sequencing (icons → refactor → tabs → cookie jar) before
implementation.

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

- [ ] Audit all emoji / ASCII glyphs across API Client components
- [ ] Map each to a FluentIcon
- [ ] Replace glyphs; align sizes to avoid layout shift
- [ ] Extend `EmptyState` for FluentIcon if needed
- [ ] Visual smoke pass; build clean (no new RZ10012)

### Phase 2 — Refactor

- [ ] Define `ApiClientState` container (page-scoped)
- [ ] Extract `ApiClientToolbar.razor`
- [ ] Extract `ApiClientWorkspace.razor` (split host)
- [ ] Re-parent tree / builder / response / worksheet panels
- [ ] Preserve dispose / cancellation / conflict banners
- [ ] Full API Client test suite green; manual smoke

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
