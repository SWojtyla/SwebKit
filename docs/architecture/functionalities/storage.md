# Storage

## What Is Supported

The React Storage workspace supports multiple configured Azure Storage accounts with connection-string or Entra authentication:

- browse Blob containers and virtual prefixes with URL-backed account/container/prefix/blob state;
- paged blob listing, filtering, sorting, multi-select, and persistent split-pane widths;
- content preview for text/JSON/XML with pretty/raw modes and size gates;
- properties, metadata editing, tags, versions, compare, and restore;
- upload by picker/dropzone with overwrite confirmation;
- download one blob or selected blobs as ZIP;
- copy direct URL, generate/copy SAS URL, and copy blobs between containers;
- browse Azure File shares/directories and inspect/download files;
- recovery view for deleted blobs with explicit restore confirmation; and
- demo-mode data for all primary workflows.

Mutations are gated by each account's `allowMutations` setting. The old read-only-MVP limitation no longer applies.

## Frontend Architecture

`StoragePageContext` is split into seven scoped contexts:

- account selection/configuration;
- blob navigation;
- file-share navigation;
- query/mutation facades;
- browser/filter/upload state;
- detail/version/copy state; and
- stable shared actions.

`StoragePage` composes `BlobBrowserPanel`, `BlobDetailPanel`, `ShareBrowserPanel`, `ShareFileDetailPanel`, and `BlobRecoveryPanel`. Blob drill-down is URL-backed, so deep links and browser history restore account/container/prefix/blob context. Pagination and version-compare state reset during render when their location key changes, preventing one frame of stale data from the previous blob or prefix.

TanStack query/mutation results exposed through context use `web/src/lib/queryFacade.ts` so unrelated local state changes do not invalidate every consumer. `useDropzone` lives in `BlobBrowserPanel`, where its fresh-per-render object cannot churn the provider.

## Sidecar Flow

```text
/storage
  → StoragePageProvider
  → hooks in web/src/lib/hooks/useStorage.ts
  → /api/storage/{accountId}/...
  → IStorageConnectionPool
  → AzureStorageClient or demo client
  → Azure Blob / File Share SDK
```

The sidecar resolves account IDs from `ProfileRepository` and pools clients. Profile saves invalidate Storage clients so edited credentials/account names take effect immediately.

## Credential Modes

| Mode | Required profile data | SDK construction |
| --- | --- | --- |
| Entra | account name | service URI + `DefaultAzureCredential` |
| Connection string | credential-store reference | resolved connection string |

Secrets remain in the credential store. SAS generation may fail when shared-key access is disabled; the UI surfaces the endpoint's actionable error.

## Data and Safety Behavior

- Blob pages carry continuation tokens; `Load more` appends results.
- When a filter has no match but another continuation token exists, the provider can continue paging until it finds a match or reaches the end.
- Binary detection occurs before text preview; large content is size-gated.
- Metadata/version/copy/restore/upload/delete actions are confirmation- or mutation-toggle-gated as appropriate.
- Direct blob URLs use the configured Azure account name, not SwebKit's internal account ID.
- Downloads are initiated by the current UI surface; there is no cross-page transfer manager.

## Main Code Locations

- `web/src/components/storage/StoragePage.tsx`
- `web/src/components/storage/StoragePageContext.tsx`
- `web/src/components/storage/BlobBrowserPanel.tsx`
- `web/src/components/storage/BlobDetailPanel.tsx`
- `web/src/components/storage/BlobRecoveryPanel.tsx`
- `web/src/components/storage/ShareBrowserPanel.tsx`
- `web/src/components/storage/ShareFileDetailPanel.tsx`
- `web/src/components/settings/StorageSettings.tsx`
- `web/src/lib/hooks/useStorage.ts`
- `src-sidecar/Endpoints/StorageEndpoints.cs`
- `src-sidecar/Services/SidecarStorageConnectionPool.cs`
- `src/SwebKit.Core/Abstractions/IStorageClient.cs`
- `src/SwebKit.Core/Domain/StorageConfig.cs`
- `src/SwebKit.Azure/Storage/AzureStorageClient.cs`

## Validation Pointers

- `web/e2e/storage.spec.ts`
- `web/e2e/storage-deferred.spec.ts`
- `web/e2e/storage-recovery.spec.ts`
- `web/e2e/storage-account-switch.spec.ts`
- `tests/SwebKit.Sidecar.Tests/StorageEndpointsMutationTests.cs`
- `tests/SwebKit.Sidecar.Tests/SidecarStorageConnectionPoolTests.cs`
- `tests/SwebKit.Azure.Tests/AzureStorageClientTests.cs`
- `tests/SwebKit.Core.Tests/StorageConfigTests.cs`
