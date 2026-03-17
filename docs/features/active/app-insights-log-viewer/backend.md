# Backend Plan — App Insights Log Viewer

---

title: "Backend Plan - App Insights Log Viewer"
owner: ""
status: "Planned"
created: "2026-03-17"
updated: "2026-03-17"

---

## Goal

Extend the existing `AppInsightsObservabilityProvider` to support direct App Insights
resource querying, surface authentication errors as structured diagnostics, and expose
the active credential identity in the provider state.

---

## Context

The existing `AppInsightsObservabilityProvider` already uses `DefaultAzureCredential`
and supports `QueryWorkspaceAsync` (Log Analytics workspace path). The new feature adds:

1. A **resource query path** via `QueryResourceAsync` keyed on an App Insights resource ID.
2. A **credential probe** on construction that resolves a token eagerly to surface auth
   failures before the first query.
3. A **`CredentialIdentity`** string on the provider (best-effort — uses token claims when
   available, falls back to `"DefaultAzureCredential"`) so the UI can show who is signed in.

---

## Impacted files

| File                                                                  | Change                                      |
| --------------------------------------------------------------------- | ------------------------------------------- |
| `src/SwebKit.Core/Domain/ObservabilityConfig.cs`                      | Add `AppInsightsResourceId`                 |
| `src/SwebKit.Core/Abstractions/IObservabilityProvider.cs`             | Add `CredentialIdentity` property           |
| `src/SwebKit.Azure/Observability/AppInsightsObservabilityProvider.cs` | Resource-query path + auth probe + identity |
| `src/SwebKit.App/Components/Pages/ObservabilityConfigForm.razor`      | New field for resource ID                   |

---

## Design

### 0 — Why Azure Resource Graph

The user may have multiple App Insights resources across multiple subscriptions. The options
were:

| Approach                                    | Packages needed                             | Calls made          | Cross-subscription? |
| ------------------------------------------- | ------------------------------------------- | ------------------- | ------------------- |
| Azure Resource Graph KQL                    | `Azure.ResourceManager.ResourceGraph` (new) | 1                   | Yes                 |
| ARM REST per subscription                   | none (HttpClient + Identity)                | 1 + N subscriptions | Yes, but slow       |
| `Azure.ResourceManager.ApplicationInsights` | 2 new packages                              | 1 + N subscriptions | Yes, but verbose    |

Resource Graph is the winner: one call, one package, cross-subscription, filterable.

Query used:

```kql
resources
| where type == 'microsoft.insights/components'
| project name, id, subscriptionId, resourceGroup, location
| order by name asc
| take 1000
```

The `take 1000` cap matches the SDK default page size. If the result is exactly 1 000,
a UI warning is shown: _"Results may be truncated — search to narrow the list."_

---

### 0a — New model: `AppInsightsResourceInfo`

```csharp
// src/SwebKit.Core/Domain/AppInsightsResourceInfo.cs
public sealed record AppInsightsResourceInfo(
    string Name,
    string ResourceId,         // full ARM resource ID
    string SubscriptionId,
    string ResourceGroup,
    string Location
);
```

Kept in `SwebKit.Core` so the config form (in `SwebKit.App`) can reference it without
taking a dependency on `SwebKit.Azure`.

---

### 0b — New interface: `IAppInsightsDiscoveryService`

```csharp
// src/SwebKit.Core/Abstractions/IAppInsightsDiscoveryService.cs
public interface IAppInsightsDiscoveryService
{
    /// <summary>
    /// Lists all App Insights components visible to the current credential.
    /// Returns an empty list (not throws) on auth failure or no resources found.
    /// </summary>
    Task<IReadOnlyList<AppInsightsResourceInfo>> ListResourcesAsync(
        string? nameFilter = null,
        CancellationToken ct = default);
}
```

`nameFilter` is applied as a server-side `| where name contains '...'` appended to the
Resource Graph query to reduce payload when the user is typing in the search box.

---

### 0c — Implementation: `AzureAppInsightsDiscoveryService`

