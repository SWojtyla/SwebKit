# Roadmap - professional-devops-tool-roadmap

---

title: "Roadmap - professional-devops-tool-roadmap"
owner: "GitHub Copilot"
status: "Planned"

---

## Intent

This file is the master sequencing plan for the next phase of SwebKit. It does not own page-level or service-level implementation detail. Every concrete delivery slice still needs its own active feature folder.

## Roadmap rules

- Do not bypass wave order without recording a decision in `decisions.md`.
- Do not turn this roadmap into a backlog dump; only durable sequence and dependency information belongs here.
- Do not modify `incident-timeline-workbench` just to make the roadmap look complete.
- Any future wave-4 or wave-5 initiative must get its own `docs/features/active/<feature-name>/` folder before implementation starts.

## Sequencing principles

- Shell consistency before new surface area. Shared page chrome, status language, notifications, and safety cues should stabilize before more feature entry points are added.
- Navigation and workspace primitives before advanced workflows. Saved investigations, favorites, and search precision should exist before complex cross-page flows depend on them.
- Configuration and readiness before deeper investigation. Operators need to know what is configured, what credentials exist, and what Azure-facing flows are actually ready before incident and domain-depth features expand.
- Domain-depth work stays modular. Service Bus, AKS, Observability, Pipelines, Redis, and Storage depth should remain separate feature folders when prioritized.

## Delivery waves

### Wave 1 - Shell UX foundation

Primary feature: `docs/features/active/shell-ux-foundation/`

Outcome:

- Route-aware shell context instead of manually tracked area state.
- Navigation grouped by operator intent.
- Consistent page headers and stable `h1` behavior across routed pages.
- Trustworthy refresh, status, loading, error, empty-state, notification, and production-safety patterns.

Why this wave comes first:

- Every later feature needs reliable shell chrome and consistent operator cues.
- Current page structure is visibly inconsistent: `DashboardPage` uses a bespoke header, `ObservabilityPage` already uses `PageToolbar`, `PipelinesPage` still uses `h2`, and `SettingsPage` uses `h3` section titles.

Entry criteria:

- Architecture docs remain current.
- `shell-ux-foundation` plan is reviewed.

Exit criteria:

- Shell context can be derived from the current route.
- Core top-level pages use one consistent header and empty/error/loading pattern.
- Status bar, top bar, and notification center semantics are stable enough for later waves to build on.

Likely code and test areas:

- `src/SwebKit.App/Components/Layout/`
- `src/SwebKit.App/Components/Shared/`
- `src/SwebKit.App/Components/Pages/`
- `tests/SwebKit.App.Tests/`
- `tests/SwebKit.E2E.Tests/`

### Wave 2 - Operator navigation and workspaces

Primary feature: `docs/features/active/operator-navigation-and-workspaces/`

Depends on:

- Wave 1 shell primitives and page-header conventions.

Outcome:

- Higher-precision command palette and unified resource search.
- Shell-level recent and favorite resources.
- Named investigation workspaces that can restore route, resource, filter, and tab context across major operator pages.

Why this wave follows shell UX:

- Search results, recent resources, and saved workspaces need a stable shell context, consistent titles, and predictable route semantics.
- Saved investigation restore should target polished page headers and shell status signals rather than pre-foundation page variants.

Entry criteria:

- Wave 1 is implemented far enough that shell context and header conventions are stable.

Exit criteria:

- Command palette search is no longer a one-off `go ` branch.
- Favorites and recent resources use one canonical shell model.
- At least the Service Bus, AKS, Observability, and Incident Timeline pages can participate in a shared workspace model.

Likely code and test areas:

- `src/SwebKit.App/Components/Shared/CommandPalette.razor`
- `src/SwebKit.App/Services/CommandRegistry.cs`
- `src/SwebKit.App/Services/SelectionContext.cs`
- `src/SwebKit.App/Services/TabService.cs`
- `src/SwebKit.Core/Configuration/UiStateRepository.cs`
- `src/SwebKit.Core/Configuration/ProfileRepository.cs`
- `src/SwebKit.Core/Domain/AppConfig.cs`
- `tests/SwebKit.App.Tests/ComponentTests.cs`
- `tests/SwebKit.App.Tests/CommandRegistryTests.cs`
- `tests/SwebKit.Core.Tests/`

### Wave 3 - Environment and configuration health

Primary feature: `docs/features/active/environment-and-configuration-health/`

Depends on:

- Wave 1 for shared shell, status, CTA, and state patterns.
- Wave 2 sequencing for stronger shell navigation and handoff paths, even if the full workspace model is not required for every slice.

Outcome:

- First-run setup checklist.
- Credential and configuration health visibility.
- Connection-health overview and environment comparison.
- Explicit readiness summary for Azure-focused workflows.

Why this wave follows workspaces/navigation:

- Health and readiness views need polished cross-shell CTAs, consistent status language, and clear navigation handoff into configuration areas.
- The resulting readiness model then becomes a prerequisite for more ambitious incident and domain-depth workflows.

Entry criteria:

- Wave 1 shell patterns are available.
- The team has agreed which checks must be read-only and what health states are allowed.

