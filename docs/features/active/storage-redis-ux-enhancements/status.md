# Status - storage-redis-ux-enhancements

---

title: "Status - storage-redis-ux-enhancements"
owner: "GitHub Copilot"
state: "Review"
jira: "not linked"
branch: ""
started: "2026-04-10"
last_updated: "2026-04-10"

---

## Quick summary

Implementation is complete for the requested storage and Redis UX changes, including the Redis hardening follow-up. Storage downloads still expose inline progress, and the Redis page now keeps large filtered key sets responsive by strictly capping each loaded match page, carrying SCAN overflow forward, canceling stale badge batches on scan/filter/cache changes, stating full-keyspace filter behavior explicitly, and using a stronger selected-row treatment in the tree.

Jira: not linked

Current focus: keep the feature in a review-ready state with the Redis follow-up validated and the docs aligned to the delivered behavior.

## Progress checklist

- [x] Active feature folder and durable planning docs created
- [x] Scope, non-goals, risks, and decisions captured
- [x] Add progress-aware storage download contract and UI state
- [x] Replace Redis purge-all toolbar action with safer full-select helpers
- [x] Add focused follow-up tests and align Redis docs with the delivered behavior

## Completed

- Added additive byte-progress reporting to the storage download path and surfaced inline progress in `StorageBlobList` and `BlobDetailPane`, including blob-version downloads.
- Moved Blob detail action messaging to a shared pane-level slot so version-tab downloads expose completion state inline.
- Removed the Redis toolbar `Purge All` CTA and replaced it with `Select all loaded` plus row-click subtree selection for loaded descendants only.
- Reused the existing selected-key delete flow for Redis bulk cleanup instead of adding a new destructive path.
- Repaired the Redis toolbar imperative selection helpers so they queue renders on the Blazor dispatcher instead of calling `StateHasChanged()` directly.
- Tightened the focused storage progress tests to run on the renderer dispatcher and assert localized size formatting.
- Reduced the initial Redis loaded-match page size and resumed `Load more` from the same filtered cursor so large keyspaces do not freeze the UI.
- Hardened Redis scan paging so advisory SCAN overshoot cannot render more than one loaded page at a time; overflow keys are buffered for the next `Load more matches` step.
- Replaced the Redis tree badge hot path with lightweight batched key-type lookups instead of loading full key metadata for every scanned match up front.
- Bound batched key-type writes to the active scan/filter/cache session so stale badge results are canceled or ignored before they mutate the next tree state.
- Made the toolbar copy explicit that the scan pattern is applied across the full Redis keyspace while the tree shows currently loaded matches only.
- Strengthened the Redis tree selected-row treatment so the current selection is visibly distinct during single-select and multi-select workflows.
- Switched Redis multi-select to use direct row clicks: key rows now toggle selection, namespace rows toggle their loaded descendants, and the old `All` / `None` subtree badges are gone.
- Passed the focused Redis hardening validation slice and a solution build.

## Remaining

- No implementation work remains for the requested scope.
- Next lifecycle step: ship and archive the feature through the normal workflow.

## Blockers

- None.

## Validation

- Test Plan: test-plan.md
- Validation status: Targeted Redis hardening validation passed; the feature is review-ready.
- Automated: `runTests` on `RedisToolbarTests.cs`, `RedisNamespaceTreeNodeTests.cs`, `DemoRedisClientTests.cs`, and `RedisScanPageAccumulatorTests.cs` passed (28/28).
- Build: `dotnet build .\SwebKit.slnx -nologo` succeeded.
- Manual: not run for this validation slice.

## Notes

- Redis selection must stay explicitly reviewable before delete; no hidden wildcard delete behavior should be introduced.
- Storage progress is a functional acceptance criterion for large downloads, not cosmetic polish.
- Redis scan filtering remains keyspace-wide at the backend; only the currently loaded match page is rendered and selectable in the tree at any moment.
- Redis scan-session resets must remain authoritative so cache, filter, and manual rescan flows cannot leak stale badge writes into the next page state.
