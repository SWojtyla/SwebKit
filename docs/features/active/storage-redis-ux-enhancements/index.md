# Feature Overview - storage-redis-ux-enhancements

---

title: "Feature Overview - storage-redis-ux-enhancements"
owner: "GitHub Copilot"
status: "Review"
jira: "not linked"
created: "2026-04-10"
updated: "2026-04-10"

---

## Goal

Improve two high-friction workflows in one small UX pass: show visible progress while blob downloads are running, and replace Redis full-database purge with safer bulk-selection helpers in the namespace tree.

## Value

Large blob downloads currently look idle while work is in progress, which makes the app feel stuck and invites duplicate clicks. Redis currently exposes a destructive page-level purge path that is faster than operators need for routine cleanup and too blunt for namespace-focused maintenance. This feature makes storage downloads observable and makes Redis bulk cleanup explicit, reviewable, and prefix-aware.

## Current state

Implementation is complete for the requested scope, targeted automated validation passed, and the feature remains active in Review until it is shipped or archived.

## Scope

- In scope:
- Progress and active-download state for single-blob downloads launched from the blob list, blob detail pane, and blob versions tab.
- Additive storage client contract changes needed to surface byte progress to the UI.
- Redis toolbar and tree updates that remove the primary `Purge All` action and add full-select helpers for loaded keys and loaded namespace subtrees.
- Reuse the existing selected-keys delete flow after selection rather than inventing a new destructive Redis API.
- Out of scope:
- New storage upload, delete, or move operations.
- Background downloads that continue after the user leaves the page.
- Server-side Redis wildcard delete or prefix delete endpoints.
- Reworking ZIP archive bulk-download progress beyond the current busy indicator.

## Dependencies

- Internal projects and paths:
- src/SwebKit.App/Components/Storage/StorageBlobList.razor
- src/SwebKit.App/Components/Storage/BlobDetailPane.razor
- src/SwebKit.App/Components/Redis/RedisToolbar.razor
- src/SwebKit.App/Components/Redis/RedisNamespaceTree.razor
- src/SwebKit.App/Components/Redis/RedisNamespaceTreeNode.razor
- src/SwebKit.App/Components/Pages/RedisPage.razor
- src/SwebKit.Core/Abstractions/IStorageClient.cs
- src/SwebKit.Core/Abstractions/IRedisClient.cs
- src/SwebKit.Azure/Storage/AzureStorageClient.cs
- Related architecture and functionality docs:
- docs/architecture/functionalities/storage.md
- docs/architecture/functionalities/redis.md
- Cross-feature and external dependencies:
- None. Jira is not linked.

## Risks & mitigations

- Risk: progress UI becomes noisy or inaccurate for tiny downloads.
- Mitigation: only show active progress while a download is running, and prefer determinate progress only when total size is known.
- Risk: subtree selection appears to cover keys that have not been scanned yet.
- Mitigation: scope helpers to loaded keys only and surface selected counts before delete.
- Risk: removing purge-all leaves no fast cleanup path for namespace-heavy caches.
- Mitigation: add `Select all loaded` plus node-level subtree selection so the existing batch delete remains fast but reviewable.
- Risk: high-frequency progress updates can thrash Blazor rendering.
- Mitigation: use SDK-native byte progress and coalesce UI updates when needed.

## Related documents

- Architecture map: docs/architecture/architecture.md
- Component design: docs/architecture/design.md
- Code navigation: docs/architecture/codebase-guide.md
- Storage functionality: docs/architecture/functionalities/storage.md
- Redis functionality: docs/architecture/functionalities/redis.md

## Quick links

- Jira: not linked
- Status: status.md
- Tests: test-plan.md
- Implementation modules: frontend.md, decisions.md
