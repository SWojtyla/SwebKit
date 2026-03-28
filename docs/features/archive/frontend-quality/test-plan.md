# Test Plan — Frontend Code Quality & Architecture Hardening

---

title: "Test Plan — Frontend Code Quality & Architecture Hardening"
owner: ""
status: "Not started"
created: "2026-03-27"
updated: "2026-03-27"

---

## Goal

Validate that all refactoring changes preserve existing behavior and achieve their quality/safety goals. No functional changes are expected — all tests verify behavioral equivalence and new quality properties (leak-free disposal, bounded collections, correct render suppression).

## Scope

- **In scope:** bUnit component tests, unit tests for services, manual verification of visual equivalence and accessibility
- **Out of scope:** E2E/Playwright tests (no functional changes), performance benchmarks (out of scope — only manual spot-checks), backend tests

## Test approach

This feature is pure refactoring. The primary testing strategy is:

1. **Baseline before changes:** Ensure existing bUnit tests pass before each wave starts
2. **Add coverage for new contracts:** Each extracted component, new service, or pattern change gets targeted tests
3. **Regression gate:** All existing tests must continue to pass after each wave
4. **Manual UX verification:** Visual comparison before/after for each decomposed page

Existing test infrastructure: `tests/SwebKit.App.Tests/` (bUnit), `tests/SwebKit.Core.Tests/`, etc.

---

## Main scenarios by wave

### Wave 0 — Safety & Memory

| #    | Scenario                                                               | Expected result                                |
| ---- | ---------------------------------------------------------------------- | ---------------------------------------------- |
| T-01 | Component subscribes to event, is disposed, event fires                | Handler NOT called; no ObjectDisposedException |
| T-02 | EventBus.Subscribe returns IDisposable; dispose it; publish event      | Handler NOT called                             |
| T-03 | EventBus.Subscribe (old void overload, if kept) emits compiler warning | Obsolete warning at build time                 |
| T-04 | TabService exceeds max tab count                                       | Oldest inactive tab is removed                 |
| T-05 | TabService: switch environment                                         | All tabs for previous environment cleared      |
| T-06 | TabService: add tabs to max, then add one more                         | Count ≤ max; TabsChanged fires                 |

### Wave 1 — Architecture & Decomposition

| #    | Scenario                                                            | Expected result                                                             |
| ---- | ------------------------------------------------------------------- | --------------------------------------------------------------------------- |
| T-07 | ServiceBusPage renders with mock IServiceBusClient                  | Same structure as before decomposition (connection bar, tree, list, detail) |
| T-08 | Extracted ServiceBusConnectionBar receives correct parameters       | Renders connection state, fires connect/disconnect events                   |
| T-09 | Extracted ServiceBusToolbar receives correct parameters             | Renders actions, fires action events                                        |
| T-10 | AksPage renders with mock IAksClient                                | Same structure as before decomposition                                      |
| T-11 | RedisPage renders with mock IRedisClient                            | Same structure as before decomposition                                      |
| T-12 | AppStateService.Config delegates to IConfigurationService           | Get/set config routes through focused service                               |
| T-13 | AppStateService.CurrentEnvironment delegates to IEnvironmentService | Get/set environment routes through focused service                          |
| T-14 | IConfigurationService unit test: load/save                          | Config persisted and loaded correctly                                       |
| T-15 | IEnvironmentService unit test: switch                               | Fires event, updates current environment                                    |
| T-16 | SwebKitComponentBase.RunAsync: normal completion                    | IsLoading = true during work, false after                                   |
| T-17 | SwebKitComponentBase.RunAsync: exception thrown                     | ErrorMessage set; IsLoading = false                                         |
| T-18 | SwebKitComponentBase.RunAsync: OperationCanceledException           | Exception re-thrown (CS-2); ErrorMessage NOT set                            |
| T-19 | SwebKitComponentBase.RunAsync: calls InvokeAsync(StateHasChanged)   | Verified via bUnit test context                                             |

### Wave 2 — Performance Polish

| #    | Scenario                                                      | Expected result                                                   |
| ---- | ------------------------------------------------------------- | ----------------------------------------------------------------- |
| T-20 | EntityTree ShouldRender: parent re-renders, no data change    | ShouldRender returns false; no child render                       |
| T-21 | MessageListView ShouldRender: new messages arrive             | ShouldRender returns true; render occurs                          |
| T-22 | RedisKeyList: render 10k keys                                 | Virtualize renders only visible subset (verify DOM element count) |
| T-23 | RedisKeyList: filter with Virtualize                          | Filtered set renders correctly                                    |
| T-24 | EntityTree progressive load: mock returns queues, then topics | UI updates after queues arrive (not waiting for topics)           |
| T-25 | EventCallback audit: UI callback triggers StateHasChanged     | Parent re-renders after child EventCallback                       |

### Wave 3 — UX Consistency & Polish

