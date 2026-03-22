# Storage QOL — Detailed Implementation Plan

**Parent:** [QOL Improvements Catalog](index.md)
**Status:** Planned
**Covers:** STG-3 through STG-12 (STG-1 blob upload and STG-2 blob delete are out of scope here)

---

## Blob Operations

### STG-3 — Bulk operations: checkbox multi-select for bulk download (zip)

**Problem.** `StorageBlobList.razor` has no multi-select. Users must download blobs one at a time
from the context menu.

**Approach.** Gate behind the existing toolbar pattern. Add a "Select" toggle button above the blob
table. When active, prepend a checkbox column to the table; each checked row adds the blob to
`_selectedBlobs`.

**State additions to `StorageBlobList.razor`:**

```csharp
private bool _multiSelectMode = false;
private readonly HashSet<string> _selectedBlobs = new(StringComparer.Ordinal);
```

**UI changes.** Header checkbox column (select all / deselect all on tick). Row checkbox bound to
`_selectedBlobs`. A floating action bar appears above the table footer when `_selectedBlobs.Count > 0`:

```razor
@if (_multiSelectMode && _selectedBlobs.Count > 0)
{
    <div class="bulk-action-bar">
        <span>@_selectedBlobs.Count selected</span>
        <FluentButton @onclick="BulkDownloadAsync" Disabled="@_loading">
            Download as ZIP
        </FluentButton>
        <FluentButton @onclick="() => _selectedBlobs.Clear()">Clear</FluentButton>
    </div>
}
```

**`BulkDownloadAsync`.** Iterates `_selectedBlobs`, streams each blob into a `ZipArchive` (using
`System.IO.Compression`), then saves the archive to Downloads:

```csharp
private async Task BulkDownloadAsync()
{
    var blobNames = _selectedBlobs.ToList();
    var zipName = $"blobs-{DateTime.Now:yyyyMMdd-HHmmss}.zip";
    var destPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        "Downloads", zipName);

    await using var fs = File.OpenWrite(destPath);
    using var zip = new System.IO.Compression.ZipArchive(fs, System.IO.Compression.ZipArchiveMode.Create);
    foreach (var blobName in blobNames)
    {
        var entry = zip.CreateEntry(Path.GetFileName(blobName));
        await using var entryStream = entry.Open();
        await Client.DownloadBlobAsync(ContainerName, blobName, entryStream);
    }
    Notifications.ShowSuccess("Bulk download complete", zipName);
    _selectedBlobs.Clear();
    _multiSelectMode = false;
}
```

**No interface change.** `IStorageClient.DownloadBlobAsync` already exists. `DemoStorageClient` must
implement `DownloadBlobAsync` to write demo content (or a stub byte array) to the destination stream.

**CSS.** Add `.bulk-action-bar` rule to `StorageBlobList.razor.css` (or scoped styles):
flex row, `padding: 6px 12px`, `background: var(--color-surface-2)`, `border-top: 1px solid var(--color-border)`.

---

### STG-4 — SAS expiry customisation

**Problem.** `CopySasAsync` in `StorageBlobList.razor` (line 274) hard-codes
`TimeSpan.FromHours(24)`. Users have no control over expiry.

**Approach.** Replace the single "Copy SAS URL (24 h)" context menu item with a submenu or a
duration picker dialog. The simpler approach (no new component) is to expand the context menu with
preset options and a custom entry:

```razor
<ContextMenu @ref="_ctxMenu">
    <button class="ctx-item" @onclick="() => CopyUrlAsync(_ctxItem!)">Copy URL</button>
    <div class="ctx-separator"></div>
    <button class="ctx-item" @onclick="() => CopySasWithExpiryAsync(_ctxItem!, TimeSpan.FromHours(1))">Copy SAS (1 h)</button>
    <button class="ctx-item" @onclick="() => CopySasWithExpiryAsync(_ctxItem!, TimeSpan.FromHours(8))">Copy SAS (8 h)</button>
    <button class="ctx-item" @onclick="() => CopySasWithExpiryAsync(_ctxItem!, TimeSpan.FromHours(24))">Copy SAS (24 h)</button>
    <button class="ctx-item" @onclick="() => CopySasWithExpiryAsync(_ctxItem!, TimeSpan.FromDays(7))">Copy SAS (7 d)</button>
    <button class="ctx-item" @onclick="() => CopySasWithExpiryAsync(_ctxItem!, TimeSpan.FromDays(30))">Copy SAS (30 d)</button>
    <button class="ctx-item" @onclick="() => OpenCustomSasDialog(_ctxItem!)">Copy SAS (custom…)</button>
    <div class="ctx-separator"></div>
    <button class="ctx-item" @onclick="() => DownloadAsync(_ctxItem!)">Download</button>
</ContextMenu>
```

