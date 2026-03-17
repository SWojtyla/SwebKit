# App Insights Log Viewer

---

title: "App Insights Log Viewer"
owner: ""
status: "Planned"
created: "2026-03-17"
updated: "2026-03-17"

---

## Goal

Surface Application Insights logs inside SwebKit. The user authenticates with their
Microsoft account via `DefaultAzureCredential` and queries logs directly from an
App Insights resource or its backing Log Analytics workspace.

## Value

Lets developers inspect App Insights traces, requests, dependencies, and exceptions
without leaving the tool. Authentication reuses the same Microsoft identity already
used for other Azure features — no separate API keys or connection strings needed.

## Scope

- Query logs from an App Insights resource using `DefaultAzureCredential`.
- Support both **direct resource** (`AppInsightsResourceId`) and **workspace** (`WorkspaceId`)
  query paths via the Azure Monitor Query SDK.
- **Resource discovery**: list all App Insights components accessible to the signed-in
  credential (across subscriptions) via Azure Resource Graph so the user can pick one
  from a searchable dropdown instead of pasting a raw resource ID.
- Display results in a log table with severity, timestamp, message, and operation ID columns.
- Detail pane for each log entry.
- Time range selector (last 1 h / 6 h / 24 h / 7 d / custom).
- Severity filter (Verbose / Information / Warning / Error / Critical).
- Text search filter applied client-side or via KQL where clause.
- Free-form KQL editor mode for advanced queries.
- Auth status indicator: shows which credential flow succeeded or an actionable error.

## Non-goals

- Metrics charts (part of the broader `observability` feature).
- Trace timeline / span hierarchy viewer (part of the broader `observability` feature).
- OTLP provider support.
- Saved query persistence (in scope for the broader `observability` feature).

## Dependencies

- `Azure.Monitor.Query` NuGet package — already present in `SwebKit.Azure`.
- `Azure.Identity` — already present; `DefaultAzureCredential` already wired in provider.
- `Azure.ResourceManager.ResourceGraph` — **new** NuGet package added to `SwebKit.Azure`.
  Used for the cross-subscription App Insights discovery query.
- `IObservabilityProvider` / `AppInsightsObservabilityProvider` — extend in place.
- `ObservabilityConfig` domain model — add `AppInsightsResourceId` field.
- New `IAppInsightsDiscoveryService` abstraction in `SwebKit.Core`.
- Existing `ObservabilityPage.razor` and `ObservabilityConfigForm.razor` — extend in place.

## Risks

- `DefaultAzureCredential` resolves at runtime; if no credential source is available, the
  query fails silently. Needs explicit error surfacing. See `backend.md`.
- Querying by `AppInsightsResourceId` returns results only if the caller has
  **Reader** or higher on the App Insights component. Workspace queries require
  **Log Analytics Reader** on the workspace. Surface both as distinct config paths.
- Resource Graph discovery returns only resources visible to the credential. Resources in
  subscriptions where the user has no RBAC role are silently excluded — this is the expected
  Azure behaviour; document it in the UI hint.
- Resource Graph has a default result cap of 1 000 rows per query. If a user has more than
  1 000 App Insights components (uncommon), paging is needed. Plan includes first-page-only
  for the initial deliverable with a count warning if the result is capped.

## Quick links

- Status: [status.md](status.md)
- Backend: [backend.md](backend.md)
- Frontend: [frontend.md](frontend.md)
- Tests: [test-plan.md](test-plan.md)
