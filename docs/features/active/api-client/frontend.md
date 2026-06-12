# Frontend — API Client

## Route and Page

**Route:** `/api-client`  
**Page:** `src/SwebKit.App/Components/Pages/ApiClientPage.razor`  
**Subfolder:** `src/SwebKit.App/Components/ApiClient/`  
**`_Imports.razor` addition** (BL-1 guard): `@using SwebKit.App.Components.ApiClient`

---

## Shell Layout

Three-pane resizable layout using the existing splitter JS interop.

```
┌───────────────────────────────────────────────────────────────────────────┐
│  Toolbar: [+ Collection] [+ Request] [  Env: production ▼ ]  [↓ Export]  │
├──────────────────┬──────────────────────────────┬─────────────────────────┤
│  Collection Tree │  Request Builder              │  Response Viewer        │
│  ─────────────── │  ─────────────────────────── │  ─────────────────────  │
│  🔍 Search       │  [GET ▼] https://{{base}}/... │  ● 200 OK  142ms  4.2KB │
│                  │  ──────────────────── [Send]  │  ─────────────────────  │
│  ▶ My Collection │  Params │ Headers │ Body │Auth│  Headers │ Body │ Raw   │
│    ├ 📁 Users    │                              │                         │
│    │ ├ GET /     │  [editor area]               │  [Monaco read-only]     │
│    │ └ POST /    │                              │                         │
│    └ POST /login │                              │                         │
└──────────────────┴──────────────────────────────┴─────────────────────────┘
```

Pane widths persisted in `UiStateRepository` (same pattern as other page pane splits).

---

## Component Inventory

### `ApiClientPage.razor`

- Injects `CollectionRepository`, `EnvironmentRepository`, `IHttpRequestExecutor`,
  `IVariableSubstitutionService`, `OAuth2TokenManager`
- Owns page-level state: selected collection, selected request, active environment,
  last response, loading/cancellation state
- Wires the three panels and the toolbar
- On navigation away: calls `CancellationTokenSource.Cancel()` to abort in-flight requests
  and calls `IAsyncDisposable.DisposeAsync()` on any active `IWebSocketClientService`

### `CollectionTree.razor`

**Performance approach:** the tree is flattened into a `List<FlatTreeNode>` for `<Virtualize>`.
Expand/collapse operations update the flattened view directly — no recursive Blazor rendering.

```csharp
public record FlatTreeNode(CollectionNode Node, int Depth, bool IsExpanded,
    bool HasChildren, string CollectionId);
```

- Right-click context menu: Rename, Duplicate, Delete, Add Folder, Add Request
- Double-click / single-click opens request in center panel
- Folder nodes show child count badge when collapsed
- Search bar at top filters the flattened list by `Name.Contains(query, OrdinalIgnoreCase)`
  — no tree re-building required

### `RequestBuilderPanel.razor`

- **Method selector:** `GET`, `POST`, `PUT`, `PATCH`, `DELETE`, `HEAD`, `OPTIONS`,
  `GRAPHQL`, `WS` (last two route to specialised sub-panels)
- **URL bar:** text input; `{{variable}}` tokens highlighted blue via Monaco inline decorations
  (Phase 8 polish; plain text input in Phase 2)
- **Tab strip:** `Params` | `Headers` | `Body` | `Auth`
- **Send button:** Ctrl+Enter shortcut; shows spinner and [Cancel] during execution
- Switches entirely to `WebSocketPanel` when method = WS; shows `GraphQlBodyPanel` when GRAPHQL

#### Body sub-components

| Sub-component | Body type | Editor |
|--------------|-----------|--------|
| `BodyEditorJson.razor` | JSON | Monaco (`json` mode, editable) |
| `BodyEditorXml.razor` | XML | Monaco (`xml` mode, editable) |
| `BodyEditorText.razor` | Plain text | Monaco (`plaintext` mode) |
| `KeyValueGrid.razor` | Form Data | Reusable key-value grid (same as Params / Headers) |
| `BodyEditorBinary.razor` | Binary | File picker; stores temp path in state |

`KeyValueGrid.razor` is a reusable shared component used in Params, Headers, and Form Data tabs.
It supports: add row, delete row, enable/disable toggle per row, drag-reorder (Phase 8).

#### Auth sub-components

| Sub-component | Auth type |
|--------------|-----------|
| `BearerAuthForm.razor` | Bearer — token input (masked), credential-store backed |
| `ApiKeyAuthForm.razor` | API Key — key name, value, placement radio (Header / Query Param) |
| `BasicAuthForm.razor` | Basic — username + password (password masked) |
| `OAuth2AuthForm.razor` | OAuth 2 — flow selector, token URL, client ID, scopes, [Get Token] |

Auth form values are loaded from / saved to `ICredentialStore` via the parent page's
`OAuth2TokenManager` or direct `ICredentialStore` calls. The `AuthConfig` on
`HttpRequestEntry` carries only the `CredentialKey`, never the secret.

### `GraphQlBodyPanel.razor`

Replaces the normal Body tab content when method = GRAPHQL.

