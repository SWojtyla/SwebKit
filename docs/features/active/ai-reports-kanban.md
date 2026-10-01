# AI reports kanban + manual trigger

State: Review

## Goal

Two user-requested changes to proactive AI insight reports:

1. **Kanban board** for AI reports instead of a flat archive list.
   Columns: **Queued** → **Ready** → **Done**. In-flight investigations render
   as a spinner on their queued card, not a separate column.
2. **Manual trigger**: a new per-rule AI investigation mode — instead of
   spending tokens on every firing, the firing prepares the full context
   (alert + topology match + the deterministic `investigate_workspace_issue`
   probe — zero tokens) and parks a **Queued** card. The user clicks
   **Investigate** to actually run the model.

## Decisions (from user)

- Columns: Queued / Ready / Done.
- Manual-vs-auto is **per-rule** (Off / Auto / Manual), not global.
- Prepare runs the deterministic probe eagerly so the queued card already
  shows what was gathered.

## Scope

### Backend (`src/SwebKit.Core`, `src-sidecar`)

- `MonitoringAlertRule`: `AiInvestigationMode` (`Off`|`Auto`|`Manual`);
  legacy `AiInvestigationEnabled` bool coerced on load
  (`false`→Off, `true`→Auto) so persisted rules keep working.
- `ProactiveInsightReport`: + `Status` (`Queued`|`Ready`|`Done`, default
  Ready for legacy reports), + `Source`, + `AlertSeverity`, +
  `PreparedContextSummary` — enough to rebuild the `AlertFiredEvent` and run
  the investigation later without the firing event still being around.
- `ProactiveInsightService`:
    - Manual mode path: after the existing gates + episode claim, run the
      `investigate_workspace_issue` probe, persist a Queued report
      (`ReportJson` = probe output), raise `InsightStatus` with a new
      `Queued` stage. No model call.
    - `RunQueuedInsightAsync(id)`: loads the queued report, rebuilds the
      firing event, runs the model-driven investigation (single-flight
      `_busy` gate unchanged); on runner failure, drafts from the stored
      probe via the existing fallback path. Success flips Status to Ready
      and raises `InsightReady` as usual.
    - `SetInsightStatusAsync(id, status)` for Ready↔Done (+ Queued→Done as
      discard).
- Endpoints: `POST /api/monitoring/insights/{id}/run`,
  `PATCH /api/monitoring/insights/{id}` (`{status}`).
- SSE: `ProactiveInsightStage.Queued` added to the existing status event.

### Web

- `api/monitoring.ts`: `aiInvestigationMode`, `status`/`alertSeverity`/
  `preparedContextSummary` on the report type, `runInsight`,
  `updateInsightStatus`.
- `AlertRuleDialog`: checkbox → Off/Auto/Manual selector.
- `AiReportsPanel` → kanban: three columns; queued cards show the prepared
  context summary + Investigate (spinner while running) / Discard;
  ready/done cards keep the existing detail view; status moves via buttons
  (no dnd library in the repo).
- `MonitoringPage`: wire the `Queued` stage to invalidate the insights
  query and surface "prepared" feed entries.

### Tests

- .NET: queue-on-manual (probe ran, zero model calls), run→Ready,
  run-on-non-queued rejected, status PATCH validation, legacy mode coercion.
- vitest: status grouping helper; rule draft mode mapping.
- e2e: stubbed insights endpoint → columns render, Investigate POSTs,
  Mark done PATCHes.

## Non-goals

- Drag-and-drop (no dnd lib; buttons instead).
- Changing the auto-investigation pipeline itself (runner, prompts).
- Queued items for rules whose firing was suppressed/silenced — suppression
  still skips queueing entirely.

## Validation results

- `dotnet test tests/SwebKit.Sidecar.Tests`: 840 passed — incl. manual-mode
  queueing with zero model calls, probe-failure-still-queues, run→Ready,
  PATCH status validation, legacy mode coercion.
- `vitest`: 791 passed.
- e2e `monitoring.spec.ts` kanban section: 4/4 — column grouping/counts,
  queued detail + Investigate POST, Discard→Done PATCH, Ready↔Done moves.
- Aikido scan on all changed files: no findings.
