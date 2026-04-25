# Test Plan - winui3-storage-parity

---

title: "Test Plan - winui3-storage-parity"
owner: ""
status: "Not started"
created: "2026-04-25"
updated: "2026-04-25"

---

## Goal

Validate that the native Storage workspace reaches the agreed browse, preview, and bulk-operation parity without regressing the current baseline browsing experience.

## Scope

- In scope: batch and ZIP workflows, version handling, preview hardening, shared detail/state adoption
- Out of scope: new storage-product features and unrelated backend changes

## Main scenarios (priority)

1. Scenario: operators can perform the agreed batch and version workflows natively. Expected result: the native page exposes the planned MAUI parity surface.
2. Scenario: large or binary content is handled safely. Expected result: preview behavior is explicit, stable, and does not misrepresent unsupported content.
3. Scenario: the existing browse and SAS-copy baseline remains intact. Expected result: new flows do not regress the current route.

## Automated coverage

- Build validation: `build-winui` must stay green.
- Unit tests: expand `tests/SwebKit.WinUI.Tests/` for Storage page-state logic and any bulk-progress or preview-mode selection logic.
- Regression target: rerun touched storage domain tests when service behavior changes.

## Test data and setup

- Demo mode covers basic page-state checks.
- Live validation needs representative storage content that exercises text, binary, versioned, and bulk-download scenarios.

## Manual checks

- Check: preview handling. Steps: open a mix of text, large, and binary items and verify the page chooses the expected preview or fallback behavior.
- Check: batch workflows. Steps: run the planned batch or ZIP flow and confirm progress, errors, and completion messaging are clear.

## Regression risks & mitigations

- Risk: preview behavior appears to succeed on unsupported content. Mitigation: add explicit unsupported or fallback states.
- Risk: batch downloads leave the page in an unclear state after partial failures. Mitigation: validate progress and result messaging explicitly.

## Acceptance criteria

- The Storage workflows called out in this plan are available natively.
- Preview and batch-state behavior remain clear and safe.
- `build-winui` stays green and focused WinUI tests cover the new state logic.

## Validation status

- Automated: Not started
- Manual: Not started

## Sign-off

- **Approved by:**
- **Date:**
- **Conditions (if any):**
