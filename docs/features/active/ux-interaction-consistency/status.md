---
status: Review
---

# App-Wide UX & Interaction Consistency — Status

- **Current phase:** Review — all 10 batches (58 units) implemented and merged into branch
  `ux-interaction-consistency`, local only (no push, no PR yet, per instruction). Full-repo
  verification passed after every merge (see Definition of Done).
- **Reported by:** Sebastien: AKS rows need a right-click to do anything (no left-click
  affordance); Redis isn't collapsed by default. Asked for a full, in-depth pass across every
  feature, weighted toward AKS as the most-used one. Then asked to implement the plan fully.
- **Implementation PR:** none yet — local branch only.

## Batch status

| Batch | Units | Status |
| --- | --- | --- |
| 0 — Shared infrastructure | 0.1–0.6 | Done — commit `abd5a6e` |
| 1 — AKS | 1.1–1.8 | Done — commits `0d38176`, `e476925` |
| 2 — Redis | 2.1–2.4 | Done — merged (commit `ba1a04b`) |
| 3 — Service Bus | 3.1–3.5 | Done — merged (commit `dcc3cd4`) |
| 4 — API Client | 4.1–4.5 | Done — merged (commit `a1ccb54`) |
| 5 — Monitoring | 5.1–5.4 | Done — merged (commit `31beb3d`) |
| 6 — Storage | 6.1–6.6 | Done — merged (commit `6ed1874`) |
| 7 — Agent | 7.1–7.7 | Done — merged (commit `cb5cb5e`); Aikido scan not run (MCP unavailable this session) |
| 8 — Settings | 8.1–8.9 | Done — merged (commit `f6a27d4`) |
| 9 — Dashboard | 9.1–9.3 | Done — merged as `52587d4` (commit `c9160ee`) |

Batches 2–8 are each being implemented in their own git worktree (`.claude/worktrees/agent-*`) by
a background agent, then merged into this branch once verified. Update each unit's row to
`In progress`/`Review`/`Done` (with a commit hash) as work lands, per the Worker instructions in
`technical-plan.md`.

## Definition of Done

- [x] Batch 0 (shared infrastructure) merged — every later batch's "via Batch 0.X" units depend on
      this landing first.
- [x] Batch 1 (AKS) merged — highest priority per the user, since AKS is the most-used feature.
      Automated verification (tsc/lint/unit/e2e/Rust) complete; manual verification against a real
      cluster is still owner Sebastien's (see checklist below — this needs live infra this session
      doesn't have).
- [x] Batches 2–3 (Redis, Service Bus) merged.
- [x] Batches 4–6 (API Client, Monitoring, Storage) merged — Storage units 6.1/6.2 (the binary
      download corruption and breadcrumb correctness bugs) both fixed and unit-tested.
- [x] Batches 7–9 (Agent, Settings, Dashboard) merged.
- [x] Every unit passed its own worker verification (`tsc -b`, lint, unit tests, targeted e2e)
      before merge, plus a second full-repo verification pass after each merge into this branch:
      `npx tsc -b`, `npm run lint` (stable at 26 errors / 98 warnings — the pre-existing baseline,
      zero new error classes introduced across all 58 units), `npm run test:unit` (398/398),
      `dotnet test` across `SwebKit.Core.Tests` (916), `SwebKit.Sidecar.Tests` (331), and
      `SwebKit.Agents.Tests` (201) — all passing, plus every touched Rust crate building and its
      `cargo test --lib` suite passing. The full Playwright e2e suite (308 tests, every spec) was
      run twice as a final cross-batch check: the first run caught one real regression — Batch 5's
      new alert-rule Save validation (unit 5.3) broke a pre-existing test in
      `contextual-assistant.spec.ts` that Batch 5's own agent had no way to know about, since it
      lives outside `monitoring.spec.ts` — fixed (commit `70d5aeb`) the same way
      `monitoring.spec.ts`'s own `createRule()` helper already had been; the second full run passed
      308/308 clean.
  - [ ] Aikido security scan (`docs/security/aikido-mcp-scan.md`) — **not run**, the MCP server
        isn't connected in this session (per your own earlier answer, skipped for this pass).
        Worth running before this goes anywhere it matters (a real PR, a shared branch).
- [ ] Manual verification checklist in `test-plan.md` completed — **owner: Sebastien** (cross-
      cluster shell safety, binary download integrity, live Test Connection, Agent tool-progress —
      all need real infra/credentials this session doesn't have).
- [ ] `docs/features/README.md` updated to move this feature to `archive/` once done, per the
      existing repo convention — hold off until the manual checklist above and your own review of
      the diff are done, since "done" here means automated verification only.

## Notes

- This plan does not overlap `aks-log-parity`, `redis-entra-auth`, `api-client-variable-scoping`,
  or `settings-save-performance` (all in Review) — see `index.md`'s "Relationship to in-flight
  work." If any of those merge changes to a file a unit here also touches before that unit starts,
  re-read the current file state before implementing rather than assuming the audit's line numbers
  still match exactly.
