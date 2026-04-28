# Status - performance-optimization-program

---

title: "Status - performance-optimization-program"
owner: "GitHub Copilot"
state: "In Progress"
jira: ""
branch: ""
started: "2026-04-28"
last_updated: "2026-04-28"

---

## Quick summary

Slice 1A is now in progress. Lightweight timing probes are in place for shell startup, route render completion, and Observability Logs first render, editor readiness, and query execution. A deterministic `%APPDATA%\SwebKit\logs\performance-baseline.log` capture path is also in place for interactive desktop baseline collection before behavior changes. Only Observability and AKS are currently treated as authoritative feature baselines.

**Jira:** not linked

**Current focus:** validate Slice 1B runtime behavior after deferring Monaco from global startup into Observability Logs first use, then capture the after-state startup and Logs timings.

## Entry criteria for implementation

- The feature docs define the slice order, working budgets, and validation expectations.
- The first slice is constrained to startup and shell behavior only.
- No publish-time experiments begin until startup and heavy-page behavior have been measured.

## Ordered execution queue

1. Slice 1A - Baseline capture and logging plan.
2. Slice 1B - Defer Monaco and any other non-startup-critical assets.
3. Slice 1C - Tighten shell cascades, layout rerender scope, and shortcut initialization.
4. Slice 2A - Optimize Observability tab lifecycle and first-use editor or chart cost.
5. Slice 2B - Optimize Service Bus list-detail rendering.
6. Slice 2C - Optimize Pipelines and Releases tree-detail rendering.
7. Slice 3A - Tighten AKS filter, panel, and interaction hot paths.
8. Slice 3B - Tighten Storage and Redis repeated-list behavior.
9. Slice 3C - Tighten Incident Timeline and Settings interaction hot paths.
10. Slice 4A - Run controlled publish-time experiments only if earlier slices leave worthwhile gains on the table.

## Progress checklist

### Wave 1 - Baseline and startup

- [x] Planning complete
- [x] Capture startup, first-route, and route-switch baseline timings
- [x] Record first-use cost for Observability Logs and any other startup-adjacent heavy surface
- [ ] Reduce globally loaded startup assets
- [ ] Review shell cascades and render boundaries
- [ ] Review command palette and keyboard shortcut initialization for one-time registration only
- [ ] Validate startup and shell regressions

### Wave 2 - Heavy workspace rendering

- [ ] Observability tab lifecycle and child rerender plan executed
- [ ] Service Bus list/detail rendering plan executed
- [ ] Pipelines/Releases tree/detail rendering plan executed
- [ ] Behavior-scoped validation complete

### Wave 3 - Interaction hot paths

- [ ] AKS filter, panel, and resize hot-path plan executed
- [ ] Storage repeated-list plan executed
- [ ] Redis repeated-list and detail-pane plan executed
- [ ] Incident Timeline refresh and evidence rendering plan executed
- [ ] Settings and config-form keystroke-path plan executed
- [ ] Manual interaction checks complete

### Wave 4 - Publish-time optimization

- [ ] Trimming/package-size experiments planned
- [ ] Publish configuration changes validated
- [ ] Docs aligned
- [ ] Ready for review

## Completed

- Compared MAUI and Blazor performance guidance against the SwebKit architecture and runtime model.
- Identified the main optimization levers as startup payload, Blazor rerender control, high-frequency event handling, and repeated-list rendering.
- Mapped concrete optimization work by feature area and execution wave.
- Added working budgets, slice-by-slice deliverables, and exit criteria so implementation can begin without another planning pass.
- Added low-risk timing instrumentation in `MainLayout` and `ObservabilityLogs` so Slice 1A can capture shell startup, route render, Logs first render, editor readiness, and query timing without changing behavior.
- Added a deterministic app-data baseline recorder targeting `%APPDATA%\SwebKit\logs\performance-baseline.log` so timing evidence can be collected without attaching a debugger.
- Verified the instrumentation compiles with a direct build of `src/SwebKit.App/SwebKit.App.csproj`.
- Captured the first manual startup-only baseline from an interactive launch with no navigation:
  - `CreateMauiApp completed` at `08:04:04`
  - `App window created` at `08:04:04`
  - shell essentials initialized in `8.49 ms`
  - shell background initialization completed in `327.19 ms`
  - shell first render ready in `3669.03 ms`
- Captured a second interactive baseline run with warm route navigation:
  - shell essentials initialized in `5.80 ms`
  - shell background initialization completed in `360.60 ms`
  - shell first render ready in `12517.27 ms`
  - Observability route rendered in `103.24 ms`
  - AKS route rendered in `1001.07 ms`
  - Non-authoritative raw timings were also captured for Service Bus, Redis, Storage, and Pipelines, but those areas are not configured and should not drive optimization priorities.
- Captured Observability Logs first-use evidence from an interactive run:
  - Observability route rendered in `450.92 ms`
  - Logs component first render in `65.43 ms`
  - Logs editor ready in `312.98 ms`
  - first Logs query completed in `367.62 ms` with `1` row
  - second Logs query completed in `85.30 ms` with `1` row
- Fixed a Monaco editor lifecycle bug in `ObservabilityLogs` that could throw `Couldn't find the editor with id ...` when presets or saved queries tried to push content into a stale editor instance during mode transitions.
- Implemented Slice 1B in code by removing global Monaco script tags from startup, adding a lazy loader under `wwwroot/js/monacoLoader.js`, and gating the Logs editor behind first-use asset loading in `ObservabilityLogs`.

## Remaining

- Implement and validate the Wave 1 startup-focused slices in order, prioritizing startup, Observability, and AKS.
- Execute feature-specific waves in priority order and update this status file after each slice closes.

## Blockers

- Slice 1B still needs runtime validation in an interactive desktop session. Compile validation passed, but first-open editor behavior and after-state timings have not been remeasured yet.

## Validation

- Test Plan: [test-plan.md](c:/Projects/Personal/SwebKit/docs/features/active/performance-optimization-program/test-plan.md)
- Validation status: Instrumentation and the app-data capture path compile, manual startup plus route-switch baselines are recorded, Logs first-use evidence is recorded, the preset-triggered Monaco crash is fixed at compile time, and the Monaco lazy-load Slice 1B change now compiles pending runtime confirmation

## Notes

- This feature intentionally separates measured behavior wins from later publish-time experiments.
- Optimizations should follow the existing `SwebKitComponentBase` and `SwebKitLayoutBase` patterns before introducing feature-local render controls.
- Each completed slice should leave behind three things: code changes, before and after evidence, and updated status and test-plan notes.
- For the current local profile, only Observability and AKS are configured enough to count as decision-grade baseline evidence. Service Bus, Redis, Storage, and Pipelines timings can remain as raw notes only.
