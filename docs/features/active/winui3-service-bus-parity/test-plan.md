# Test Plan - winui3-service-bus-parity

---

title: "Test Plan - winui3-service-bus-parity"
owner: ""
status: "In Progress"
created: "2026-04-25"
updated: "2026-04-25"

---

## Goal

Validate that the native Service Bus workspace closes the current MAUI parity baseline for scheduled work, compose/template reuse, message-list controls, and destructive safety without losing restore behavior.

## Scope

- In scope: scheduled manager baseline, template save/apply plus scheduled send, text-filter and preference persistence, confirmation-gated destructive actions, remaining restore hardening follow-up
- Out of scope: new backend broker capabilities and unrelated UI polish

## Main scenarios (priority)

1. Scenario: scheduled-message workflows are available natively. Expected result: operators can schedule messages, inspect scheduled entries, cancel broker-schedulable entries, and remove local history without returning to MAUI.
2. Scenario: templates and list controls match the established operator workflow. Expected result: saved templates, text filters, saved filters, built-in field visibility, and load-more state persist correctly for the current scope; broader reopen/restart restore hardening remains a follow-up item.
3. Scenario: destructive actions remain safe. Expected result: DLQ resubmit/complete and scheduled destructive actions require clear confirmation cues before execution.

## Automated coverage

- Build validation: `build-winui` must stay green.
- Unit tests: expand `tests/SwebKit.WinUI.Tests/` for Service Bus view-model state and any template or filter persistence logic.
- Current automated slice: `ServiceBusPageViewModelTests` covers template persistence, scheduled send plus local scheduled-history storage, text filtering, saved-filter persistence, and built-in field preference persistence for the native workspace; `ServiceBusPagePresentationTests` covers compose-dialog presentation state, confirmation copy, and the scheduled-workspace XAML wiring.
- Current automated gap: workspace restore still needs explicit reopen/navigation coverage for the richer native tab state.
- Regression target: rerun relevant domain tests if shared Service Bus or configuration behavior changes.

## Test data and setup

- Demo mode is sufficient for list and state rendering checks.
- Live validation needs a representative namespace with entities that exercise scheduled messages and destructive flows.

## Manual checks

- Check: compose/template parity. Steps: open the native compose dialog, save a template, reapply it, then send and schedule from the same workflow.
- Check: scheduled workflow parity. Steps: open the native Service Bus workspace, inspect scheduled messages, cancel a future message, and verify local removal only clears the saved entry.
- Check: destructive safety. Steps: attempt DLQ resubmit/complete and scheduled destructive actions and confirm the page requires the expected production-safety acknowledgment.

## Regression risks & mitigations

- Risk: advanced list behavior breaks workspace restore. Mitigation: validate persistence across navigation and restart.
- Risk: template flows overcomplicate the page state. Mitigation: cover the save, load, and execute loops explicitly in tests.
- Risk: page-level dialog wiring drifts from the validated view-model behavior. Mitigation: keep the page presentation helper and `ServiceBusPagePresentationTests` aligned whenever compose or confirmation copy changes.

## Acceptance criteria

- The MAUI-only Service Bus workflows called out in this plan are available natively.
- Destructive actions preserve or improve safety cues.
- `build-winui` stays green and focused WinUI tests cover the new state logic.

## Validation status

- Automated: `build-winui` passed; `dotnet test .\tests\SwebKit.WinUI.Tests\SwebKit.WinUI.Tests.csproj --filter ServiceBusPageViewModelTests|ServiceBusPagePresentationTests` passed on 2026-04-25 for the scheduled/template/list-control baseline plus page-level compose/confirmation/scheduled-workspace coverage.
- Manual: Not started

## Sign-off

- **Approved by:**
- **Date:**
- **Conditions (if any):**
