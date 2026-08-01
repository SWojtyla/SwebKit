# Status — Tauri + React as the Primary Tool

## Current State

`Proposed`

**Jira:** not linked

## Progress

- [x] Research complete: 6 parallel passes (existing-docs synthesis, web app inventory, MAUI
      parity gap analysis, sidecar code-quality audit, test-coverage/packaging audit, hands-on UX
      review of the running app)
- [x] `production-readiness-review.md` written — consolidated findings, ranked
- [x] `ux-plan.md` written — phased UX/UI/functionality plan
- [x] `technical-plan.md` written — phased clean-code/architecture/performance/security plan
- [x] `test-plan.md` written — phased test-coverage and CI plan
- [ ] Docs-hygiene fixes (technical-plan.md Module 1) — not started
- [ ] Phase 0 critical bug fixes (ux-plan.md) — not started
- [ ] Phase 1: land uncommitted work (`monitoring-rebuild`, `api-client-git-completion`,
      `api-client-ux-overhaul`) — not started
- [ ] Everything else — not started

## Verification

Not yet applicable — no implementation work has started. This section will track build/test results
once Phase 0 fixes begin landing, following the same format as other active features' `status.md`.

## Definition of Done

1. Every critical/high-severity bug found in `production-readiness-review.md` §3 is fixed and has a
   regression test.
2. Every feature-parity gap rated "Important" in §4 is closed or explicitly re-classified as an
   intentional substitution with a documented rationale.
3. The three uncommitted-but-complete features (`monitoring-rebuild`, `api-client-git-completion`,
   `api-client-ux-overhaul`) are committed, manually verified in the real Tauri shell, and merged.
4. Crash telemetry, sidecar file logging, and sidecar crash recovery are all in place
   (`technical-plan.md` Module 6).
5. CI runs Vitest and Rust checks in addition to its current .NET/Playwright coverage.
6. The sidecar's endpoint/service test coverage matches the standard already proven on
   `ApiClientEndpointsPreviewTests.cs` for all 9 endpoint files.
7. `docs/architecture/architecture.md`/`design.md` describe the Tauri+React+sidecar system as
   primary, and all dangling doc references identified in this review are resolved.
8. A packaging decision (code signing, auto-update, app identifier) is made and implemented or
   explicitly deferred with a reason, not left as an unaddressed placeholder.
