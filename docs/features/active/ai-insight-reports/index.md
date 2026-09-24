# AI Insight Reports

**Status:** In Progress

## Goal

Persist the output of background proactive investigations (workspace-intelligence Module 4) and
give them a permanent home in the Monitoring feature, instead of letting them evaporate into a
transient insight card and an in-memory chat session that idle-evicts after 30 minutes.

## Value

Today a completed AI investigation is reachable only through (a) a `ProactiveInsightCard` that
exists solely in the live SSE feed (dismissal persisted to sessionStorage) and (b) a seeded
`proactive-*` chat session in `AgentSessionStore` — in-memory, evicted after 30 min idle, and not
openable from any UI surface. Users get a "report ready" notification and then cannot find the
report anywhere.

## Scope

- New `ProactiveInsightReport` model + `ProactiveInsightReportRepository`
  (`monitoring-insights.json`, capped at 100 newest) following the `AlertRuleRepository` pattern.
- `GET /api/monitoring/insights`, `DELETE /api/monitoring/insights/{id}`,
  `POST /api/monitoring/insights/{id}/open-chat` (re-seeds the chat session from the persisted
  report if it was evicted, returns session history).
- New "AI Reports" tab on the Monitoring page (`?tab=reports`, `?report=<id>` deep-link): list +
  formatted detail (hypothesis, severity, evidence, next steps, proposed fix, tools used).
- Report-scoped chat: `ContextualAssistant`/`useContextualAgent` accept a fixed `sessionId` +
  initial messages, so "Discuss in chat" continues the actual seeded session.
- Investigation prompt improvements: background-investigation prompt variant drops the
  interactive-chat response-format/tool-policy boilerplate that contradicted the JSON-only
  output contract; new optional `proposed_fix` field carries a concrete corrected snippet
  (YAML/env var/connection string) when the root cause is a misconfiguration.
- Seeded chat session renders the report as readable markdown (sections + fenced code block)
  instead of `summary + raw JSON dump`.
- `ProactiveInsightCard` primary action becomes "View report" → deep-links to the new tab.

## Non-goals

- No session switcher on the global /agent page (report chat opens as a contextual panel).
- No changes to alert evaluation, rule CRUD, or the investigation tool loop itself.

## Links

- `src-sidecar/Services/ProactiveInsightService.cs` — pipeline that produces reports
- `src-sidecar/Services/ProactiveInvestigationRunner.cs` — model-driven investigation + JSON contract
- `web/src/components/monitoring/MonitoringPage.tsx` — tab host
