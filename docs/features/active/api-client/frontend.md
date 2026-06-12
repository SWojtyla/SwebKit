# Frontend — API Client

## Route and Page

**Route:** `/api-client`  
**Page:** `src/SwebKit.App/Components/Pages/ApiClientPage.razor`  
**Subfolder:** `src/SwebKit.App/Components/ApiClient/`  
**`_Imports.razor` addition** (BL-1 guard): `@using SwebKit.App.Components.ApiClient`

---

## Shell Layout

Two-pane resizable layout. The **collection tree** sits on the left. The **right pane** contains
the request builder (top) and response viewer (bottom), split by a horizontal splitter.

The **request quick-nav panel** is a collapsible overlay (`Ctrl+P`) that lists every request
across all collections for fast keyboard navigation — it does not take up permanent layout space.

```
┌───────────────────────────────────────────────────────────────────────────┐
│  Toolbar: [+ Collection] [+ Request]  [  Env: production ▼ ]  [↓ Export] │
├──────────────────┬────────────────────────────────────────────────────────┤
│  Collection Tree │  Request Builder                                        │
│  ─────────────── │  ──────────────────────────────────────────────────    │
│  🔍 Search       │  [GET ▼] https://{{base}}/api/v1/users   [Send]◆       │
│                  │           └─ base = https://api.dev.acme.com            │
│  ▶ My Collection │  ──────────────────────────────────────────────────    │
│    ├ 📁 Users    │  Params │ Headers │ Body │ Auth │ Capture               │
│    │ ├ GET /     │  [editor area]                                          │
│    │ └ POST /    │  ──────────────────────────────────────────────────    │
│    └ POST /login │  Response Viewer                                        │
│                  │  ● 200 OK  142ms  4.2KB                                 │
│                  │  Headers │ Body │ Raw │ Captures (⚠ 1 warning)          │
│                  │  [Monaco read-only]                                     │
└──────────────────┴────────────────────────────────────────────────────────┘
```
◆ dirty indicator (asterisk) when unsaved and auto-save is off

Pane widths persisted in `UiStateRepository` (same pattern as other page pane splits).

---

## Component Inventory

### `ApiClientPage.razor`

- Injects `CollectionRepository`, `EnvironmentRepository`, `IHttpRequestExecutor`,
  `IVariableSubstitutionService`, `IVariablePreviewService`, `IPostRequestCaptureExecutor`,
  `IAuthInheritanceResolver`, `OAuth2TokenManager`
- Owns page-level state: active collection, selected request, active environment,
  last response, last capture result, loading/cancellation state
- **Single-request focus model:** only one request open at a time; selecting another request
  from the tree or quick-nav panel loads it into the builder (prompts save if dirty and
  auto-save is off)
- Wires the two panels, the toolbar, and the optional quick-nav overlay
- On navigation away: calls `CancellationTokenSource.Cancel()` and
  `IAsyncDisposable.DisposeAsync()` on any active `IGraphQlSubscriptionService`

### `RequestQuickNavPanel.razor`

- Overlay panel toggled by `Ctrl+P` or the `[≡]` button in the toolbar
- Lists all requests across all collections in a flat scrollable list (flattened, `<Virtualize>`)
- Each row: `[Collection / Folder /] Request name`, method badge
- Keyboard navigation: arrow keys to move, `Enter` to open, `Escape` to close
- Text filter box at top: filters list by name/collection prefix
- Does NOT take permanent layout space — shown as an overlay on the left panel

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
- **URL bar:** text input with `{{variable}}` inline preview: below each `{{token}}` a small
  resolved-value badge appears (`base` → `https://api.dev.acme.com`; secrets show `••••••••`);
  populated by `IVariablePreviewService` on URL change, debounced 300 ms
- **Send button:** `Ctrl+Enter`; shows spinner and `[Cancel]` (`Escape`) during execution;
  dirty indicator (asterisk in title) when request has unsaved changes and auto-save is off
- **Body editor variable preview strip:** when body content contains `{{token}}` patterns, a
  small collapsed strip above the Monaco editor shows resolved previews
- Switches entirely to `WebSocketPanel` when method = WS; shows `GraphQlBodyPanel` when GRAPHQL

