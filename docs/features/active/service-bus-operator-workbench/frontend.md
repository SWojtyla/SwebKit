# Frontend Plan - service-bus-operator-workbench

---

title: "Frontend Plan - service-bus-operator-workbench"
owner: "GitHub Copilot"
status: "Planned"

---

## Goal

Keep `/service-bus` as the single operator page, but make it much more effective for triage-heavy and batch-heavy workflows by surfacing richer message context, trace pivots, and preview-first bulk actions.

## Impacted areas

- Existing page and layout:
- `src/SwebKit.App/Components/Pages/ServiceBusPage.razor`
- `src/SwebKit.App/Components/Pages/ServiceBusPage.razor.css`
- Existing Service Bus components:
- `src/SwebKit.App/Components/ServiceBus/ServiceBusNamespacePanel.razor`
- `src/SwebKit.App/Components/ServiceBus/MessageListView.razor`
- `src/SwebKit.App/Components/ServiceBus/MessageDetailPane.razor`
- `src/SwebKit.App/Components/ServiceBus/DlqView.razor`
- `src/SwebKit.App/Components/ServiceBus/MessageComposer.razor`
- `src/SwebKit.App/Components/ServiceBus/ScheduledMessages.razor`
- `src/SwebKit.App/Components/ServiceBus/EntityTree.razor`
- `src/SwebKit.App/Components/ServiceBus/TemplatePicker.razor`
- Planned new UI components:
- `src/SwebKit.App/Components/ServiceBus/SessionExplorerPanel.razor`
- `src/SwebKit.App/Components/ServiceBus/MessageTracePanel.razor`
- `src/SwebKit.App/Components/ServiceBus/BatchReplayDialog.razor`
- `src/SwebKit.App/Components/ServiceBus/BatchSendDialog.razor`

## UX notes

- Triage depth.
- The message detail pane should promote existing `SbMessage` fields like `DeadLetterReason`, `DeadLetterErrorDescription`, `SessionId`, and `SystemProperties.PartitionKey` out of raw-property territory and into the default operator view.
- Expiry should be shown as both an absolute timestamp and an operator-friendly age or urgency cue when `ExpiresAt` is present.
- Session workflows.
- Session visibility should be explicit and on-demand. Operators need to know whether an entity is sessionized, what sessions are active, and how many messages are grouped under a session.
- Avoid a design that continuously polls sessions in the background.
- Trace workflows.
- Trace pivots should show which identifier is being used and which downstream surface is available: Incident Timeline, Observability logs, or a local filtered message view.
- Trace UI should prefer explanation text over a generic "correlated" badge.
- Batch workflows.
- Replay and send actions should always pass through a preview dialog that explains message count, target entity, remap rules, environment, and expected execution mode.
- Execution summaries should be per-item aware when partial failure occurs.
- Accessibility.
- New detail fields, trace actions, and batch dialogs must remain keyboard reachable.
- Confirmation copy must not rely on color alone to signal destructive actions.

## API / contract changes

- The UI can immediately use some existing model fields from `ServiceBusModels.cs` without waiting for new backend contracts.
- Additive UI contracts may still be needed for session summaries, trace references, and batch preview results.
- Keep existing message list and detail components reusable by projecting richer metadata rather than building parallel detail surfaces.
- Any downstream handoff should reuse app-layer navigation patterns instead of hard-coding route logic inside list rows.

## Tasks

### Wave 1 - richer triage surfaces [blazor-expert]

- [ ] Promote DLQ, expiry, session, and partition metadata into the default detail layout.
- [ ] Add explicit trace actions and a trace panel that explains each pivot key.
- [ ] Add session visibility without destabilizing the current entity and message layout.

### Wave 2 - preview-first batch dialogs [blazor-expert]

- [ ] Add replay preview and execution-summary UI.
- [ ] Add batch send import and validation UI.
- [ ] Reuse current environment-aware confirmation patterns for destructive actions.

### Wave 3 - layout and performance hardening [blazor-expert]

- [ ] Keep large list and panel interactions responsive.
- [ ] Add any saved trace pivot or bookmark UX only if the earlier waves justify the added surface area.

## Validation

- Component tests: Not started. Extend `ServiceBusPageTests`, `MessageListViewTests`, `ServiceBusNamespacePanelTests`, and add focused coverage for new session and batch dialogs.
- Manual UX checks:
- Verify DLQ metadata is visible without raw property inspection.
- Verify session inspection stays bounded and does not feel like a hidden listener.
- Verify trace pivots explain which identifier is in use.
- Verify replay and send previews are explicit before execution.

## Notes

- Follow `blazor-maui.md` rules for new Service Bus child components. If a new subfolder is introduced, `_Imports.razor` must be updated before the page composes the component.
- Keep the page grounded in the existing split-panel layout and selected-entity action bar instead of building a separate workbench route.
