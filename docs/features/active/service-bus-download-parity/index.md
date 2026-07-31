# Service Bus Download, Counts, and MAUI/Tauri Parity

**Status:** Planned  
**Target branch:** `main`  
**Branch:** `devin/service-bus-download-parity`

## Scope

Close the remaining MAUI/Tauri Service Bus gaps around downloading messages, showing accurate counts, and loading large message lists:

1. **Download messages** — single message JSON and bulk ZIP (selected or filtered) in both the MAUI/Blazor app and the Tauri/React app.
2. **Message counts** — the message-list status and the Active/DLQ tab counts must reflect the same totals as the global entity tree.
3. **Large lists** — Tauri/React must load additional messages as the user scrolls, using the Service Bus `fromSequenceNumber` peek support that MAUI already uses.
4. **Parity check** — document which MAUI/Blazor Service Bus features are present, missing, or different in the React rewrite.

## Outcomes

- A user can download the currently selected Service Bus message as a JSON file.
- A user can download a ZIP of the selected or filtered messages from the message list.
- The message-list footer shows `Showing X of Y` where `Y` is the broker-reported total for the current view (Active/DLQ).
- The Active/DLQ tab counts on the Service Bus page update after every send/complete/resubmit/purge and match the entity-tree counts.
- Tauri/React scrolls through very long queues and fetches the next page automatically when the user reaches the bottom.
- The parity table identifies any remaining behavioral differences for follow-up.

## Non-Goals

- New Service Bus resource kinds or new monitoring/dashboard integration.
- Redesign of the message composer, advanced filters, scheduled-messages UI, or batch-replay UI.
- Adding MAUI features that are not already in the React rewrite (e.g., Incident Timeline Investigate) — these are noted in the parity table instead.

## Verification

- `npm run build` (Vite/React)
- `dotnet build src-sidecar/SwebKit.Sidecar.csproj`
- `dotnet test tests/SwebKit.Sidecar.Tests`
- `npx playwright test` (with attention to `e2e/service-bus.spec.ts`)

See `technical-plan.md` for the ordered symbol-level implementation plan and `test-plan.md` for the test scenarios.
