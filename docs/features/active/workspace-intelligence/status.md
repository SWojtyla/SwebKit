# Workspace Intelligence — Status

Created 2026-08-02, on branch `feature/ai-augmented-app`, as a follow-on to `ai-augmented-app`
implemented on the same branch. Nothing implemented yet.

## Open decision (blocks nothing yet, but needs an answer before Modules 3-4 are final)

Application Insights/Observability as a correlation input was named in the original request but
conflicts with an earlier product decision dropping Observability from the Tauri+React rewrite. See
`index.md`'s "Decision needed" section. Current plan scope: AKS + Service Bus + Redis + Storage +
Monitoring's own alert rules (none of which include Application Insights today either).

## Modules (see technical-plan.md for detail)

Part A — correlation:
- [ ] Module 1 — Workspace topology data model + manual curation
- [ ] Module 2 — Heuristic relationship suggestions
- [ ] Module 3 — Cross-area correlation tool + workspace-wide escalation (depends on
      `ai-augmented-app` Modules 3-4)
- [ ] Module 4 — Proactive insights from Monitoring alerts (depends on Module 3)

Part B — context management (Module 5 can start independently/in parallel with `ai-augmented-app`):
- [ ] Module 5 — Token-aware context budgeting
- [ ] Module 6 — Reasoning trace + usage indicator (depends on Module 5)
- [ ] Module 7 — Local-model adaptive behavior (depends on Modules 5-6, and on `ai-augmented-app`
      Module 1's capability testing)

## Notes

- `MonitoringAlertEvaluationService.AlertFired` already exists with per-rule cooldown — confirmed by
  reading the source before writing this plan, not assumed.
- `AlertRuleSource` already covers exactly AKS/Service Bus/Redis/Storage, no Application Insights —
  confirmed the same way.
- `AgentChatStep` (MAUI-side reasoning trace type) already exists and is unused outside MAUI — Module
  6 reuses it rather than inventing a new trace shape.
