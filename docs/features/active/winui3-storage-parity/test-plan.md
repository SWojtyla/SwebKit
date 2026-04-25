# Test Plan - winui3-storage-parity

---

title: "Test Plan - winui3-storage-parity"
owner: ""
status: "Review"
created: "2026-04-25"
updated: "2026-04-25"

---

## Goal

Validate that the native Storage workspace reaches the agreed browse, preview, version-history, and ZIP-download parity without regressing the baseline browsing experience.

## Scope

- In scope: loaded-blob ZIP workflows, version handling, preview hardening regression checks, shared detail/state behavior in the native workspace
- Out of scope: new storage-product features and unrelated backend changes

## Main scenarios (priority)

1. Scenario: operators can perform the agreed batch and version workflows natively. Expected result: the native page exposes version compare/download plus mutation-gated restore and loaded-blob ZIP download.
2. Scenario: large or binary content is handled safely. Expected result: preview behavior is explicit, stable, and does not misrepresent unsupported content.
3. Scenario: the existing browse and SAS-copy baseline remains intact. Expected result: new flows do not regress the current route.

## Automated coverage

- Build validation: `build-winui` must stay green.
- Unit tests: `tests/SwebKit.WinUI.Tests/StoragePageViewModelTests.cs` now covers version loading, compare, mutation-gated restore visibility, version download, and loaded-blob ZIP download behavior.
- Regression target: rerun touched storage domain tests when service behavior changes.

## Test data and setup

- Demo mode covers basic page-state checks.
- Live validation needs representative storage content that exercises text, binary, versioned, restorable, and bulk-download scenarios.

## Manual checks

- Check: preview handling. Steps: open a mix of text, large, and binary items and verify the page chooses the expected preview or fallback behavior.
- Check: version workflows. Steps: open a versioned blob, compare an older version, download that version, and verify restore appears only when versions are available and the storage profile allows mutations.
- Check: batch workflows. Steps: enter selection mode, page in the required blobs, download them as ZIP, and confirm progress, errors, and completion messaging are clear.

## Regression risks & mitigations

- Risk: preview behavior appears to succeed on unsupported content. Mitigation: add explicit unsupported or fallback states.
- Risk: batch downloads leave the page in an unclear state after partial failures. Mitigation: validate progress and result messaging explicitly.
- Risk: deleted-blob recovery looks complete even though deleted blobs are not discoverable. Mitigation: keep undelete out of the completed parity claims and track deleted-blob discovery separately.

## Acceptance criteria

- The Storage workflows called out in this plan are available natively.
- Preview and batch-state behavior remain clear and safe.
- `build-winui` stays green and focused WinUI tests cover the new version and ZIP-selection state logic.

## Validation status

- Automated: `dotnet test .\tests\SwebKit.WinUI.Tests\SwebKit.WinUI.Tests.csproj --filter StoragePageViewModelTests` passing with 8 focused tests
- Manual: Pending WinUI UI smoke over live version-aware storage content and loaded-blob ZIP download

## Sign-off

- **Approved by:**
- **Date:**
- **Conditions (if any):**
