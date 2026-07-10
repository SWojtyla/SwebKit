# Frontend / UI Module — Tester Feedback UX Polish

Covers the Blazor + CSS + JS-interop side of the batch. Backend/platform items are in
`backend.md`. Item numbers match `index.md`.

---

## A3 (#8) — Demo banner contrast

**Files**

- `src/SwebKit.App/Components/Layout/MainLayout.razor` (~lines 29-45) — `<div class="demo-banner">`
- `src/SwebKit.App/wwwroot/styles/02-shell-navigation.css` (~lines 400-427) — `.demo-banner`

**Current**

- Background `var(--color-warning)` (orange), text `#1a1a1a`, weight 600, `font-size-sm` (~13px),
  padding `4px 16px`, `grid-row: 2`. "Disable" button uses `rgba(0,0,0,0.15)` hover.

**Change**

- Increase legibility: bump weight to 700, increase vertical padding, add a subtle
  `1px solid rgba(0,0,0,0.25)` bottom border, and verify text/background contrast ≥ WCAG AA in
  both light and dark themes (the banner sits above theme tokens so check both).
- Replace the subtle "Disable" text button with a higher-contrast outline button so the primary
  action is discoverable.
- No layout/grid-row change — banner stays in `grid-row: 2`.

---

## C1 (#2) — AKS logs "go to tail" + sidepanel buttons

**Files**

- `src/SwebKit.App/Components/Aks/PodLogView.razor` (~1-70) — toolbar: live toggle, range, jump
- `src/SwebKit.App/Components/Aks/MultiPodLogView.razor` (~430-495) — `StreamLogsAsync()`,
  timer-driven render loop, `JumpToLatest` → `SwebKit.scrollToBottom`
- `src/SwebKit.App/Components/Aks/AksDetailPanels.razor` (~1-150) — panel host, tab switching

**Current**

- Logs buffer in memory, ~10 renders/sec. `JumpToLatest` scrolls synchronously. There is a
  "Tailing latest…" label only while live+loading; no persistent live/paused/historical
  indicator. Buttons present: Close, Older, Newer, JumpToLatest, LoadOlderHistory, Clear,
  CopyVisible, ExportAll, TogglePause — currently a flat, crowded row.

**Change**

1. **One-click tail** — a single prominent "Go to live / Tail" button that: resumes live
   (un-pauses), scrolls to bottom, and stays visually "armed" while tailing. When the user scrolls
   up, auto-detect and switch to "paused/historical" and show the button as a call-to-action.
2. **Sticky status footer** — small footer showing one of: `Live • tailing`, `Paused at line N`,
   `Historical (older loaded)`, so the user always knows what they are looking at.
3. **Button grouping** — regroup the toolbar into logical clusters: navigation (Older / Newer /
   Jump), state (Tail / Pause), and data (Copy / Export / Clear). Use existing FluentIcon
   vocabulary; keep Close top-right. No behavior change to the underlying stream, only layout +
   the tail affordance.

**Note (perf)** — keep the existing throttled render loop (PERF2-2); do not render per log line.
The scroll-to-bottom should ride the same rAF/interop path, not a per-append scroll.

---

## C2 (#9) — AKS namespace dropdown: selected first

**Files**

- `src/SwebKit.App/Components/Aks/AksConnectionBar.razor` — picker UI (~40-290),
  `OrderNamespaces()` (~337), `GetNamespaceSortIndex()`

**Current**

```csharp
private IEnumerable<string> OrderNamespaces(IEnumerable<string> namespaces)
    => namespaces
        .Where(ns => !string.IsNullOrWhiteSpace(ns))
        .Distinct(StringComparer.Ordinal)
        .OrderBy(GetNamespaceSortIndex)
        .ThenBy(ns => ns, StringComparer.Ordinal);
```

Order = server list order, then alphabetical. Selected (`_pendingNamespaces`) are not hoisted.

**Change**

- Hoist selected namespaces to the top so they are easy to review/deselect:

```csharp
.OrderBy(ns => _pendingNamespaces.Contains(ns) ? 0 : 1)
.ThenBy(GetNamespaceSortIndex)
.ThenBy(ns => ns, StringComparer.Ordinal)
```

- Selection ordering must reflect the _pending_ (in-flight edit) set, not the applied set, so the
  list reorders live as the user toggles. Preserve a stable order within the selected group
  (server index then name) to avoid items jumping while toggling.
- Optional: a thin divider row between the selected group and the rest for scanability.

---

## C3 (#10) — AKS keyboard friendliness

**Files**

