# Test Plan — AI Insight Reports

## Unit tests

- `tests/SwebKit.Core.Tests/ProactiveInsightReportRepositoryTests.cs`
  - empty when file missing; add → round-trip; newest-first ordering; 100-report cap trims oldest; delete; upsert-by-id.
- `tests/SwebKit.Sidecar.Tests/ProactiveInvestigationRunnerTests.cs`
  - `proposed_fix` object parsed into the result; absent/null → null; malformed → tolerated.
- `tests/SwebKit.Sidecar.Tests/AgentSystemPromptBuilderTests.cs`
  - background variant omits interactive response-format guidance and the "switch to Ask & do" line.
- `tests/SwebKit.Sidecar.Tests/ProactiveInsightServiceTests.cs`
  - completed investigation persists a report retrievable by id; open-chat re-seeds an evicted session and returns its history.

## Web

- `web/src/components/monitoring/aiReportFormat.ts` pure helpers (severity badge, timestamp) + `.test.ts`.
- e2e (`web/e2e/monitoring.spec.ts`): AI Reports tab renders (empty state acceptable — reports need a real investigation).

## Manual verification

- Fire a demo rule with AI investigation → report appears in AI Reports tab without reload.
- "View report" from the header card deep-links to the detail.
- "Discuss in chat" opens the contextual panel seeded with the report exchange; a follow-up question answers with report context.
- Restart sidecar → reports still listed; "Discuss in chat" still works (re-seed path).
