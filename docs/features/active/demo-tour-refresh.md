# Demo tour refresh — full-surface walkthrough with try-it hints

State: In Progress

## Goal

The guided tour was 11 passive stops, ended on the "PRD day" story the user
wanted gone, and predates multi-context AKS, the AI reports kanban, and the
typed Service Bus filters. Rebuild it as a real new-user tour: one or two
stops per surface, copy written for onboarding, optional "Try it" hints.

## Decisions (from user)

- "Try it" hints are suggestions — **Next is never gated** (no wait-for-action).
- Drop both PRD-day stops entirely. `demo-sql-prd` + the Access report stay
  as a feature (Settings → Access) — just not tour content.
- Full scope (~16 stops), including the recently shipped surfaces.

## Scope

- `web/src/lib/stores/demo-tour.ts` — `DemoTourStep.tryIt?: string`; drops
  `scenarioId`/`connection` (only the PRD steps used them). 16 stops:
  cockpit, command palette, AKS (contexts / namespaces / row convention /
  logs-shell-PF), Service Bus (explorer / typed filters / DLQ), Redis,
  Storage, SQL, API Client, Agent, Monitoring kanban, wrap-up.
- `web/src/components/layout/DemoTour.tsx` — renders `demo-tour-try-it`
  hint block; Escape stops the tour.
- `web/e2e/dashboard.spec.ts` — updated title assertions; replaced the
  PRD-day test with a full 16-stop route walk + Escape exit.
- `.agents/skills/testing-swebkit-demo-tour/SKILL.md` — route table +
  assertion notes updated.

## Validation

- `tsc --noEmit` clean.
- `e2e/dashboard.spec.ts` — tour tests green (start/next, row-convention,
  full 16-stop walk + Escape). Suite run had one unrelated flake in
  "queries the configured AKS namespace" which passes in isolation.