```csharp
// src/SwebKit.Azure/Observability/AzureAppInsightsDiscoveryService.cs
public sealed class AzureAppInsightsDiscoveryService : IAppInsightsDiscoveryService
{
    private readonly ResourceGraphClient _graphClient;

    public AzureAppInsightsDiscoveryService()
    {
        var credential = new DefaultAzureCredential();
        _graphClient = new ResourceGraphClient(credential);
    }

    public async Task<IReadOnlyList<AppInsightsResourceInfo>> ListResourcesAsync(
        string? nameFilter = null, CancellationToken ct = default)
    {
        var kql = new System.Text.StringBuilder();
        kql.AppendLine("resources");
        kql.AppendLine("| where type == 'microsoft.insights/components'");
        if (!string.IsNullOrWhiteSpace(nameFilter))
            kql.AppendLine($"| where name contains '{nameFilter.Replace("'", "''")}'");
        kql.AppendLine("| project name, id, subscriptionId, resourceGroup, location");
        kql.AppendLine("| order by name asc");
        kql.AppendLine("| take 1000");

        var request = new QueryContent(kql.ToString());
        try
        {
            var response = await _graphClient.ResourcesAsync(request, ct);
            return ParseResults(response.Value);
        }
        catch (AuthenticationFailedException)
        {
            return [];
        }
    }

    private static IReadOnlyList<AppInsightsResourceInfo> ParseResults(ResourceQueryResult result)
    {
        // result.Data is a JsonElement with .rows array
        // Columns: name, id, subscriptionId, resourceGroup, location (in order)
        var list = new List<AppInsightsResourceInfo>();
        foreach (var row in result.Data.EnumerateArray())
        {
            list.Add(new AppInsightsResourceInfo(
                Name:           row[0].GetString()!,
                ResourceId:     row[1].GetString()!,
                SubscriptionId: row[2].GetString()!,
                ResourceGroup:  row[3].GetString()!,
                Location:       row[4].GetString()!));
        }
        return list;
    }
}
```

`ResourceGraphClient` comes from `Azure.ResourceManager.ResourceGraph`.
Package reference to add: `<PackageReference Include="Azure.ResourceManager.ResourceGraph" Version="1.1.0" />`

Note: the `nameFilter` injection uses parameterised KQL-safe escaping (`replace ' with ''`).
This is safe because the Resource Graph API is not a SQL injection surface — it's a
read-only, identity-scoped Azure control plane API restricted to resources the caller can
already see. KQL string injection here cannot escalate privilege beyond what the credential
already has access to.

---

### 0d — DI registration

Register in `MauiProgram.cs`:

```csharp
builder.Services.AddSingleton<IAppInsightsDiscoveryService, AzureAppInsightsDiscoveryService>();
```

The service is a singleton because `DefaultAzureCredential` and `ResourceGraphClient` are
both thread-safe and intended to be long-lived.

---

### 1 — Config model extension

```csharp
// ObservabilityConfig.cs
public string? AppInsightsResourceId { get; set; }
// Example value:
// /subscriptions/{sub}/resourceGroups/{rg}/providers/microsoft.insights/components/{name}
```

`AppInsightsResourceId` and `WorkspaceId` are alternative query targets.
Precedence rule: if `AppInsightsResourceId` is set, use `QueryResourceAsync`;
otherwise fall back to `QueryWorkspaceAsync` with `WorkspaceId`.

---

### 2 — Provider interface addition

```csharp
// IObservabilityProvider.cs
string? CredentialIdentity { get; }
```

Populated after the first successful token acquisition. Used by the UI auth status badge.

---

### 3 — Provider implementation

#### 3a Query dispatch

```csharp
// AppInsightsObservabilityProvider.cs
public async Task<IReadOnlyList<LogEntry>> QueryLogsAsync(LogQuery query, CancellationToken ct = default)
{
    var kql = query.RawKql ?? BuildKql(query);
    var range = ParseTimeRange(query.TimeRange);

    LogsQueryResult result;

    if (!string.IsNullOrEmpty(_config.AppInsightsResourceId))
    {
        result = (await _logsClient.QueryResourceAsync(
            new ResourceIdentifier(_config.AppInsightsResourceId),
            kql, range, cancellationToken: ct)).Value;
    }
    else if (!string.IsNullOrEmpty(_config.WorkspaceId))
    {
        result = (await _logsClient.QueryWorkspaceAsync(
            _config.WorkspaceId, kql, range, cancellationToken: ct)).Value;
    }
    else
    {
        throw new InvalidOperationException(
            "Observability is not configured: set AppInsightsResourceId or WorkspaceId.");
    }

    return MapLogRows(result);
}
```

Same dispatch pattern applies to `GetTraceAsync`.

#### 3b Auth probe + identity resolution

