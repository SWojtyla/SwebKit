# Test Plan — company-readiness-polish

## Strategy

Manual visual review per screen after each implementation pass. No automated UI tests — this is a polish feature. Automated checks are inventory-only.

## Per-screen acceptance: general rules (apply to all screens)

1. **Light and dark theme** — open each screen in both themes, confirm no broken colours, clipping, or invisible text.
2. **Empty state** — disconnect or clear config for the relevant service. Confirm the empty state is shown and gives actionable guidance.
3. **Loading state** — trigger a slow load (demo mode or throttled). Confirm LoadingSpinner is shown and disappears when data arrives.
4. **Error state** — simulate a failure (bad credentials or network off). Confirm ErrorCallout is shown and Retry works.
5. **No console errors** — open DevTools (F12 in MAUI WebView) and confirm no JS errors or Blazor unhandled exceptions.

## Cross-cutting checks

- [ ] Run `scripts/style-inventory.ps1 -Top 20` — raw button count must not increase vs style-system-polish-9 final baseline (413 counted).
- [ ] Run `dotnet build` — zero errors, zero new warnings.
- [ ] Run `dotnet test` — all existing tests pass.

## Screen-specific acceptance criteria

See [screens.md](./screens.md) — each screen section ends with an "Acceptance criteria" block.

## Regression guard

The following areas are known-working and must not regress:

- Service Bus: peek, replay, DLQ drain, scheduled messages
- AKS: port-forward sessions, pod exec, events toggle
- Redis: key scan, value view, delete, multi-select batch
- Pipelines: pipeline list, approval action, release list
- API Client: request send/receive, environment switching, WebSocket connect
- Settings: save/reset round-trip for each service section
