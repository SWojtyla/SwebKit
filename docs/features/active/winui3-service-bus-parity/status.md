# Status - winui3-service-bus-parity

---

title: "Status - winui3-service-bus-parity"
owner: ""
state: "Review"
jira: "not linked"
branch: "winui-rewrite"
started: "2026-04-25"
last_updated: "2026-04-26"

---

## Quick summary

The native Service Bus workspace now covers the full non-incident parity bar requested for the WinUI cutover: replay target selection, batch DLQ replay, advanced list tooling, and shell workspace snapshot parity are all implemented on top of the earlier batch-send and selected-message action lift. Incident investigation and trace pivots stay out of scope for this feature because that surface is slated for removal rather than migration.

**Jira:** not linked

**Current focus:** hold the feature in review while the repo-level WinUI build blocker outside Service Bus is resolved and the updated docs stay aligned with the delivered native behavior.

## Progress checklist

- [x] MAUI versus WinUI gap captured
- [x] Scheduled message manager baseline implemented
- [x] Native compose/template/schedule baseline implemented
- [x] Confirmation-gated destructive Service Bus actions implemented
- [x] Native message-tab filter/search slice implemented
- [x] Built-in message-field visibility persists per namespace/entity/mode scope
- [x] Native batch-send workflow implemented
- [x] Native selected-message quick actions implemented for edit, schedule, save-template, copy, and session filtering
- [x] Native replay target selection implemented for selected messages
- [x] Native batch DLQ replay implemented with remap support
- [x] Native advanced list tooling implemented for multi-rule filters, filtered delete, purge, JSON export, row density, and custom property columns
- [x] Service Bus now publishes and restores shell workspace snapshots for active tabs and tab sets
- [x] Focused WinUI tests added for scheduled send, template persistence, filters, and preferences
- [x] Page-level WinUI coverage added for compose dialog state, confirmation copy, and scheduled-workspace wiring
- [x] Docs aligned after implementation begins

## Completed

- Confirmed that Service Bus no longer blocks the native-route baseline.
- Isolated the remaining parity debt as workflow depth rather than missing route coverage.
- Added a namespace-level scheduled-message manager backed by the persisted scheduled-message repository.
- Added a native compose dialog that can save/apply templates and send immediately or schedule for later.
- Added confirmation-gated DLQ resubmit/complete flows and scheduled cancel/remove-local actions.
- Added native message-tab text filtering over the loaded message window.
- Added a saved-filter apply/delete path backed by `UiStateRepository` for the current namespace/entity scope.
- Added built-in message-field visibility preferences and load-more support for the current namespace/entity/mode tab scope.
- Added a native batch-send dialog backed by the existing `IServiceBusClient.SendBatchAsync` contract.
- Added selected-message quick actions in WinUI for edit-resubmit, schedule, save-as-template, clipboard copy, and filter-to-session.
- Added native replay target selection, property remap handling, and batch DLQ replay on top of the existing Service Bus contracts.
- Added advanced native list tooling for multi-rule filters, persisted advanced filter profiles, filtered delete, purge, JSON export, row density, and custom application-property columns.
- Added shell workspace snapshot publish or restore support so Service Bus favorites, saved workspaces, and route-first reopen flows can rehydrate tab state natively.
- Added focused `ServiceBusPageViewModelTests` coverage for template persistence, scheduled send/local history, filtering, and preference persistence.
- Added focused WinUI coverage for batch-send parsing and batching plus compose-draft seeding from an existing message.
- Added `ServiceBusPagePresentationTests` coverage for compose-dialog presentation state, confirmation copy, scheduled-workspace wiring, and the new list-tooling action surface.

## Remaining

- Incident investigation and trace-pivot actions remain outside this feature and are expected to disappear with incident-timeline removal rather than be ported into WinUI.
- Broader repo-level WinUI validation is blocked by unrelated compile work in the AKS surface.

## Close-out checklist

- [x] Close replay target selection parity.
- [x] Close batch DLQ replay parity.
- [x] Close advanced list-tooling parity.
- [x] Close Service Bus workspace favorite and restore parity.
- [ ] Clear unrelated WinUI project blockers and rerun broader build or test coverage if required before archive.

## Blockers

- Unrelated validation blocker: the WinUI project currently fails in `src/SwebKit.WinUI/ViewModels/Aks/AksPageViewModel.Resources.cs` because `GetEventsAsync` is being called with a named `cancellationToken` argument that the current overload does not accept. That blocks broader project-level `dotnet test` and `build-winui` validation for this feature.

## Validation

- Test Plan: link to `test-plan.md`
- Validation status: touched Service Bus source and focused test files are diagnostics-clean after the replay, list-tooling, and workspace parity lift. Broader WinUI project validation, including rerunning the focused `dotnet test` command, is currently blocked by the unrelated AKS compile error noted above.
- Automated checks: `get_errors` on the touched WinUI Service Bus files and focused test files passed; the broader WinUI project build and current `dotnet test` reruns are blocked by the unrelated AKS error.

## Notes

- The feature was reopened because the user bar was full MAUI-to-WinUI migration rather than the earlier baseline-only acceptance.
- The native cutover bar for non-incident Service Bus workflows is now met; the folder stays active only because broader project validation is still blocked outside this feature slice.
