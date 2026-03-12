# Frontend Plan - Redis Manager

---

title: "Frontend Plan - Redis Manager"
owner: ""
status: "Done"

---

## Goal

Deliver a Redis management UI integrated into the SwebKit app shell with key browsing, inspection, editing, and server overview — consistent with the AKS page UX patterns.

## Impacted areas

- `src/SwebKit.App/Components/Pages/RedisPage.razor` + `.razor.css` (new)
- `src/SwebKit.App/Components/Redis/` (new — subcomponents)
- `src/SwebKit.App/Components/Pages/RedisConfigForm.razor` (new — settings form)
- `src/SwebKit.App/Components/Layout/LeftNav.razor` (add Redis nav item)
- `src/SwebKit.App/Components/Pages/SettingsPage.razor` (add Redis config accordion)

## UX notes

- Follow AKS page patterns: toolbar with controls, main content area, slide-out detail panels.
- Destructive operations (delete, flush) use the shared confirmation bar with production guard.
- Large values truncated by default with expand option.
- Key list uses virtual scroll or "Load more" for large keyspaces.

## Page layout

```
┌─────────────────────────────────────────────────────────────┐
│ Toolbar: [Connection alias ▾] [DB: 0 ▾] [Pattern: ____]   │
│          [Scan] [Auto-refresh ●] [↻ Refresh] [Server Info] │
├──────────────────────────────────┬──────────────────────────┤
│ Key List                         │ Key Detail Panel         │
│                                  │                          │
│ 🔑 user:1001         string     │ Key: user:1001           │
│ 🔑 user:1002         string     │ Type: string             │
│ 🔑 session:abc123    hash    ←  │ TTL: 3600s [Edit] [Remove]│
│ 🔑 cache:products    list       │ Encoding: embstr         │
│ 🔑 leaderboard       zset       │ Memory: 128 bytes        │
│                                  │                          │
│ [Load more...]                   │ Value:                   │
│                                  │ ┌──────────────────────┐ │
│                                  │ │ {"name":"John",...}  │ │
│                                  │ └──────────────────────┘ │
│                                  │ [Edit] [Delete] [Copy]   │
├──────────────────────────────────┴──────────────────────────┤
│ Status bar: 1,243 keys scanned | Connected | DB0            │
└─────────────────────────────────────────────────────────────┘
```

## Components

### RedisPage.razor (main page)

- Toolbar: connection selector, DB selector (0-15), pattern input, scan/refresh buttons, auto-refresh toggle, server info button
- Key list: left panel with virtual scroll, type icon per key, click to inspect
- Key detail: right slide-out panel (ResizablePanel) showing key info, value, and actions
- Status bar: key count, connection status, active database
- Context menu on key rows: View, Edit, Copy Value, Set TTL, Delete

### RedisKeyList.razor (key browser)

- Cursor-based scan with "Load more" button
- Pattern input with debounced search
- Type indicators: color-coded badges (string=blue, hash=green, list=orange, set=purple, zset=red, stream=cyan)
- Bulk selection with checkboxes for multi-delete
- Sort by name (default)

### RedisKeyDetail.razor (inspection/edit panel)

- Key metadata: type, TTL, encoding, memory usage
- Value viewer: type-aware rendering:
  - **String**: raw text with JSON pretty-print detection
  - **Hash**: field/value table with inline edit per field
  - **List**: indexed items with pagination
  - **Set**: member list
  - **Sorted Set**: member + score table
- Edit mode: inline editing for strings and hash fields (non-production only by default)
- TTL controls: display current TTL, set new TTL, remove TTL
- Actions: Delete key, Copy value to clipboard

### RedisServerInfo.razor (server dashboard)

- Modal or slide-out panel showing `INFO` output
- Sections: Server (version, uptime), Clients (connected), Memory (used, peak, fragmentation), Stats (commands processed, hit/miss ratio), Keyspace (per-DB key counts)
- Auto-refresh while open

### RedisConfigForm.razor (settings)

- Connection string input (password masked)
- Alias input (friendly name)
- Default database selector (0-15)
- Test Connection button with inline feedback
- Integrated into SettingsPage accordion alongside AKS and other configs

## States

| State             | UI                                                         |
| ----------------- | ---------------------------------------------------------- |
| Not configured    | "Go to Settings to configure Redis" link                   |
| Connecting        | Loading spinner in toolbar                                 |
| Connected         | Green dot, alias shown, key list populated                 |
| Connection failed | Red dot, error message, retry button                       |
| Scanning          | Spinner in key list, "Scanning..." text                    |
| Empty keyspace    | "No keys found" with pattern hint                          |
| Key selected      | Detail panel slides open                                   |
| Edit mode         | Input fields replace read-only values, Save/Cancel buttons |
| Bulk select       | Checkbox column visible, "Delete N keys" button in toolbar |

## Tasks

- [x] Add Redis nav item to LeftNav (icon: database/cache icon)
- [x] Create RedisPage.razor with toolbar and layout
- [x] Create RedisKeyList.razor with SCAN-based browsing
- [x] Create RedisKeyDetail.razor with type-aware value display
- [x] Create RedisServerInfo.razor with INFO dashboard
- [x] Create RedisConfigForm.razor for settings
- [x] Add Redis accordion to SettingsPage
- [x] Wire context menus for key actions
- [x] Add bulk selection and multi-delete
- [x] Add inline edit for string values and hash fields
- [x] Add TTL management controls
- [x] Add auto-refresh toggle (reuse AutoRefreshToggle component)
- [x] Add scoped CSS for all Redis components
- [x] Handle all loading, error, and empty states

## Validation

- Component tests: Deferred (no dedicated Redis UI component tests yet)
- Manual UX checks: See `test-plan.md`

## Notes

- Reuse existing shared components: `ResizablePanel`, `AutoRefreshToggle`, `ConfirmBar` (rename from AksConfirmBar to shared), `ContextMenu`, `LoadingSpinner`, `ResourceFilter`.
- JSON values should be auto-detected and pretty-printed with syntax highlighting.
- Large values (>10KB) truncated with "Show full value" toggle to prevent UI freezing.
- Password masking in connection string: mask everything after `password=` up to the next comma.
