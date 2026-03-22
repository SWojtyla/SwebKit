# Backend — Application Insights Viewer

## New Project: `SwebKit.Observability`

A new class library project in `src/SwebKit.Observability/` implements all Azure Monitor / App Insights communication. This keeps the Azure-specific SDK dependencies isolated, consistent with how `SwebKit.Azure`, `SwebKit.Kubernetes`, and `SwebKit.Redis` are structured.

```
src/SwebKit.Observability/
  AppInsightsClient.cs           # IAppInsightsClient implementation
  AppInsightsDiscoveryService.cs # Subscription + resource enumeration
  Models/
    AppInsightsQueryResult.cs
    AppInsightsResourceInfo.cs
    ExceptionGroup.cs
    OperationPerformance.cs
    AvailabilityResult.cs
  Extensions/
    ServiceCollectionExtensions.cs
```

---

## Core Interface: `IAppInsightsClient` (in `SwebKit.Core`)

```csharp
public interface IAppInsightsClient
{
    // Resource discovery
    IAsyncEnumerable<AppInsightsResourceInfo> ListResourcesAsync(CancellationToken ct);

    // Overview
    Task<OverviewMetrics> GetOverviewAsync(string resourceId, TimeRange range, CancellationToken ct);

    // Failures
    Task<IReadOnlyList<ExceptionGroup>> GetTopExceptionsAsync(string resourceId, TimeRange range, int top, CancellationToken ct);
    Task<IReadOnlyList<LogRow>> GetExceptionDetailsAsync(string resourceId, string exceptionType, TimeRange range, CancellationToken ct);

    // Performance
    Task<IReadOnlyList<OperationPerformance>> GetOperationPerformanceAsync(string resourceId, TimeRange range, CancellationToken ct);

    // Logs
    Task<LogQueryResult> RunKqlAsync(string resourceId, string kql, TimeRange range, CancellationToken ct);

    // Availability
    Task<IReadOnlyList<AvailabilityResult>> GetAvailabilityAsync(string resourceId, TimeRange range, CancellationToken ct);
}
```

---

## Models (in `SwebKit.Core`)

```csharp
public record AppInsightsResourceInfo(
    string ResourceId,
    string Name,
    string SubscriptionId,
    string SubscriptionName,
    string ResourceGroup,
    string Location,
    string InstrumentationKey
);

public record TimeRange(DateTimeOffset Start, DateTimeOffset End)
{
    public static TimeRange LastHour   => new(DateTimeOffset.UtcNow.AddHours(-1),  DateTimeOffset.UtcNow);
    public static TimeRange Last6Hours => new(DateTimeOffset.UtcNow.AddHours(-6),  DateTimeOffset.UtcNow);
    public static TimeRange Last24Hours=> new(DateTimeOffset.UtcNow.AddHours(-24), DateTimeOffset.UtcNow);
    public static TimeRange Last7Days  => new(DateTimeOffset.UtcNow.AddDays(-7),   DateTimeOffset.UtcNow);
    public static TimeRange Last30Days => new(DateTimeOffset.UtcNow.AddDays(-30),  DateTimeOffset.UtcNow);
}

public record OverviewMetrics(
    long RequestCount,
    double FailureRate,          // 0.0–1.0
    double P50ResponseTimeMs,
    double P95ResponseTimeMs,
    long ExceptionCount,
    double AvailabilityPct,
    IReadOnlyList<TimeSeriesPoint> RequestTrend,
    IReadOnlyList<TimeSeriesPoint> FailureTrend
);

public record ExceptionGroup(
    string ExceptionType,
    string ProblemId,
    long Count,
    DateTimeOffset LastSeen,
    string? SampleMessage
);

public record OperationPerformance(
    string OperationName,
    long RequestCount,
    double FailureRate,
    double P50Ms,
    double P95Ms,
    double P99Ms
);

public record LogRow(IReadOnlyDictionary<string, object?> Columns);

public record LogQueryResult(
    IReadOnlyList<string> ColumnNames,
    IReadOnlyList<LogRow> Rows,
    TimeSpan ExecutionTime,
    bool Truncated           // true if row limit was hit
);
```

