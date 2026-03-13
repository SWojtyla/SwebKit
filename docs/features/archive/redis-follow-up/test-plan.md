# Test Plan - Redis Follow-up

---

title: "Test Plan - Redis Follow-up"
owner: ""
status: "Done"
created: "2026-03-12"
updated: "2026-03-13"

---

## Goal

Validate that Redis follow-up UX and model changes support multi-cache usage, namespace grouping, and prefix memory analysis without regressions in existing Redis operations.

## Scope

- In scope: multi-cache config and selection, namespace tree grouping with custom separator, prefix memory analysis, action/button label and flow updates, pattern examples/help UI.
- Out of scope: live performance benchmarking against production-scale Redis and cluster-specific topology features.

## Main scenarios (priority)

1. Multi-cache selection: User can configure multiple caches and switch active cache from dropdown. — Expected result: selected cache drives all operations and labels.
2. Cache naming: User-defined cache names are shown in toolbar/connection indicator. — Expected result: no static `Redis` label when name exists.
3. Namespace tree grouping: Keys are grouped by separator (default and custom). — Expected result: tree updates correctly when separator changes.
4. Prefix memory analysis: Per-prefix memory distribution is shown and sums align with sampled/known keys. — Expected result: values are coherent with scan coverage indicator.
5. Purge all wording and flow: `Purge All` replaces `Flush DB` in actions and confirmations. — Expected result: destructive guard still enforced.
6. Pattern examples/help: User sees examples (`user:*`, `session:*`, etc.) and can run scan successfully. — Expected result: examples are clear and improve discoverability.
7. Removed server info: Server info button/action is absent. — Expected result: no dead controls or inaccessible routes.

## Automated coverage

- Unit tests: namespace grouping parser, separator handling, memory aggregation logic, config migration logic.
- Component tests: cache dropdown behavior, pattern examples rendering, purge-all action text and confirmation wiring.
- Integration tests: Redis page service wiring for selected cache context.

## Test data and setup

- Extend demo keyspace with namespace-rich keys (e.g., `user:profile:1001`, `tenant|order|pending`).
- Include mixed separators and enough keys to verify grouping behavior.
- Include known memory-distribution fixtures for deterministic assertions.

## Manual checks

- Check: Configure 2+ caches, switch between them, verify key lists and connection labels update.
- Check: Change namespace separator and verify tree regrouping.
- Check: Trigger purge-all in non-prod and prod (typed confirmation) and verify wording/guards.
- Check: Use pattern example values and verify scan results.

## Regression risks & mitigations

- Risk: Existing single-cache configs fail after model change. — Mitigation: default migration path with compatibility tests.
- Risk: Namespace grouping slows rendering on large keyspaces. — Mitigation: incremental aggregation and virtualization.
- Risk: Memory analysis values mislead users when scan is partial. — Mitigation: display scan coverage and sampling note.

## Acceptance criteria

- All high-priority scenarios pass.
- Existing Redis tests remain green.
- New model remains backward compatible with existing stored config.
- Docs reflect final behavior and limits.

## Validation status

- Automated: Passing (190+ non-E2E tests, zero build warnings)
- Manual: Key flows verified during development

## Sign-off

- Owner:
- Date: 2026-03-13
