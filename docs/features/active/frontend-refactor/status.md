# Frontend Refactor — Status

**Status:** In Progress

## Progress checklist

### Phase 1 — CSS Architecture ✅
- [x] Add spacing token scale (`--spacing-xs/sm/md/lg/xl`) to `app.css`
- [x] Add typography token scale (`--font-size-xs/sm/md/lg/xl`) to `app.css`
- [x] Add z-index token scale (`--z-dropdown/modal/toast/overlay`) to `app.css`
- [x] Add utility classes: `.form-input`, `.surface-card`, `.text-*`, `.empty-state*` to `app.css`
- [x] Replace hard-coded font sizes in all CSS isolation files
- [x] Replace magic padding/gap/margin values with spacing tokens in all CSS isolation files
- [x] Replace hard-coded colors (`#FF6B6B` → `--color-error`, `#FFB86C` → `--color-warning`) in `PodLogView.razor.css`
- [x] Update z-index values to use token variables across all CSS files
- [x] Restructure `AksPage.razor.css` (1,183 lines) with 7 logical section headers

### Phase 2 — Shared UI Primitives ✅
- [x] Create `Components/Shared/EmptyState.razor`
- [x] Create `Components/Shared/Modal.razor` + `Modal.razor.css`
- [x] Create `Components/Shared/Dropdown.razor` + `Dropdown.razor.css`
- [x] Create `Components/Shared/SelectionService.cs`
- [x] Create `Components/Shared/AutoRefreshController.cs`
- [x] Wire `<EmptyState />` into `MessageListView.razor` (2 occurrences replaced)
- [x] Wire `<Modal />` into `MessageListView.razor` (save-filter dialog)
- [x] Wire `<Modal />` into `ServiceBusPage.razor` (composer modal)

### Phase 3 — Component Splitting ⬜
- [ ] Split `AksPage.razor` (400+ lines) into `DeploymentGrid`, `PodGrid`, `ConfigMapGrid`, `HpaPanel`, `YamlViewer`
- [ ] Refactor `MessageListView.razor` (540 lines): extract filter state, export logic into dedicated classes
- [ ] Refactor `ServiceBusPage.razor` (300+ lines): extract namespace+tab wiring

### Phase 4 — Async & Lifecycle Fixes ✅
- [x] Fix fire-and-forget `_ = Task.Run(...)` in `PodLogView.razor` — stored as `_streamTask`, cancelled and awaited on dispose
- [x] Replace per-line `StateHasChanged` in `PodLogView.razor` with batched render (every 20 lines)
- [x] Fix `System.Timers.Timer` not disposed in `MessageListView.razor` — replaced with `AutoRefreshController`
- [x] Fix fire-and-forget `EventCallback.InvokeAsync` in `MessageListView.razor` — now properly awaited
- [x] Fix async lambda in `ServiceBusPage.razor` — `OnMessageSelected` callback converted to `async`
- [x] Add `try/catch` to JS interop in `MessageListView.razor` (download)
- [x] Add `try/catch` to JS interop in `ServiceBusPage.razor` (splitter init)
- [x] Add `try/catch` to JS interop in `MainLayout.razor` (keyboard shortcuts)

### Phase 5 — Code Duplication ✅
- [x] `SelectionService<T>` created and wired into `MessageListView.razor`
- [x] `AutoRefreshController` created and wired into `MessageListView.razor`

## Remaining work
- Component splitting of large pages (Phase 3) — higher risk, separate session

## Blockers

None.
