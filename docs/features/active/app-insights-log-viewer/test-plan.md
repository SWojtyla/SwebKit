# Test Plan — App Insights Log Viewer

---

title: "Test Plan - App Insights Log Viewer"
owner: ""
status: "Planned"
created: "2026-03-17"
updated: "2026-03-17"

---

## Scope

Unit tests for the provider query dispatch and auth-probe logic.
Manual checks for UI behaviour and end-to-end connectivity.

No E2E automation is planned for this deliverable (no real Azure resource available in CI).

---

## Unit tests — `SwebKit.Azure.Tests`

### UT-1 Resource query path dispatched when `AppInsightsResourceId` is set

**Given** config with `AppInsightsResourceId = "/subscriptions/..."` and no `WorkspaceId`  
**When** `QueryLogsAsync` is called  
**Then** `LogsQueryClient.QueryResourceAsync` is called; `QueryWorkspaceAsync` is not called.

### UT-2 Workspace path dispatched when only `WorkspaceId` is set

**Given** config with `WorkspaceId = "abc"` and no `AppInsightsResourceId`  
**When** `QueryLogsAsync` is called  
**Then** `LogsQueryClient.QueryWorkspaceAsync` is called.

### UT-3 `InvalidOperationException` when neither is configured

**Given** config with both `AppInsightsResourceId` and `WorkspaceId` null or empty  
**When** `QueryLogsAsync` is called  
**Then** `InvalidOperationException` is thrown with the expected message.

### UT-4 `TestConnectionAsync` returns `false` and sets `LastAuthError` on auth failure

**Given** `DefaultAzureCredential` that throws `AuthenticationFailedException`  
**When** `TestConnectionAsync` is called  
**Then** returns `false`, `LastAuthError` is non-null, `IsConnected` is false.

### UT-5 `TestConnectionAsync` returns `true` and sets `CredentialIdentity` on success

**Given** stubbed credential that returns a valid JWT token with `upn` claim  
**When** `TestConnectionAsync` is called  
**Then** returns `true`, `CredentialIdentity` equals the `upn` value, `IsConnected` is true.

### UT-6 Severity filter appended to KQL by `BuildKql`

**Given** `LogQuery` with `SeverityLevels = [2, 3]` (Warning, Error)  
**When** `BuildKql` is called  
**Then** returned KQL contains `where severityLevel in (2, 3)`.

### UT-9 `ListResourcesAsync` returns mapped results

**Given** a stubbed `ResourceGraphClient` that returns a `QueryResult` JSON with two rows  
**When** `AzureAppInsightsDiscoveryService.ListResourcesAsync()` is called  
**Then** returns two `AppInsightsResourceInfo` records with correct `Name`, `ResourceId`,
`SubscriptionId`, `ResourceGroup`, and `Location` fields.

### UT-10 `ListResourcesAsync` appends `nameFilter` to the KQL query

**Given** a stubbed `ResourceGraphClient` that captures the query string  
**When** `ListResourcesAsync(nameFilter: "my-app")` is called  
**Then** the captured KQL contains `where name contains 'my-app'`.

### UT-11 `ListResourcesAsync` returns empty list on `AuthenticationFailedException`

**Given** a `ResourceGraphClient` stub that throws `AuthenticationFailedException`  
**When** `ListResourcesAsync()` is called  
**Then** returns an empty list without throwing.

### UT-12 `ListResourcesAsync` escapes single quotes in `nameFilter`

**Given** `nameFilter = "it's-app"`  
**When** `ListResourcesAsync` is called  
**Then** the KQL contains `where name contains 'it''s-app'` (not `'it's-app'`).

---

## Unit tests — `SwebKit.App.Tests`

### UT-7 Auth status bar reflects `CredentialIdentity`

**Given** `ObservabilityPage` with a mock provider whose `CredentialIdentity = "dev@contoso.com"`  
**When** page renders after connection test  
**Then** the rendered markup contains `"dev@contoso.com"`.

### UT-8 Auth error callout shown when `LastAuthError` is set

**Given** mock provider with `IsConnected = false` and `LastAuthError = "No account found"`  
**When** page renders  
**Then** the rendered markup contains `"No account found"`.

---

## Manual checks

### MC-1 Real App Insights resource query

1. Configure an environment with a valid `AppInsightsResourceId`.
2. Be signed in with `az login` or Visual Studio with a user that has **Reader** on
   the resource.
3. Open Observability page, click "Connect / Test".
4. Expect: green badge with the signed-in UPN.
5. Run a query (Last 1 hour).
6. Expect: rows appear in the log grid matching entries visible in the Azure Portal.

### MC-2 Workspace-only path still works

1. Remove `AppInsightsResourceId`; set only `WorkspaceId`.
2. Query logs.
3. Expect: results returned via the workspace path.

### MC-3 No credential available

1. Sign out of all Azure credential sources (VS, az cli, environment vars).
2. Click "Connect / Test".
3. Expect: red error callout appears with a readable message. No crash.

### MC-4 Large result set virtualization

1. Query an App Insights resource with a 7-day time range and no filters.
2. Expect: grid scrolls without visible stutter; no browser tab freeze in the WebView.

### MC-5 Severity filter

1. Select only "Error" and "Critical" from the severity filter.
2. Run a query.
3. Expect: all rows in the result have `severityLevel >= 3`.

### MC-6 Config form — resource ID persists

1. Enter an `AppInsightsResourceId` in the config form and save.
2. Restart the app.
3. Expect: field is repopulated from the saved profile.

### MC-7 Resource discovery

1. Open the Observability config form.
2. Click the resource picker search box (or the Refresh button).
3. Expect: spinner appears briefly, then a list of App Insights resources is shown.
4. Expect: resources are grouped by subscription name.
5. Type a partial name into the search box.
6. Expect: list narrows to matching resources (within ~300 ms debounce).
7. Select a resource from the list.
8. Expect: the resource ID field below the picker is populated with the full ARM path.
9. Save the form.
10. Expect: subsequent log queries use that resource.

### MC-8 Discovery when no credential

1. Sign out of all Azure credential sources.
2. Open the config form and click Refresh.
3. Expect: inline warning text appears (_"Could not discover resources — check your login"_).
4. Expect: the resource ID text field is still editable for manual entry.

---

## Acceptance criteria

All unit tests pass.  
All manual checks pass with no regressions to existing Service Bus or other features.  
`docs/architecture/functionalities/observability.md` updated to reflect the new
query paths and auth behaviour.
