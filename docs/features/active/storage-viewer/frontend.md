# Frontend Plan — Storage Account Viewer

---

title: "Frontend Plan - Storage Account Viewer"
owner: ""
status: "Not started"
created: "2026-03-18"
updated: "2026-03-18"

---

## Goal

Add a Storage page that lets developers browse a configured Azure Storage account directly
inside SwebKit: select a container, navigate virtual folder hierarchies, scan blob lists,
inspect full blob properties, preview text/JSON/XML content inline, and download or copy
URLs — all without touching the Azure portal.

---

## Impacted files

| File | Change |
|---|---|
| `src/SwebKit.App/Components/Pages/StoragePage.razor` | **New** main page |
| `src/SwebKit.App/Components/Pages/StorageConfigForm.razor` | **New** config form |
| `src/SwebKit.App/Components/Storage/StorageContainerTree.razor` | **New** left-pane container tree |
| `src/SwebKit.App/Components/Storage/StorageBlobList.razor` | **New** center blob grid + virtual folder nav |
| `src/SwebKit.App/Components/Storage/BlobDetailPane.razor` | **New** right/tab properties + preview pane |
| `src/SwebKit.App/_Imports.razor` | Add `@using SwebKit.App.Components.Storage` |
| `src/SwebKit.App/Components/Layout/NavMenu.razor` (or equivalent) | Add Storage nav entry |
| `src/SwebKit.App/Components/Pages/SettingsPage.razor` | Add Storage config section |

---

## Component layout

```
StoragePage
├── [Left pane — fixed width ~260px]
│   └── StorageContainerTree
│       ├── Container list (FluentTreeView or FluentNavGroup)
│       │   └── Each entry: icon + container name
│       └── Loading / empty / error states
└── [Center pane — fills remaining width]
    ├── StorageBlobList                      (shown when a container is selected)
    │   ├── Breadcrumb bar                   (root > folder > subfolder)
    │   ├── FluentDataGrid (blobs + prefixes)
    │   │   └── Columns: Icon | Name | Size | Content-Type | Last Modified | Actions
    │   ├── Row actions: Open folder / View details / Copy URL / Copy SAS / Download
    │   └── "Load more" button + item count
    └── BlobDetailPane                       (shown when a blob row is selected)
        ├── Properties table (name, size, content-type, ETag, last modified,
        │   access tier, lease status, content-encoding, cache-control)
        ├── Metadata section (key-value pairs, expandable)
        ├── Tags section (key-value pairs, expandable)
        ├── Action bar: [Download] [Copy URL] [Copy SAS URL]
        └── Content preview panel
            ├── Size warning banner (>512 KB): "This blob is large. Load preview anyway?"
            ├── Binary notice: "Binary content — use Download to inspect this blob."
            ├── Text/JSON/XML viewer (FluentTextArea or code block with pre/code)
            └── [Copy content] button
```

---

## 1 — `StorageConfigForm.razor`

**Path:** `src/SwebKit.App/Components/Pages/StorageConfigForm.razor`

Follows the existing config form pattern (see `ServiceBusConfigForm.razor` or
`ObservabilityConfigForm.razor` for reference layout and validation style).

### Fields

| Field | Control | Notes |
|---|---|---|
| Account Name | `FluentTextField` | Required when `UseAad = true`. Placeholder: `mystorageaccount` |
| Use AAD (DefaultAzureCredential) | `FluentCheckbox` | Toggles credential mode |
| Connection String Ref | `FluentTextField` | Required when `UseAad = false`. Label: "Credential ref key (Windows Credential Manager)". Hidden (or greyed) when `UseAad = true`. |
| Test Connection | `FluentButton` (Appearance="Accent") | Calls `IStorageClient.TestConnectionAsync`; shows spinner + green/red badge |

### Validation behaviour

- If `UseAad = true` and `ConnectionStringRef` is non-empty: show inline warning
  _"Connection String Ref is ignored when AAD is enabled."_ (non-blocking).