| #    | Scenario                                    | Expected result                                  |
| ---- | ------------------------------------------- | ------------------------------------------------ |
| T-26 | ErrorCallout: display error with details    | Title, message, expandable details visible       |
| T-27 | ErrorCallout: dismiss                       | Component disappears                             |
| T-28 | Modal: open via shared pattern              | Consistent overlay, focus trap, Escape dismisses |
| T-29 | PageToolbar: render with filter + actions   | Filter input, action buttons, count label render |
| T-30 | ARIA: icon-only buttons have aria-label     | Grep audit confirms zero missing labels          |
| T-31 | SkeletonRows: initial list load             | Skeleton rows displayed during load              |
| T-32 | Tab persistence: close app, reopen          | Previously open tabs restored                    |
| T-33 | Tab persistence: invalid saved tabs         | Invalid tabs silently skipped                    |
| T-34 | Bulk delete (Redis): 10 keys                | Progress shows 0/10 → 10/10                      |
| T-35 | Bulk resubmit (ServiceBus): partial failure | Failed items indicated, retry available          |

### Wave 4 — CSS & Style Cleanup

| #    | Scenario                                            | Expected result                                           |
| ---- | --------------------------------------------------- | --------------------------------------------------------- |
| T-36 | Component renders after inline style extraction     | Visual output identical (manual comparison)               |
| T-37 | CSS isolation: component-specific styles don't leak | Styles scoped to component only                           |
| T-38 | JS interop failure: Monaco init fails               | Warning logged with method name; component does not crash |
| T-39 | JS interop failure: xterm.js init fails             | Warning logged; component shows error state               |

---

## Automated coverage

### bUnit component tests (`tests/SwebKit.App.Tests/`)

| Area                        | New/updated tests | Target                             |
| --------------------------- | ----------------- | ---------------------------------- |
| Wave 0 — Event disposal     | T-01, T-02, T-06  | New                                |
| Wave 0 — TabService         | T-04, T-05, T-06  | New or extend `TabService` tests   |
| Wave 1 — Page decomposition | T-07 through T-11 | Update existing page tests         |
| Wave 1 — Focused services   | T-12 through T-15 | New                                |
| Wave 1 — ComponentBase      | T-16 through T-19 | New                                |
| Wave 2 — ShouldRender       | T-20, T-21        | New                                |
| Wave 2 — Virtualize         | T-22, T-23        | New or update `RedisKeyList` tests |
| Wave 2 — Progressive load   | T-24              | New                                |
| Wave 3 — ErrorCallout       | T-26, T-27        | New or update                      |
| Wave 3 — Modal              | T-28              | New or update                      |
| Wave 3 — Tab persistence    | T-32, T-33        | New                                |
| Wave 3 — Bulk ops           | T-34, T-35        | New                                |

### Unit tests (`tests/SwebKit.Core.Tests/` or `tests/SwebKit.App.Tests/`)

| Area                   | New tests  | Target        |
| ---------------------- | ---------- | ------------- |
| EventBus IDisposable   | T-02       | New           |
| TabService cap/cleanup | T-04, T-05 | New or extend |
| ConfigurationService   | T-14       | New           |
| EnvironmentService     | T-15       | New           |

## Test data and setup

- **bUnit:** Use `TestContext` with mocked services via `Services.AddSingleton<IServiceBusClient>(mockClient)`
- **AppStateService mocks:** Create stub focused services (IConfigurationService, IEnvironmentService) for isolation
- **Large data sets:** Generate 10k Redis keys in-memory for virtualization tests (T-22)
- **Tab persistence:** Use in-memory storage mock for `ui-state.json` operations

## Manual checks

| #    | Check                               | Steps                                                                     |
| ---- | ----------------------------------- | ------------------------------------------------------------------------- |
| M-01 | Visual equivalence — ServiceBusPage | Compare screenshot before/after FQ-1 decomposition                        |
| M-02 | Visual equivalence — AksPage        | Compare screenshot before/after FQ-1 decomposition                        |
| M-03 | Visual equivalence — RedisPage      | Compare screenshot before/after FQ-1 decomposition                        |
| M-04 | Memory stability                    | Open/close pages 50+ times → check memory via Task Manager or diagnostics |
| M-05 | Accessibility spot-check            | Tab through ServiceBusPage with Narrator → verify all buttons announced   |
| M-06 | CSS extraction visual diff          | Compare page renders before/after FQ-19 per file                          |
| M-07 | Redis 10k keys                      | Load 10k keys → verify smooth scrolling                                   |
| M-08 | Tab restore                         | Close app with 5 tabs open → reopen → verify tabs restored                |
| M-09 | Bulk ops progress                   | Select 10 Redis keys → delete → verify progress indicator                 |

## Regression risks & mitigations

- **Risk:** Component decomposition (FQ-1) changes DOM structure, breaking CSS selectors — **Mitigation:** Use CSS isolation (`.razor.css`) for component-specific styles; avoid deep descendant selectors
- **Risk:** AppStateService facade (FQ-2) introduces subtle state synchronization bugs — **Mitigation:** Focused services own state; facade delegates without caching
- **Risk:** ShouldRender overrides (FQ-6) suppress needed renders — **Mitigation:** Only suppress parent-triggered renders; always include `IsLoading` in the hash
- **Risk:** CSS extraction (FQ-19) changes specificity or cascade — **Mitigation:** Extract one file at a time with visual verification

## Acceptance criteria

- All existing `SwebKit.App.Tests` pass before and after each wave
- All new test scenarios (T-01 through T-39) pass
- Zero icon-only buttons without `aria-label` (automated grep check)
- Zero silent JS interop catch blocks (automated grep check)
- Manual visual checks pass for all 3 decomposed pages
- Memory stable after 50+ page open/close cycles

## Validation status

- Automated: Not started
- Manual: Not started

## Sign-off

- Owner:
- Date:
