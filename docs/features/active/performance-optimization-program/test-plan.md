# Test Plan - performance-optimization-program

---

title: "Test Plan - performance-optimization-program"
owner: "GitHub Copilot"
status: "Planned"
created: "2026-04-28"
updated: "2026-04-28"

---

## Goal

Validate that each performance change improves responsiveness or startup cost without introducing stale UI, broken interactions, or feature regressions across the main SwebKit workspaces.

## Measurement protocol

Each implementation slice must capture and compare before and after evidence using the same machine, build configuration, and route sequence.

| Item            | Rule                                                                              |
| --------------- | --------------------------------------------------------------------------------- |
| Machine profile | Use one stable Windows machine for comparison within a slice                      |
| Build shape     | Compare like for like; do not compare Debug against Release inside the same slice |
| Route sequence  | Use the same ordered route path for every repeated measurement                    |
| Data mode       | Record whether demo mode or real profile data was used                            |
| Repetitions     | Capture at least 3 runs for startup and 5 runs for high-frequency interactions    |
| Evidence        | Record timings, notable UI symptoms, and any errors or warnings                   |

## Baseline evidence to capture before Wave 1

- Use `%APPDATA%\SwebKit\logs\performance-baseline.log` as the baseline evidence source. The file is populated by the new timing probes emitted from `MainLayout`, `ObservabilityLogs`, and early app startup markers.
- Before each capture run, close any existing `SwebKit.App` process and delete `%APPDATA%\SwebKit\logs\performance-baseline.log` so the next launch produces a clean sample.
- For the current local profile, treat only Observability and AKS timings as decision-grade evidence. Ignore Service Bus, Redis, Storage, and Pipelines when deciding Slice 1 priorities unless those areas are later configured and remeasured.
- First captured sample from an interactive startup-only run with no navigation:
  - shell essentials initialized in `8.49 ms`
  - shell background initialization completed in `327.19 ms`
  - shell first render ready in `3669.03 ms`
- Second captured sample from an interactive run with route navigation:
  - shell essentials initialized in `5.80 ms`
  - shell background initialization completed in `360.60 ms`
  - shell first render ready in `12517.27 ms`
  - Observability route rendered in `103.24 ms`
  - AKS route rendered in `1001.07 ms`
  - Non-authoritative raw timings were also captured for unconfigured areas and are intentionally excluded from optimization decisions.
- Third captured sample from an interactive Observability Logs session:
  - Observability route rendered in `450.92 ms`
  - Logs component first render in `65.43 ms`
  - Logs editor ready in `312.98 ms`
  - first Logs query completed in `367.62 ms` with `1` row
  - second Logs query completed in `85.30 ms` with `1` row
- Cold launch to visible shell chrome.
- Cold launch to first usable route after initialization.
- Warm navigation time between Dashboard, Observability, and AKS.
- First open cost for Observability Logs.
- Filter response and selection response on one representative page for AKS.
- Memory and CPU observations during a 60 second session that includes a heavy page, a streaming or refresh path, and route changes.

## Scope

- In scope: startup timing, shell interactions, page navigation, repeated-list behavior, tab switching, search/filter responsiveness, streaming/log rendering, and targeted package-loading changes.
- Out of scope: synthetic microbenchmarks that do not map to real user workflows, and platform coverage outside the supported Windows desktop flow.

## Main scenarios (priority)

1. Scenario: cold launch to first interactive shell - Expected result: startup time and perceived readiness improve after Wave 1 without losing banners, nav state, or initial hydration behavior.
2. Scenario: switch between configured heavy workspaces such as Observability and AKS - Expected result: route switching and first usable content stay responsive and do not regress after render-boundary changes.
3. Scenario: interact with large or repeated UI surfaces such as pod lists, pipeline trees, message lists, and log views - Expected result: scrolling, filtering, selection, and panel open/close remain smooth with correct state updates.
4. Scenario: open the Observability Logs experience - Expected result: Monaco lazy loading defers startup cost but still loads reliably on first demand with no broken editor initialization.
5. Scenario: auto-refresh, timers, and streaming surfaces - Expected result: timers or refresh loops do not cause runaway rerenders, stale views, or CPU-heavy behavior.

## Wave-specific validation

### Wave 1 - Baseline and startup

- Validate startup timings before and after the change set.
- Verify `MainLayout` still shows the shell immediately and preserves warning banners, tab restore, and background initialization behavior.
- Verify global keyboard shortcuts still register exactly once and continue working after navigation.
- Verify first open of Observability Logs loads Monaco successfully after startup deferral.

### Wave 2 - Heavy workspaces

- Observability: validate tab switching, pending-query drill-through, chart rendering, and Logs first use and reopen flows.
- Service Bus and Pipelines or Releases: defer validation unless those areas are configured for the local profile and intentionally brought back into scope.

### Wave 3 - Interaction hot paths

