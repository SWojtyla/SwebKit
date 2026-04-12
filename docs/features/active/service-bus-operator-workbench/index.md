# Feature Overview - service-bus-operator-workbench

---

title: "Feature Overview - service-bus-operator-workbench"
owner: "GitHub Copilot"
status: "Planned"
jira: "not linked"
created: "2026-04-12"
updated: "2026-04-12"

---

## Goal

Deepen the `/service-bus` page into a stronger operator workbench for triage and safe bulk actions by surfacing richer DLQ and session context, improving message-trace pivots, and adding preview-first batch send or replay workflows.

## Value

The current Service Bus page already covers namespace connection, entity browse, filtering, compose, export, scheduled messages, and DLQ replay or completion. What it lacks is deeper investigation context where operators spend the most time: why a message is dead-lettered, when it will expire, how a session is behaving, what partition or correlation key ties multiple messages together, and how to perform bounded replay or send actions over a larger selected set.

This feature keeps the existing page model and production safety posture. It improves operator clarity and throughput without turning the desktop app into a long-running message processor.

## Scope

- Wave 1 - richer triage context.
- Surface `DeadLetterReason`, `DeadLetterErrorDescription`, expiry, TTL-derived cues, `SessionId`, and `PartitionKey` consistently in message lists and detail panes.
- Add session and partition visibility so sessionized workloads can be investigated without leaving the page.
- Add message-trace pivots from explicit identifiers such as `MessageId`, `CorrelationId`, `SessionId`, and known application-property keys.
- Provide handoff actions into `/incident-timeline` or `/observability` when the trace pivot has a clear bounded context.
- Wave 2 - preview-first batch operations.
- Add batch send from templates or imported JSON payload sets.
- Add batch replay over selected or filtered messages with explicit preview, remap rules, target entity, and production confirmation.
- Extend execution summaries so operators can see what succeeded, failed, or was skipped.
- Wave 3 - operator polish and performance hardening.
- Improve large-window triage behavior for high-volume queues and subscriptions.
- Add saved trace pivots or operator bookmarks if the batch and trace flows prove useful.
- Out of scope.
- Queue, topic, or subscription provisioning or deletion.
- Hidden background consumers, lock renewers, or automatic retries.
- Automatic replay or completion based on inferred patterns.
- Message-schema-specific editors beyond the existing generic composer and remap surfaces.

## Dependencies

- Existing Service Bus feature base: `docs/architecture/functionalities/service-bus.md`.
- Existing routes and pages: `/service-bus`, `/settings`, and downstream optional handoff to `/incident-timeline`.
- Existing contracts and models: `IServiceBusClient`, `ServiceBusModels`, `RemapRules`, `ScheduledMessageEntry`, and `DeadLetterSequenceProcessor`.
- Cross-feature alignment: `incident-investigation-workflows` and `incident-timeline-workbench` should consume clearer trace pivots and richer message evidence, but this feature should still deliver standalone value.
- Relevant pitfalls: `docs/pitfalls/azure-sdk.md`, `docs/pitfalls/blazor-maui.md`, `docs/pitfalls/dotnet-csharp.md`, `docs/pitfalls/agent-workflow.md`.

## Risks & mitigations

- Risk: batch replay or send workflows increase destructive potential in production. Mitigation: preview-first UX, environment-aware confirmations, explicit target summaries, and clear execution outcomes.
- Risk: session inspection can become slow or misleading if implemented as exhaustive background reads. Mitigation: on-demand session queries only, bounded windows, and visible limitations.
- Risk: message trace pivots could overjoin unrelated messages. Mitigation: trace relationships must use explicit identifiers only and describe why the pivot exists.
- Risk: scoped connection strings or limited claims may hide data unexpectedly. Mitigation: reuse current `AzureServiceBusClient` connection semantics and surface degraded capability clearly.

## Related documents

- Architecture map: `docs/architecture/architecture.md`
- Component design: `docs/architecture/design.md`
- Code navigation: `docs/architecture/codebase-guide.md`
- Service Bus functionality: `docs/architecture/functionalities/service-bus.md`
- Incident Timeline functionality: `docs/architecture/functionalities/incident-timeline.md`
- Settings functionality: `docs/architecture/functionalities/settings-and-configuration.md`

## Quick links

- Jira: not linked
- Status: `status.md`
- Tests: `test-plan.md`
- Implementation modules: `frontend.md`, `backend.md`, `decisions.md`
