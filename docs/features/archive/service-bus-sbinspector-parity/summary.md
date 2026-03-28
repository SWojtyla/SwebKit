# Archive Summary - service-bus-sbinspector-parity

---

title: "Archive Summary - service-bus-sbinspector-parity"
owner: "Unassigned"
jira: "not linked"
completed_date: "2026-03-28"
pr: ""
commit: ""

---

## Goal

Close SBInspector parity gaps for Service Bus operations in SwebKit so users can perform day-to-day triage and administration in one tool, while preserving SwebKit safety-first UX and accessibility patterns.

## Delivered

- Implemented queue/topic/subscription enable-disable operations with status-aware entity rendering.
- Added active-message single delete and purge-all workflows, including production confirmation safeguards and auto-refresh behavior.
- Delivered advanced multi-field filtering with operators, filter persistence/toggles, delete-filtered actions, and filtered export (JSON in scope).
- Delivered column customization (built-in and custom property columns) plus row-density preference persistence.
- Added load-more pagination behavior that preserves filter and selection continuity for large datasets.
- Completed message template parity with create/save/apply/edit/delete flows, searchable template selection, explicit row selection, and inline validation for invalid rename/edit inputs.
- Extended backend contracts and implementations (`IServiceBusClient`, Azure client, demo client) and kept profile persistence backward-compatible.
- Added/updated focused component and unit test coverage across waves for Service Bus UI and persistence behavior.

## Key decisions

- Prioritized capability parity over UI cloning to keep SwebKit interaction consistency intact.
- Sequenced delivery by severity in five waves to reduce regression blast radius and keep validation incremental.
- Kept strict production safety gates for destructive operations (single delete, purge, delete filtered).
- Persisted productivity preferences and templates via backward-compatible optional fields.
- Kept this feature scoped to functional parity and deferred settings/theming parity and CSV filtered export.

## Validation performed

- `dotnet build SwebKit.slnx`: pass (warnings only, no new blockers reported for this feature).
- `dotnet test tests/SwebKit.App.Tests/SwebKit.App.Tests.csproj --filter "FullyQualifiedName~MessageComposerTests|FullyQualifiedName~TemplatePickerTests"`: pass (12/12).
- `dotnet test tests/SwebKit.App.Tests/SwebKit.App.Tests.csproj --filter "FullyQualifiedName~MessageListViewTests"`: pass.
- `dotnet test tests/SwebKit.Core.Tests/SwebKit.Core.Tests.csproj --filter "FullyQualifiedName~UiStateFilterTests"`: pass.
- Known baseline note during close-out: unrelated full-suite failures remained outside feature scope in existing `--no-build` full-suite runs.

## Lessons learned

- Expanding peek-window load-more delivered parity behavior without immediate contract churn for continuation tokens.
- Stable component test selectors for template actions improved test reliability without changing runtime UX.
- Profile-data isolation via `SWEBKIT_APPDATA_ROOT` test override and serialized collection setup is important for deterministic persistence tests.

## Follow-up

- Add CSV option for filtered export in `MessageListView` parity follow-up — owner: Product backlog (TBD).
- Revisit settings/theming parity only if explicitly requested as separate scope — owner: Product/UX (TBD).
- Re-verify and remediate unrelated full-suite baseline failures during next general quality pass — owner: Engineering (TBD).

## Archive note

> This file is present because the feature had no linked Jira ticket (Path B). Durable archive location: `docs/features/archive/service-bus-sbinspector-parity/`.
