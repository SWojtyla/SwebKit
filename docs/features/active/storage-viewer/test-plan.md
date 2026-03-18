# Test Plan — Storage Account Viewer

---

title: "Test Plan - Storage Account Viewer"
owner: ""
status: "Not started"
created: "2026-03-18"
updated: "2026-03-18"

---

## Scope

Unit tests for `AzureStorageClient` logic and `StorageConfig` model.
Component render tests for config form and blob list UI.
Manual end-to-end verification against a real Azure Storage account.

No E2E automation against real Azure resources is run in CI — that requires provisioned
test infrastructure not currently in scope.

---

## Unit tests — `SwebKit.Azure.Tests`

Target class: `AzureStorageClient` (using a fake/stub `ICredentialStore` and mocked
`BlobServiceClient` via constructor injection or a test-only factory overload).

### UT-1 — Connection string mode: `BlobServiceClient` built from credential store

**Given** `StorageConfig` with `UseAad = false`, `ConnectionStringRef = "my-ref"`, and
a credential store that returns `"DefaultEndpointsProtocol=https;AccountName=…"` for
key `"my-ref"`  
**When** `AzureStorageClient` is constructed  
**Then** `BlobServiceClient` is created (no exception thrown); no AAD path invoked.

---

### UT-2 — AAD mode: `BlobServiceClient` built from `DefaultAzureCredential` + account name URL

**Given** `StorageConfig` with `UseAad = true`, `AccountName = "myaccount"`  
**When** `AzureStorageClient` is constructed  
**Then** `BlobServiceClient` is created with endpoint `https://myaccount.blob.core.windows.net`
and a `DefaultAzureCredential` instance; `ConnectionStringRef` is ignored.

---

### UT-3 — Invalid config: neither connection string ref nor account name set

**Given** `StorageConfig` with `UseAad = false` and `ConnectionStringRef = null`  
**When** `AzureStorageClient` is constructed  
**Then** `InvalidOperationException` is thrown with a message describing the config requirement.

---

### UT-4 — `TestConnectionAsync` returns `true` when listing succeeds

**Given** a stubbed `BlobServiceClient` whose `GetBlobContainersAsync` enumerator returns
one item  
**When** `TestConnectionAsync` is called  
**Then** returns `true` without throwing.

---

### UT-5 — `TestConnectionAsync` propagates exceptions from the SDK

**Given** a stubbed `BlobServiceClient` whose `GetBlobContainersAsync` throws
`RequestFailedException`  
**When** `TestConnectionAsync` is called  
**Then** `RequestFailedException` is not swallowed and propagates to the caller.

---

### UT-6 — `ListContainersAsync` maps all SDK container properties to model

**Given** a stub returning two `BlobContainerItem` entries with known Name, LastModified,
and LeaseStatus values  
**When** `ListContainersAsync` is called  
**Then** returns two `StorageContainerItem` records whose fields match the stub data exactly.

---

### UT-7 — `ListBlobsAsync` returns blobs and virtual folder prefixes from a single page

**Given** a stubbed page of `BlobHierarchyItem` with two blobs and one virtual folder
prefix at root prefix `""`  
**When** `ListBlobsAsync(containerName, prefix: "", ct)` is called  
**Then** returns a `StorageBlobPage` with three items: two with `IsPrefix = false` and
one with `IsPrefix = true`. `ContinuationToken` matches the stub page token.

---

### UT-8 — `ListBlobsAsync` passes `prefix` and `continuationToken` to the SDK

**Given** a `StorageBlobList` call with `prefix = "data/logs/"` and
`continuationToken = "tok123"`  
**When** `ListBlobsAsync` is called  
**Then** the underlying `GetBlobsByHierarchyAsync` call receives `prefix = "data/logs/"`
and `AsPages` continuation `"tok123"`.

---

### UT-9 — `GetBlobContentAsync` detects binary content and returns early

**Given** a blob whose `ContentType = "image/png"` and `SizeBytes = 4096`  
**When** `GetBlobContentAsync` is called  
**Then** returns `StorageBlobContent` with `IsBinary = true`, `Content = ""`,
`WasTruncated = false`. No byte-range download call is made.

---

### UT-10 — `GetBlobContentAsync` enforces `maxBytes` and sets `WasTruncated`

**Given** a text blob with `ContentType = "application/json"`, `SizeBytes = 1_000_000`
(1 MB), `maxBytes = 524_288`  
**When** `GetBlobContentAsync` is called  
**Then** a range request for `[0, 524_288)` is issued; `WasTruncated = true`;
`TotalSizeBytes = 1_000_000`.

---

## Unit tests — `SwebKit.Core.Tests`

Target class: `StorageConfig` model.

### UT-C1 — `StorageConfig` round-trips through `System.Text.Json` serialization

**Given** a `StorageConfig` with `AccountName = "foo"`, `ConnectionStringRef = "bar"`,
`UseAad = true`  
**When** serialized to JSON and deserialized back  
**Then** all three fields match the original values exactly.

---

### UT-C2 — `StorageConfig` with missing `ConnectionStringRef` field deserializes to null ref

**Given** JSON `{ "AccountName": "foo", "UseAad": false }`  
**When** deserialized to `StorageConfig`  
**Then** `ConnectionStringRef` is null (not a `NullReferenceException`).

