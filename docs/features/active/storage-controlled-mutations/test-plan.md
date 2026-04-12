# Test Plan - storage-controlled-mutations

---

title: "Test Plan - storage-controlled-mutations"
owner: "GitHub Copilot"
status: "Not started"
created: "2026-04-12"
updated: "2026-04-12"

---

## Goal

Validate that Storage mutations remain opt-in, capability-aware, and explicit enough for production use while uploads, copy, metadata changes, diff views, and recovery flows behave correctly across supported blob-account configurations.

## Scope

- In scope: mutation opt-in behavior, upload and copy, metadata updates, version diff, recovery, capability detection, overwrite confirmation, progress and cancellation, and Storage page regression safety.
- Out of scope: bulk delete, container management, background transfer orchestration, and cross-account import workflows.

## Main scenarios (priority)

1. Scenario: `AllowMutations` is false for the selected storage account. - Expected result: the Storage page stays read-only and exposes guidance instead of mutation actions.
2. Scenario: `AllowMutations` is true in a non-production environment. - Expected result: upload and copy actions are available with clear destination summary and progress.
3. Scenario: Upload or copy would overwrite an existing blob in production. - Expected result: the dialog shows source, destination, overwrite intent, and requires typed `CONFIRM` before the action runs.
4. Scenario: Metadata edits add, change, and remove keys. - Expected result: the UI shows a before-versus-after diff and only applies the requested changes.
5. Scenario: The operator compares the current blob with an older version of a text blob. - Expected result: a bounded text diff renders with version context and no silent truncation confusion.
6. Scenario: The operator compares versions of a binary or oversized blob. - Expected result: the UI falls back to metadata, size, and version information rather than an unreadable text diff.
7. Scenario: Blob versioning is enabled and the operator restores a previous version. - Expected result: the selected version is promoted safely, history remains intact, and the UI refreshes to the new current version state.
8. Scenario: Soft delete is enabled and a deleted blob can be undeleted. - Expected result: recovery is offered only when capability detection confirms support.
9. Scenario: The credential cannot perform one mutation type. - Expected result: that capability is marked unavailable and the rest of the page remains usable.
10. Scenario: A large upload is canceled or fails mid-stream. - Expected result: progress state clears correctly and no stale success message is shown.
11. Scenario: Demo mode is active. - Expected result: deterministic upload/copy/metadata/diff/recovery fixtures exist for component and manual testing.

## Automated coverage

- Azure client tests: `tests/SwebKit.Azure.Tests/AzureStorageClientTests.cs`
- Extend constructor-only coverage with additive tests for upload, copy, metadata, capability detection, and recovery helpers.
- Core tests: `tests/SwebKit.Core.Tests/StorageConfigTests.cs`
- Add additive config coverage for the mutation opt-in field and any new storage capability or mutation DTOs.
- App tests: `tests/SwebKit.App.Tests`
- Extend `StorageDownloadProgressTests.cs` for mutation progress patterns where the UI reuses similar progress behavior.
- Add likely new tests such as `StorageMutationDialogTests.cs`, `BlobVersionDiffTests.cs`, and `BlobRecoveryPanelTests.cs`.
- End-to-end tests: `tests/SwebKit.E2E.Tests`
- Add only a narrow smoke slice if needed to prove that the Storage page still loads and mutation controls stay hidden when disabled; most behavior should stay in bUnit and client-level tests.
- CI gates: all storage mutation tests pass and current Storage browse/download tests remain green.

## Test data and setup

- Blob fixtures covering text, JSON, XML, and binary content.
- Version fixtures with current version plus two historical versions and different metadata values.
- Soft-delete fixtures for recoverable and non-recoverable blobs.
- Capability fixtures for `versioning enabled`, `soft delete enabled`, `shared key unavailable`, and `metadata update forbidden`.
- Upload and copy fixtures with both overwrite and non-overwrite destinations.

## Manual checks

- Check: Read-only mode - steps
- Open Storage on an account with mutations disabled and verify that no upload/copy/edit/recover actions are offered.
- Check: Upload and copy confirmation - steps
- Enable mutations, start an upload or same-account copy, and verify the dialog clearly states account, container, destination path, and overwrite intent.
- Check: Metadata diff - steps
- Edit metadata for one blob, review the diff preview, and verify the resulting property view reflects only the intended changes.
- Check: Recovery behavior - steps
- Compare versions, restore one version, and verify the page refreshes to the new current version state while preserving history where versioning is enabled.

## Regression risks & mitigations

- Risk: mutation controls bleed into accounts that should stay read-only. - Mitigation: explicit tests for `AllowMutations = false` and a clear page-level mode banner.
- Risk: capability detection becomes inaccurate across different auth modes. - Mitigation: Azure client tests for AAD and connection-string cases plus UI states for unavailable capabilities.
- Risk: version diff degrades current preview performance. - Mitigation: reuse existing preview caps and keep diff loading explicit.
- Risk: overwrite confirmation copy is not explicit enough for production use. - Mitigation: require typed confirmation and assert the full source/destination summary in component tests.

## Acceptance criteria

- All high-priority scenarios pass in focused Azure, App, and Core tests.
- Mutation controls are hidden by default and only appear for accounts that explicitly opt in.
- Overwrite and recovery actions remain explicit and confirmation-gated.
- Capability-limited environments degrade to clear `Unavailable` states instead of unhandled errors.
- Storage docs and feature docs are updated together with implementation.

## Validation status

- Automated: Not started
- Manual: Not started

## Sign-off

- Approved by:
- Date:
- Conditions (if any):
