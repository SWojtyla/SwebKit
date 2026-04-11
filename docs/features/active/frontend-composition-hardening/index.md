# Feature Overview - frontend-composition-hardening

---

title: "Feature Overview - frontend-composition-hardening"
owner: "GitHub Copilot"
status: "Planned"
jira: "not linked"
created: "2026-04-11"
updated: "2026-04-11"

---

## Goal

Reduce frontend change cost and hidden failure modes by introducing thin composition seams for the heaviest operational pages, replacing timing-based page coordination, and making shell failures visible and testable without redesigning current workflows. Preserve a snappy, reactive UI while this hardening lands: shell startup, page refresh, drill-through, and reconnect paths should remain interactive, show progress locally, and avoid freezing the visible surface during loading.

## Value

SwebKit already has solid component-level patterns, useful page caching, and good lifecycle and cancellation awareness. The biggest maintainability pressure is now at the top of the page stack: `AksPage`, `ServiceBusPage`, and `ObservabilityPage` still mix UI state, infrastructure construction, and async coordination inside the Razor page itself. `MainLayout` also treats important shell failures as console-only best effort, which makes regression diagnosis harder and leaves operators without a clear signal when startup behavior degrades.

This feature keeps the current user experience intact while reducing refactor risk for future work on Observability, AKS, and Service Bus. It also protects operator trust in the desktop shell: internal composition cleanup must not make the app feel slower or more stalled while async work is in flight.

## Scope

- In scope:
- Add thin, additive composition seams for Observability provider creation, Service Bus namespace connection, and AKS client bootstrap.
- Replace page-owned concrete construction of `AzureAppInsightsProvider`, `AzureServiceBusClient`, and `KubernetesAksClient` in the scoped frontend paths.
- Replace the Observability failure-to-logs drill-through delay with an explicit render-ready handoff.
- Replace shell console-only async error handling with a shared surfaced error path plus structured logging.
- Make non-blocking responsiveness an explicit constraint for the scoped shell, Observability, Service Bus, and AKS paths. Long-running bootstrap or refresh work should use local loading states, cached snapshots, or background reconnect behavior instead of blocking the whole page.
- Add shell and page-composition regression coverage around `MainLayout`, service registration, request cancellation, and drill-through behavior.
- Preserve existing routed pages, current UX structure, `SwebKitComponentBase` behavior, `PageDataCache` snapshot behavior, and lifecycle or cancellation awareness.
- Out of scope:
- New incident or investigation workflows.
- Visual redesign, layout refresh, or navigation IA changes.
- Full decomposition of AKS detail panels or Service Bus workspace components.
- Broad cleanup of Dashboard, Storage, Redis, Pipelines, or Settings beyond shared shell patterns.
- Broad rendering-performance work such as virtualization, whole-app repaint tuning, or a generalized loading-state redesign.
- Replacing every direct client construction in the app in one pass.

## Implementation waves

- Wave 1 - Shared seams and shell hardening.
- Define the small contracts needed to stop Razor pages from owning infrastructure construction.
- Introduce a shell error presentation path for background initialization and JS registration failures.
- Wave 2 - Observability hardening.
- Move provider activation behind an injected seam.
- Replace timing-based failure-to-logs coordination with explicit readiness signaling.
- Wave 3 - Service Bus and AKS bootstrap hardening.
- Move namespace and client bootstrap logic behind injected seams while preserving current page behavior.
- Add composition-level regression tests and align touched docs.

## Dependencies

- Architecture constraints remain anchored to `docs/architecture/architecture.md`, `docs/architecture/design.md`, and `docs/architecture/codebase-guide.md`.
- The feature depends on `src/SwebKit.App` for page orchestration, `src/SwebKit.Core` for additive abstractions, and the existing integration projects for real client implementations.
- Relevant pitfalls are `docs/pitfalls/blazor-maui.md`, `docs/pitfalls/dotnet-csharp.md`, and `docs/pitfalls/agent-workflow.md`.
- Functionality docs likely to need updates during implementation are:
- `docs/architecture/functionalities/observability.md`
- `docs/architecture/functionalities/service-bus.md`
- `docs/architecture/functionalities/aks.md`

## Risks & mitigations

- Risk: the work balloons into a whole-app frontend rewrite.
- Mitigation: keep scope limited to `MainLayout`, Observability, Service Bus, AKS bootstrap, and related tests.
- Risk: factory extraction drifts away from the documented architecture.
- Mitigation: keep page orchestration in `SwebKit.App`, keep additive creation contracts in `SwebKit.Core`, and keep concrete implementations out of Razor pages.
- Risk: new shell error surfacing becomes noisy.
- Mitigation: only route actionable failures to the shared UX surface and keep detailed diagnostics in `ILogger` output.
- Risk: test seams become MAUI-specific and brittle.
- Mitigation: favor small injected coordinators and factories plus bUnit-friendly doubles over UI-thread dependent logic.
- Risk: seam extraction accidentally adds heavier blocking loaders or extra await chains that make the UI feel less fluid.
- Mitigation: prefer progressive hydration, last-request-wins cancellation, cached snapshot reuse where already present, and targeted local busy states instead of full-page blocking waits.

## Related documents

- Architecture map: `docs/architecture/architecture.md`
- Component design: `docs/architecture/design.md`
- Code navigation: `docs/architecture/codebase-guide.md`
- Baseline active feature style: `docs/features/active/incident-timeline-workbench/index.md`
- Pitfalls index: `docs/pitfalls/index.md`

## Quick links

- Jira: not linked
- Status: `status.md`
- Tests: `test-plan.md`
- Implementation modules: `frontend.md`, `decisions.md`
