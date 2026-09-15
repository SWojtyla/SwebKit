# Agent Correlation — Status

## State

`Planned` — created 2026-09-15 via `swebiplan` after the "how smart will the ACP agent
be at correlating" analysis. Scope confirmed by the user: all modules below
(tier 1 fixes + tier 2 additions + the log-scanning extension).

## Modules

| # | Module | Surface | State |
| - | ------ | ------- | ----- |
| 1 | ACP capability parity in UI gates | frontend | Planned |
| 2 | Scope-fence transparency (prompt + bridge hints) | backend | Planned |
| 3 | Scope-escalation signal + retry affordance | backend + frontend | Planned |
| 4 | Thought/plan rendering in chat | frontend | Planned |
| 5 | `analyze_storage_health` composite tool | backend | Planned |
| 6 | Log/trace-derived relationship suggestions | backend | Planned |

## Notes

- Module 1 is a bug, not a feature: backend bypassed the stored-capability gate for
  ACP in commit `d50db85`; the UI gate (`ContextualAssistant.tsx` and anywhere else
  `capability === "Unknown"` gates agent UI) was not updated.
- Module 3 depends on Module 2's distinct out-of-scope error; without it there is no
  deterministic signal to key the affordance on. The local-model path needs nothing
  here — its tools list simply omits out-of-scope tools, so the model can't call them.
- Module 6 stays inside the existing "suggestions are user-confirmed" non-goal — it
  widens the evidence scan, not the trust level.