---

## Resource Discovery

**Class:** `AppInsightsDiscoveryService` in `SwebKit.Observability`

Uses `Azure.ResourceManager`:
```csharp
var armClient = new ArmClient(new DefaultAzureCredential());
await foreach (var sub in armClient.GetSubscriptions().GetAllAsync(ct))
{
    await foreach (var ai in sub.GetApplicationInsightsComponentsAsync(ct))
    {
        yield return new AppInsightsResourceInfo(
            ai.Id!.ToString(),
            ai.Data.ApplicationId,
            sub.Id.SubscriptionId,
            sub.Data.DisplayName,
            ai.Id.ResourceGroupName!,
            ai.Data.Location.Name,
            ai.Data.InstrumentationKey
        );
    }
}
```

Discovery is triggered once on first load and can be refreshed manually. Results are cached in-memory for the session. A loading spinner with a count (`Scanning 3 / 12 subscriptions…`) is shown during enumeration.

---

## Query Implementation

All queries go through `LogsQueryClient` targeting the App Insights resource ID directly (no workspace redirect needed when using resource-scoped queries).

```csharp
var client = new LogsQueryClient(new DefaultAzureCredential());
var response = await client.QueryResourceAsync(
    new ResourceIdentifier(resourceId),
    kql,
    new QueryTimeRange(range.Start, range.End),
    cancellationToken: ct
);
```

**Pitfall:** `QueryResourceAsync` requires the resource ID to be the App Insights component resource ID (not a Log Analytics workspace ID). Passing the wrong ID returns an empty result without a clear error.

---

## Built-in KQL Presets

Stored as static readonly strings in `KqlPresets.cs`:

| Preset ID | Name | Description |
|---|---|---|
| `top-exceptions` | Top Exceptions | Top 20 exception types by count, last N hours |
| `failed-requests` | Failed Requests | HTTP 4xx/5xx requests grouped by operation |
| `slow-requests` | Slow Requests | Requests with P95 duration > 2 s |
| `dependency-failures` | Dependency Failures | Failed external calls by dependency name |
| `custom-events` | Custom Events | All custom events with their properties |
| `availability-timeline` | Availability Timeline | Availability test results over time |
| `user-sessions` | Active Sessions | Unique session count over time |

Presets use KQL `let timeRange = ${range};` at the top so the selected time range is automatically substituted.

---

## Configuration Model Addition

Add to `AppConfig` in `SwebKit.Core`:

```csharp
public class AppInsightsConfig
{
    public string? LastSelectedResourceId { get; set; }
    public int MaxRowsPerQuery { get; set; } = 500;
    public List<SavedQuery> SavedQueries { get; set; } = [];
}

public class SavedQuery
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = "";
    public string Kql { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
```

---

## DI Registration

In `SwebKit.Observability/Extensions/ServiceCollectionExtensions.cs`:

```csharp
public static IServiceCollection AddObservability(this IServiceCollection services)
{
    services.AddSingleton<AppInsightsDiscoveryService>();
    services.AddSingleton<IAppInsightsClient, AzureAppInsightsClient>();
    return services;
}
```

Called from `MauiProgram.cs` alongside the other feature registrations.

---

## Tasks

- [ ] Create `SwebKit.Observability` csproj; add to solution
- [ ] Add NuGet: `Azure.Monitor.Query`, `Azure.ResourceManager.ApplicationInsights`, `Azure.Identity`
- [ ] Add `IAppInsightsClient` + all models to `SwebKit.Core`
- [ ] Implement `AppInsightsDiscoveryService`
- [ ] Implement `AzureAppInsightsClient` for all interface methods
- [ ] Add `AppInsightsConfig` + `SavedQuery` to `AppConfig`
- [ ] Add DI wiring in `MauiProgram.cs`
- [ ] Add `KqlPresets.cs` with preset library
- [ ] Write unit tests in `SwebKit.Observability.Tests`