- `src/SwebKit.App/Components/Pages/AksPage.razor` — F5 refresh; no scoped shortcuts / tab order
- `src/SwebKit.App/Components/Aks/AksConnectionBar.razor` (~268-280) — `OnNsKeyDown` (Esc/Enter)
- `src/SwebKit.App/Components/Shared/KeyboardShortcutsPanel.razor` — global shortcut docs
- `src/SwebKit.App/wwwroot/js/keyboardShortcuts.js` — global key layer

**Change**

1. **Tab order** — assign a logical `tabindex` flow: connection bar → resource-type tabs → grid →
   detail sidepanel actions. Ensure the namespace dropdown is fully operable by keyboard (open,
   arrow to move, space to toggle, Enter to apply, Esc to cancel — extend `OnNsKeyDown`).
2. **AKS-scoped shortcuts** (active only when the AKS page is focused, registered/unregistered on
   page dispose to avoid leaks): jump to logs, jump to resource tabs, focus grid, focus/close
   detail panel. Reuse the existing `keyboardShortcuts.js` registration path rather than raw
   `@onkeydown` on the page root.
3. **Docs** — add the new AKS shortcuts to `KeyboardShortcutsPanel.razor`.

**Guard** — do not reuse chords already globally bound (Ctrl+P command palette, Ctrl+W / Ctrl+Tab
page-tab nav — see the API Client feature's Phase 3 caveat about chord collisions). Pick
non-conflicting chords (e.g. Alt+letter) and confirm against `keyboardShortcuts.js`.

---

## D1 (#3) — Redis "select all keys" position

**Files**

- `src/SwebKit.App/Components/Redis/RedisToolbar.razor` (~31-55) — `SelectAllLoaded` button
- `src/SwebKit.App/Components/Redis/RedisKeyList.razor` (~15-30) — `OnToggleAllVisible` checkbox
- `src/SwebKit.App/Components/Redis/RedisNamespaceTree.razor` — per-node checkboxes
- `src/SwebKit.App/Components/Pages/RedisPage.razor` (~80-170)

**Current**

- Two overlapping affordances confuse scope: a toolbar "Select All Loaded" button (top) and an
  "all visible" checkbox inside the key list. Multi-select is behind `MultiSelectMode`.

**Change**

- Co-locate the "select all" control with the list it acts on and label it by scope. Preferred:
  a single header checkbox at the top of `RedisKeyList` labeled to its true scope
  ("Select all loaded keys" / "Select all visible") with a tri-state (none/some/all) indicator,
  and remove the duplicate toolbar button (or keep only one, clearly scoped).
- Only appears in `MultiSelectMode`; verify the selection count and per-row checkboxes stay in
  sync with the header tri-state.
- This is a placement/labeling fix — do not change the selection data model.

---

## F1 (#4) — Sidepanel resize lag

**Files**

- `src/SwebKit.App/wwwroot/js/splitter.js` (~1-100) — drag handler, `onMouseMove`, `applyWidth`
- `src/SwebKit.App/wwwroot/js/uiState.js` (~45-120) — `initResizer` (agent panel)
- `src/SwebKit.App/Components/Shared/SidePanel.razor`
- `src/SwebKit.App/wwwroot/styles/03-workspaces.css` (~755-780) — `.pane-splitter`, flex layout

**Current**

- JS sets `paneEl.style.width` directly on every `mousemove`. The perceived lag is a CSS
  `transition` on `width`/`flex` animating each width change, so the pane eases toward the cursor
  instead of tracking it.

**Change**

1. **Remove transition during drag** — ensure no `transition: width|flex` applies while dragging.
   Add a `.resizing` / `.active` class on the pane during drag that sets `transition: none` (and
   optionally `will-change: width`), removed on mouseup.
2. If a settle animation is desired, apply a short `transition` **only after** mouseup, never
   during the drag.
3. Confirm `onMouseMove` writes width synchronously (it does); coalesce with `requestAnimationFrame`
   if repaint queuing is observed, but the transition removal is the primary fix.
4. **Shared surface** — the splitter serves multiple workspaces. Re-verify AKS, Redis, Service
   Bus, and the agent panel after the change.

---

## E1 (#1) — Service Bus: show credential used on error (UI side)

**Files**

- `src/SwebKit.App/Components/ServiceBus/ServiceBusNamespacePanel.razor` (~217-290) — error render
  in `sb-ns-error-message`, connection-string parse

Backend detail (safe extraction, secret handling) is in `backend.md` E1. UI change:

- On `NsState.ConnectionError`, render a structured diagnostic block **below** the message with
  non-secret fields only: resolved endpoint host, SAS key **name** (not value), and the
  secret-reference / credential source label (e.g. which Key Vault secret name or config key was
  used). Never the connection string or key material.
- Style as a muted, monospace key/value list inside the existing error callout; no new component
  needed beyond a small markup addition.
