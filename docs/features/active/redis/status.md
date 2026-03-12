# Status - Redis Manager

---

title: "Status - Redis Manager"
owner: ""
state: "Planned"
branch: ""
started: ""
last_updated: "2026-03-12"

---

## Quick summary

Redis management module: connection management with aliasing, key browsing via SCAN, inspection, inline editing, TTL management, bulk delete, flush, server info dashboard.

**Current focus:** Planning complete — ready for implementation.

## Progress checklist

- [x] Planning complete
- [ ] Design reviewed
- [ ] Backend implementation
- [ ] Frontend implementation
- [ ] Tests
- [ ] Docs aligned
- [ ] Ready for review

## Completed

- Feature proposed, scoped, and finalized.
- Scope decided: MVP includes key editing, TTL management, server info dashboard, and connection aliasing.
- Backend plan (`backend.md`): `IRedisClient` interface, `RedisClient` (StackExchange.Redis), `DemoRedisClient`, models, `RedisConfig` domain model.
- Frontend plan (`frontend.md`): `RedisPage`, `RedisKeyList`, `RedisKeyDetail`, `RedisServerInfo`, `RedisConfigForm` components. Layout, states, and reusable component strategy defined.
- Test plan (`test-plan.md`): 19+ DemoRedisClient tests, helper tests, demo keyspace data, manual check scenarios.
- Decision records (`decisions.md`): StackExchange.Redis, SCAN-only browsing, production guard, connection string storage.

## Remaining

- Implementation: backend (new `SwebKit.Redis` project, interfaces, models, demo client).
- Implementation: frontend (page, components, settings form, nav item).
- Unit tests.
- Manual validation.

## Blockers

- None.

## Validation

- Test Plan: `test-plan.md`
- Validation status: Not started

## Notes

- Follow AKS patterns: config per environment, abstraction interface + demo client, production guards for destructive operations.
- Reuse shared components: `ResizablePanel`, `AutoRefreshToggle`, `ConfirmBar`, `ContextMenu`, `LoadingSpinner`.
- `StackExchange.Redis` added to new `SwebKit.Redis` project only — no dependency leakage into Core.