Exit criteria:

- Operators can tell what is configured, what credentials are present, and which major Azure-facing flows are actually ready.
- Environment comparison can explain config drift without exposing secrets.
- The app surfaces a first-run path that does not require editing JSON files by hand.

Likely code and test areas:

- `src/SwebKit.App/Components/Pages/DashboardPage.razor`
- `src/SwebKit.App/Components/Pages/SettingsPage.razor`
- `src/SwebKit.App/Components/Shared/HealthTile.razor`
- `src/SwebKit.App/Platforms/Windows/WindowsCredentialStore.cs`
- `src/SwebKit.Core/Configuration/ProfileRepository.cs`
- `src/SwebKit.Core/Domain/AppConfig.cs`
- `src/SwebKit.Core/Services/ConnectionStateService.cs`
- `tests/SwebKit.App.Tests/`
- `tests/SwebKit.Core.Tests/`
- `tests/SwebKit.Azure.Tests/`
- `tests/SwebKit.Kubernetes.Tests/`
- `tests/SwebKit.DevOps.Tests/`

### Wave 4 - Incident workflow expansion

Primary feature:

- `docs/features/active/incident-investigation-workflows/`

Depends on:

- `docs/features/active/incident-timeline-workbench/`
- `docs/features/active/operator-navigation-and-workspaces/`
- `docs/features/active/environment-and-configuration-health/`

Outcome:

- Drill-through into Incident Timeline from Observability, Service Bus, and Pipelines.
- Correlation-aware handoff that preserves seed provenance without implying root cause.
- Snapshot export and mapping-proposal workflows that remain evidence-first and operator-confirmed.
- Later-wave watchlist and light-automation groundwork that stays deferred behind manual workflows.

Why this wave is fourth:

- Incident workflows benefit directly from polished shell context, saved workspaces, and explicit environment readiness.
- Expanding incident workflows earlier would force those missing foundations to be rebuilt inside the incident feature itself.

Entry criteria:

- `incident-timeline-workbench` is validated far enough in live environments that follow-on handoffs are worth hardening.
- Wave 2 route, selection, and workspace semantics are stable.
- Wave 3 readiness and configuration handoff patterns are available for investigation seeding.

Exit criteria:

- Observability, Service Bus, and Pipelines can all launch an evidence-backed incident investigation with explicit seed provenance.
- Incident snapshot export exists with explicit coverage and redaction rules.
- Mapping proposals remain operator-confirmed and are not silently persisted.

Likely code and test areas:

- `src/SwebKit.App/Components/Pages/IncidentTimelinePage.razor`
- `src/SwebKit.App/Components/Pages/ObservabilityPage.razor`
- `src/SwebKit.App/Components/Pages/ServiceBusPage.razor`
- `src/SwebKit.App/Components/Pages/PipelinesPage.razor`
- `src/SwebKit.Core/Services/IncidentTimelineService.cs`
- `src/SwebKit.Core/Domain/IncidentTimelineConfig.cs`
- `tests/SwebKit.App.Tests/`
- `tests/SwebKit.Core.Tests/`
- `tests/SwebKit.E2E.Tests/`

### Wave 5 - Domain-depth features

Wave-5 rule:

- Each domain-depth investment must be split into its own active feature folder.
- The roadmap may sequence them, but their detailed scope remains in their own feature folders.

#### Wave 5A - Deployment and messaging assurance

Primary features:

- `docs/features/active/pipelines-deployment-assurance/`
- `docs/features/active/service-bus-operator-workbench/`

Why first inside Wave 5:

- These features close high-value operator loops around deployment trust and message triage that benefit immediately from the earlier shell, workspace, readiness, and incident foundations.

#### Wave 5B - Runtime diagnostics depth

Primary features:

- `docs/features/active/aks-runtime-diagnostics-depth/`
- `docs/features/active/observability-explainer-and-reliability/`

Why second inside Wave 5:

- These features deepen runtime understanding after the operator already has better launch, navigation, assurance, and investigation flows.

#### Wave 5C - Data-plane operations depth

Primary features:

- `docs/features/active/redis-ops-insights/`
- `docs/features/active/storage-controlled-mutations/`

Why third inside Wave 5:

- These features add specialized diagnostics and guarded mutation workflows that are valuable but should build on the shared production-safety and readiness patterns established earlier.

Wave-5 active feature folders:

- `docs/features/active/pipelines-deployment-assurance/`
- `docs/features/active/service-bus-operator-workbench/`
- `docs/features/active/aks-runtime-diagnostics-depth/`
- `docs/features/active/observability-explainer-and-reliability/`
- `docs/features/active/redis-ops-insights/`
- `docs/features/active/storage-controlled-mutations/`

The specific implementation start inside Wave 5 should still respect team bandwidth, but the default order is 5A then 5B then 5C unless an explicit decision records a justified deviation.

## Update cadence

- Update this roadmap when a wave changes dependency status.
- Do not use this file for day-to-day implementation notes.
- When a later-wave feature becomes concrete, create its own folder first and then update this roadmap to reference it.
