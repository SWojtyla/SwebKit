# Test Plan - redis-ops-insights

---

title: "Test Plan - redis-ops-insights"
owner: "GitHub Copilot"
status: "Not started"
created: "2026-04-12"
updated: "2026-04-12"

---

## Goal

Validate that Redis ops insights report correct TTL posture, bounded hot-key or slowlog evidence, and read-only Pub/Sub visibility while remaining explicit about scan coverage, unsupported commands, and page responsiveness.

## Scope

- In scope: TTL bucket math, expiry forecast accuracy, slowlog parsing, hot-key signal explanation, Pub/Sub visibility, cancellation behavior, demo-mode parity, and Redis page regression safety.
- Out of scope: continuous performance monitoring, production traffic capture, auto-remediation, and multi-node Redis cluster analysis.

## Main scenarios (priority)

1. Scenario: Loaded scan contains a mix of no-TTL, short-lived, and long-lived keys. - Expected result: TTL buckets, summary counts, and forecast windows are correct and explicitly scoped to the loaded scan set.
2. Scenario: The loaded scan covers only a fraction of the estimated keyspace. - Expected result: every TTL and hot-key insight surface shows partial coverage and does not imply full-cache certainty.
3. Scenario: A forecast window of 5 minutes, 30 minutes, 6 hours, and 24 hours is selected. - Expected result: the forecast counts reflect the loaded key TTL values and exclude no-TTL keys from expiry counts.
4. Scenario: `OBJECT FREQ` and `OBJECT IDLETIME` are unavailable. - Expected result: hot-key analysis degrades gracefully and explains that only slowlog or size/TTL heuristics are available.
5. Scenario: Slowlog contains repeated commands against the same key or prefix. - Expected result: grouped summaries show the dominant command, duration, and related key or prefix without requiring raw payload capture.
6. Scenario: Slowlog access is denied or unsupported. - Expected result: the panel renders an unsupported state and the rest of the Redis page remains usable.
7. Scenario: Pub/Sub has active channels and subscriber counts. - Expected result: channels, subscriber totals, and pattern subscription counts render in a read-only summary with optional prefix filtering.
8. Scenario: Pub/Sub is idle. - Expected result: the UI shows an empty-but-healthy state rather than an error.
9. Scenario: The user changes cache entry or restarts a scan while diagnostics are loading. - Expected result: prior requests are canceled, stale results do not render, and the newest cache context wins.
10. Scenario: Demo mode is active. - Expected result: the Redis page exposes deterministic TTL, slowlog, hot-key, and Pub/Sub fixtures that support both manual testing and component tests.

## Automated coverage

- Unit tests: `tests/SwebKit.Core.Tests`
- Update `RedisKeyspaceHealthAnalyzerTests.cs` for shared coverage/confidence behavior that TTL and hot-key insights reuse.
- Extend `RedisClientTests.cs` and `DemoRedisClientTests.cs` for new slowlog and Pub/Sub contracts plus graceful degradation behavior.
- Add likely new tests such as `RedisTtlForecastAnalyzerTests.cs` and `RedisOpsInsightsAggregatorTests.cs` for bucket math, forecast windows, and signal attribution.
- Component tests: `tests/SwebKit.App.Tests`
- Extend `RedisKeyspaceHealthExplorerTests.cs` for shared coverage messaging and drill-through expectations.
- Add likely new tests such as `RedisOpsInsightsPanelTests.cs`, `RedisSlowLogPanelTests.cs`, and `RedisPubSubPanelTests.cs` for loading, empty, unsupported, and partial-coverage states.
- End-to-end tests: `tests/SwebKit.E2E.Tests`
- Add a narrow smoke slice only if the page-level layout materially changes; most behavior can stay in bUnit because the feature is page-local and data-driven.
- CI gates: all new Redis-focused tests pass and the existing Redis page tests remain green.

## Test data and setup

- Deterministic Redis fixtures with known TTL distributions, including no-TTL keys and keys expiring inside each forecast bucket.
- Slowlog fixture data that covers repeated commands on one key, one prefix, and unrelated commands.
- Pub/Sub fixture data with zero channels, a few channels, and multiple channels sharing a prefix.
- Capability fixtures where `SLOWLOG`, `OBJECT FREQ`, or Pub/Sub introspection are unsupported or permission-limited.
- Cancellation harnesses that switch cache entry, database, or scan filter while diagnostics requests are still running.

## Manual checks

- Check: TTL posture drill-through - steps
- Load a Redis key set with mixed TTLs, run TTL analysis, and verify that selecting a bucket or forecast slice can still drill into relevant keys without losing the existing selected-key workflow.
- Check: Slowlog unsupported state - steps
- Use an environment where slowlog access is unavailable and verify the panel explains the limitation without surfacing a page-level error.
- Check: Pub/Sub visibility - steps
- Refresh the Pub/Sub panel, verify the active channel counts, and confirm that no subscribe or publish action is exposed.
- Check: Page density and navigation - steps
- Ensure the right-column diagnostics remain usable on the current Redis page layout and do not crowd out key detail inspection.

## Regression risks & mitigations

- Risk: diagnostics reuse existing scan state incorrectly and show stale results after cache changes. - Mitigation: explicit cancellation and session-version tests for every panel.
- Risk: loading more metadata causes regressions in current Redis scan responsiveness. - Mitigation: manual analyze buttons, bounded concurrency, and focused performance checks on loaded-key counts.
- Risk: unsupported server commands become visible as unhandled exceptions. - Mitigation: client and component tests for capability-based degradation.
- Risk: the page starts to imply full-cache health even when only a partial scan is loaded. - Mitigation: enforce coverage/confidence assertions in both unit and component tests.

## Acceptance criteria

- All high-priority scenarios pass in focused Core and App test suites.
- TTL, hot-key, and Pub/Sub surfaces stay explicit about loaded-scan coverage.
- Unsupported or permission-limited commands degrade to a visible informational state instead of crashing the page.
- No new diagnostics introduce background polling or block existing key-browse and key-detail workflows.
- Redis functionality docs and feature docs are updated together with implementation.

## Validation status

- Automated: Not started
- Manual: Not started

## Sign-off

- Approved by:
- Date:
- Conditions (if any):
