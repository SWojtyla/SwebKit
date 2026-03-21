# Frontend Refactor — Test Plan

## Scope

This is a refactor, not a new feature. The test goal is to prevent regressions and add coverage for components that currently have none.

---

## 1. Regression tests — existing bUnit tests must stay green

Run all tests in `SwebKit.App.Tests` after every phase. No test may be removed or skipped.

---

## 2. New bUnit tests — shared primitives

### 2.1 `EmptyState.razor`

| Scenario | Assert |
|----------|--------|
| Renders with icon + title | Both visible in rendered output |
| Subtitle omitted | No subtitle element rendered |
| Subtitle provided | Subtitle rendered |

### 2.2 `Modal.razor`

| Scenario | Assert |
|----------|--------|
| `IsOpen = false` | Nothing rendered |
| `IsOpen = true` | Backdrop and container rendered |
| Click backdrop | `OnClose` callback invoked |
| Click container | `OnClose` NOT invoked (stopPropagation) |
| `ChildContent` renders | Content appears inside container |

### 2.3 `Dropdown.razor`

| Scenario | Assert |
|----------|--------|
| `IsOpen = false` | Nothing rendered |
| `IsOpen = true` | Menu rendered |
| Click backdrop | `OnClose` invoked |

---

## 3. New unit tests — logic extractions

### 3.1 `SelectionService<T>`

| Scenario | Assert |
|----------|--------|
| Toggle on | Item in `Selected` |
| Toggle off | Item removed from `Selected` |
| Toggle on twice | Single entry (set dedup) |
| `Clear()` | `Selected` is empty |
| `IsSelected` | Returns correct bool |

### 3.2 `AutoRefreshController`

| Scenario | Assert |
|----------|--------|
| Set interval, wait | Callback invoked at least once |
| `Stop()` before tick | Callback not invoked |
| `DisposeAsync` | No further invocations after dispose |
| Set interval twice | Old timer stopped, new one starts |

---

## 4. Visual / manual checks (per phase)

### Phase 1 — CSS tokens

- [ ] No visual change in Service Bus page after token substitution
- [ ] No visual change in AKS page after `AksPage.razor.css` split
- [ ] Z-index: command palette renders above modal; modal renders above dropdowns

### Phase 2 — Shared primitives

- [ ] Empty state renders identically to old inline version in `MessageListView`
- [ ] `Modal` backdrop click closes composer in `ServiceBusPage`
- [ ] `Dropdown` in `MessageListView` filter opens and closes correctly

### Phase 3 — Component splitting

- [ ] All AKS grids load and display data after split
- [ ] HPA panel opens and closes correctly
- [ ] YAML viewer renders syntax-highlighted YAML

### Phase 4 — Async fixes

- [ ] Pod log streaming starts, updates UI, and stops on navigation away (no ghost updates)
- [ ] Auto-refresh in message list continues working at 5s / 10s / 30s
- [ ] Component unmount with active log stream does not throw `ObjectDisposedException`

---

## 5. Acceptance criteria

- All existing tests pass
- All new unit tests pass
- No console errors in browser devtools during normal usage
- No visible regressions in any page (verified by walkthrough)
