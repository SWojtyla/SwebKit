# Test Plan - winui3-service-bus-parity

---

title: "Test Plan - winui3-service-bus-parity"
owner: ""
status: "Not started"
created: "2026-04-25"
updated: "2026-04-25"

---

## Goal

Validate that the native Service Bus workspace reaches MAUI workflow parity for advanced message operations without losing safety cues or restore behavior.

## Scope

- In scope: scheduled workflows, templates, advanced list control, bulk-safety confirmations, workspace restore
- Out of scope: new backend broker capabilities and unrelated UI polish

## Main scenarios (priority)

1. Scenario: scheduled-message workflows are available natively. Expected result: operators can review and manage scheduled work without returning to MAUI.
2. Scenario: templates and list controls match the established operator workflow. Expected result: saved templates, filters, and column choices persist and reload correctly.
3. Scenario: destructive actions remain safe. Expected result: bulk delete or replay flows require the same or stronger confirmation cues as MAUI.

## Automated coverage

- Build validation: `build-winui` must stay green.
- Unit tests: expand `tests/SwebKit.WinUI.Tests/` for Service Bus view-model state and any template or filter persistence logic.
- Regression target: rerun relevant domain tests if shared Service Bus or configuration behavior changes.

## Test data and setup

- Demo mode is sufficient for list and state rendering checks.
- Live validation needs a representative namespace with entities that exercise scheduled messages and destructive flows.

## Manual checks

- Check: scheduled workflow parity. Steps: open the native Service Bus workspace, inspect scheduled messages, and verify the action set matches the planned parity scope.
- Check: destructive safety. Steps: attempt a bulk destructive action and confirm the page requires the expected production-safety acknowledgment.

## Regression risks & mitigations

- Risk: advanced list behavior breaks workspace restore. Mitigation: validate persistence across navigation and restart.
- Risk: template flows overcomplicate the page state. Mitigation: cover the save, load, and execute loops explicitly in tests.

## Acceptance criteria

- The MAUI-only Service Bus workflows called out in this plan are available natively.
- Destructive actions preserve or improve safety cues.
- `build-winui` stays green and focused WinUI tests cover the new state logic.

## Validation status

- Automated: Not started
- Manual: Not started

## Sign-off

- **Approved by:**
- **Date:**
- **Conditions (if any):**
