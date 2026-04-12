# Backend Plan - storage-controlled-mutations

---

title: "Backend Plan - storage-controlled-mutations"
owner: "GitHub Copilot"
status: "Not started"

---

## Goal

Add an additive Azure Blob mutation contract that supports guarded upload, copy, metadata updates, version-aware diff data, and recovery while keeping read-only mode as the default runtime posture.

## Impacted areas

- Existing source and model paths:
- `src/SwebKit.Core/Abstractions/IStorageClient.cs`
- `src/SwebKit.Core/Domain/StorageConfig.cs`
- `src/SwebKit.Core/Domain/StorageModels.cs`
- `src/SwebKit.Azure/Storage/AzureStorageClient.cs`
- `src/SwebKit.Core/Services/DemoStorageClient.cs`
- Likely new or expanded support files:
- `tests/SwebKit.Azure.Tests/AzureStorageClientTests.cs`
- `tests/SwebKit.Core.Tests/StorageConfigTests.cs`
- Likely new focused tests such as `StorageMutationContractTests.cs` if the shared model surface grows materially.

## Design

### 1. Mutation enablement is explicit configuration, not an inferred capability

Wave 1 should extend `StorageConfig` with an additive field such as `AllowMutations`.

- Default is `false` so all current environments remain read-only.
- The field is environment-account scoped, not global, because one environment may allow mutation in a sandbox account but not a production account.

This is the feature's primary safety boundary.

### 2. Capability detection is separate from permission to mutate

Even when `AllowMutations` is true, not every account or credential supports every action. The client should expose a capability snapshot that can answer questions such as:

- Is blob versioning enabled?
- Is soft delete enabled?
- Is metadata update allowed for the current credential?
- Can same-account copy be performed?

Capability detection should use Azure Storage service properties and best-effort operation probes where required, returning explicit `Unavailable` states rather than implicit UI failures.

### 3. Mutation contracts stay single-blob scoped

The first implementation should avoid bulk or wildcard operations. Likely additive client methods:

- `UploadBlobAsync(containerName, blobName, source, options, progress, ct)`
- `CopyBlobAsync(sourceContainer, sourceBlobName, destinationContainer, destinationBlobName, sourceVersionId, overwrite, ct)`
- `SetBlobMetadataAsync(containerName, blobName, metadata, ifMatchEtag, ct)`
- `GetStorageCapabilitiesAsync(ct)`
- `RestoreBlobVersionAsync(containerName, blobName, versionId, ct)`
- `UndeleteBlobAsync(containerName, blobName, ct)`

Existing read methods should likely gain optional `versionId` support for property and content retrieval so the diff UI does not need a second ad hoc contract.

### 4. Recovery should preserve history where possible

When versioning is enabled, restoring a version should be implemented as copy-forward into the current blob path so the recovered content becomes a new current version and history remains intact.

When only soft delete is enabled, undelete should restore the blob if the account supports it. The client should report when the account cannot support recovery rather than exposing a dead action.

### 5. Writes should be overwrite-aware and ETag-aware

Uploads and copy actions need an explicit overwrite flag. Metadata updates should use ETag-aware writes where practical so SwebKit does not silently overwrite a blob that changed between inspection and mutation.

## API / Contracts

- Likely additive model changes in `StorageModels.cs`:
- `StorageCapabilities`
- `BlobUploadOptions`
- `BlobCopyOptions`
- `BlobMetadataDiff`
- `BlobVersionComparison`
- `BlobRecoveryState`
- Likely additive config change in `StorageConfig.cs`:
- `AllowMutations`
- Existing methods that likely need optional `versionId` support:
- `GetBlobPropertiesAsync`
- `GetBlobContentAsync`
- `DownloadBlobAsync` already supports `versionId` and should remain the canonical download seam.
- Backward compatibility notes:
- Existing read-only clients continue to work when `AllowMutations` is absent or false.
- Existing `profiles.json` should deserialize with the new field defaulting safely to false.

## Tasks

### Wave 1 - Mutation policy plus upload and copy [dotnet-expert]

- [ ] Extend `StorageConfig` with an additive mutation opt-in field and update serialization coverage.
- [ ] Add capability snapshot models and client methods.
- [ ] Implement upload and same-account copy in `AzureStorageClient` and `DemoStorageClient`.
- [ ] Add tests for overwrite intent, capability reporting, and progress-aware uploads.

### Wave 2 - Metadata updates and version diff support [dotnet-expert]

- [ ] Add metadata update contract and ETag-aware write path.
- [ ] Extend existing property/content retrieval to support version-aware compare workflows.
- [ ] Add diff-support models that the UI can consume without embedding Blob SDK logic.
- [ ] Add tests for metadata patch behavior and version-aware retrieval.

### Wave 3 - Recovery [dotnet-expert]

- [ ] Implement copy-forward restore for versioned blobs.
- [ ] Implement undelete support for soft-deleted blobs where available.
- [ ] Return explicit `Unavailable` or `Not supported` results when the account cannot recover content.
- [ ] Update Storage functionality documentation after implementation is validated.

## Migration and runtime changes

- `StorageConfig` gains additive mutation state with a safe default of `false`.
- No infrastructure deployment change is required, but useful recovery or diff experiences depend on account features such as versioning and soft delete.
- Runtime behavior must not change for existing environments until the new opt-in flag is enabled.

## Validation

- Unit tests: Not started
- Integration tests: Not started
- Manual checks:
- Verify the client reports unavailable capabilities cleanly under both AAD and connection-string auth.
- Verify overwrite-aware methods require explicit intent.
- Verify recovery paths preserve history on version-enabled accounts.

## Notes

- Apply `docs/pitfalls/azure-sdk.md` guidance when adding pageable or capability queries and when handling auth-dependent feature differences.
- Apply `docs/pitfalls/dotnet-csharp.md` guidance for cancellation and exception flow in uploads and recovery operations.
- Single-blob mutation scope is part of the safety design, not a temporary omission.