#### Body sub-components

| Sub-component            | Body type  | Editor                                             |
| ------------------------ | ---------- | -------------------------------------------------- |
| `BodyEditorJson.razor`   | JSON       | Monaco (`json` mode, editable)                     |
| `BodyEditorXml.razor`    | XML        | Monaco (`xml` mode, editable)                      |
| `BodyEditorText.razor`   | Plain text | Monaco (`plaintext` mode)                          |
| `KeyValueGrid.razor`     | Form Data  | Reusable key-value grid (same as Params / Headers) |
| `BodyEditorBinary.razor` | Binary     | File picker; stores temp path in state             |

`KeyValueGrid.razor` is a reusable shared component used in Params, Headers, and Form Data tabs.
It supports: add row, delete row, enable/disable toggle per row, drag-reorder (Phase 8).

#### Auth sub-components

| Sub-component          | Auth type                                                                   |
| ---------------------- | --------------------------------------------------------------------------- |
| `BearerAuthForm.razor` | Bearer — token input (masked), credential-store backed                      |
| `ApiKeyAuthForm.razor` | API Key — key name, value, placement radio (Header / Query Param)           |
| `BasicAuthForm.razor`  | Basic — username + password (password masked)                               |
| `OAuth2AuthForm.razor` | OAuth 2 — flow selector, token URL, client ID, scopes, [Get Token]          |

When a request has `Auth = null`, the Auth tab renders the resolved inherited auth in a greyed
"Inherited from [folder/collection name]" banner above the form, with a [Override for this
request] button. Clicking override copies the inherited config into the request's own `AuthConfig`.

### `GraphQlBodyPanel.razor`

Replaces the normal Body tab content when method = GRAPHQL.

- Left: Monaco query editor (`graphql` mode)
- Right: Monaco variables editor (`json` mode)
- **Operation selector dropdown:** parses document for named operations on every edit
  (debounced 500 ms); shows dropdown when ≥2 operations found; selected operation name
  sent as `operationName` in request body
- Footer: [Introspect Schema] button — triggers `__schema` introspection and feeds result to
  `monaco-graphql` for autocomplete; **on introspection error:** shows a dismissible warning
  banner above the editor, allows continued editing
- **Subscription detection:** when the selected operation is `subscription`, the Send button
  label changes to [Subscribe]; execution flows through `IGraphQlSubscriptionService`;
  streaming `next` messages appear in `ResponseViewerPanel` as they arrive;
  [Stop subscription] button replaces [Cancel] while subscribed

### `WebSocketPanel.razor`

Standalone panel replacing the right-panel layout when method = WS.

```
┌── WebSocket ───────────────────────────────────────────────────────────────┐
│  Headers  │  ● Connect / ■ Disconnect              ● Connected             │
│  [wss://{{base}}/ws]                                                       │
├────────────────────────────────────────────────────────────────────────────┤
│  Message log (virtualized, cap 10 000)                      [Clear log]    │
│  ↓ 12:41:03  {"event":"connected"}                                         │
│  ↑ 12:41:05  {"action":"subscribe","channel":"prices"}                     │
│  ↓ 12:41:05  {"event":"ack"}                                               │
├────────────────────────────────────────────────────────────────────────────┤
│  [Text ▼] [Saved: subscribe ▼]  [message...]         [Send] [💾]           │
└────────────────────────────────────────────────────────────────────────────┘
```

- **Headers tab** at the top (for `Sec-WebSocket-Protocol` and other upgrade headers;
  uses `KeyValueGrid.razor`)
- Message type selector: Text / Binary (binary shows hex representation)
- **Saved message templates dropdown:** populated from `WebSocketEntry.SavedMessages`;
  selecting a template loads its content into the composer
- **[💾 Save] button** in composer — prompts for a name, adds to `WebSocketEntry.SavedMessages`,
  saves via `CollectionRepository`
- `<Virtualize>` on the message log; maximum 10 000 frames retained (oldest dropped silently)
- Connection state badge: Disconnected (grey) / Connecting (yellow) / Connected (green) / Faulted (red)
- Consumes `IWebSocketClientService` via `IAsyncEnumerable<WebSocketMessage>` in a background task;
  posts to UI via `InvokeAsync(StateHasChanged)` (BL-2 guard)

