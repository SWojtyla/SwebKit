# Test Plan - storage-redis-ux-enhancements

---

title: "Test Plan - storage-redis-ux-enhancements"
owner: "GitHub Copilot"
status: "Review"
created: "2026-04-10"
updated: "2026-04-10"

---

## Goal

Validate that blob downloads expose clear in-flight progress and completion/failure state, and that Redis bulk cleanup shifts from full-database purge to selection-first subtree helpers without hiding destructive scope.

## Scope

- In scope: single-blob download progress UX, Redis selection helper behavior, selected-key delete flow, and regressions in existing storage and Redis interactions.
- Out of scope: upload/delete changes for storage, background download manager behavior, and new server-side Redis delete contracts.

## Main scenarios (priority)

1. Scenario: download a large blob from the detail pane. Expected result: the UI shows active progress with transferred bytes and completion state, and repeat-click behavior is blocked while the same download is in flight.
2. Scenario: download a blob from the blob list or context menu. Expected result: the same progress pattern appears and clears correctly on success or failure.
3. Scenario: download a blob version from the versions tab. Expected result: version download reuses the shared progress behavior and still writes to the Downloads folder.
4. Scenario: fail a blob download mid-stream. Expected result: progress state is cleared, error messaging is visible, and the UI does not remain stuck in a loading state.
5. Scenario: click `Select all loaded` in Redis multi-select mode. Expected result: all currently loaded keys become selected, the count matches the loaded key set, and no unscanned keys are implied.
6. Scenario: trigger `Select subtree` on a namespace node. Expected result: all loaded descendant key leaves under that prefix are selected and the toolbar count reflects the exact descendant count.
7. Scenario: clear a subtree or clear all selection. Expected result: only the intended keys are deselected and batch-delete affordances update immediately.
8. Scenario: delete keys after using full-select helpers. Expected result: confirmation still occurs, only selected keys are removed, and the page clears stale detail state if the selected key was deleted.
9. Scenario: load a Redis page after the feature lands. Expected result: there is no direct page-level `Purge All` action in the main toolbar.

## Automated coverage

- Focused component coverage runs in `tests/SwebKit.App.Tests/RedisToolbarTests.cs`, `tests/SwebKit.App.Tests/RedisNamespaceTreeNodeTests.cs`, and `tests/SwebKit.App.Tests/StorageDownloadProgressTests.cs`.
- Azure storage client coverage runs in `tests/SwebKit.Azure.Tests/AzureStorageClientTests.cs`.
- The targeted automated validation passed 16 of 16 tests.
- `dotnet build .\SwebKit.slnx -nologo` succeeded after the feature changes.

## Test data and setup

- Storage fixtures should include a small blob, a large blob where progress visibly advances, and a forced failure path.
- Redis fixtures should include keys under at least two prefixes with nested descendants so subtree counts are deterministic.
- One Redis test case should keep pagination incomplete to verify the UI does not imply deletion beyond loaded keys.

## Manual checks

- Check: large blob download feedback - steps
- Start a large download from the detail pane, verify visible progress, wait for completion, and confirm the final message remains clear and brief.
- Check: list download feedback parity - steps
- Start a download from the blob list and verify the UX matches the detail-pane flow.
- Check: Redis subtree helper safety - steps
- Enter multi-select mode, use subtree selection on one prefix, verify the count, then cancel instead of deleting.
- Check: Redis destructive path removal - steps
- Open the Redis page and verify cleanup is driven through selection plus confirmation rather than direct database purge.

## Regression risks & mitigations

- Risk: progress updates trigger too many renders in MAUI Blazor.
- Mitigation: coalesce updates and validate responsiveness with a large blob fixture.
- Risk: subtree helpers accidentally select prefix labels instead of only leaf keys.
- Mitigation: add component assertions against exact loaded leaf counts.
- Risk: removing purge-all leaves dead code or stale command wiring in the toolbar/page.
- Mitigation: verify toolbar rendering and action bindings with component tests.

## Acceptance criteria

- Large blob downloads visibly progress while in flight and clear state cleanly on completion or error.
- Redis cleanup is selection-first: no direct full-database purge action remains in the main page UX.
- Subtree selection and full-select helpers act only on loaded keys and always show explicit counts before delete.
- Tests and functionality docs are updated together with the implementation.

## Validation status

- Automated: Completed
- Manual: Not run in this validation slice
