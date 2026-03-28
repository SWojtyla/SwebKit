# Backend Plan - <feature-name>

---

title: "Backend Plan - <feature-name>"
owner: ""
status: "Not started"

---

## Goal

Describe the backend outcome: what changes, what it enables, and any non-functional expectations (throughput, latency, reliability).

## Impacted areas

- Projects / services: `src/...`
- Databases, queues, caches

## Design

Describe the approach: key types, services, or patterns involved. Reference the relevant section of `docs/architecture/design.md` if it applies. Note any divergence from existing patterns and why.

## API / Contracts

- API endpoints, messages, DTOs, and schema changes
- Backwards compatibility notes

## Tasks

- [ ] Define/update contracts and interfaces
- [ ] Implement domain logic
- [ ] Implement infrastructure/persistence changes
- [ ] Add/update error handling
- [ ] Add/update logging & telemetry
- [ ] Add/update unit & integration tests
- [ ] Record key design choices in `decisions.md` _(if decisions exist)_

## Migration and runtime changes

_(Omit if not applicable.)_

- Migration steps: config, data, or schema changes required
- Operational runbook: what to do on deploy or rollback

## Validation

- Unit tests: Not started / In progress / Passed
- Integration tests: Not started / In progress / Passed
- Manual checks: list of acceptance steps

## Notes

- Important implementation notes, performance considerations
