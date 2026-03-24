# Status — Performance Improvements

---

title: "Status — Performance Improvements"
owner: ""
state: "Planned"
branch: ""
started: ""
last_updated: "2026-03-24"

---

## Quick summary

Plan complete. No implementation started yet.

**Current focus:** Review plan, then begin Wave 0 (app startup non-blocking init).

## Progress checklist

- [x] Planning complete
- [ ] Design reviewed
- [ ] Wave 0 — App startup & MainLayout (PERF-1 to PERF-4)
- [ ] Wave 1 — Async event bus (PERF-5, PERF-6)
- [ ] Wave 2 — Per-page loading optimization (PERF-7 to PERF-12)
- [ ] Wave 3 — Loading UX / skeleton screens (PERF-13 to PERF-16)
- [ ] Wave 4 — Navigation state caching (PERF-17, PERF-18)
- [ ] Tests (unit / integration)
- [ ] Docs aligned
- [ ] Ready for review

## Completed

- Feature plan created with full item breakdown

## Remaining

- All implementation waves (0–4)
- QOL prerequisite: UI-8 (error boundary) and UI-9 (skeleton loaders) should land first or in parallel with Wave 0
- Test coverage for async event bus and caching
- Performance baseline measurement (before/after)

## Blockers

- None — plan is self-contained. UI-8/UI-9 from QOL are soft prerequisites (recommended but not blocking).

## Validation

- Test Plan: [test-plan.md](./test-plan.md)
- Validation status: Not started

## Implementation waves

| Wave  | Items                                             | Priority  | Dependencies                |
| ----- | ------------------------------------------------- | --------- | --------------------------- |
| **0** | PERF-1, PERF-2, PERF-3, PERF-4                    | 🔴 HIGH   | None (do first)             |
| **1** | PERF-5, PERF-6                                    | 🔴 HIGH   | None (parallel with Wave 0) |
| **2** | PERF-7, PERF-8, PERF-9, PERF-10, PERF-11, PERF-12 | 🟡 MEDIUM | Wave 0                      |
| **3** | PERF-13, PERF-14, PERF-15, PERF-16                | 🟡 MEDIUM | UI-9 from QOL, Wave 0       |
| **4** | PERF-17, PERF-18                                  | 🟡 MEDIUM | Wave 0                      |

## Notes

- No Jira ticket — local planning exercise
- Measure perceived load time before/after each wave to track impact
