# API Client UX Refactor

## Goal

Address four post-review findings on the archived API Client foundation so it feels like a
daily-driver tool rather than a capable prototype:

1. **Optional request tabs** — let users keep several requests open at once, behind a toggle so
   the current single-request model stays available for those who prefer it.
2. **Consistent iconography** — replace ad-hoc emoji and literal ASCII glyphs with a single
   `FluentIcon` vocabulary across the API Client surface.
3. **Cookie jar (deferrable)** — automatically store `Set-Cookie` responses and replay them on
   same-domain requests, so login → authenticated-call flows work without hand-copying cookies.
4. **`ApiClientPage.razor` refactor** — the page is 2,198 lines with all state owned in one
   component. Extract an owned state container and presentational child components to cut the
   regression surface (splitter/targeting bugs already trace back to this coupling).

## Value

The engineering underneath the API Client is strong (secret-by-reference, Git-linked collections,
script-free capture). These four items are the ergonomic and maintainability layer that decides
whether someone actually switches to it from Postman/Bruno. Tabs and cookie handling close the two
biggest daily-workflow gaps; the icon pass removes the "prototype" feel; the refactor is the
enabler that makes tabs tractable and future work safer.

## Scope

### Phase 1 — Iconography unification (independent, low risk)

- Replace all emoji and literal ASCII glyphs in the API Client components with `FluentIcon`.
- Establish a small, reused icon set (chevrons, close, dirty, warning, environment, subscription,
  size, timing) so future components stay consistent.
- No behavioural change; purely presentational.

### Phase 2 — `ApiClientPage.razor` refactor (foundation)

- Introduce an owned state container for API Client page state (active request, dirty tracking,
  collections/environments, linked roots, worksheet mode, conflict/message banners).
- Extract presentational child components from the monolith while keeping **parent/state-owned**
  data flow (per the archived lesson: large Blazor workspaces need parent-owned state).
- No user-visible behavioural change; validated by existing tests plus manual smoke.

### Phase 3 — Optional request tabs (opt-in toggle)

- Add a user setting `ApiClientRequestTabs` (default **off** = today's single-request model).
- When enabled, an open-requests tab strip sits above `RequestBuilderPanel`; each tab carries its
  own dirty state, in-flight cancellation, and editor lifecycle.
- Built on the Phase 2 state container so per-tab state is managed cleanly, not bolted onto the
  page fields (this is exactly the complexity DEC-11 flagged as "add later without structural
  changes").

### Phase 4 — Cookie jar (deferrable)

- Add an opt-in per-domain cookie store applied in the request execution path.
- Provide a minimal UI to view and clear stored cookies per domain.
- Secret-safe: cookies follow the existing secret-scrubbing rules for exports, cURL, examples,
  diffs, and linked files.

## Non-Goals

- No pre-request scripting, arbitrary code execution, gRPC, mock servers, or hosted collaboration.
- No change to the secret-by-reference model, linked-root Git behaviour, or export formats beyond
  cookie scrubbing.
- Tabs do NOT revive request pinning or the removed active-collection runner.
- Cookie jar does NOT introduce a shared/synced cookie store; it is machine-local only.
- The refactor does NOT scatter page state into uncontrolled child fields; state stays owned.

## Dependencies

- Archived foundation: `docs/features/archive/api-client/summary.md`, `.../decisions.md` (DEC-11).
- Architecture: `docs/architecture/functionalities/api-client.md`, `docs/architecture/codebase-guide.md`.
- Existing components under `src/SwebKit.App/Components/ApiClient/`.
- Request execution in `src/SwebKit.Core` (`HttpRequestExecutor` / `IHttpRequestExecutor`).
- Domain models `src/SwebKit.Core/Domain/ApiClientModels.cs`.
- Settings via `UserSettingsRepository` (mirrors existing `VerifyApiClientSsl` toggle).
- Command registrations via `CommandRegistry` (send/new-request shortcuts must stay tab-aware).
- Relevant pitfalls: `docs/pitfalls/blazor-maui.md` (BL-2, BL-4, BL-5, BL-7), `docs/pitfalls/dotnet-csharp.md`.

## Risks & Mitigations

| Risk                                                               | Mitigation                                                                                                     |
| ------------------------------------------------------------------ | -------------------------------------------------------------------------------------------------------------- |
| Refactor introduces regressions in a large, stateful page          | Do the refactor as behaviour-preserving extraction; run full API Client test suite + manual smoke before tabs. |
| Extracting components resets state via `@if` destroy/create        | Keep state in an owned container, not child fields; use `display:none` where DOM must persist (BL-4).          |
| Tabs reintroduce concurrent-request race conditions DEC-11 avoided | Per-tab `CancellationTokenSource`; cancel on tab close/switch/dispose (BL-7); one in-flight send per tab.      |
| Tabs complexity leaks to users who did not want them               | Ship default-off toggle; single-request path unchanged when disabled.                                          |
| Cookie jar accidentally persists or leaks session secrets          | Treat cookies as secret-adjacent; scrub from exports/cURL/examples/diffs; machine-local storage only; opt-in.  |
| Icon swap changes layout/spacing                                   | Match existing glyph sizes; visual smoke pass per toolbar/tree/response surface.                               |
| Keyboard shortcuts (`Ctrl+S`, Send, `Ctrl+P`) break under tabs     | Route shortcuts through the active tab in the state container; add focused tests.                              |

## Related Documents

- Status: `status.md`
- Frontend module: `frontend.md`
- Backend module: `backend.md`
- Decisions: `decisions.md`
- Test plan: `test-plan.md`
- Adjacent active feature (orchestration, not this): `docs/features/active/api-client-advanced-workflows/`
