# Frontend Refactor — Status

**Status:** Planned

## Progress checklist

### Phase 1 — CSS Architecture
- [ ] Add spacing token scale (`--spacing-xs/sm/md/lg/xl`) to `app.css`
- [ ] Add typography token scale (`--font-size-xs/sm/md/lg`) to `app.css`
- [ ] Add z-index token scale (`--z-dropdown/modal/overlay`) to `app.css`
- [ ] Replace hard-coded font sizes in all CSS isolation files
- [ ] Replace magic padding values with spacing tokens in all CSS isolation files
- [ ] Replace hard-coded colors (e.g. `#FF6B6B`, `#FFB86C`) with color variables
- [ ] Split `AksPage.razor.css` (1,183 lines) into logical sub-files

### Phase 2 — Shared UI Primitives
- [ ] Extract `<EmptyState />` component (icon + title + description)
- [ ] Extract `<Modal />` / `<Overlay />` component (backdrop + container)
- [ ] Extract `<Dropdown />` component (positioned popup list)
- [ ] Extract `<FormInput />` CSS class (replace 54 inline `background/border/padding` copies)
- [ ] Extract `<PrimaryButton />` / `<GhostButton />` CSS classes or components

### Phase 3 — Component Splitting
- [ ] Split `AksPage.razor` (400+ lines) into `DeploymentGrid`, `PodGrid`, `ConfigMapGrid`, `HpaPanel`, `YamlViewer`
- [ ] Refactor `MessageListView.razor` (540 lines): extract filter state, export logic
- [ ] Refactor `ServiceBusPage.razor` (300+ lines): extract namespace+tab wiring

### Phase 4 — Async & Lifecycle Fixes
- [ ] Fix fire-and-forget `_ = Task.Run(...)` in `PodLogView.razor` — store and cancel on dispose
- [ ] Fix fire-and-forget `InvokeAsync` calls in `MessageListView.razor` and `ServiceBusPage.razor`
- [ ] Fix `System.Timers.Timer` not disposed in `MessageListView.razor`
- [ ] Add `try/catch` and logging to all JS interop calls
- [ ] Replace generic `catch { }` blocks in `ServiceBusPage.razor` splitter init with logged warning
- [ ] Review and reduce excessive `StateHasChanged()` calls (148 across 27 files)

### Phase 5 — Code Duplication
- [ ] Extract `SelectionService<T>` for multi-select toggle pattern (3+ duplications)
- [ ] Extract `AutoRefreshService` for timer management (2+ duplications)

## Blockers

None.