Extract the existing `CopySasAsync` body into `CopySasWithExpiryAsync(StorageBlobItem item, TimeSpan expiry)`.

For the custom option, add a small inline state:

```csharp
private bool _customSasVisible = false;
private StorageBlobItem? _customSasItem;
private int _customSasHours = 24;
```

Render a `<FluentDialog>` or a small overlay when `_customSasVisible == true` with a numeric input
for hours and a Confirm button that calls `CopySasWithExpiryAsync(_customSasItem!, TimeSpan.FromHours(_customSasHours))`.

**`IStorageClient.GetBlobSasUrlAsync`** already accepts a `TimeSpan expiry` parameter. No interface
change required.

**Notification message.** Update the success notification to include the actual expiry:
`$"SAS URL copied (expires {FormatExpiry(expiry)})"`.

---

### STG-5 — Copy blob relative path context menu option

**Problem.** The context menu offers "Copy URL" (full HTTPS URL) and "Copy SAS URL" but not the
blob name relative to the container root, which is what most API callers need.

**Change.** Add a "Copy relative path" item to the `ContextMenu` in `StorageBlobList.razor`, between
Copy URL and Copy SAS:

```razor
<button class="ctx-item" @onclick="() => CopyRelativePathAsync(_ctxItem!)">Copy relative path</button>
```

```csharp
private async Task CopyRelativePathAsync(StorageBlobItem item)
{
    try
    {
        await JS.InvokeVoidAsync("navigator.clipboard.writeText", item.Name);
        _actionOk = true;
        _actionMessage = "Relative path copied.";
        Notifications.ShowSuccess("Copied", item.Name);
    }
    catch (Exception ex)
    {
        _actionOk = false;
        _actionMessage = $"Copy failed: {ex.Message}";
    }
    await InvokeAsync(StateHasChanged);
}
```

`item.Name` is already the full blob path relative to the container root (e.g.,
`folder/subfolder/file.json`). No interface or backend change required.

---

### STG-6 — Blob versioning support (Versions tab in BlobDetailPane)

**Problem.** `BlobDetailPane.razor` has no visibility into historical blob versions even when
versioning is enabled on the container.

**Interface change.** Add to `IStorageClient`:

```csharp
Task<IReadOnlyList<BlobVersionItem>> ListBlobVersionsAsync(
    string containerName,
    string blobName,
    CancellationToken ct = default);
```

**Model** (add to `StorageModels.cs`):

```csharp
public record BlobVersionItem(
    string VersionId,
    DateTimeOffset? CreatedOn,
    long? ContentLength,
    bool IsCurrentVersion);
```

**Implementation** in `AzureStorageClient.cs`. Use `BlobContainerClient.GetBlobsAsync` with
`BlobTraits.None`, `BlobStates.Version`, and `prefix = blobName`. Filter items where `item.Name == blobName`.
Map `item.VersionId`, `item.Properties.CreatedOn`, `item.Properties.ContentLength`, and
`item.IsCurrentVersion`.

**`DemoStorageClient`.** Return two synthetic `BlobVersionItem`s for any blob — one current, one
historical (timestamp 7 days prior, smaller size).

**UI in `BlobDetailPane.razor`.** Add a tab bar at the top of the pane (`Properties` | `Versions`).
The Versions tab renders a table:

```
Version ID               Created               Size          Current?
2024-11-01T10:00:00Z     2024-11-01 10:00      12.3 KB       ✓
2024-10-28T08:30:00Z     2024-10-28 08:30      11.9 KB       —
```

