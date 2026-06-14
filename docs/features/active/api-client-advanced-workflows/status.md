# Status — API Client Advanced Workflows

## Current State

`Planned`

## Quick Summary

Planning complete for advanced API Client workflows: trace correlation, visual response diff, no-code assertions, and chained request flows.

**Jira:** not linked

**Current focus:** Review scope and sequencing before implementation begins.

## Progress Checklist

### Planning

- [x] Scope captured
- [x] Architecture touchpoints identified
- [x] Backend module drafted
- [x] Frontend module drafted
- [x] Test plan drafted
- [x] Initial decisions captured

### Wave 1 — Trace Correlation

- [ ] Correlation config model
- [ ] Header/query/body injection strategy
- [ ] App Insights query handoff
- [ ] UI action from request/flow result to traces
- [ ] Focused tests and manual trace check

### Wave 2 — Visual Response Diff

- [ ] Response example/result diff model
- [ ] JSON/text/header/status/timing diff service
- [ ] Diff viewer UI
- [ ] Environment/run comparison workflow
- [ ] Focused tests with scrubbed examples

### Wave 3 — No-Code Assertions

- [ ] Assertion domain model
- [ ] Assertion evaluator service
- [ ] Assertion builder UI
- [ ] Request and flow result integration
- [ ] Focused tests for status/header/body/timing assertions

### Wave 4 — Request Flows

- [ ] Flow and step domain model
- [ ] Flow runner service
- [ ] Capture/assertion reuse between steps
- [ ] JSONPath helper/autocomplete affordance
- [ ] Flow editor and run result UI
- [ ] Cancellation and failure policy tests

## Completed

- Follow-up scope separated from completed API Client foundation.
- Existing API Client feature prepared for archive as historical foundation.
- Feedback cleanup completed for the current API Client surface: consolidated API repo controls, import/export controls, and variables controls into menus; removed active collection runner and request pinning; made Body the default REST request tab; fixed linked-repo request creation targeting; hardened splitter initialization during collection switches; cleaned response history styling.

## Remaining

- Review plan and confirm implementation order.
- Implement waves in small slices, starting with trace correlation or assertion model depending on priority.

## Blockers

_None._

## Validation

- Test Plan: `test-plan.md`
- Validation status: Advanced-workflow implementation not started. API Client feedback cleanup passed focused diagnostics, style inventory, and app build with local signing disabled on 2026-06-14.

## Notes

This is a follow-up feature. Do not revive `docs/features/archive/api-client/` as active requirements; use it only for historical decisions and implementation precedent.
