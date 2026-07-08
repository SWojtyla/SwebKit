# Frontend — API Client UX Refactor

## Scope

All work lives under `src/SwebKit.App/Components/ApiClient/`. Three of the four workstreams are
frontend-led (icons, refactor, tabs); the cookie jar has a small UI surface documented in
`backend.md`.

## Current Component Map (sizes at plan time)

| Component                    | Lines | Role                                           |
| ---------------------------- | ----: | ---------------------------------------------- |
| `ApiClientPage.razor`        | 2,198 | Monolith: toolbar, tree host, workspace, state |
| `CollectionTree.razor`       |   796 | Flattened + virtualized tree                   |
| `RequestBuilderPanel.razor`  |   549 | URL bar, method picker, tabs, send             |
| `GraphQlPanel.razor`         |   430 | GraphQL editor/introspection                   |
| `ResponseViewerPanel.razor`  |   396 | Status/body/headers/history                    |
| `RequestQuickNavPanel.razor` |   174 | `Ctrl+P` overlay for request switching         |

---

## Phase 1 — Iconography unification

### Known offenders (audit these files, do not assume the list is complete)

- `ApiClientPage.razor`
  - Environment button label uses `🌍 Envs`.
  - Dropdown chevrons use the literal ASCII letter `v` (`<span ...__chevron">v</span>`) and the
    unicode `▾` in the env picker.
  - Dirty badge `● Unsaved`.
- `ResponseViewerPanel.razor`
  - `⚡ Subscription`, `📦` size, `🕐` timing, `⚠` truncated/error glyphs.
- `RequestBuilderPanel.razor`
  - `⚠ SSL off` badge, `●` dirty dot.
- `RequestQuickNavPanel.razor`
  - `✕` close button.
- Empty state icons passed as emoji strings (e.g. `EmptyState Icon="📦"`).

### Approach

- Replace each glyph with the equivalent `Microsoft.FluentUI.AspNetCore.Components` `FluentIcon`
  (`Icons.Regular.Size16.*` / `Size12.*`) already used elsewhere in the page (e.g. `AddSquare`,
  `Add`, `Save`, chevrons in `CollectionTree`).
- Suggested mapping (confirm exact icon names during implementation):
  - chevron down/right → `ChevronDown` / `ChevronRight` (already used in `CollectionTree`).
  - close → `Dismiss`. dirty → `Circle` (filled) or a small CSS dot, kept consistent.
  - environment → `Globe`. subscription → `Flash`. size → `Box`. timing → `Clock`.
  - warning → `Warning`. SSL off → `ShieldError` or `Warning`.
- For `EmptyState`, prefer a `FluentIcon` render fragment over an emoji string parameter; adjust the
  `EmptyState` component if its `Icon` parameter is string-only.
- Keep widths aligned to the current glyph size (12–16px) to avoid layout shift.

### Constraints

- Presentational only — no logic changes.
- Verify no `RZ10012` warnings from new component usage (BL-1) if any new subfolder is introduced.

---

## Phase 2 — `ApiClientPage.razor` refactor

### Target structure

Introduce an **owned state container** and split the monolith into presentational children that
receive state + callbacks. State stays parent-owned (archived lesson: large Blazor workspaces need
parent-owned state; do not scatter into child fields).

Proposed decomposition:

- `ApiClientPage.razor` — thin host: owns the state container, wires child components, handles
  navigation/command registration.
- `ApiClientState` (POCO or page-scoped state object) — active request, dirty flags, collections,
  environments, linked roots, active environment id, worksheet mode, conflict/message banners,
  request history. Not a DI singleton (page-scoped lifetime).
- New presentational components (names indicative):
  - `ApiClientToolbar.razor` — create actions, Git Repos menu, Import/Export menu, target chip,
    Variables menu, Envs button + env picker.
  - `ApiClientWorkspace.razor` — split host for tree + request builder + response viewer.
  - Keep `CollectionTree`, `RequestBuilderPanel`, `ResponseViewerPanel`, `RequestQuickNavPanel`,
    worksheet panels as-is; re-parent them under the new host.

### Rules

- Behaviour-preserving: no user-visible change in this phase.
- Do NOT wrap stateful children in `@if` that destroys/recreates them where state must survive
  (BL-4); use `display:none` if DOM persistence is required.
- Guard any `OnParametersSetAsync` data loads with reference checks (BL-5) and set guards before
  `await` (BL-3).
- Dispatch `StateHasChanged` via `InvokeAsync` after awaits (BL-2).
- Preserve `IAsyncDisposable` and stream cancellation on dispose (BL-7).

---

## Phase 3 — Optional request tabs

### Setting

- Add `ApiClientRequestTabs` (bool, default `false`) to user settings, mirroring how
  `VerifyApiClientSsl` is read in `RequestBuilderPanel` via `UserSettings.Settings`.
- Expose the toggle in the existing settings surface (same place SSL verification lives).

### Behaviour when OFF (default)

- Identical to today: one active request; tree click / `Ctrl+P` switches; dirty-switch prompt.

### Behaviour when ON

- A tab strip renders above `RequestBuilderPanel`, one tab per open request.
- Opening a request from the tree/quick-nav adds or focuses a tab (no implicit replace).
- Each open tab holds its own: dirty state, in-flight send + `CancellationTokenSource`, editor
  instance lifecycle, response/result + history, subscription/WebSocket session ownership.
- Closing a dirty tab prompts save/discard (reuse the existing dirty-switch prompt).
- Middle-click / close button / `Ctrl+W` closes a tab; `Ctrl+Tab` cycles (confirm shortcut
  availability against `CommandRegistry`).

### State model

- Open tabs live in the Phase 2 `ApiClientState` as an ordered list of open-request view models,
  plus an active-tab index. `RequestBuilderPanel` and `ResponseViewerPanel` bind to the active tab.
- One in-flight send per tab; switching tabs must not cancel another tab's send.

### Persistence

- MVP: open tabs are session-only (not persisted across app restart). Persisting the open-tab set
  is a follow-up, not required for this feature.

---

## Phase 4 — Cookie jar UI (deferrable)

- A small "Cookies" affordance (menu item or panel) listing stored cookies grouped by domain, with
  per-domain and global clear.
- A per-request or global toggle to enable/disable cookie capture (default off).
- Rendering must respect secret scrubbing; see `backend.md` for storage and execution details.

## Affected Files (frontend)

- `src/SwebKit.App/Components/ApiClient/ApiClientPage.razor` (+`.css`)
- `src/SwebKit.App/Components/ApiClient/RequestBuilderPanel.razor`
- `src/SwebKit.App/Components/ApiClient/ResponseViewerPanel.razor`
- `src/SwebKit.App/Components/ApiClient/RequestQuickNavPanel.razor`
- New: `ApiClientToolbar.razor`, `ApiClientWorkspace.razor`, tab strip component, cookie panel.
- `src/SwebKit.App/Components/ApiClient/_Imports`/`_Imports.razor` if a new subfolder is added (BL-1).