Each row has a "Download" button calling `Client.DownloadBlobAsync(containerName, blobName, stream)`
with the blob name suffixed by `?versionId=...` — or using the SDK's `BlobClient(blobName)
.WithVersion(versionId)`. The download helper in `BlobDetailPane` should be extended to accept an
optional `versionId` parameter.

**Guard.** If `ListBlobVersionsAsync` returns an empty list or throws
`RequestFailedException (BlobAccessTierNotSupportedForAccountType)`, show an inline note:
"Versioning is not enabled on this container."

---

### STG-7 — Binary detection via magic bytes (first 512 bytes sniff)

**Problem.** `GetBlobContentAsync` in `AzureStorageClient.cs` returns `IsBinary = true` only when
the content-type is not in a text allowlist. Blobs uploaded without a correct content-type (e.g.,
defaulting to `application/octet-stream`) are shown as binary even if they are valid UTF-8 text.

**Approach.** After the content-type check, if `IsBinary == true`, download the first 512 bytes and
apply a magic-byte heuristic:

1. Read `Math.Min(512, blob.Length)` bytes using a byte-range request:
   `blobClient.DownloadContentAsync(new HttpRange(0, 512))`.
2. Apply `BinaryContentDetector.IsBinary(ReadOnlySpan<byte> sample)`:
   - Return `false` (treat as text) if the sample is valid UTF-8 and contains fewer than 5% non-printable bytes (< 32 excluding `\t`, `\r`, `\n`).
   - Return `true` for known binary magic bytes: `%PDF`, `PK\x03\x04` (zip), `\x89PNG`, `\xFF\xD8\xFF` (JPEG), `GIF8`, `\x1F\x8B` (gzip), `MZ` (PE exe).
3. Override `IsBinary` based on the sniff result.

**`BinaryContentDetector`** — new static class in `SwebKit.Azure/Storage/BinaryContentDetector.cs`.

**`StorageBlobContent` model** — add a `DetectionMethod` field (`"content-type"` | `"magic-bytes"`)
for diagnostics, shown as a small tooltip in `BlobDetailPane`.

**`DemoStorageClient`.** Already returns pre-set content strings; `IsBinary` remains driven by
the mock content-type. No change needed.

**Tests.** Add to `AzureStorageClientTests.cs` or a dedicated `BinaryContentDetectorTests.cs`:
PNG header → binary; `{"key":"val"}` bytes → not binary; null bytes present → binary.

---

## Container Browser

### STG-8 — Container-level properties tooltip/badges

**Problem.** `StorageContainerTree.razor` shows only the container name. Users cannot tell whether a
container has a public access level or an active lease without navigating into it.

**Approach.** Extend `StorageContainerItem` in `StorageModels.cs` to carry additional properties:

```csharp
public record StorageContainerItem(
    string Name,
    DateTimeOffset? LastModified,
    string? LeaseStatus,        // "locked" | "unlocked" | null
    string? PublicAccess);      // "blob" | "container" | null (= private)
```

`AzureStorageClient.ListContainersAsync` already calls `GetContainersAsync()` which includes
`BlobContainerProperties`. Map `p.LeaseStatus.ToString()`, `p.LastModified`, and
`p.PublicAccess.ToString()` (converting SDK enum to nullable string).

**UI in `StorageContainerTree.razor`.** Render inline badges after the container name when the
container row is hovered or always when the value is non-default:

```razor
<span class="container-name">@container.Name</span>
@if (container.PublicAccess is "blob" or "container")
{
    <span class="badge badge-warn" title="Public access: @container.PublicAccess">Public</span>
}
@if (container.LeaseStatus == "locked")
{
    <span class="badge badge-info" title="Lease: locked">Leased</span>
}
```

Add a `FluentTooltip` on the container row showing `Last Modified: {LastModified:yyyy-MM-dd HH:mm}`.

**`DemoStorageClient`.** Seed `DemoContainerItems` with realistic varied property values (one locked,
one with public access, others private).

---

### STG-9 — Sorting options for blob list

**Problem.** Blobs appear in server listing order (alphabetical by name within a prefix page). Users
cannot sort by size or last modified.

**Approach.** Client-side sort applied to `_items` in `StorageBlobList.razor` after each page load.
Folder prefixes (`IsPrefix == true`) always sort to the top regardless of the sort column.

**State additions:**

```csharp
private string _sortColumn = "name";   // "name" | "size" | "modified"
private bool _sortAscending = true;
```

**Sorted items computed property:**

```csharp
private IEnumerable<StorageBlobItem> SortedItems =>
    _items.Where(i => i.IsPrefix)
          .Concat(_items.Where(i => !i.IsPrefix).OrderBy(i => _sortColumn switch {
              "size"     => (object)(i.SizeBytes ?? 0),
              "modified" => i.LastModified ?? DateTimeOffset.MinValue,
              _          => i.Name
          }, _sortAscending ? Comparer<object>.Default : ReverseComparer.Instance));
```

**UI.** Make table headers into sort-toggle buttons. Add a sort indicator (↑ / ↓) to the active
column header. On header click, toggle direction if the same column; switch column and reset to
ascending otherwise.

```razor
<th @onclick="() => SetSort("name")" class="sortable @(SortClass("name"))">Name</th>
<th @onclick="() => SetSort("size")" class="sortable @(SortClass("size"))">Size</th>
<th @onclick="() => SetSort("modified")" class="sortable @(SortClass("modified"))">Last Modified</th>
```

**Note.** Sort is applied only to loaded items. With "Load more" pagination, sort is not global
across all server pages — add a note in the UI ("Sorted within loaded items").

---

### STG-10 — Search/filter blobs in container

**Problem.** With many blobs in a prefix folder, there is no way to filter without knowing the exact
prefix to navigate to.

**Approach.** Add a filter input above the blob table. Two modes:

1. **Client-side filter** — typing filters `_items` by `item.Name.Contains(filter, OrdinalIgnoreCase)`.
   Fast; works on already-loaded items.
2. **Server-side prefix filter** — if the filter text does not contain wildcards, append it to
   `_prefix` and call `ListBlobsAsync(ContainerName, _prefix + filter, reset: true)`. This is
   useful when the container has more blobs than the loaded page.

Start with client-side only (simpler, no interface change). Add a "Search server-side" button for
cases where results are truncated.

**State:**

```csharp
private string _filterText = string.Empty;
```

**Filtered items computed property:**

```csharp
private IEnumerable<StorageBlobItem> FilteredItems =>
    string.IsNullOrWhiteSpace(_filterText)
        ? SortedItems
        : SortedItems.Where(i => i.Name.Contains(_filterText, StringComparison.OrdinalIgnoreCase));
