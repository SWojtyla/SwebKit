# Status — Redis TTL Visualisation

---

title: "Status - Redis TTL Visualisation"
owner: ""
state: "Planned"
branch: ""
started: ""
last_updated: "2026-03-21"

---

## Quick summary

Current state: Planned — feature scoped, awaiting implementation start. Contained to `RedisKeyDetail.razor`.

## Progress checklist

- [x] Planning complete
- [ ] Design reviewed
- [ ] Frontend implementation
- [ ] Tests (unit / manual)
- [ ] Docs aligned (`redis.md` updated)
- [ ] Ready for review

## Completed

- Feature scoped in `index.md`

## Remaining

- Author `frontend.md` with component design
- Author `test-plan.md`
- Implement human-readable TTL formatter (utility method)
- Implement expiry progress bar with colour thresholds
- Implement live client-side countdown (`PeriodicTimer`)
- Implement 30-second background TTL refresh to correct drift
- Implement "Set TTL" inline action (if not already present in `RedisKeyDetail`)
- Update `docs/architecture/functionalities/redis.md`

## Blockers

None.

## Validation

Not started.
