---
name: testing-swebkit-demo-tour
description: How to end-to-end test the SwebKit AI Cockpit guided demo tour and verify demo-mode data on every stop.
---

# Testing the SwebKit Demo Tour

## Devin Secrets Needed

None.

## One-time environment

- See `testing-swebkit` for .NET/Node setup and port/coordinate scaling notes.
- Use `nvm use 22` and `export PATH="$HOME/.dotnet:$PATH"` each session.

## Starting the app for the tour

1. Sidecar:
    ```bash
    cd /home/ubuntu/repos/SwebKit/src-sidecar
    dotnet run --project SwebKit.Sidecar.csproj --urls "http://127.0.0.1:5199"
    ```
2. Web app:
    ```bash
    cd /home/ubuntu/repos/SwebKit/web
    npm run dev
    ```
3. Open http://localhost:1420 in Chrome.

## Tour controls

- Launch button: `data-testid="demo-tour-start"` with text `Start demo tour`.
- Tour card: `data-testid="demo-tour-card"`.
- Step title: `data-testid="demo-tour-step-title"`.
- Step description: `data-testid="demo-tour-step-description"`.
- Progress text: `data-testid="demo-tour-progress"`.
- Next: `data-testid="demo-tour-next"` (last step reads `Finish`).
- Back: `data-testid="demo-tour-previous"` (disabled on step 1).
- Stop: `data-testid="demo-tour-stop"` (X icon).

## Expected tour route/data evidence

| #   | Step                            | Route          | Try-it hint           | Demo data to assert                                                        |
| --- | ------------------------------- | -------------- | --------------------- | -------------------------------------------------------------------------- |
| 1   | Welcome to the cockpit          | `/`            | —                     | Health tiles show `Ready`, deployments > 0, service cards populated        |
| 2   | Command palette                 | `/`            | Ctrl+K                | `command-palette-trigger` in top bar                                       |
| 3   | AKS: pick your clusters         | `/aks`         | attach 2nd cluster    | `aks-context-select` opens; demo contexts listed                           |
| 4   | Namespaces, grouped per cluster | `/aks`         | pick per-cluster ns   | `aks-namespace-select` groups by cluster when 2+ attached                  |
| 5   | Rows: click vs. right-click     | `/aks`         | —                     | Same AKS page — spotlight on the resource table convention                 |
| 6   | Logs, shell, port-forward       | `/aks`         | open pod logs         | Resource table rows present                                                |
| 7   | Service Bus explorer            | `/service-bus` | peek a queue          | Namespace dropdown contains `orders-dev` and `payments-dev`                |
| 8   | Filters that understand values  | `/service-bus` | date rule on TimeSent | Messages carry `NServiceBus.TimeSent` + `attempt` props                    |
| 9   | Dead-letter triage              | `/service-bus` | flip to DLQ           | `sb-view-selector` with `sb-view-dlq` option                               |
| 10  | Redis explorer                  | `/redis`       | —                     | Cache selector shows `Demo Cache`, key tree ~17 keys                       |
| 11  | Blob storage                    | `/storage`     | —                     | Container buttons `configs`, `exports`, `fixtures`                         |
| 12  | SQL workbench                   | `/sql`         | run saved query       | `sql-title` + schema tree on `demo-sql`                                    |
| 13  | API Client                      | `/api-client`  | send demo request     | Collections `Demo API Samples`, `JSONPlaceholder`, `HTTPBin`, `GitHub API` |
| 14  | AI Agent                        | `/agent`       | ask about health      | Agent input visible, no crash                                              |
| 15  | Monitoring & AI reports         | `/monitoring`  | open a report card    | Rules/history tabs; AI Reports kanban                                      |
| 16  | Make it yours                   | `/`            | —                     | Pins + deep links callout                                                  |

The "PRD day" walkthrough was removed (2026-10): `demo-sql-prd` and the Access report
still exist as a feature (Settings → Access, `access.spec.ts`), just not tour stops.
Steps with `tryIt` render a `demo-tour-try-it` hint block — suggestions only, Next is
never gated. `demo-tour.ts` `DemoTourStep` no longer carries `scenarioId`/`connection`.

## Key test assertions

- Starting the tour auto-enables demo mode: the top-right toggle text changes from `Live` to `Demo` / `Demo Mode ON` and dashboard metrics update within a few seconds.
- Each `Next` click advances the progress text (e.g. `1 / 16` to `2 / 16`) and navigates to the route above.
- `Back` returns to the previous step and route.
- `Stop` (or `Escape`) removes `demo-tour-card` immediately and leaves the current page rendered.

## Common gotchas

- The demo tour mutates the demo-mode flag through `useToggleDemoMode`, so do not pre-enable demo mode if you are testing the auto-enable behavior.
- Demo data on Service Bus/Storage/Agent/Monitoring is intentionally light; focus on navigation and the absence of error banners.
- If `localhost:1420` or `127.0.0.1:5199` are already in use, kill the existing processes (use `ss -ltnp` or `ps`) before restarting.
- To exercise the full tour in Playwright, the existing `e2e/dashboard.spec.ts` includes `starts demo tour and navigates to the next stop`; expand it for the full route loop.
