# Feature Overview - service-bus-sbinspector-parity

---

title: "Feature Overview - service-bus-sbinspector-parity"
owner: "Unassigned"
status: "Planned"
jira: "not linked"
created: "2026-03-28"
updated: "2026-03-28"

---

## Goal

Enable SwebKit Service Bus to match SBInspector's operational feature depth while preserving SwebKit's clearer, safer, and more user-friendly workflows.

## Value

This closes high-impact operational gaps that currently force users to switch tools for day-to-day Service Bus triage. The outcome is one consistent SwebKit experience for queue/topic/subscription administration, message operations, and high-volume diagnostics, without losing SwebKit's production safety cues, keyboard consistency, and approachable UI patterns.

## Scope

- In scope:
  - Parity for high-severity feature gaps:
    - Entity enable/disable (queue/topic/subscription)
    - Delete single message from UI
    - Purge all messages with safety guardrails
    - Advanced multi-field filtering with operators
    - Delete filtered result sets and export filtered result sets (JSON in scope; CSV deferred to follow-up)
    - Column customization, including custom property columns
    - Pagination/load-more behavior for large datasets
    - Message templates in composer workflows
  - Parity for medium-severity gaps:
    - Filter persistence and filter toggle behavior
    - Row density controls and persistence
    - Auto-refresh after mutative operations
  - Preserve SwebKit UX consistency:
    - Reuse shared interaction patterns
    - Keep production safety confirmations and visual cues
    - Keep keyboard navigation and accessibility consistent

> Delivery waves
>
> 1. Wave 1: Critical entity and message management
> 2. Wave 2: Advanced filtering and filtered operations
> 3. Wave 3: Column customization and density
> 4. Wave 4: Pagination and load-more
> 5. Wave 5: Message templates

- Out of scope:
  - Rebuilding SwebKit UI to visually copy SBInspector
  - Re-architecting unrelated feature areas (AKS, Redis, Observability, Storage)
  - New Azure infrastructure provisioning
  - Cross-feature configuration redesign beyond Service Bus-related preferences
  - Settings/theming parity with SBInspector; existing SwebKit team themes remain authoritative for this feature

## Assumptions

- No Jira ticket is currently linked; acceptance will be tracked through this feature folder until a ticket is provided.
- SBInspector parity baseline is defined by the validated gap list in this planning request and current SBInspector behavior at planning time.
- Capability parity does not require layout parity; SwebKit's existing UX language remains the default.
- This feature prioritizes functional parity and operational capability over settings/theming parity.
- Filtered export parity for this feature is JSON-first; CSV export is explicitly deferred to follow-up scope.

## Dependencies

- Architecture and workflow constraints:
  - `ai-setup/ways-of-working/ai-workflow.md`
  - `ai-setup/ways-of-working/definition-of-done.md`
  - `docs/architecture/architecture.md`
  - `docs/architecture/design.md`
  - `docs/architecture/codebase-guide.md`
  - `docs/architecture/functionalities/service-bus.md`
- SwebKit Service Bus touchpoints:
  - `src/SwebKit.App/Components/Pages/ServiceBusPage.razor`
  - `src/SwebKit.App/Components/ServiceBus/EntityTree.razor`
  - `src/SwebKit.App/Components/ServiceBus/MessageListView.razor`
  - `src/SwebKit.App/Components/ServiceBus/MessageComposer.razor`
  - `src/SwebKit.App/Components/ServiceBus/DlqView.razor`
  - `src/SwebKit.App/Components/ServiceBus/ScheduledMessages.razor`
  - `src/SwebKit.Core/Abstractions/IServiceBusClient.cs`
  - `src/SwebKit.Azure/ServiceBus/AzureServiceBusClient.cs`
- Pitfall constraints that must shape implementation:
  - `docs/pitfalls/agent-workflow.md`
  - `docs/pitfalls/azure-sdk.md`
  - `docs/pitfalls/blazor-maui.md`
  - `docs/pitfalls/dotnet-csharp.md`

## Risks & mitigations

- Risk: Backend parity work introduces auth/claim confusion for admin operations.
  - Mitigation: Validate listing/test paths against the same admin claims and explicitly surface insufficient permission states (AZ-1).
- Risk: Scoped connection strings lead to misleading empty namespace listings.
  - Mitigation: Preserve scoped entity behavior and expose clear UI context about scope limitations (AZ-2).
- Risk: Large list/filter changes create rendering/performance regressions in Blazor Hybrid.
  - Mitigation: Use guarded lifecycle patterns, throttled UI updates where needed, and component-level tests for state transitions (BL-2/BL-3/BL-5/BL-8).
- Risk: Destructive operations increase production incident risk.
  - Mitigation: Keep confirmation gates, production-tier visual cues, and keyboard-safe confirmation flows.
- Risk: Plan or architecture drift during a multi-wave delivery.
  - Mitigation: Keep `status.md` current per wave and record tradeoffs in `decisions.md` (AW-1/AW-5).

## Related documents

- Functional deep dive: `docs/architecture/functionalities/service-bus.md`
- Architecture map: `docs/architecture/architecture.md`
- Component flow constraints: `docs/architecture/design.md`
- Code navigation: `docs/architecture/codebase-guide.md`
- Pitfalls:
  - `docs/pitfalls/agent-workflow.md`
  - `docs/pitfalls/azure-sdk.md`
  - `docs/pitfalls/blazor-maui.md`
  - `docs/pitfalls/dotnet-csharp.md`

## Quick links

- Jira: not linked
- Status: `status.md`
- Tests: `test-plan.md`
- Implementation modules: `backend.md`, `frontend.md`, `decisions.md`

## Documentation coupling requirement

When implementation changes Service Bus behavior, update `docs/architecture/functionalities/service-bus.md` in the same change set.
