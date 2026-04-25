# Status - winui3-service-bus-parity

---

title: "Status - winui3-service-bus-parity"
owner: ""
state: "In Progress"
jira: "not linked"
branch: "winui-rewrite"
started: "2026-04-25"
last_updated: "2026-04-25"

---

## Quick summary

The native Service Bus workspace now covers the main parity baseline: scheduled-message manager tabs, a native compose dialog with template save/apply and scheduled send, confirmation-gated DLQ and scheduled destructive actions, and richer message-list controls (text filtering, saved filters, built-in field visibility, and load-more). Deferred follow-up still tracked under this feature is the hosted-only filter/export/purge/row-density/custom-column/full-template-management surface plus workspace-restore hardening and page-level WinUI coverage.

**Jira:** not linked

**Current focus:** keep the delivered native baseline green and documented clearly while the deferred hosted-only parity and restore-hardening follow-up remains visible.

## Progress checklist

- [x] MAUI versus WinUI gap captured
- [x] Scheduled message manager baseline implemented
- [x] Native compose/template/schedule baseline implemented
- [x] Confirmation-gated destructive Service Bus actions implemented
- [x] Native message-tab filter/search slice implemented
- [x] Built-in message-field visibility persists per namespace/entity/mode scope
- [x] Focused WinUI tests added for scheduled send, template persistence, filters, and preferences
- [ ] Hosted-only filter/export/purge/row-density/custom-column/template-management parity
- [ ] Workspace-restore hardening and page-level WinUI coverage
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
- Added focused `ServiceBusPageViewModelTests` coverage for template persistence, scheduled send/local history, filtering, and preference persistence.

## Remaining

- Add the remaining hosted-only multi-rule filters, filtered delete/export, purge, row-density, custom application-property columns, and fuller template-management behavior from the hosted Service Bus workspace.
- Harden workspace restore so the richer native tab state survives broader reopen/navigation scenarios.
- Add page-level WinUI validation for the compose dialog, confirmation wiring, and scheduled-workspace render path.

## Blockers

- None for this bounded slice.
- Broader parity work still benefits from the layout redesign and settings completeness dependencies landing first.

## Validation

- Test Plan: link to `test-plan.md`
- Validation status: Native parity baseline validated; page-level WinUI coverage still pending
- Automated checks: `build-winui`; `dotnet test .\tests\SwebKit.WinUI.Tests\SwebKit.WinUI.Tests.csproj --filter ServiceBusPageViewModelTests`

## Notes

- This feature remains a medium-priority parity slice, not the first cutover dependency.
- The current implementation intentionally stops short of the hosted advanced filter/export/purge/row-density/custom-column/template-management surface and does not attempt full workspace-restore hardening yet.
- The deferred hosted-only surface remains tracked in this folder as follow-up, not as part of the shipped native baseline.
