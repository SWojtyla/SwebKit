# Test Plan — Performance & UI Responsiveness

## Approach

All Phase 1-3 changes are **behaviour-preserving**, so the primary safety net is the existing
test suite plus targeted smoke on the affected surfaces. Add focused tests only where behaviour is
subtle (cache invalidation, snapshot-under-concurrency, loop lifecycle). Performance is validated
qualitatively (smoke) and, where available, quantitatively via `PerformanceBaselineRecorder`.

## Test Levels

| Level        | Tooling                                      | Used for                                              |
| ------------ | -------------------------------------------- | ----------------------------------------------------- |
| Unit         | xUnit in `tests/SwebKit.*.Tests`             | Cache invalidation, snapshot reads, loop lifecycle    |
| Component    | bUnit in `tests/SwebKit.App.Tests`           | `@key`, virtualization, render-count where renderable |
| Manual smoke | Interactive MAUI (`build-maui-windows` task) | Perceived smoothness on hot paths                     |
| Baseline     | `PerformanceBaselineRecorder`                | Before/after timings on AKS open-panel & log tail     |

> Known limitation (from the API Client feature): some `ApiClientPage`/workspace components cannot
> be bUnit-rendered because they transitively pull in the MAUI-only `FilePicker`. For those, verify
> by code-trace + manual smoke and note it here.

## Phase 1 — Async / UI-thread stalls

| #   | Scenario                                                                            | Level  | Expected                                                    |
| --- | ----------------------------------------------------------------------------------- | ------ | ----------------------------------------------------------- |
| 1   | Read `PodHealthMonitorService.RecentEvents` while the poll loop writes concurrently | Unit   | No blocking; returns a consistent snapshot; no torn state   |
| 2   | Dispose `AlertMonitorService` / `PodHealthMonitorService` while loop is running     | Unit   | Loop cancels promptly; no unobserved exception              |
| 3   | App exit with active port-forward sessions                                          | Manual | Process exits without hang; sessions stopped (or timed out) |
| 4   | Baseline capture cancels on service shutdown                                        | Unit   | `TakeBaselineAsync` observes cancellation; no `None` token  |

## Phase 2 — Blazor render hot paths

| #   | Scenario                                              | Level     | Expected                                                           |
| --- | ----------------------------------------------------- | --------- | ------------------------------------------------------------------ |
| 1   | Open/close each AKS detail panel type                 | Manual    | Panel opens/refreshes/closes; no full-page freeze/flicker          |
| 2   | Change internal panel state (e.g. YAML search toggle) | Component | Parent `AksPage` does **not** re-render its resource grids         |
| 3   | Filter pods/deployments, then reload data             | Unit      | Filtered cache invalidates; results correct after reload           |
| 4   | Filter with large collection (500+ pods)              | Manual    | Filtering stays responsive; no repeated full re-filter per render  |
| 5   | Tail pod logs with no new lines for 30s               | Manual    | CPU stays low; no per-tick re-render when not dirty                |
| 6   | Tail pod logs under high line volume                  | Manual    | Batched rendering; no render storm; scroll stays smooth            |
| 7   | Agent chat with 500+ messages                         | Manual    | Only visible messages in DOM; scroll smooth; auto-scroll preserved |
| 8   | AKS events list with 100+ events                      | Manual    | Virtualized; scroll/filter smooth                                  |

## Phase 3 — Render correctness & micro-optimizations

| #   | Scenario                                                  | Level     | Expected                                                  |
| --- | --------------------------------------------------------- | --------- | --------------------------------------------------------- |
| 1   | Add/remove/reorder Service Bus custom columns             | Component | `@key` present; no full-loop DOM rebuild; no flicker      |
| 2   | Switch Observability presets / saved queries              | Manual    | No flicker; correct selection                             |
| 3   | Open request builder repeatedly                           | Component | `Enum.GetValues` cached; method list stable               |
| 4   | Accumulate notifications, open history                    | Manual    | No `.Reverse()` per render; list order correct            |
| 5   | Sort Service Bus grid, trigger unrelated parent re-render | Component | Sort not recomputed when key unchanged                    |
| 6   | Expand/collapse large collection tree                     | Component | `ShouldRender` suppresses unrelated re-renders            |
| 7   | Dashboard with many tiles + auto-refresh                  | Manual    | Only changed tiles re-render; navigation stays responsive |
| 8   | Rapid edits in API Client (auto-save debounce)            | Manual    | `PeriodicTimer` debounce works; no duplicate saves / race |

## Phase 4 — Structural cleanliness (deferrable)

| #   | Scenario                                                    | Level | Expected                                               |
| --- | ----------------------------------------------------------- | ----- | ------------------------------------------------------ |
| 1   | Full `SwebKit.Kubernetes.Tests` after `partial class` split | Unit  | Green; no public API change; `IAksClient` unchanged    |
| 2   | Each library project after `ConfigureAwait(false)` sweep    | Unit  | Green per project; no deadlock/behaviour change        |
| 3   | `.Result`-after-`WhenAll` replaced                          | Unit  | Same results; existing tests green                     |
| 4   | DevOps approval fallback path exercised                     | Unit  | Fallback still works; now logs the swallowed exception |

## Exit Criteria

- All existing test suites remain green (no new failures vs. baseline).
- Focused new tests for Phase 1 cache/snapshot/loop lifecycle pass.
- Manual smoke on the hot paths shows no regressions and visibly smoother AKS panel/log/chat UX.
- Aikido full scan clean on changed first-party files for each merged phase.
