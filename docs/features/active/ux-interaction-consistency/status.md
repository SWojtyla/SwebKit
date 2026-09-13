---
status: Proposed
---

# App-Wide UX & Interaction Consistency — Status

- **Current phase:** Proposed — plan complete, no implementation started.
- **Reported by:** Sebastien: AKS rows need a right-click to do anything (no left-click
  affordance); Redis isn't collapsed by default. Asked for a full, in-depth pass across every
  feature, weighted toward AKS as the most-used one.
- **Implementation PR:** none yet.

## Batch status

| Batch | Units | Status |
| --- | --- | --- |
| 0 — Shared infrastructure | 0.1–0.6 | Not started |
| 1 — AKS | 1.1–1.8 | Not started |
| 2 — Redis | 2.1–2.4 | Not started |
| 3 — Service Bus | 3.1–3.5 | Not started |
| 4 — API Client | 4.1–4.5 | Not started |
| 5 — Monitoring | 5.1–5.4 | Review |
| 6 — Storage | 6.1–6.6 | Not started |
| 7 — Agent | 7.1–7.7 | Not started |
| 8 — Settings | 8.1–8.9 | Not started |
| 9 — Dashboard | 9.1–9.3 | Not started |

Update each unit's row to `In progress`/`Review`/`Done` (with a PR link) as work lands, per the
Worker instructions in `technical-plan.md`.

## Definition of Done

- [ ] Batch 0 (shared infrastructure) merged — every later batch's "via Batch 0.X" units depend on
      this landing first.
- [ ] Batch 1 (AKS) merged and manually verified against a real cluster — highest priority per the
      user, since AKS is the most-used feature.
- [ ] Batches 2–3 (Redis, Service Bus) merged.
- [ ] Batches 4–6 (API Client, Monitoring, Storage) merged — Storage units 6.1/6.2 are correctness
      bugs (data corruption, wrong navigation labels) and should not wait on the rest of the batch
      ordering if they can land sooner.
- [ ] Batches 7–9 (Agent, Settings, Dashboard) merged.
- [ ] Every unit passed its own worker verification (`tsc -b`, lint, unit tests, targeted e2e,
      manual exercise, Aikido scan) before merge.
- [ ] Manual verification checklist in `test-plan.md` completed — **owner: Sebastien** (cross-
      cluster shell safety, binary download integrity, live Test Connection, Agent tool-progress).
- [ ] `docs/features/README.md` updated to move this feature to `archive/` once done, per the
      existing repo convention.

## Notes

- This plan does not overlap `aks-log-parity`, `redis-entra-auth`, `api-client-variable-scoping`,
  or `settings-save-performance` (all in Review) — see `index.md`'s "Relationship to in-flight
  work." If any of those merge changes to a file a unit here also touches before that unit starts,
  re-read the current file state before implementing rather than assuming the audit's line numbers
  still match exactly.
