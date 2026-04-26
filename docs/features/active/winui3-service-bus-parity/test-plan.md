# Test Plan - winui3-service-bus-parity

---

title: "Test Plan - winui3-service-bus-parity"
owner: ""
status: "In Progress"
created: "2026-04-25"
updated: "2026-04-26"

---

## Goal

Validate that the native Service Bus workspace reaches the requested non-incident MAUI parity bar, with replay, batch DLQ replay, advanced list tooling, and shell workspace restore all covered natively while incident-timeline actions stay explicitly out of scope.

## Scope

- In scope: scheduled manager baseline, template save/apply plus scheduled send, native batch send, selected-message quick actions, replay target selection, batch DLQ replay, advanced filter and preference persistence, filtered delete, purge, export JSON, row density, custom property columns, and shell workspace restore
- Out of scope: new backend broker capabilities and unrelated UI polish
- Out of scope: incident investigation and trace pivots, which are being removed rather than ported into WinUI

## Main scenarios (priority)

1. Scenario: scheduled-message workflows are available natively. Expected result: operators can schedule messages, inspect scheduled entries, cancel broker-schedulable entries, and remove local history without returning to MAUI.
2. Scenario: templates and list controls match the established operator workflow. Expected result: saved templates, text filters, saved filters, built-in field visibility, and load-more state persist correctly for the current scope.
3. Scenario: batch send is available natively. Expected result: operators can validate and preview a JSON batch, send valid entries in chunks, and receive a partial-success summary without returning to MAUI.
4. Scenario: selected-message quick actions are available natively. Expected result: operators can edit-resubmit, schedule, save a template, copy body or full message content, and filter the current list to the selected session from the WinUI detail pane.
5. Scenario: replay and dead-letter recovery are available natively. Expected result: operators can replay a selected message to a chosen namespace or entity, batch replay selected DLQ entries with remap rules, and preserve confirmation or result clarity.
6. Scenario: advanced list tooling is available natively. Expected result: operators can combine multi-rule filters, save or restore advanced criteria, delete filtered messages, purge the current mode, export visible messages as JSON, and adjust row density or custom application-property columns without returning to MAUI.
7. Scenario: shell workspace restore works for Service Bus. Expected result: opening a recent or favorite Service Bus resource restores the active tab and tab set after route-first navigation.
8. Scenario: destructive actions remain safe. Expected result: DLQ resubmit/complete, filtered delete, purge, and scheduled destructive actions require clear confirmation cues before execution.

## Automated coverage

- Build validation: `build-winui` must stay green.
- Unit tests: expand `tests/SwebKit.WinUI.Tests/` for Service Bus view-model state and any template or filter persistence logic.
- Current automated slice: `ServiceBusPageViewModelTests` covers template persistence, scheduled send plus local scheduled-history storage, replay remap behavior, batch DLQ replay, advanced-rule filter persistence, list preferences, filtered delete, and workspace snapshot publish or restore; `ServiceBusBatchSendWorkflowTests` covers native batch-send parsing and chunked send behavior; `ServiceBusPagePresentationTests` covers compose-dialog presentation state, confirmation copy, scheduled-workspace wiring, and the native list-tooling action surface.
- Current automated blocker: broader WinUI build or test execution is blocked by an unrelated `GetEventsAsync` compile error in the AKS page resources file.
- Regression target: rerun relevant domain tests if shared Service Bus or configuration behavior changes.

## Test data and setup

- Demo mode is sufficient for list and state rendering checks.
- Live validation needs a representative namespace with entities that exercise scheduled messages and destructive flows.

## Manual checks

- Check: compose/template parity. Steps: open the native compose dialog, save a template, reapply it, then send and schedule from the same workflow.
- Check: scheduled workflow parity. Steps: open the native Service Bus workspace, inspect scheduled messages, cancel a future message, and verify local removal only clears the saved entry.
- Check: batch send parity. Steps: open the native batch-send dialog, paste a mixed-validity JSON payload, review the preview, execute the batch, and verify the result summary matches the valid/invalid split.
- Check: selected-message quick actions. Steps: select a message, use edit-resubmit, schedule, save-template, copy, and filter-to-session from the native detail pane, and verify the action affects the current workspace as expected.
- Check: replay parity. Steps: select a message, open Replay, choose another namespace or entity, apply optional remap rules, and verify the replayed message lands with the expected overrides.
- Check: advanced list tooling parity. Steps: enable advanced filtering, combine rules, save and restore the rule set, add a custom property column, switch row density, export visible messages, then run filtered delete or purge with confirmation.
- Check: workspace restore parity. Steps: open more than one Service Bus tab, switch the active tab, trigger a shell recent or favorite reopen, and verify the tab set and active workspace rehydrate after navigation.
- Check: destructive safety. Steps: attempt DLQ resubmit/complete, filtered delete, purge, and scheduled destructive actions and confirm the page presents the expected confirmation dialog before execution.

## Regression risks & mitigations

- Risk: advanced list behavior breaks workspace restore. Mitigation: validate persistence across navigation and restart.
- Risk: template flows overcomplicate the page state. Mitigation: cover the save, load, and execute loops explicitly in tests.
- Risk: page-level dialog wiring drifts from the validated view-model behavior. Mitigation: keep the page presentation helper and `ServiceBusPagePresentationTests` aligned whenever compose or confirmation copy changes.
- Risk: repo-wide WinUI build blockers can hide Service Bus regressions. Mitigation: keep focused Service Bus page diagnostics and tests runnable even when unrelated areas fail the broader build.

## Acceptance criteria

- The MAUI-only Service Bus workflows that this feature explicitly marks complete are available natively.
- Destructive actions preserve or improve safety cues.
- Focused WinUI tests cover the new state logic, and broader `build-winui` validation can be rerun once the unrelated AKS compile error is cleared.
- Incident-timeline actions remain explicitly out of scope rather than being implied as parity gaps.

## Validation status

- Automated: `get_errors` passed on the touched WinUI Service Bus files and focused Service Bus test files. Broader `build-winui`, the focused `dotnet test` rerun, and wider WinUI test execution are currently blocked by the unrelated AKS compile error.
- Manual: Replay targeting, batch DLQ replay, advanced list tooling, and workspace restore still need live WinUI verification against a representative namespace once the shared build blocker is removed.

## Sign-off

- **Approved by:**
- **Date:** 2026-04-26
- **Conditions (if any):** Feature can leave active status after the unrelated AKS compile blocker is cleared and a broader WinUI validation pass confirms the focused Service Bus results on the full project build.
