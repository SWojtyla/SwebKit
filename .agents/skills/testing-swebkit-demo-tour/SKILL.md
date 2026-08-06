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

| # | Step | Route | Demo data to assert |
|---|------|-------|----------------------|
| 1 | AI Cockpit | `/` | Health tiles show `Ready`, deployments > 0, service cards show `2 namespaces`, `1 cache`, `1 account` |
| 2 | Kubernetes | `/aks` | Deployment table with pods counts and `Available`/`Progressing`/`Unavailable` rows |
| 3 | Service Bus | `/service-bus` | Namespace dropdown contains `orders-dev` and `payments-dev` |
| 4 | Redis | `/redis` | Cache selector shows `Demo Cache` and key tree with ~17 keys |
| 5 | Storage | `/storage` | Container buttons `configs`, `exports`, `fixtures` |
| 6 | API Client | `/api-client` | Collection tree includes `Demo API Samples`, `JSONPlaceholder`, `HTTPBin`, `GitHub API` |
| 7 | AI Agent | `/agent` | Agent input visible, no crash (empty conversation) |
| 8 | Monitoring | `/monitoring` | `Alert Rules (0)` and `Alert History (0)` tabs visible |

## Key test assertions

- Starting the tour auto-enables demo mode: the top-right toggle text changes from `Live` to `Demo` / `Demo Mode ON` and dashboard metrics update within a few seconds.
- Each `Next` click advances the progress text (e.g. `1 / 8` to `2 / 8`) and navigates to the route above.
- `Back` returns to the previous step and route.
- `Stop` removes `demo-tour-card` immediately and leaves the current page rendered.

## Common gotchas

- The demo tour mutates the demo-mode flag through `useToggleDemoMode`, so do not pre-enable demo mode if you are testing the auto-enable behavior.
- Demo data on Service Bus/Storage/Agent/Monitoring is intentionally light; focus on navigation and the absence of error banners.
- If `localhost:1420` or `127.0.0.1:5199` are already in use, kill the existing processes (use `ss -ltnp` or `ps`) before restarting.
- To exercise the full tour in Playwright, the existing `e2e/dashboard.spec.ts` includes `starts demo tour and navigates to the next stop`; expand it for the full route loop.
