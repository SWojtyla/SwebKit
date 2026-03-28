# Archive Summary — Component Decomposition

**Archived:** 2026-03-28
**Final state:** Done
**Jira ticket:** None

---

## What was delivered

- **AksPage** decomposed from 2,415 → 1,342 lines (44% reduction): extracted `AksYamlViewer.razor`, `AksHelmPanel.razor`, `AksConnectionBar.razor`, `AksDetailPanels.razor`
- **RedisPage** decomposed from 1,071 → 892 lines (17% reduction): extracted `RedisConnectionBar.razor`, `RedisToolbar.razor`
- **ServiceBusPage** decomposed from 792 → 535 lines (32% reduction): extracted `ServiceBusNamespacePanel.razor` + `NsState.cs`
- 7 new focused components, each independently testable
- 26 new bUnit tests covering all extracted components (378 total passing)
- `NavigateInList` generic helper extracted from `SelectRelative` (−49 lines from AksPage)

## Key technical decisions

- **D-001:** AksPage first — highest complexity, front-loaded risk
- **D-002:** `AppStateService` decomposition (FQ-2) dropped — 91-line facade with clear single responsibility; decomposing would add noise not clarity
- **D-004:** AksPage target was <300 lines; settled at 1,342 — accepted because the remaining lines are legitimately orchestration (79 action handlers mapping 8 resource types to child component calls)
- **BL-12 (new pitfall):** `@ref` inside `@if` — calling `OpenAsync` directly on the child bypasses the parent's `HasOpenPanel` re-evaluation; always use the pending-open pattern so the parent re-renders first

## Bugs fixed during validation

- **NullReferenceException on first YAML open** (`AksDetailPanels.OpenYamlAsync`): `_yamlViewer` was null because `@if (HasOpenPanel)` was false; fixed with pending-open + `OnAfterRenderAsync` pattern
- **YAML panel only opens once** (reopening silently failed): the `_yamlViewer is not null` fast-path bypassed parent re-render; fixed by always routing through the pending path (documented as BL-12)
- **Monaco `blazorMonaco was undefined`** (`ObservabilityLogs`): `jsInterop.js` must load *before* `loader.js` — it contains the AMD `require` pre-config object; loading it after overwrites the AMD runtime

## Lessons learned / pitfalls recorded

- **BL-12** added to `docs/pitfalls/blazor-maui.md`: never shortcut to direct child method when parent `@if` controls child existence