- If `UseAad = false` and `ConnectionStringRef` is empty: disable Save, show
  _"Connection String Ref is required."_
- If `UseAad = true` and `AccountName` is empty: disable Save, show
  _"Account Name is required for AAD authentication."_
- Test Connection result: display `TestConnectionAsync` outcome as a badge:
  green "Connected" or red error message from the thrown exception.

### Save behaviour

On Save, write `StorageConfig` into the active `ProjectEnvironment`. Persist via the
existing project/environment persistence mechanism. Configuration is per-environment.

---

## 2 — `StoragePage.razor`

**Path:** `src/SwebKit.App/Components/Pages/StoragePage.razor`

**Route:** `/storage` (add to nav tree alongside existing service pages)

### Layout

Two-column layout using `FluentStack` (or the existing split-pane pattern):
- Left column: fixed width ~260 px, scrollable, contains `StorageContainerTree`.
- Right/center column: fills remaining space; shows `StorageBlobList` when a container
  is selected, or an empty state prompt otherwise.

The `BlobDetailPane` is rendered inside the center column, below or alongside
`StorageBlobList`, as a collapsible/resizable pane. It only renders when `_selectedBlob`
is non-null.

### State owned by `StoragePage`

- `_selectedContainer` — the container name currently expanded in the tree.
- `_currentPrefix` — the virtual folder prefix currently shown in `StorageBlobList`.
- `_selectedBlob` — the `StorageBlobItem` currently selected for detail; null = no detail pane.
- `_storageClient` — resolved from DI on page init, rebuilt on environment change.

### Environment change

On active environment change (via `AppStateService`), rebuild `_storageClient` from the
new environment's `StorageConfig`. If `StorageConfig` is null, show a config prompt:
_"Storage is not configured for this environment."_ with a [Configure] button linking to
`StorageConfigForm`.

---

## 3 — `StorageContainerTree.razor`

**Path:** `src/SwebKit.App/Components/Storage/StorageContainerTree.razor`

### Parameters

| Parameter | Type | Notes |
|---|---|---|
| `Client` | `IStorageClient` | Required |
| `SelectedContainer` | `string?` | Two-way bindable via EventCallback |
| `SelectedContainerChanged` | `EventCallback<string?>` | Parent notified on selection |

### Behaviour

- On first render, call `Client.ListContainersAsync()` and populate the list. Show a
  spinner while loading. Show an `ErrorCallout` on failure.
- Each container renders as a clickable `FluentNavLink` or tree item. Selecting one
  fires `SelectedContainerChanged` and the parent updates `_selectedContainer`.
- No on-demand sub-tree expansion in the container list — containers are flat. Virtual
  folder navigation happens inside `StorageBlobList`.
- A "Refresh" icon button at the top of the pane re-triggers `ListContainersAsync`.
- Guard state before `await` per pitfall BL-3: set `_loadedClient` before any awaits.

---

## 4 — `StorageBlobList.razor`

**Path:** `src/SwebKit.App/Components/Storage/StorageBlobList.razor`

### Parameters

| Parameter | Type | Notes |
|---|---|---|
| `Client` | `IStorageClient` | Required |
| `ContainerName` | `string` | Required; reacts to changes |
| `SelectedBlob` | `StorageBlobItem?` | Two-way via EventCallback |
| `SelectedBlobChanged` | `EventCallback<StorageBlobItem?>` | Fired on row selection |

### Internal state

- `_prefix` — current virtual folder prefix string (e.g. `"images/gallery/"`).
- `_breadcrumbs` — list of `(Label, Prefix)` derived from `_prefix` by splitting on `/`.
- `_items` — current page of `StorageBlobItem` records.
- `_continuationToken` — next page token; null when no more pages.
- `_loading`, `_hasMore`.

### Breadcrumb