```csharp
public AppInsightsObservabilityProvider(ObservabilityConfig config, ICredentialStore _)
{
    _config = config;
    var credential = new DefaultAzureCredential();
    _logsClient = new LogsQueryClient(credential);
    _metricsClient = new MetricsQueryClient(credential);
    // Store the credential for the identity probe
    _credential = credential;
}

public override async Task<bool> TestConnectionAsync(CancellationToken ct = default)
{
    try
    {
        // Resolve a token first — surfaces credential failures before query
        var tokenCtx = new TokenRequestContext(["https://api.loganalytics.io/.default"]);
        var token = await _credential.GetTokenAsync(tokenCtx, ct);

        // Best-effort: parse upn or sub claim from JWT payload
        CredentialIdentity = TryParseIdentityFromToken(token.Token);

        // Then validate connectivity with a cheap query
        await QueryLogsAsync(new LogQuery { RawKql = "union * | take 0" }, ct);
        IsConnected = true;
        return true;
    }
    catch (AuthenticationFailedException ex)
    {
        IsConnected = false;
        LastAuthError = ex.Message;
        return false;
    }
    catch
    {
        IsConnected = false;
        return false;
    }
}
```

`TryParseIdentityFromToken` base64-decodes the JWT payload and extracts `upn` or `email`
claim; returns `null` on any parse failure.

#### 3c KQL builder — default App Insights query

The existing `BuildKql(LogQuery)` targets Log Analytics tables. When using the resource
path, the reachable table names are identical (`traces`, `requests`, `dependencies`,
`exceptions`, `customEvents`). No change needed to the KQL builder itself.

---

## Error handling

| Situation                        | Behaviour                                                                                            |
| -------------------------------- | ---------------------------------------------------------------------------------------------------- |
| No credential found              | `TestConnectionAsync` catches `AuthenticationFailedException`, sets `LastAuthError`, returns `false` |
| No resource/workspace configured | `QueryLogsAsync` throws `InvalidOperationException` immediately                                      |
| Query timeout                    | Propagated as `RequestFailedException`; caught by the page and shown in error callout                |
| Partial data (warning)           | `LogsQueryResult.Status == Partial`; log count returned with a UI warning                            |

---

## Tasks

- [ ] Add `Azure.ResourceManager.ResourceGraph` package to `SwebKit.Azure.csproj`.
- [ ] Create `AppInsightsResourceInfo` record.
  - File: `src/SwebKit.Core/Domain/AppInsightsResourceInfo.cs`
- [ ] Create `IAppInsightsDiscoveryService` interface.
  - File: `src/SwebKit.Core/Abstractions/IAppInsightsDiscoveryService.cs`
- [ ] Implement `AzureAppInsightsDiscoveryService` with Resource Graph query.
  - File: `src/SwebKit.Azure/Observability/AzureAppInsightsDiscoveryService.cs`
- [ ] Register `IAppInsightsDiscoveryService` in DI.
  - File: `src/SwebKit.App/MauiProgram.cs`
- [ ] Add `AppInsightsResourceId` to `ObservabilityConfig`.
  - File: `src/SwebKit.Core/Domain/ObservabilityConfig.cs`
- [ ] Add `CredentialIdentity` and `LastAuthError` to `IObservabilityProvider`.
  - File: `src/SwebKit.Core/Abstractions/IObservabilityProvider.cs`
- [ ] Implement resource-query dispatch in `QueryLogsAsync` and `GetTraceAsync`.
  - File: `src/SwebKit.Azure/Observability/AppInsightsObservabilityProvider.cs`
- [ ] Implement auth probe in `TestConnectionAsync` (token + identity extraction).
  - File: `src/SwebKit.Azure/Observability/AppInsightsObservabilityProvider.cs`
- [ ] Update `DemoObservabilityProvider` to satisfy new interface members.
  - File: `src/SwebKit.Core/Services/DemoObservabilityProvider.cs`

---

## Acceptance checks

- [ ] `ListResourcesAsync` returns all App Insights resources visible to the credential.
- [ ] `ListResourcesAsync` applies `nameFilter` as server-side KQL clause.
- [ ] `ListResourcesAsync` returns empty list (does not throw) when credential fails.
- [ ] `ListResourcesAsync` returns empty list when user has no resources.
- [ ] Provider constructs without throwing when `DefaultAzureCredential` has no sources.
- [ ] `TestConnectionAsync` returns `false` and populates `LastAuthError` when no auth.
- [ ] `QueryLogsAsync` dispatches to `QueryResourceAsync` when `AppInsightsResourceId` is set.
- [ ] `QueryLogsAsync` falls back to `QueryWorkspaceAsync` when only `WorkspaceId` is set.
- [ ] `CredentialIdentity` is non-null after a successful `TestConnectionAsync`.
- [ ] `InvalidOperationException` thrown when neither resource ID nor workspace ID is set.
