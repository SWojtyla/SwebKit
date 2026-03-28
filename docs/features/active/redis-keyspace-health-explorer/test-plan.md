# Test Plan - redis-keyspace-health-explorer

---

title: "Test Plan - redis-keyspace-health-explorer"
owner: ""
status: "Not started"
created: "2026-03-28"
updated: "2026-03-28"

---

## Goal

Validate that Redis Keyspace Health Explorer correctly detects and presents risky keys and prefixes (no TTL, heavy prefixes, oversized values, possible hot keys) with stable behavior under large scans and repeated refreshes.

## Scope

- In scope:
  - Risk scoring correctness for key-level and prefix-level findings.
  - UI behavior for loading, partial coverage, filtered findings, and key drill-through.
  - Cancellation and repeated scan stability in Redis page workflows.
  - Compatibility with both demo and real Redis clients where feasible.
- Out of scope:
  - Automatic remediation actions (not part of this feature scope).
  - Long-running production performance benchmarks outside test environments.

## Main scenarios (priority)

1. Scenario: Keys without TTL are detected and marked at expected severity.
   - Expected result: Health report includes no-TTL findings with stable ordering and counts.
2. Scenario: Oversized values cross configured thresholds.
   - Expected result: Findings severity matches threshold policy and displays memory bytes.
3. Scenario: Heavy prefixes dominate memory or key count.
   - Expected result: Prefix summary ranks heavy prefixes correctly and exposes percentage context.
4. Scenario: Possible hot keys with optional frequency/idle signals.
   - Expected result: Feature flags probable hot keys when signals are present; shows "unavailable" fallback when not supported.
5. Scenario: Partial scan coverage.
   - Expected result: UI displays coverage indicator and does not present report as complete keyspace truth.
6. Scenario: Re-scan while prior scan is in-flight.
   - Expected result: Previous operation cancels cleanly, no stale results overwrite latest run, UI remains responsive.
7. Scenario: Selecting finding opens key detail panel.
   - Expected result: Selected key syncs with existing detail pane and refreshes safely.
8. Scenario: Empty/no-risk state.
   - Expected result: UI shows clear neutral state, not error state.

## Automated coverage

- Unit tests: tests/SwebKit.Core.Tests
  - New: RedisKeyspaceHealthAnalyzerTests.cs
  - Target coverage: >= 85% on new analyzer and scoring code paths.
- Component tests: tests/SwebKit.App.Tests
  - New: RedisKeyspaceHealthExplorerTests.cs
  - Updates: RedisToolbarTests.cs and/or new RedisPage health wiring tests as needed.
- Integration tests: tests/SwebKit.Core.Tests
  - Extend RedisClient-focused tests for optional metadata retrieval and graceful fallback behavior.
  - If live Redis fixture is used, guard with environment flag and keep deterministic defaults.
- End-to-end tests: tests/SwebKit.E2E.Tests
  - Extend AppUiTests.cs with Redis navigation plus health panel smoke flow in demo mode.
  - Add at least one scenario for filter interaction + key drill-through.

## Test data and setup

- Use DemoRedisClient-backed seed data including:
  - keys with null TTL,
  - keys with large synthetic payloads,
  - high-density shared prefixes,
  - keys with and without optional hot-key signals.
- For Redis integration checks (optional), use a disposable Redis instance with fixed dataset.
- Ensure tests cover separator variations to validate prefix bucketing consistency.

## Manual checks

- Check: Redis page health panel states (loading/loaded/error/empty)
  - Steps: Navigate to Redis page, run health scan, toggle filters, verify badges and counts update.
- Check: Finding to detail drill-through
  - Steps: Click a finding row, confirm key detail panel loads target key and metadata.
- Check: Coverage messaging
  - Steps: Run scan with partial loaded keyset and confirm warning/confidence copy is visible.
- Check: Cancellation behavior
  - Steps: Trigger scan, change pattern quickly, start second scan; verify first run does not leak stale UI state.

## Regression risks & mitigations

- Risk: Increased render frequency freezes UI during large scans.
  - Mitigation: Batch updates and verify render throttling in component tests.
- Risk: Cancellation swallowed by broad exception handlers.
  - Mitigation: Add tests that assert OperationCanceledException propagation where expected.
- Risk: Prefix-heavy calculations regress existing memory analysis view.
  - Mitigation: Keep analyzer isolated and add dedicated unit tests for prefix aggregation invariants.

## Acceptance criteria

- All P1 scenarios pass.
- New analyzer/component/e2e tests pass in CI lanes used by this repo.
- No critical regressions in existing Redis connection and toolbar tests.
- Feature docs (status, decisions, test plan) are updated to reflect implementation state.

## Validation status

- Automated: Not started
- Manual: Not started

## Sign-off

- **Approved by:**
- **Date:**
- **Conditions (if any):**
