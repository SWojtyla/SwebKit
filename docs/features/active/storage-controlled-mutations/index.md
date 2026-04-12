# Feature Overview - storage-controlled-mutations

---

title: "Feature Overview - storage-controlled-mutations"
owner: "GitHub Copilot"
status: "Planned"
jira: "not linked"
created: "2026-04-12"
updated: "2026-04-12"

---

## Goal

Evolve the current Storage experience from read-only inspection into a deliberately guarded mutation workspace for uploads, copy, metadata updates, version comparison, and recovery, while keeping production-safety controls explicit and defaulting to safe behavior.

## Value

The Storage page already gives SwebKit a strong read-only blob workflow:

- container and prefix browsing
- property and metadata inspection
- content preview
- version listing and download
- SAS and direct URL copy

Operators still leave SwebKit for common follow-up actions:

- uploading a replacement blob into the same path
- copying a version or blob into a safe recovery path
- editing metadata after an incident or deployment fix
- comparing the current blob against an older version
- restoring soft-deleted or older content

Those are exactly the operations where production-safe controls need to be obvious. This feature makes the actions available in context, but only behind explicit opt-in, capability checks, and confirmation patterns.

## Scope

- In scope:
- Per-storage-account mutation enablement so the page stays read-only unless an environment explicitly allows mutations.
- Upload of a new blob or replacement current version into the selected container and prefix with progress and overwrite preview.
- Same-account server-side copy of a blob or selected version to another path or container.
- Metadata updates with a before-versus-after diff preview.
- Version diff for current-versus-version and version-versus-version comparisons with text-first behavior and safe fallback for binary or oversized blobs.
- Soft-delete or version recovery with capability detection and explicit confirmation.
- Additive Storage client and demo-client support plus focused tests in Azure, App, and Core test projects.
- Out of scope:
- Hard delete, bulk delete, or container-level destructive operations.
- Container creation, deletion, or lifecycle-policy management.
- Cross-account copy from arbitrary external URIs.
- Background transfer queueing or resumable upload management.
- Tag-management workflows beyond current read-only inspection.

> Waves
>
> - Wave 1: mutation safety model plus upload and copy.
> - Wave 2: metadata update and version diff.
> - Wave 3: soft-delete and version recovery.

## Dependencies

- Internal projects and likely touched paths:
- `src/SwebKit.App/Components/Pages/StoragePage.razor`
- `src/SwebKit.App/Components/Pages/StorageConfigForm.razor`
- `src/SwebKit.App/Components/Storage/StorageBlobList.razor`
- `src/SwebKit.App/Components/Storage/BlobDetailPane.razor`
- `src/SwebKit.Core/Abstractions/IStorageClient.cs`
- `src/SwebKit.Core/Domain/StorageConfig.cs`
- `src/SwebKit.Core/Domain/StorageModels.cs`
- `src/SwebKit.Azure/Storage/AzureStorageClient.cs`
- `src/SwebKit.Core/Services/DemoStorageClient.cs`
- Architecture docs expected to be updated when implementation lands:
- `docs/architecture/functionalities/storage.md`
- Pitfall files that apply:
- `docs/pitfalls/blazor-maui.md`
- `docs/pitfalls/azure-sdk.md`
- `docs/pitfalls/dotnet-csharp.md`
- `docs/pitfalls/agent-workflow.md`

## Risks & mitigations

- Risk: uploads or copy actions overwrite important production blobs too easily. - Mitigation: default mutations off per account, show destination summary clearly, and require typed confirmation in production or overwrite flows.
- Risk: different accounts expose different capabilities for versioning, soft delete, tags, and shared key. - Mitigation: detect capabilities explicitly and show `Unavailable` states instead of exposing unusable actions.
- Risk: text diff on large or binary blobs becomes slow or misleading. - Mitigation: keep preview caps, fall back to metadata-only diff, and require explicit `Load anyway` only where already consistent with current preview behavior.
- Risk: mutation UI overwhelms a page that is currently optimized for inspection. - Mitigation: keep read-only mode as the default visual state and group new actions in focused dialogs or version/recovery subpanels.
- Risk: optimistic metadata updates overwrite another actor's changes. - Mitigation: use ETag-aware writes where practical and render the current ETag in the confirmation context.

## Related documents

- Architecture map: `docs/architecture/architecture.md`
- Component design: `docs/architecture/design.md`
- Code navigation: `docs/architecture/codebase-guide.md`
- Functionality deep dive: `docs/architecture/functionalities/storage.md`
- Pitfalls index: `docs/pitfalls/index.md`

## Quick links

- Jira: not linked
- Status: `status.md`
- Tests: `test-plan.md`
- Implementation modules: `backend.md`, `frontend.md`, `decisions.md`
