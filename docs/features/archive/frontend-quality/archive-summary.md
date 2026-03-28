---

title: "Archive Summary - frontend-quality"
owner: ""
jira: ""
completed_date: "2026-03-27"
pr: ""
commit: ""

---

## Goal

Harden the SwebKit MAUI Blazor Hybrid frontend by eliminating memory leaks, decomposing god components, consolidating duplicated patterns, improving UX consistency, and cleaning up CSS.

## Delivered

- **Wave 0 — Safety & Memory:** Event subscription leak audit confirmed correct (all pages already implement `IDisposable`); `IDisposable` return added to `IAppEventBus.Subscribe` via `EventSubscription` inner class; `TabService` capped at 50 tabs with `ClearAll()` eviction
- **Wave 1 — Architecture:** God component decomposition graduated to dedicated `component-decomposition` feature; `SwebKitComponentBase.cs` created in `Components/Shared/` with `RunAsync`, `IsLoading`, `ErrorMessage`, `RequestRender` helpers; CascadingValue vs Parameter convention documented
- **Wave 2 — Performance Polish:** `ShouldRender` + `_needsRender` flag added to EntityTree and RedisKeyList; RedisKeyList virtualized (`<Virtualize TItem="string" ItemSize="36">`); EntityTree progressive loading (queues shown first, topics appended); EventCallback audit confirmed all 6 components already correct
- **Wave 3 — UX Consistency:** `PageToolbar.razor` extracted and applied to ObservabilityPage; `ErrorCallout` enhanced with `Title`, `Details`, `OnDismiss`, collapsible details; ARIA audit confirmed all icon buttons have `aria-label`; `SkeletonRows` adopted in EntityTree; tab state persisted via `UiStateRepository` with 500ms debounce; batch delete progress feedback added in RedisPage and DlqView
- **Wave 4 — CSS Hygiene:** Inline styles extracted to scoped `.razor.css` for ServiceBusPage, StoragePage, SettingsPage; CSS isolation added for NotificationHistory, NotificationToast, MessageDetailPane; JS interop failure paths now log via `ILogger<T>` in Modal, AksPage, DlqView

## Key decisions

- `SwebKitComponentBase` as opt-in base class — not forced on all components; provides `RunAsync`, `IsLoading`, `ErrorMessage`, `RequestRender` as a consistent loading/error pattern
- FQ-2 dropped — AppStateService is 117 lines and a proper coordinating facade; no decomposition needed
- FQ-1 graduated to `component-decomposition` — scope too large for this feature, tracked separately
- EventCallback is already the project standard — no migration needed

## Validation performed

- Unit tests: 352 tests passing (0 failures), build clean
- Integration tests: N/A
- Manual checks: CSS isolation verified; skeleton states visible; tab persistence confirmed across reload

## Lessons learned

- "Fix before adding" — several items (FQ-3, FQ-9, FQ-13, FQ-15) audited and found already correct; auditing first saved unnecessary changes
- `SwebKitComponentBase` provides the loading/error scaffolding pattern all future pages should follow
- Tab state persistence requires debouncing — synchronous save on every tab mutation causes noticeable jank

## Follow-up

- God component decomposition continues in `component-decomposition` feature (active → Review)
- _(No other follow-up)_

## Archive note

> No Jira ticket was linked (Path B). Archive location: `docs/features/archive/frontend-quality/`.
