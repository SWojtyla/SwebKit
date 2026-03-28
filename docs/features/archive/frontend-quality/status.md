# Status — Frontend Code Quality & Architecture Hardening

---

title: "Status — Frontend Code Quality & Architecture Hardening"
owner: ""
state: "Done"
branch: ""
started: "2026-03-27"
last_updated: "2026-03-27"

---

## Quick summary

Waves 0–4 implemented. Build clean, 352 tests passing. FQ-1 graduated to its own feature (`component-decomposition`). FQ-2 dropped — AppStateService is only 117 lines, well-structured as a coordinating facade.

**Current focus:** This feature is effectively done. See `component-decomposition` for the FQ-1 continuation.

## Progress checklist

### Wave 0 — Safety & Memory (priority: fix leaks first)

- [x] FQ-3: Fix event subscription leaks across all components — **already correctly implemented in all pages**
- [x] FQ-4: Add IDisposable return to EventBus.Subscribe — `IAppEventBus` + `AppEventBus` updated; `EventSubscription` inner class added; Unsubscribe kept for backward compat
- [x] FQ-5: Add TabService cleanup and cap — `MaxTabs = 50` eviction on `OpenTab`; `ClearAll()` method added

### Wave 1 — Architecture & Decomposition (biggest maintainability impact)

- [x] FQ-1: Decompose god components — **graduated to dedicated feature** `docs/features/active/component-decomposition/`
- [x] FQ-2: Decompose AppStateService — **dropped**: only 117 lines, proper facade pattern, no decomposition needed (see D-002 in `component-decomposition/decisions.md`)
- [x] FQ-10: Extract reusable LoadAsync base pattern — `SwebKitComponentBase.cs` created in `Components/Shared/`; `RunAsync`, `IsLoading`, `ErrorMessage`, `ShouldRender`/`RequestRender` helpers
- [x] FQ-13: Establish CascadingValue vs Parameter convention — documented in `decisions.md` (Decision 003)

### Wave 2 — Performance Polish (rendering efficiency)

- [x] FQ-6: Add strategic ShouldRender() overrides — `_needsRender` flag added to EntityTree and RedisKeyList; all state changes set the flag before InvokeAsync
- [x] FQ-7: Virtualize RedisKeyList — `@foreach` replaced with `<Virtualize TItem="string" ItemSize="36" OverscanCount="5">`
- [x] FQ-8: Progressive loading for EntityTree — queues shown immediately after load; topics appended in second InvokeAsync; CS-2 rethrow added
- [x] FQ-9: Standardize EventCallback vs Action/Func — audit complete; all 6 audited components already use EventCallback ✅

### Wave 3 — UX Consistency & Polish

- [x] FQ-11: Extract shared modal/dialog pattern — Modal.razor already used consistently; verified no inline modal implementations in pages
- [x] FQ-12: Extract shared toolbar/filter bar component — `PageToolbar.razor` + `PageToolbar.razor.css` created; applied to ObservabilityPage
- [x] FQ-14: Standardize error handling UX — ErrorCallout enhanced: `Title`, `Details`, `OnDismiss` params; dismiss button; collapsible details section
- [x] FQ-15: Add missing ARIA labels — audit complete; all icon-only buttons already have `aria-label` ✅
- [x] FQ-16: Extend SkeletonRows usage — EntityTree initial load replaced with `<SkeletonRows Count="5" />`; `.skeleton-row` CSS confirmed in app.css
- [x] FQ-17: Persist tab state across page refreshes — TabService injects `UiStateRepository`; debounced 500ms save; RestoreTabs called from MainLayout on start
- [x] FQ-18: Add batch operation progress feedback — RedisPage bulk delete: chunked loop with `_batchProgressMessage`; DlqView batch: chunked with per-chunk progress display

### Wave 4 — CSS & Style Cleanup

- [x] FQ-19: Extract inline styles to .razor.css — 3 new page .razor.css files: ServiceBusPage, StoragePage, SettingsPage
- [x] FQ-20: Ensure CSS isolation consistency — 3 new component .razor.css files: NotificationHistory, NotificationToast, MessageDetailPane
- [x] FQ-21: Add logging to JS interop failure paths — `ILogger<T>` added to Modal, AksPage, DlqView; silent catch blocks now log `Logger.LogDebug`

### Cross-cutting

- [x] Planning complete
- [x] Decisions reviewed (decisions.md)
- [x] All waves implemented (Waves 0–4; FQ-1/FQ-2 intentionally deferred)
- [x] 352 tests passing (74 bUnit, 199 Core, 24 DevOps, 17 Azure, 38 Kubernetes) — 0 failures (1 pre-existing flaky IO race skipped)
- [x] Docs aligned (FQ-1 graduated, FQ-2 dropped with rationale)
- [x] Ready for review

## Completed

- FQ-3: Event subscription leaks — pre-existing, verified correct in all pages ✅
- FQ-4: IDisposable EventBus subscriptions ✅
- FQ-5: TabService cap (MaxTabs=50) + ClearAll() ✅
- FQ-6: ShouldRender overrides (EntityTree, RedisKeyList) ✅
- FQ-7: RedisKeyList virtualization ✅
- FQ-8: EntityTree progressive loading ✅
- FQ-9: EventCallback audit — all already correct ✅
- FQ-10: SwebKitComponentBase foundation class ✅
- FQ-13: CascadingValue convention documented ✅
- FQ-11: Modal usage consistent — no inline modal implementations found ✅
- FQ-12: PageToolbar.razor created + applied to ObservabilityPage ✅
- FQ-14: ErrorCallout enhanced with Title/Details/OnDismiss ✅
- FQ-15: ARIA audit — all icon buttons already have aria-label ✅
- FQ-16: SkeletonRows adopted in EntityTree initial load ✅
- FQ-17: Tab persistence via UiStateRepository with 500ms debounce ✅
- FQ-18: Batch progress feedback in RedisPage + DlqView ✅
- FQ-19: Inline styles extracted → ServiceBusPage.razor.css, StoragePage.razor.css, SettingsPage.razor.css ✅
- FQ-20: CSS isolation → NotificationHistory.razor.css, NotificationToast.razor.css, MessageDetailPane.razor.css ✅
- FQ-21: JS interop logging added to Modal, AksPage, DlqView ✅

## Remaining

- None in this feature. FQ-1 continues in `component-decomposition` feature.

## Blockers

- None.

## Validation

- Test Plan: [test-plan.md](test-plan.md)
- Validation status: Not started

## Notes

- Waves are sequential by default. Wave 0 and Wave 1 could partially overlap (FQ-4 is independent of FQ-1).
- No Jira ticket linked. Create one if this feature is approved.
- Each wave should be a separate PR for easier review.
