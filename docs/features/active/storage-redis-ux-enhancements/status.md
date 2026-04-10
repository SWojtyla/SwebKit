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

Implementation is complete for the requested storage and Redis UX changes. Storage downloads now show inline in-flight progress in the blob list and detail pane, version downloads surface completion state in the shared detail-pane message area, and Redis bulk cleanup now starts from explicit selection helpers instead of a page-level purge CTA.

Jira: not linked

Current focus: keep the feature in a review-ready state while it awaits the normal ship or archive workflow.

## Progress checklist

- [x] Active feature folder and durable planning docs created
- [x] Scope, non-goals, risks, and decisions captured
- [x] Add progress-aware storage download contract and UI state
- [x] Replace Redis purge-all toolbar action with safer full-select helpers
- [x] Add focused tests and align functionality docs with delivered behavior

## Completed

- Added additive byte-progress reporting to the storage download path and surfaced inline progress in `StorageBlobList` and `BlobDetailPane`, including blob-version downloads.
- Moved Blob detail action messaging to a shared pane-level slot so version-tab downloads expose completion state inline.
- Removed the Redis toolbar `Purge All` CTA and replaced it with `Select all loaded` plus namespace-level `All` / `None` subtree helpers for loaded descendants only.
- Reused the existing selected-key delete flow for Redis bulk cleanup instead of adding a new destructive path.
- Repaired the Redis toolbar imperative selection helpers so they queue renders on the Blazor dispatcher instead of calling `StateHasChanged()` directly.
- Tightened the focused storage progress tests to run on the renderer dispatcher and assert localized size formatting.
- Passed the targeted storage/Redis validation subset and a solution build.

## Remaining

- No implementation work remains for the requested scope.
- Next lifecycle step: ship and archive the feature through the normal workflow.

## Blockers

- None.

## Validation

- Test Plan: test-plan.md
- Validation status: Targeted automated validation passed; the feature is review-ready.
- Automated: `runTests` on `RedisToolbarTests.cs`, `RedisNamespaceTreeNodeTests.cs`, `StorageDownloadProgressTests.cs`, and `AzureStorageClientTests.cs` passed (16/16).
- Build: `dotnet build .\SwebKit.slnx -nologo` succeeded.
- Manual: not run for this validation slice.

## Notes

- Redis selection helpers must stay explicitly reviewable before delete; no hidden wildcard delete behavior should be introduced.
- Storage progress is a functional acceptance criterion for large downloads, not cosmetic polish.
