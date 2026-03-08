# Archive Summary - Service Bus

---

title: "Archive Summary - Service Bus"
owner: ""
completed_date: "2026-03-08"
pr: ""
commit: ""

---

## Goal

Provide a global Service Bus workspace for inspecting and operating on queues/topics/subscriptions, including DLQ remediation and message send workflows.

## Delivered

- Namespace panel and EntityTree
- Tabbed message inspector and DLQ view
- DLQ multi-select + batch resubmit/delete
- Message composer and template management
- Focused UI bug fixes and polish (2026-03-08)

## Key decisions

- Namespaces are global and stored in profiles; add-by-connection-string UX.
- Explicit Active vs DLQ selection in the entity tree.

## Validation performed

- Unit and component tests for key UI fixes
- Manual smoke validations for DLQ flows (ongoing)

## Lessons learned

- Surface counts clearly when total vs rendered windows differ.
- Guard all mutative flows with production confirmation.

## Follow-up

- Implement scenario editor and runner
- Persist per-entity filter state

## Archive metadata

- Archive location: `docs/features/archive/service-bus/original-2026-03-08/`
- Migrated to: `docs/features/active/service-bus/`
