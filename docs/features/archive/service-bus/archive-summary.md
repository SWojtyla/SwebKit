# Archive Summary — Service Bus

Feature archived on 2026-03-08.

Concise summary:

# Archive Summary - Service-bus

---

title: "Archive Summary - Service-bus"
owner: ""
completed_date: "2026-03-08"
pr: ""
commit: ""

---

## Goal

Provide a practical, developer-focused Service Bus workspace for inspecting and remediating messages across namespaces (inspect queues/topics/subscriptions, DLQ remediation, compose/send messages, and export for analysis).

## Delivered

- Global namespace registry (add by connection string) with persisted namespace list.
- Entity tree covering queues, topics, and subscriptions with live counts.
- Message inspector (`MessageDetailPane`) with copy and export actions.
- DLQ remediation: multi-select, batch resubmit and delete flows with user confirmation.
- Message composer, message templates (save/load/delete), and send flows.
- UX polish: grid/layout fixes, keyboard navigation, resizable splitter, and copy feedback.

## Key decisions

- Store global namespaces in `ProfileData.ServiceBusNamespaces` and prefer a primary `AzureServiceBusClient(string connectionString)` constructor for simplicity.
- Require explicit production confirmation for all mutative operations and write minimal audit entries for traceability.
- Do not rely on server-side listing of scheduled messages; if scheduling is added, store scheduled-message metadata client-side.

## Validation performed

- Component/unit tests covering core UI components and domain models (see test projects under `tests/`).
- Manual smoke tests for namespace add, DLQ operations, composer send, and template lifecycle.
- Integration/e2e tests for long-running scenario orchestration remain outstanding and are tracked as follow-up.

## Lessons learned

- When a cloud service lacks an admin list API (scheduled messages), the client tooling must track metadata locally.
- Keep archive summaries concise — move detailed technical plans to version control history rather than the active archive.

## Follow-up

- Add integration/e2e tests for end-to-end send/peek/resubmit workflows (owner: TBD).
- Consider adding Scheduled Message Manager and Replay-to-Other-Namespace as separate enhancement features if needed.

## Archive metadata

- Archive location: `docs/features/archive/service-bus/archive-summary.md`
- Related active enhancement feature: `docs/features/active/service-bus-enhancements/`
- Detailed design and technical plans were intentionally removed from the archive to keep this summary concise; full history is available in the git log if deeper traceability is required.