- AKS: validate filter typing, panel resize, logs, YAML, pod detail, and any auto-refresh pause or resume behavior.
- Storage: validate container switch, blob list updates, and detail preview or download actions.
- Redis: validate key browsing, selection, and detail rendering without page-wide redraw symptoms.
- Incident Timeline: validate scope changes, explicit refresh, coverage strip, and detail panel updates.
- Settings: validate keystroke-heavy forms, save flows, and persisted state updates.

### Wave 4 - Publish-time optimization

- Validate that publish-time changes do not break Blazor interop, library loading, or reflection-heavy components.
- Rerun startup smoke and first-use heavy-page smoke after every packaging experiment.

## Automated coverage

- Unit tests: existing feature test projects under `tests/` should be extended only for touched slices, especially render-state, initialization, and feature-specific page behaviors.
- Integration tests: targeted checks for lazy asset loading and page initialization behavior where component tests are practical.
- End-to-end tests: smoke flows for startup, navigation, and at least one heavy path per optimized feature should be added or updated in `tests/SwebKit.E2E.Tests` once implementation starts.

### Preferred automated additions by slice

- Slice 1B and 1C: component tests around startup state, first render, and deferred JS-loading behavior where feasible in `tests/SwebKit.App.Tests`.
- Slice 2A: focused tests for Observability tab state, pending query handoff, and Logs initialization.
- Slice 2B: focused tests for Service Bus tab state and selection behavior only if Service Bus is configured and returned to scope.
- Slice 3A: focused tests for AKS page state guards and throttled update behavior; use the existing AKS-focused test command from repo memory when relevant.

## Test data and setup

- Capture timings on Windows in a representative local environment before and after each optimization slice.
- Use both demo mode and at least one real configured profile where practical to avoid overfitting to synthetic data.
- Preserve a stable comparison route set for the current profile: Dashboard, Observability, and AKS.
- Keep at least one comparison run with warm caches and one from a cold app launch for startup-adjacent work.
- Record whether any external dependency slowness affected the run so UI improvements are not confused with backend latency swings.

## Manual checks

- Check: baseline log capture - steps: close all running `SwebKit.App` processes, delete `%APPDATA%\SwebKit\logs\performance-baseline.log`, launch the app from an interactive Windows session, and confirm the file contains startup markers before collecting route-level timings.
- Check: shell startup - steps: launch from a cold state, record time to shell chrome and time to first usable page, verify theme, banners, tabs, and keyboard shortcuts still initialize correctly.
- Check: Observability logs - steps: open Observability, navigate to Logs, verify editor loads on first open, run a query, switch tabs, and confirm no lost state or broken JS interop.
- Check: AKS interaction hot paths - steps: filter resources rapidly, resize panels, open logs and YAML/detail panels, verify no visible lag spikes or stale panels.
- Check: Service Bus message browsing and Pipelines or releases - defer for this profile unless those areas are configured and brought back into scope.
- Check: incident timeline refresh - steps: change scope, keep current evidence visible, refresh explicitly, and verify only the latest result is rendered.
- Check: settings form typing - steps: edit multiple settings inputs, verify no visible keystroke lag, and confirm saves still persist correctly.

## Regression risks & mitigations

- Risk: `ShouldRender` or non-rendering event handling suppresses necessary UI updates - Mitigation: pair each change with state-transition checks and targeted component or manual validation.
- Risk: lazy JS loading breaks first-render behavior in Blazor Hybrid - Mitigation: validate first-open and reopen flows, and respect the DOM/interop timing rules in [docs/pitfalls/blazor-maui.md](c:/Projects/Personal/SwebKit/docs/pitfalls/blazor-maui.md).
- Risk: virtualization changes behavior or keyboard interaction in grids and lists - Mitigation: verify selection, scrolling, filtering, and action affordances explicitly.
- Risk: trimming or publish changes break package behavior - Mitigation: keep publish-time changes isolated to Wave 4 with smoke and regression checks.

## Acceptance criteria

- Each implemented performance slice has before/after measurements or a clearly recorded user-perceived improvement check.
- No critical behavior regressions are introduced in the touched feature area.
- Startup and heavy workspace interactions remain functionally correct in manual smoke testing.
- Tests and docs are updated alongside implementation changes.
- Slice closure requires explicit exit criteria in the status file, not just a code change.

## Validation status

- Automated: Compile validation passed for the initial Slice 1A instrumentation and app-data capture changes
- Automated: Compile validation passed for the Slice 1A instrumentation changes and the Observability Logs Monaco lifecycle fix
- Automated: Compile validation passed for Slice 1B Monaco startup deferral
- Manual: Startup, route-switch, and Observability Logs first-use baselines captured in an interactive desktop session using `%APPDATA%\SwebKit\logs\performance-baseline.log`
- Manual next step: confirm first open of Observability Logs still loads Monaco correctly after lazy loading, then capture updated startup and Logs timings for before/after comparison

## Sign-off

- **Approved by:**
- **Date:**
- **Conditions (if any):**