Computed from `_prefix` by splitting on `/`. Root entry always shown (label: container
name). Each segment is navigable by click; clicking a segment navigates back (sets
`_prefix` to the prefix up to that segment and reloads). Never re-fetches the full list
from root on back navigation — just reloads for the target prefix.

```
Example:
  prefix = "data/logs/2026/"
  Breadcrumb: [mystoragecontainer] > [data] > [logs] > [2026]
  Clicking [logs] → prefix = "data/logs/" → reload
```

### Blob grid columns

| Column | Source | Notes |
|---|---|---|
| Icon | Based on `IsPrefix` or file extension | Folder icon for prefix; file icon for blob |
| Name | `StorageBlobItem.Name` (last segment only) | Full name shown as tooltip |
| Size | `SizeBytes` formatted (B / KB / MB / GB) | Empty for virtual folders |
| Content-Type | `StorageBlobItem.ContentType` | Empty for virtual folders |
| Last Modified | `LastModified` formatted as `yyyy-MM-dd HH:mm` | Empty for virtual folders |
| Actions | Inline buttons | See below |

### Row actions

| Action | Appears for | Behaviour |
|---|---|---|
| Open folder | `IsPrefix = true` | Set `_prefix` to item prefix; reload |
| View details | `IsBlob = true` | Fire `SelectedBlobChanged(item)` |
| Copy URL | `IsBlob = true` | Build `https://{accountName}.blob.core.windows.net/{container}/{blobName}`; copy to clipboard |
| Copy SAS URL | `IsBlob = true` | Call `Client.GetBlobSasUrlAsync(24h expiry)`; copy; show error toast if fails |
| Download | `IsBlob = true` | Invoke OS save dialog or download to default Downloads folder; call `Client.DownloadBlobAsync` |

### Pagination

Show item count: _"Showing X items"_. When `_hasMore = true`, show a "Load more" button
that fetches the next page and appends to `_items`. Continuation token is held in
`_continuationToken`.

### Container/prefix change

When `ContainerName` changes (parent selects a new container), reset `_prefix = ""`,
`_items`, `_breadcrumbs`, and reload. Set the guard variable before the first await
(pitfall BL-3).

### Rule: `StateHasChanged` after async loads

All `LoadAsync` calls must use `await InvokeAsync(StateHasChanged)` at completion
(pitfall BL-2).

---

## 5 — `BlobDetailPane.razor`

**Path:** `src/SwebKit.App/Components/Storage/BlobDetailPane.razor`

### Parameters

| Parameter | Type | Notes |
|---|---|---|
| `Client` | `IStorageClient` | Required |
| `ContainerName` | `string` | Required |
| `Blob` | `StorageBlobItem` | Required; reacts to changes — fetch properties on change |

### Sections

#### Properties table

Render a two-column key-value table (`FluentDataGrid` or simple `<table>`) with:
Name, Size (formatted), Content-Type, ETag, Last Modified, Access Tier, Tier Inferred,
Lease Status, Lease State, Content-Encoding, Content-Language, Cache-Control.

#### Metadata

Rendered as a collapsible `FluentAccordionItem`. Key-value pairs from
`BlobProperties.Metadata`. Empty state: _"No metadata."_

#### Tags

Same pattern as Metadata. Empty state: _"No tags."_

#### Action bar

`[Download]` — triggers download via `Client.DownloadBlobAsync`.  
`[Copy URL]` — copies direct URL (no SAS) to clipboard.  
`[Copy SAS URL]` — calls `Client.GetBlobSasUrlAsync(expiry: 24h)`; on
`RequestFailedException`, show inline error message (not a dialog — just a red
`FluentMessageBar` below the button bar): _"SAS URL generation failed: [exception message].
The storage account may have shared key access disabled."_

#### Content preview

- On load: call `Client.GetBlobContentAsync` with `maxBytes = 524_288` (512 KB).
- If `IsBinary = true`: show _"Binary content — use Download to inspect this blob."_
  No preview rendered.
