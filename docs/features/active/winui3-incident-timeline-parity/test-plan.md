# Test Plan - winui3-incident-timeline-parity

---

title: "Test Plan - winui3-incident-timeline-parity"
owner: ""
status: "In Progress"
created: "2026-04-27"
updated: "2026-04-27"

---

## Goal

Validate that the native Incident Timeline route reaches the agreed WinUI parity slice: a usable investigation workbench, a native settings or mapping repair path, aligned shell copy, and focused regression safety around the shared incident backend.

## Scope

- In scope: native route navigation, workbench scope and refresh interactions, source coverage and evidence presentation, main page states, native Incident Timeline settings coverage, dashboard or cutover copy alignment, native source-page investigation launch, and focused WinUI validation in `tests/SwebKit.WinUI.Tests/`
- Out of scope: backend evidence aggregation redesign and new Incident Timeline capabilities that do not already exist in MAUI
- Out of scope: new launch sources beyond the currently implemented WinUI Service Bus, Pipelines, and Observability entry points

## Main scenarios (priority)

1. Scenario: selecting Incident Timeline in WinUI navigation opens a real native investigation workspace. Expected result: the route no longer stops at the current scaffold-only page and does not fall back to generic placeholder behavior.
2. Scenario: the native page supports the core MAUI workbench flow. Expected result: operators can set scope, refresh manually, review source coverage, inspect evidence rows, and open detail content with clear empty, loading, partial, and error states.
3. Scenario: mapping guidance leads to a real native repair path. Expected result: page-level guidance and settings navigation land on an Incident Timeline settings section that supports workload mappings instead of a deferred placeholder.
4. Scenario: dashboard and cutover copy match the shipped WinUI state. Expected result: once this slice lands, native dashboard messaging no longer says Incident Timeline is outside the migration scope.
5. Scenario: shared incident backend behavior stays stable. Expected result: the WinUI lift reuses the existing incident models and services without introducing route-local regressions in scope interpretation or mapping expectations.
6. Scenario: native source-page drill-through seeds Incident Timeline correctly. Expected result: Service Bus, Pipelines, and Observability can navigate to Incident Timeline with a typed seed, a provenance callout, and the right preselected evidence sources.

## Automated coverage

- Build validation: `build-winui` must stay green and is the default inner-loop executable validation for WinUI route, XAML, and view-model changes.
- Existing tests: keep `tests/SwebKit.WinUI.Tests/` green.
- New tests: add focused WinUI coverage for Incident Timeline navigation, representative page-state transitions, Incident Timeline settings-section selection or repair navigation, and dashboard copy alignment.
- Validation cadence rule: do not use raw `dotnet test tests/SwebKit.WinUI.Tests/SwebKit.WinUI.Tests.csproj ...` as an inner-loop check. After `build-winui` is green, use a final focused `dotnet test ... --filter "<exact tests>" --no-build` pass only for the changed WinUI tests.
- Regression target: rerun relevant shared incident or configuration tests if implementation touches shared contracts or configuration-health glue.

## Test data and setup

- Demo or mocked service coverage should exercise bootstrap, empty, partial, guidance, and error states without requiring live Azure resources.
- Live validation needs a representative environment with AKS, Observability, Service Bus, and DevOps configuration plus at least one workload mapping.

## Manual checks

- Check: route parity. Steps: select Incident Timeline from WinUI navigation and verify the page exposes a real investigation workspace instead of the current stub copy.
- Check: settings repair path. Steps: trigger mapping guidance or open Settings for Incident Timeline, add or update a workload mapping, return to the page, and verify the native route can use the repaired configuration.
- Check: workbench parity. Steps: change scope, refresh, inspect coverage, select evidence items, and confirm the main page states remain clear.
- Check: shell copy alignment. Steps: open the WinUI dashboard and any route-local readiness messaging and verify Incident Timeline is no longer framed as outside the current cutover scope.
- Check: source-page drill-through. Steps: launch Incident Timeline from native Service Bus, Pipelines, and Observability, then verify the landing state shows source provenance, preserves the intended source toggles, and routes to Settings when a mapping is still missing.

## Regression risks & mitigations

- Risk: the page gains route ownership but still misses the actual operator workflow. Mitigation: treat workbench behaviors and page states as first-class acceptance criteria.
- Risk: page guidance and settings navigation drift apart. Mitigation: validate the native settings-section key and repair loop explicitly.
- Risk: dashboard copy remains stale after the route becomes real. Mitigation: include shell copy review in the same validation pass as the route work.
- Risk: WinUI inner-loop validation regresses back to slow raw `dotnet test` runs. Mitigation: treat `build-winui` as the default inner-loop gate and reserve filtered `dotnet test --no-build` for the final focused pass.

## Acceptance criteria

- The WinUI `incident-timeline` route exposes a usable native workbench rather than only a scaffold page.
- Native Settings includes the Incident Timeline mapping surface needed by the page guidance and repair path.
- Dashboard and cutover-facing copy match the delivered route state.
- Focused WinUI validation uses the cheaper final-gate path: `build-winui` first, then filtered `dotnet test --no-build` only where test execution is actually needed.
- Cross-page native investigation launch is intentionally implemented only for the evidenced Service Bus, Pipelines, and Observability source pages.

## Validation status

- Automated: `build-winui` pass; compile-only WinUI test-project build pass; final focused `dotnet test --no-build` rerun still pending
- Manual: Not started

## Sign-off

- **Approved by:**
- **Date:**
- **Conditions (if any):**
