# Service Bus Enhancements

---

title: "Feature - Service Bus Enhancements"
owner: ""
status: "Proposed"
created: "2026-03-08"
updated: "2026-03-08"

---

## Summary

Bundle of productivity and reliability features for the Service Bus workspace: quick wins (pretty message rendering, saved filters, exports, keyboard shortcuts), Edit & Resubmit, Scheduled Message Manager, and Replay-to-Other-Namespace with remapping rules.

## Goals

- Improve developer productivity and message inspection ergonomics.
- Enable safe, auditable message mutation and replay workflows.
- Support scheduling workflows and cross-namespace replay scenarios.

## Success criteria / Metrics

- UI: formatted message rendering and saved filters used by developers.
- Flows: edit/resubmit and replay succeed end-to-end in test harness.
- Scheduling: scheduled messages appear in UI and can be cancelled.

## Status

- Link to `status.md` for live progress tracking

## Scope

- In scope: Message rendering, saved filters, export, keyboard shortcuts, edit/resubmit, scheduling manager, replay-to-other-namespace with remap rules.
- Out of scope: complex body transformation engines (only simple templating / passthrough initially).

## Dependencies

- `ProfileRepository` / `UiStateRepository` for persistence
- `IServiceBusClient` surface changes
- UI components under `src/SwebKit.App/Components/ServiceBus/`

## Related documents

- Backend plan: `backend.md`
- Frontend plan: `frontend.md`
- Tests: `test-plan.md`
- Decisions: `decisions.md`
- Status: `status.md`