- If `WasTruncated = true` AND blob size > 2 MB: show _"Preview limited to 512 KB.
  Download for full content."_ (informational `FluentMessageBar`).
- If `WasTruncated = true` AND blob size is between 512 KB and 2 MB: show
  _"This blob is X KB. Load full preview?"_ with a `[Load]` button that calls
  `GetBlobContentAsync(maxBytes: 2_097_152)`.
- If content type is `application/json`: pretty-print via `JsonSerializer`
  (deserialize + re-serialize with `WriteIndented = true`). If JSON is malformed, fall
  back to raw text display.
- If content type is `application/xml` or `text/xml`: display as-is (no transformation).
  Syntax highlighting is not in scope for MVP.
- All text content rendered in a fixed-font `<pre>` block inside a scrollable container
  (max height ~400px).
- `[Copy content]` button copies the rendered string to the clipboard.

### Loading state

Show a `FluentProgressRing` while properties or content are loading. Show a
`FluentMessageBar` (Severity="Error") on failure. The pane must not block the rest of
the page — load is triggered by `OnParametersSetAsync`, guard set before first await
(pitfall BL-3).

---

## UX decisions

| Decision | Rationale |
|---|---|
| Read-only in MVP | Prevents accidental mutations to production storage. Write operations are a future scope item requiring an explicit per-env toggle. |
| Virtual folder breadcrumb, not a tree | Matches how developers mentally model blob storage. Flattening into a tree creates infinite depth complexity. |
| Size warning at 512 KB, hard cap at 2 MB | 512 KB is comfortably above most config/log blobs (typical use case) but well below the threshold where memory starts to matter on a desktop app. |
| SAS URL errors shown inline, not dialog | Consistent with the error-surfacing pattern on other pages. Dialogs interrupt workflow. |
| Copy direct URL always available; SAS URL on demand | Direct URL works for networks with storage-account-level access. SAS URL is an escape hatch. |
| No delete / upload in MVP | Safe default. Add behind an explicit mutations toggle in a later iteration. |

---

## Frontend tasks

- [ ] 1. Add `@using SwebKit.App.Components.Storage` to `_Imports.razor`
- [ ] 2. Add Storage nav entry (icon + label) to navigation menu
- [ ] 3. Scaffold `StoragePage.razor` (route, layout, environment-change handler, empty state)
- [ ] 4. Scaffold `StorageConfigForm.razor` with fields, validation, and Test Connection
- [ ] 5. Wire `StorageConfigForm` into Settings page
- [ ] 6. Implement `StorageContainerTree.razor` — list, selection, refresh, error state
- [ ] 7. Implement `StorageBlobList.razor` — grid columns, breadcrumb, pagination
- [ ] 8. Implement virtual folder navigation in `StorageBlobList` (prefix management + back nav)
- [ ] 9. Implement row actions in `StorageBlobList` (open folder, view details, copy URL, copy SAS, download)
- [ ] 10. Implement `BlobDetailPane.razor` — properties table, metadata, tags
- [ ] 11. Implement action bar in `BlobDetailPane` (download, copy URL, copy SAS with error handling)
- [ ] 12. Implement content preview in `BlobDetailPane` (binary notice, size warning, JSON pretty-print, copy button)
- [ ] 13. Verify all async state updates use `InvokeAsync(StateHasChanged)` (pitfall BL-2)
- [ ] 14. Verify guard variables are set before first `await` in all `OnParametersSetAsync` overrides (pitfall BL-3)
- [ ] 15. Verify `StorageContainerTree` and `StorageBlobList` handle null / unconfigured `IStorageClient`

## Validation

- Component tests: Not started
- Manual UX checks: connect real storage account; browse containers; navigate virtual folders; preview JSON blob; download blob; copy SAS URL; test error surfaces on unconfigured env
