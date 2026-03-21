# Frontend Refactor — Status

**Status:** Done

## Progress checklist

### Phase 1 — CSS Architecture ✅
- [x] Add spacing token scale (`--spacing-xs/sm/md/lg/xl`) to `app.css`
- [x] Add typography token scale (`--font-size-xs/sm/md/lg/xl`) to `app.css`
- [x] Add z-index token scale (`--z-dropdown/modal/toast/overlay`) to `app.css`
- [x] Add utility classes: `.form-input`, `.surface-card`, `.text-*`, `.empty-state*` to `app.css`
- [x] Replace hard-coded font sizes in all CSS isolation files
- [x] Replace magic padding/gap/margin values with spacing tokens in all CSS isolation files
- [x] Replace hard-coded colors in `PodLogView.razor.css`
- [x] Update z-index values to use token variables across all CSS files
- [x] Restructure `AksPage.razor.css` (1,183 lines) with 7 logical section headers

### Phase 2 — Shared UI Primitives ✅
- [x] Create `Components/Shared/EmptyState.razor`
- [x] Create `Components/Shared/Modal.razor` + `Modal.razor.css`
- [x] Create `Components/Shared/Dropdown.razor` + `Dropdown.razor.css`
- [x] Create `Components/Shared/SelectionService.cs`
- [x] Create `Components/Shared/AutoRefreshController.cs`
- [x] Wire `<EmptyState />` into `MessageListView.razor`
- [x] Wire `<Modal />` into `MessageListView.razor` and `ServiceBusPage.razor`

### Phase 3 — Component Splitting ✅
- [x] Split `AksPage.razor` into 9 child components: `DeploymentGrid`, `StatefulSetGrid`, `PodGrid`, `ConfigMapGrid`, `SecretGrid`, `IngressGrid`, `HelmGrid`, `CronJobGrid`, `HpaPanel`
- [x] Each component has its own `.razor.css` with scoped styles

### Phase 4 — Async & Lifecycle Fixes ✅
- [x] Fix fire-and-forget `Task.Run` in `PodLogView.razor`
- [x] Batch `StateHasChanged` in `PodLogView.razor` streaming loop
- [x] Fix timer disposal in `MessageListView.razor` via `AutoRefreshController`
- [x] Fix un-awaited `EventCallback.InvokeAsync` in `MessageListView.razor` and `ServiceBusPage.razor`
- [x] Add `try/catch` to JS interop in `MessageListView.razor`, `ServiceBusPage.razor`, `MainLayout.razor`

### Phase 5 — Code Duplication ✅
- [x] `SelectionService<T>` wired into `MessageListView.razor`
- [x] `AutoRefreshController` wired into `MessageListView.razor`

## Blockers

None.