### `ResponseViewerPanel.razor`

- Status badge: colour-coded (2xx green, 3xx blue, 4xx amber, 5xx red)
- Metadata row: elapsed time, response size, content type
- Tab strip: **Headers** | **Body** | **Raw** | **Captures** (shown only when request has capture rules)
- **Captures tab:** shows each rule's result — ✓ matched value or ⚠ warning message
- Body: Monaco read-only with auto-detected language (JSON, XML, HTML, plaintext)
- JSON auto-pretty-print on load when content-type is `application/json`
- Body > 500 KB: shows first 500 KB + `[Load full response (X MB)]` affordance
- Copy-to-clipboard button on body panel
- **GraphQL subscriptions:** when streaming `next` messages, the Body tab continuously appends
  messages (newest at top); a `[Stop subscription]` button replaces the spinner

### `EnvironmentManagerPanel.razor`

Accessible via toolbar environment switcher dropdown → [Manage Environments].

- List of environments with [+ New] [✎ Edit] [🗑 Delete] per row
- Active environment highlighted; clicking a row sets it as active for the page session
- `EnvironmentEditor.razor` — inline edit within a dialog:
  - Variable grid: key | type (Plain / Secret / Key Vault) | value
  - Secret type: value shown as `••••••`; stored/loaded via `ICredentialStore`
  - Key Vault type: input = KV secret name; resolved live at send time via `DefaultAzureCredential`
  - [Test resolution] button resolves the selected variable against the current environment
    for preview (shows result or `[KV_UNAVAILABLE]`)
- `CollectionVariableEditor.razor` — accessible from right-click menu on collection node:
  - Grid of key/value pairs (collection-level, always active)
  - Separate from environment variables; always shown in variable preview

### `PostRequestCaptureBuilder.razor`

Rendered below the `ResponseViewerPanel` when the active request has (or is being given) capture rules.

Layout per rule row:
```
[Source: Response Body (JSONPath) ▼] [$.access_token   ] → [token     ] in [Environment ▼] [🗑]
[Source: Response Header          ▼] [Authorization    ] → [auth      ] in [Collection  ▼] [🗑]
```

- [+ Add Capture] button appends a new empty rule row
- [Test capture] button re-evaluates all rules against the last response and shows results inline
- Rules saved to `HttpRequestEntry.CaptureRules` on every change (auto-save or explicit save)
- Failed rules shown with a red ⚠ icon and the error message

### `CollectionExportDialog.razor`

Triggered from toolbar [\u2193 Export] button.

- Format selector: SwebKit Native | Postman v2.1 | Bruno
- "Include environments" checkbox (default: checked for SwebKit, unchecked for others)
- [Export collection] → file save dialog
- [Import collection] → file open dialog; format auto-detected via `ICollectionImporter.CanImport`;
  name collision → auto-renamed to "Name (2)"
- [Import environment] → file open dialog; accepts SwebKit environment JSON or Postman
  collection file (extracts variables as environment)

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

| Shortcut       | Action                           | Scope                  |
| -------------- | -------------------------------- | ---------------------- |
| `Ctrl+Enter`   | Send current request             | API Client page active |
| `Ctrl+N`       | New request in active collection | API Client page active |
| `Ctrl+Shift+N` | New collection                   | API Client page active |
| `Ctrl+E`       | Open environment manager         | API Client page active |
| `Ctrl+P`       | Toggle request quick-nav panel   | API Client page active |
| `Escape`       | Cancel in-flight request         | API Client page active |

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

| Surface               | Guard                               | Implementation                                                |
| --------------------- | ----------------------------------- | ------------------------------------------------------------- |
| Collection tree       | Flattened `<Virtualize>` list       | `FlatTreeNode` list; expand collapses in-place                |
| WebSocket message log | `<Virtualize>` on message list      | Max 10 000 frames retained in memory                          |
| Response body         | 500 KB cap                          | Read limited in `HttpRequestExecutor`; [Load full] affordance |
| Monaco init           | Lazy load                           | Dynamic JS `import()` on first editor mount                   |
| Collection load       | Repository returns list in one read | No per-request lazy loading from disk                         |