---

### UT-C3 — `ProjectEnvironment` serializes and deserializes `StorageConfig` as a nullable field

**Given** a `ProjectEnvironment` with `Storage = null`  
**When** serialized to JSON  
**Then** the `storage` JSON key is absent or null; deserialization produces
`Storage = null` without error.

---

## Component / render tests — `SwebKit.App.Tests`

Using the test harness already established in the project (bUnit or equivalent).

### CT-1 — `StorageConfigForm` shows account name required error when UseAad is true and name is empty

**Given** `StorageConfigForm` rendered with `UseAad = true` and `AccountName = ""`  
**When** the Save button is clicked  
**Then** an error message _"Account Name is required for AAD authentication"_ is visible
and the button is disabled.

---

### CT-2 — `StorageConfigForm` shows inline warning when UseAad is true and ConnectionStringRef is set

**Given** `StorageConfigForm` rendered with `UseAad = true` and `ConnectionStringRef = "my-ref"`  
**When** the form is rendered  
**Then** warning text _"Connection String Ref is ignored when AAD is enabled"_ is visible
as a non-blocking inline message.

---

### CT-3 — `StorageBlobList` renders virtual folder prefix as a folder item with open action

**Given** `StorageBlobList` bound to a stub client returning one `StorageBlobItem` with
`IsPrefix = true`, `Name = "data/"`  
**When** rendered  
**Then** the grid contains one row with a folder icon; no size, content-type, or
last-modified values shown; the "Open folder" action button is present.

---

### CT-4 — `StorageBlobList` breadcrumb renders correct segments for a nested prefix

**Given** `StorageBlobList` with `_prefix = "data/logs/2026/"`  
**When** rendered  
**Then** the breadcrumb bar shows three segments after the container root: "data", "logs",
"2026"; each is clickable.

---

## Manual verification scenarios

### MV-1 — Connect to a real storage account via AAD

1. Configure a Storage environment with `UseAad = true`, `AccountName = <real account>`.
2. Open `StoragePage`. Click "Test Connection" in the config form.
3. **Expected:** green "Connected" badge. No credential errors.
4. Container tree loads. Containers are listed.

---

### MV-2 — Connect to a real storage account via connection string

1. Store a valid connection string in Windows Credential Manager under key `"test-storage"`.
2. Configure storage env with `UseAad = false`, `ConnectionStringRef = "test-storage"`.
3. Open `StoragePage`.
4. **Expected:** containers listed; no AAD dependency triggered.

---

### MV-3 — Browse containers and enter a virtual folder

1. Select a container with virtual folder structure.
2. Click a folder prefix in the blob list.
3. **Expected:** breadcrumb advances; blob list shows contents of that prefix. Back
   navigation (clicking a previous breadcrumb segment) returns to the previous prefix
   without re-fetching from root.

---

### MV-4 — View full properties of a blob

1. Select a blob from the list.
2. **Expected:** `BlobDetailPane` shows name, size (formatted), content-type, ETag, last
   modified, access tier, lease status. Metadata and tags sections expand/collapse.

---

### MV-5 — Preview a JSON blob inline

1. Select a blob with `ContentType = application/json` and size < 512 KB.
2. **Expected:** `BlobDetailPane` shows a pretty-printed JSON block. Copy button
   copies the formatted text to clipboard.

---

### MV-6 — Preview size warning for a large blob

1. Select a blob with size between 512 KB and 2 MB.
2. **Expected:** warning banner visible: _"This blob is X KB. Load full preview?"_ with a
   [Load] button. Clicking [Load] shows the full content.

---

### MV-7 — Download a blob to local filesystem

1. Select any blob, click Download.
2. **Expected:** OS save dialog opens (or file saved to Downloads). Blob written to
   disk without corruption. No size cap applied.

---

### MV-8 — Copy SAS URL

1. Select a blob in a storage account with shared key access enabled.
2. Click "Copy SAS URL".
3. **Expected:** URL copied to clipboard; URL is valid and accessible in a browser
   for the default 24-hour window.

---

### MV-9 — SAS URL error for AAD-only account

1. Select a blob in a storage account with `allowSharedKeyAccess = false`.
2. Click "Copy SAS URL".
3. **Expected:** inline error message: _"SAS URL generation failed… The storage account may
   have shared key access disabled."_ No dialog shown. Direct URL copy still works.

---

### MV-10 — Unconfigured environment shows config prompt

1. Switch to an environment with no `StorageConfig` set.
2. Navigate to Storage page.
3. **Expected:** empty-state message with [Configure] link to `StorageConfigForm`.
   No crash, no null reference errors.

---

## Acceptance criteria

- All unit tests (UT-1 through UT-10, UT-C1 through UT-C3) pass in CI.
- All component render tests (CT-1 through CT-4) pass in CI.
- Manual scenarios MV-1 through MV-10 verified against a real Azure Storage account.
- No unhandled exceptions reach the crash handler for any tested flow.
- `OperationCanceledException` propagates cleanly on cancellation (not swallowed).
- `_Imports.razor` contains `@using SwebKit.App.Components.Storage` before any component
  in that namespace is used.
- `status.md` checklist reflects completion state at time of review.

## Validation status

- Automated: Not started
- Manual: Not started

## Sign-off

- Owner:
- Date:
