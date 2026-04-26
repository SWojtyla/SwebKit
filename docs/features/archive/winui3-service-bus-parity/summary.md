# Archive Summary - winui3-service-bus-parity

---

title: "Archive Summary - winui3-service-bus-parity"
owner: ""
jira: "not linked"
completed_date: "2026-04-26"
pr: "not linked"
commit: "not captured"

---

## Goal

Bring the highest-value remaining MAUI Service Bus operator workflows onto native WinUI so scheduled work, compose/template reuse, richer message-list control, and core destructive message operations no longer require the Blazor host.

## Delivered

- Added a native scheduled-message manager backed by the persisted scheduled-message repository.
- Added a native compose dialog with template save/apply, immediate send, and scheduled send flows.
- Added confirmation-gated DLQ resubmit or complete flows plus scheduled cancel and remove-local actions.
- Added native text filtering, saved filters, built-in message-field visibility preferences, and load-more support for the current namespace, entity, and mode scope.
- Added page-level WinUI coverage for compose-dialog presentation state, confirmation copy, and the scheduled-workspace shell.

## Key decisions

- Treat the scheduled/template/list-control/destructive-action baseline as the completed native parity slice instead of holding the feature open for deeper hosted-only workflow depth.
- Keep hosted-only multi-rule filters, filtered delete/export, purge, row-density, custom application-property columns, fuller template-management behavior, and broader reopen or restart restore hardening as explicit future follow-up.
- Preserve the current Service Bus page as a page-local consumer of the shared layout and settings baselines rather than reopening global layout or settings work here.

## Validation performed

- Build validation: `build-winui` remained part of the regression bar for the native Service Bus route.
- Automated tests: `dotnet test .\tests\SwebKit.WinUI.Tests\SwebKit.WinUI.Tests.csproj --filter ServiceBusPageViewModelTests|ServiceBusPagePresentationTests` passed on 2026-04-25.
- Automated coverage includes template persistence, scheduled send and local scheduled-history storage, text filtering, saved-filter persistence, built-in field preference persistence, compose-dialog presentation state, confirmation copy, and the scheduled-workspace XAML wiring.
- Manual review: the final cutover review can still exercise the shipped native baseline, but no remaining manual check blocks close-out of this parity slice.

## Lessons learned

- Service Bus parity becomes easier to close honestly when the feature is anchored to the operator workflows that actually block cutover instead of every hosted-only workflow the Blazor surface ever accumulated.
- Page-level presentation tests were necessary because dialog wiring and confirmation copy could drift even when the underlying view-model tests stayed green.

## Follow-up

- Hosted-only multi-rule filters, filtered delete/export, purge, row-density, custom application-property columns, and fuller template-management behavior if native demand justifies them later — owner: future Service Bus follow-up
- Broader reopen or restart workspace-restore hardening for richer native tab state if later evidence shows it is needed — owner: future Service Bus follow-up
- Final end-to-end validation of the shipped native baseline alongside the wider WinUI cutover review — owner: `winui3-cutover-audit-hardening`

## Archive note

> This file is present because the feature had no Jira ticket. Archive location: `docs/features/archive/winui3-service-bus-parity/`.