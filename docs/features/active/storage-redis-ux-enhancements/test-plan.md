# Test Plan - storage-redis-ux-enhancements

---

title: "Test Plan - storage-redis-ux-enhancements"
owner: "GitHub Copilot"
status: "Review"
created: "2026-04-10"
updated: "2026-04-10"

---

## Goal

Validate that blob downloads expose clear in-flight progress and completion/failure state, and that Redis bulk cleanup shifts from full-database purge to selection-first subtree helpers without hiding destructive scope while keeping large filtered key sets responsive.

## Scope

- In scope: single-blob download progress UX, Redis selection helper behavior, keyspace-wide scan/filter messaging, bounded loaded-match pagination, selected-key delete flow, and regressions in existing storage and Redis interactions.
- Out of scope: upload/delete changes for storage, background download manager behavior, and new server-side Redis delete contracts.

## Main scenarios (priority)

1. Scenario: download a large blob from the detail pane. Expected result: the UI shows active progress with transferred bytes and completion state, and repeat-click behavior is blocked while the same download is in flight.
2. Scenario: download a blob from the blob list or context menu. Expected result: the same progress pattern appears and clears correctly on success or failure.
3. Scenario: download a blob version from the versions tab. Expected result: version download reuses the shared progress behavior and still writes to the Downloads folder.
4. Scenario: fail a blob download mid-stream. Expected result: progress state is cleared, error messaging is visible, and the UI does not remain stuck in a loading state.
5. Scenario: scan Redis with a prefix pattern when only the first loaded match page is present. Expected result: the pattern is applied across the full Redis keyspace, and `Load more matches` continues the same filtered result set rather than showing unrelated keys.
6. Scenario: Redis returns more matches than requested for a single SCAN step. Expected result: the page still renders only one loaded-match page immediately, and the overflow appears on the next `Load more matches` step.
7. Scenario: rescan, change filter, or switch cache while key-type badges from the prior tree are still resolving. Expected result: stale badge writes do not populate the newer tree state.
8. Scenario: scan a large Redis keyspace. Expected result: the first loaded match page renders promptly, and type badges continue to populate in small batches without freezing the page.
9. Scenario: click `Select all loaded` in Redis multi-select mode. Expected result: all currently loaded matching keys become selected, the count matches the loaded set, and no unscanned keys are implied.
10. Scenario: click a namespace row in Redis multi-select mode when not all loaded descendants are selected. Expected result: all loaded descendant key leaves under that prefix become selected, the toolbar count reflects the exact descendant count, and the row remains expandable via the chevron control.
11. Scenario: click the same namespace row again after all loaded descendants are selected, or clear all selection from the toolbar. Expected result: only the intended loaded keys are deselected and batch-delete affordances update immediately.
12. Scenario: delete keys after using full-select helpers. Expected result: confirmation still occurs, only selected keys are removed, and the page clears stale detail state if the selected key was deleted.
13. Scenario: load a Redis page after the follow-up lands. Expected result: there is no direct page-level `Purge All` action in the main toolbar, and the selected key row is visually obvious in the tree.

## Automated coverage

- Focused component coverage runs in `tests/SwebKit.App.Tests/RedisToolbarTests.cs`, `tests/SwebKit.App.Tests/RedisNamespaceTreeNodeTests.cs`, and `tests/SwebKit.App.Tests/StorageDownloadProgressTests.cs`.
- Redis scan behavior coverage runs in `tests/SwebKit.Core.Tests/DemoRedisClientTests.cs`.
- Redis SCAN overflow carry-forward coverage runs in `tests/SwebKit.Core.Tests/RedisScanPageAccumulatorTests.cs`.
- Azure storage client coverage runs in `tests/SwebKit.Azure.Tests/AzureStorageClientTests.cs`.
- The Redis hardening validation passed 28 of 28 targeted tests in this pass.
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
- Check: Redis row-click subtree safety - steps
- Enter multi-select mode, click one namespace row, verify the count, click the chevron to confirm expand/collapse still works independently, then cancel instead of deleting.
- Check: Redis destructive path removal - steps
- Open the Redis page and verify cleanup is driven through selection plus confirmation rather than direct database purge.

## Regression risks & mitigations

- Risk: progress updates trigger too many renders in MAUI Blazor.
- Mitigation: coalesce updates and validate responsiveness with a large blob fixture.
- Risk: namespace row clicks accidentally select prefix labels instead of only leaf keys.
- Mitigation: add component assertions against exact loaded leaf counts.
- Risk: removing purge-all leaves dead code or stale command wiring in the toolbar/page.
- Mitigation: verify toolbar rendering and action bindings with component tests.
- Risk: Redis scan still blocks on expensive metadata calls even after pagination is reduced.
- Mitigation: validate the tree path against the lightweight `GetKeyTypeAsync` batch flow and keep full metadata loading on explicit drill-in paths.
- Risk: a stale scan or cache-switch badge batch repopulates the next tree state after the user has already moved on.
- Mitigation: cancel or supersede older key-type work on every new scan context and keep coverage around the hardened scan-session boundary.

## Acceptance criteria

- Large blob downloads visibly progress while in flight and clear state cleanly on completion or error.
- Redis cleanup is selection-first: no direct full-database purge action remains in the main page UX.
- Redis filter messaging is explicit that patterns apply across the full keyspace while the tree stays limited to currently loaded matches.
- Redis SCAN overflow never renders more than one requested loaded-match page at a time; overflow remains available on the next `Load more matches` step.
- Namespace row toggles and full-select helpers act only on loaded keys and always show explicit counts before delete.
- Tests and functionality docs are updated together with the implementation.

## Validation status

- Automated: Completed
- Manual: Not run in this validation slice