- Left: Monaco query editor (`graphql` mode)
- Right: Monaco variables editor (`json` mode)
- Footer: [Introspect Schema] button — triggers `__schema` introspection and feeds result to
  `monaco-graphql` for autocomplete; schema shown in collapsible explorer panel

### `WebSocketPanel.razor`

Standalone panel replacing the three-panel layout when method = WS.

```
┌── WebSocket ──────────────────────────────────────────────────────────────┐
│  [wss://{{base}}/ws]  [Connect]           ● Connected                     │
├───────────────────────────────────────────────────────────────────────────┤
│  Message log (virtualized)                              [Clear log]        │
│  ↓ 12:41:03  {"event":"connected"}                                        │
│  ↑ 12:41:05  {"action":"subscribe","channel":"prices"}                    │
│  ↓ 12:41:05  {"event":"ack"}                                              │
├───────────────────────────────────────────────────────────────────────────┤
│  [Text ▼]  [message to send...]                              [Send]        │
└───────────────────────────────────────────────────────────────────────────┘
```

- Message type selector: Text / Binary (binary shows hex representation)
- `<Virtualize>` on the message log to handle thousands of frames
- Connection state badge: Disconnected (grey) / Connecting (yellow) / Connected (green) / Faulted (red)
- Consumes `IWebSocketClientService` via `IAsyncEnumerable<WebSocketMessage>` in a background task;
  posts to UI via `InvokeAsync(StateHasChanged)` (BL-2 guard)

### `ResponseViewerPanel.razor`

- Status badge: colour-coded (2xx green, 3xx blue, 4xx amber, 5xx red)
- Metadata row: elapsed time, response size, content type
- Tab strip: **Headers** | **Body** | **Raw**
- Body: Monaco read-only with auto-detected language (JSON, XML, HTML, plaintext)
- JSON auto-pretty-print on load when content-type is `application/json`
- Body > 500 KB: shows first 500 KB + `[Load full response (X MB)]` affordance
- Copy-to-clipboard button on body panel

### `EnvironmentManagerPanel.razor`

Accessible via toolbar environment switcher dropdown → [Manage Environments].

- List of environments with [+ New] [✎ Edit] [🗑 Delete] per row
- Active environment highlighted; clicking a row sets it as active for the page session
- `EnvironmentEditor.razor` — inline edit within a dialog:
  - Variable grid: key | type (Plain / Secret / Key Vault) | value
  - Secret type: value shown as `••••••`; stored/loaded via `ICredentialStore`
  - Key Vault type: input = KV secret name; resolved live at send time
  - [Test resolution] button resolves the selected variable against the current environment
    for preview (shows result or `[KV_UNAVAILABLE]`)

### `CollectionExportDialog.razor`

Triggered from toolbar [↓ Export] button.

- Format selector: SwebKit Native | Postman v2.1 | Bruno
- "Include environments" checkbox (default: checked for SwebKit, unchecked for others)
- [Export collection] → file save dialog
- [Import] → file open dialog; format auto-detected via `ICollectionImporter.CanImport`

---

## Navigation

**`LeftNav.razor` addition:**

```razor
<NavLink href="/api-client" Match="NavLinkMatch.Prefix">
    <span class="nav-icon">⚡</span>
    API Client
</NavLink>
```

Placed in the same nav group as Service Bus, Storage, Redis (integration tools section).

---

## Keyboard Shortcuts

Registered in `CommandRegistry.cs` (same pattern as existing global shortcuts):

| Shortcut | Action | Scope |
|----------|--------|-------|
| `Ctrl+Enter` | Send current request | API Client page active |
| `Ctrl+N` | New request in active collection | API Client page active |
| `Ctrl+Shift+N` | New collection | API Client page active |
| `Ctrl+E` | Open environment manager | API Client page active |
| `Escape` | Cancel in-flight request | API Client page active |

---

## Monaco Editor Integration

Monaco is already integrated for YAML (AKS) and log viewing (Observability). The existing
`wwwroot/js/` interop modules are extended:

**New JS module:** `src/SwebKit.App/wwwroot/js/apiClientEditor.js`

Exports:
- `initEditor(containerId, language, readOnly, initialContent)` → returns editor instance ref
- `setContent(editorRef, content)`
- `getContent(editorRef)` → `string`
- `setLanguage(editorRef, language)`
- `setSchema(editorRef, schemaJson)` → feeds GraphQL schema to `monaco-graphql`
- `disposeEditor(editorRef)`

**Lazy loading:** Monaco JS is loaded via dynamic `import()` on first invocation of `initEditor`.
The module is NOT imported at app startup. This keeps the initial WebView load time unaffected.

**Languages registered:** `json`, `xml`, `html`, `graphql` (via `monaco-graphql`), `plaintext`

---

## Performance Guards

| Surface | Guard | Implementation |
|---------|-------|----------------|
| Collection tree | Flattened `<Virtualize>` list | `FlatTreeNode` list; expand collapses in-place |
| WebSocket message log | `<Virtualize>` on message list | Max 10 000 frames retained in memory |
| Response body | 500 KB cap | Read limited in `HttpRequestExecutor`; [Load full] affordance |
| Monaco init | Lazy load | Dynamic JS `import()` on first editor mount |
| Collection load | Repository returns list in one read | No per-request lazy loading from disk |
