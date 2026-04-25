# Status - winui3-storage-parity

---

title: "Status - winui3-storage-parity"
owner: ""
state: "Planned"
jira: "not linked"
branch: "winui-rewrite"
started: "2026-04-25"
last_updated: "2026-04-25"

---

## Quick summary

Storage already has a credible native baseline. The remaining work is batch, version, and preview-depth parity after the shared layout and settings dependencies land.

**Jira:** not linked

**Current focus:** define the remaining MAUI-only Storage workflows and the preview/bulk-state behaviors they require in WinUI.

## Progress checklist

- [x] MAUI versus WinUI Storage gap captured
- [ ] Batch and version workflows confirmed
- [ ] Large-file and binary-preview hardening scope confirmed
- [ ] Shared detail/state primitive adoption planned
- [ ] Focused validation approach defined
- [ ] Docs aligned after implementation begins

## Completed

- Confirmed that browse, detail, and SAS-copy baselines already exist natively.
- Isolated the remaining Storage gap as batch and preview-depth behavior rather than missing route coverage.

## Remaining

- Restore the MAUI batch and version workflows that still matter for cutover.
- Harden preview behavior for large or binary content.
- Align batch-state and failure messaging with the shared layout primitives.

## Blockers

- Layout redesign and settings completeness are intended to land first.

## Validation

- Test Plan: link to `test-plan.md`
- Validation status: Not started

## Notes

- Storage can run in parallel with Redis and Service Bus once the first shared features are complete.
