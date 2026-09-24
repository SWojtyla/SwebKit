# Status — AI Insight Reports

**Status:** Review

## Changes

- `src/SwebKit.Core/Models/MonitoringModels.cs` — `ProactiveInsightReport`, `ProposedFix` models.
- `src/SwebKit.Core/Configuration/AppDataPaths.cs` — `MonitoringInsightsJson` path.
- `src/SwebKit.Core/Abstractions/IProactiveInsightReportRepository.cs` + `Configuration/ProactiveInsightReportRepository.cs` — JSON-file persistence, newest-first, capped at 100.
- `src-sidecar/Services/ProactiveInvestigationRunner.cs` — `proposed_fix` in the output contract + parsing; tightened instructions.
- `src-sidecar/Services/AgentSystemPromptBuilder.cs` — `forBackgroundInvestigation` variant.
- `src-sidecar/Services/ProactiveInsightService.cs` — persists each report; formats the seeded session as markdown; `EnsureSession` re-seeds from the persisted report.
- `src-sidecar/Services/SidecarAgentChatService.cs` — seed accepts pre-formatted content; `GetSessionMessages` transcript accessor.
- `src-sidecar/Program.cs` — `IProactiveInsightReportRepository` DI registration.
- `src-sidecar/Endpoints/MonitoringEndpoints.cs` — insights list/delete/open-chat endpoints.
- `web/src/lib/api.ts` + `hooks/useMonitoring.ts` — report types, list/delete/open-chat calls.
- `web/src/components/monitoring/` — `AiReportsPanel`, `AiReportDetail`, third tab, card → "View report".
- `web/src/lib/hooks/useContextualAgent.ts` + `components/agent/ContextualAssistant.tsx` — fixed `sessionId`, `initialMessages`, `defaultScope` props.
- `web/src/components/monitoring/aiReportFormat.ts` (+`.test.ts`) — severity badge/label + time formatting.
- `web/e2e/monitoring.spec.ts` — 6 new AI Reports tests; updated the old "Investigate → /agent" test for the new deep-link behavior.
- `tests/SwebKit.Sidecar.Tests/ProactiveInsightServiceTests.cs` — persistence + `EnsureSession` tests.
- `tests/SwebKit.Sidecar.Tests/ProactiveInvestigationRunnerTests.cs` — `proposed_fix` parsing + background prompt tests.
- `tests/SwebKit.Sidecar.Tests/AgentSystemPromptBuilderTests.cs` — background-variant prompt tests.
- `tests/SwebKit.Core.Tests/ProactiveInsightReportRepositoryTests.cs` — repository round-trip/order/cap/delete.

## Validation

All executed and green:

- `dotnet build` (src-sidecar): 0 warnings, 0 errors.
- `dotnet test` SwebKit.Sidecar.Tests: **526/526 passed** — new coverage for report
  persistence, `EnsureSession` re-seed, `proposed_fix` parsing (object/null/bare-string),
  and the background prompt variant.
- `dotnet test` SwebKit.Core.Tests: **1050/1050 passed** — new
  `ProactiveInsightReportRepositoryTests` (round-trip, newest-first, upsert, cap, delete).
- `npm run build` (web): tsc + vite clean.
- `npm run test:unit` (web): **502/502 passed** — incl. `aiReportFormat.test.ts`.
- `npx playwright test e2e/monitoring.spec.ts`: **33/33 passed** — 6 new AI Reports
  tests (list/detail, error, empty, card → deep-link, delete confirm, discuss-in-chat).
- Aikido `aikido_scan_paths` on all 26 touched files: no findings in changed code
  (2 pre-existing hits in untouched regions of `AppDataPaths.cs`/`api.ts`; checkov
  IaC scanner unavailable on this machine — install issue, not a finding).

Not yet run: manual smoke of a real investigation end-to-end against a live cluster.
