# Status — Status Bar Improvements

---

title: "Status - Status Bar Improvements"
owner: ""
state: "Planned"
branch: ""
started: ""
last_updated: "2026-03-21"

---

## Quick summary

Current state: Planned — feature scoped, awaiting implementation start.

## Progress checklist

- [x] Planning complete
- [ ] Design reviewed
- [ ] Frontend implementation
- [ ] Tests (unit / manual)
- [ ] Docs aligned
- [ ] Ready for review

## Completed

- Feature scoped in `index.md`

## Remaining

- Author `frontend.md` with component breakdown
- Author `test-plan.md`
- Design `IConnectionStateService` (or lightweight event convention) for broadcasting service health
- Implement environment name + prod badge in status bar
- Implement per-service connection state indicators
- Implement last refresh timestamp (wired to `RefreshRequestedEvent`)
- Implement port-forward session count (depends on `aks-port-forward-sessions` feature)
- Refine existing background task indicator (name alongside spinner)
- Manual verification: prod badge, connection states, refresh timestamp update

## Blockers

- Port-forward count depends on `aks-port-forward-sessions` feature being implemented first (or concurrently).

## Validation

Not started.
