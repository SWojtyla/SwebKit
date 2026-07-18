# Blazor Performance Standardization

## Goal

Standardize render coalescing and ShouldRender patterns across all Blazor components to eliminate inconsistent implementations, reduce maintenance burden, and improve performance optimization effectiveness. Consolidate multiple render control patterns into a unified `SwebKitComponentBase` approach with configurable debounce windows and proper lifecycle management.

**Jira:** not linked

## Quick Links

- Status: `status.md`
- Technical plan: `technical-plan.md`
- Test plan: `test-plan.md`
- Architecture context: `docs/architecture/functionalities/dashboard.md`,
  `docs/agents/architecture.md` (Component Architecture)

## Scope

- **Standardize render coalescing** across DashboardPage, AksPage, CollectionTree, and other components using `SwebKitComponentBase.RequestCoalescedRender()`
- **Consolidate ShouldRender implementations** - remove custom overrides in favor of base class `RequestRender()` pattern
- **Configurable debounce windows** - replace hardcoded 75ms/150ms delays with configurable options
- **Improved cancellation handling** - add proper IDisposable pattern to base class for cleanup
- **Performance telemetry** - add render frequency counters and coalescing effectiveness metrics
- **Maintain existing behavior** - ensure no regressions in current performance optimizations

## Non-Goals

- No changes to virtualization implementations (PodGrid, DeploymentGrid, CollectionTree virtualization)
- No changes to data loading patterns (AksPage parallel dataset loading)
- No new performance optimization techniques beyond standardization
- No changes to component business logic or data contracts

## Dependencies

- `SwebKitComponentBase` base class (`src/SwebKit.App/Components/Shared/SwebKitComponentBase.cs`)
- DashboardPage render coalescing (`src/SwebKit.App/Components/Pages/DashboardPage.Rendering.cs`)
- AksPage incremental rendering (`src/SwebKit.App/Components/Pages/AksPage.razor`)
- CollectionTree render control (`src/SwebKit.App/Components/ApiClient/CollectionTree.razor`)

## Risks

| Risk                                                                                | Mitigation                                                                                                      |
| ----------------------------------------------------------------------------------- | --------------------------------------------------------------------------------------------------------------- |
| Breaking existing performance optimizations during standardization                 | Phase-based migration with per-component validation; keep old implementations as fallback during transition    |
| Configurable debounce windows introduce tuning complexity                          | Provide sensible defaults; document tuning guidelines; add environment-specific presets                       |
| Base class changes affect all derived components                                     | Make enhancements additive/optional; use virtual methods for customization; extensive regression testing        |
| Telemetry overhead impacts performance                                              | Make metrics collection optional/conditional; use low-overhead counters; sample-based logging                  |

## Waves

| Wave | What                                                                                     | Module             |
| ---- | ---------------------------------------------------------------------------------------- | ------------------ |
| A    | Enhance SwebKitComponentBase with configurable coalescing and lifecycle management      | `technical-plan.md` |
| B    | Migrate DashboardPage to base class coalescing pattern                                  | `technical-plan.md` |
| C    | Migrate CollectionTree to use RequestRender() instead of ShouldRender override          | `technical-plan.md` |
| D    | Add configurable debounce windows and app settings integration                           | `technical-plan.md` |
| E    | Add performance telemetry and metrics collection                                         | `technical-plan.md` |
| F    | Validation, regression testing, and documentation update                                | `test-plan.md`     |