```

**UI.** Render a search input in the breadcrumb bar area (right-aligned):

```razor
<input type="search" placeholder="Filter…" @bind="_filterText" @bind:event="oninput"
       class="blob-filter-input" />
```

Replace `@foreach (var item in _items)` with `@foreach (var item in FilteredItems)` in the table body.
Show a "X of Y items match" count label when filter is active.

**No `IStorageClient` interface change** for the basic implementation.

---

## Config & Auth

### STG-11 — Connection string masking in StorageConfigForm

**Problem.** `StorageConfigForm.razor` displays the `ConnectionStringRef` value in a plain text
input. During screen sharing the credential key is visible.

**Approach.** The `ConnectionStringRef` field is a logical key into `ICredentialStore`, not the
actual connection string itself — so it is less sensitive than a raw connection string. However it
still reveals the naming convention. Apply the same password-field approach as RDS-10:

Replace the `<input type="text">` for `ConnectionStringRef` with:

```razor
<input type="@(_showConnRef ? "text" : "password")"
       @bind="Config.ConnectionStringRef"
       class="form-input" autocomplete="off" />
<button type="button" @onclick="() => _showConnRef = !_showConnRef" class="icon-btn">
    @(_showConnRef ? "🙈" : "👁")
</button>
```

Add `private bool _showConnRef = false;` to the component.

When the shared `PasswordField` component from UI-19 lands, swap this for
`<PasswordField @bind-Value="Config.ConnectionStringRef" />`.

No backend or interface change required.

---

### STG-12 — Container-level SAS action

**Problem.** Users can only generate blob-level SAS tokens. To share access to an entire container
(e.g., for bulk operations by a downstream service), they need a container SAS.

**Interface change.** Add to `IStorageClient`:

```csharp
Task<string> GetContainerSasUrlAsync(
    string containerName,
    TimeSpan expiry,
    CancellationToken ct = default);
```

**Implementation in `AzureStorageClient.cs`.** Get a `BlobContainerClient`, call
`GenerateSasUri(BlobContainerSasPermissions.Read | BlobContainerSasPermissions.List, expiry)`.
Throws `RequestFailedException` (AuthorizationPermissionMismatch) when shared key access is
disallowed — same pattern as blob SAS, same inline error surface.

**`DemoStorageClient`.** Return a synthetic `https://demo.blob.core.windows.net/{containerName}?sv=...` string.

**UI in `StorageContainerTree.razor`.** Add a context menu (right-click or a "⋮" action button) on
each container row with options:

```
Copy container SAS (1 h)
Copy container SAS (24 h)
Copy container SAS (7 d)
Copy container SAS (custom…)
```

The custom option follows the same pattern as STG-4: small duration input dialog.

Success notification: `"Container SAS copied (expires {duration})"`.

This context menu is separate from the blob-list context menu in `StorageBlobList.razor` and lives
entirely in `StorageContainerTree.razor`.

---

## Implementation Order

1. **STG-11** (connection string masking) — isolated UI change, no interface impact, highest priority for security hygiene.
2. **STG-5** (copy relative path) — one context menu item, no interface changes.
3. **STG-9** (blob list sorting) — client-side only, no interface changes; improves usability immediately.
4. **STG-10** (blob filter/search) — client-side filter builds on STG-9's computed property pattern.
5. **STG-4** (SAS expiry customisation) — extends existing `CopySasAsync`; requires careful UI for the custom dialog but no interface changes.
6. **STG-8** (container properties badges) — requires extending `StorageContainerItem` model and `ListContainersAsync` mapping.
7. **STG-7** (binary detection via magic bytes) — adds `BinaryContentDetector` utility class; self-contained but touches `AzureStorageClient`.
8. **STG-3** (bulk download as ZIP) — requires multi-select state machinery and `System.IO.Compression` usage; do after simpler items are stable.
9. **STG-12** (container-level SAS) — requires new `IStorageClient` method, `AzureStorageClient` implementation, demo stub, and tree context menu.
10. **STG-6** (blob versioning) — largest change: new interface method, new model, SDK version API, new Versions tab in `BlobDetailPane`; do last.
