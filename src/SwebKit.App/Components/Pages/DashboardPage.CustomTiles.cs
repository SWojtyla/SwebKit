using SwebKit.App.Components.Shared;
using SwebKit.Core.Abstractions;
using SwebKit.Core.Configuration;
using SwebKit.Core.Domain;
using SwebKit.Core.Services;

namespace SwebKit.App.Components.Pages;

/// <remarks>Service Bus entity and AKS namespace watch tile state, metrics, and add/edit actions.</remarks>
public partial class DashboardPage
{
    private sealed record DashboardServiceBusNamespaceOption(string Id, string Label, string FullyQualifiedNamespace);
    private sealed record DashboardServiceBusEntityMetric(
        long ActiveMessages,
        long DeadLetterMessages,
        long ScheduledMessages,
        DateTimeOffset LastUpdated,
        string? Error = null);
    private sealed record DashboardAksNamespaceMetric(
        int PodCount,
        int UnhealthyPods,
        int RestartCount,
        DateTimeOffset LastUpdated,
        string? Error = null);

    private static readonly Guid DemoOrdersNamespaceId = new("00000000-0000-0000-0000-000000000001");
    private static readonly Guid DemoPaymentsNamespaceId = new("00000000-0000-0000-0000-000000000002");

    private string _newServiceBusNamespaceId = string.Empty;
    private string _newServiceBusEntityPath = string.Empty;
    private string _newServiceBusTileTitle = string.Empty;
    private string _newServiceBusTileSize = "2x1";
    private string _newAksContext = string.Empty;
    private string _newAksNamespace = string.Empty;
    private string _newAksTileTitle = string.Empty;
    private string _newAksTileSize = "2x1";
    private string _editAksContext = string.Empty;
    private string _editAksNamespace = string.Empty;
    private string _editAksTitle = string.Empty;
    private string _editServiceBusNamespaceId = string.Empty;
    private string _editServiceBusEntityPath = string.Empty;
    private string _editServiceBusTitle = string.Empty;
    private readonly Lock _customTileMetricsLock = new();
    private readonly HashSet<string> _serviceBusEntityTilesLoading = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _aksNamespaceTilesLoading = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, DashboardServiceBusEntityMetric> _serviceBusEntityMetrics =
new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, DashboardAksNamespaceMetric> _aksNamespaceMetrics =
new(StringComparer.OrdinalIgnoreCase);

    private async Task AddServiceBusEntityTileAsync()
    {
        if (!_dashboardPreferencesReady)
        {
            return;
        }

        var namespaceOptions = GetServiceBusNamespaceOptions();
        var namespaceOption = namespaceOptions
            .FirstOrDefault(option => string.Equals(option.Id, _newServiceBusNamespaceId, StringComparison.OrdinalIgnoreCase))
            ?? (namespaceOptions.Count > 0 ? namespaceOptions[0] : null);
        var entityPath = _newServiceBusEntityPath.Trim();
        if (namespaceOption is null || string.IsNullOrWhiteSpace(entityPath))
        {
            _customizerMessage = "Choose a Service Bus namespace and entity path.";
            return;
        }

        var title = string.IsNullOrWhiteSpace(_newServiceBusTileTitle)
            ? entityPath.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).LastOrDefault() ??
entityPath
            : _newServiceBusTileTitle.Trim();

        var tile = new DashboardTilePreference
        {
            TileId = DashboardTileRegistry.CreateInstanceId(DashboardTileRegistry.ServiceBusEntityWatch),
            IsVisible = true,
            Size = NormalizeWidgetSize(_newServiceBusTileSize),
            Settings = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["title"] = title,
                ["namespaceId"] = namespaceOption.Id,
                ["namespaceLabel"] = namespaceOption.Label,
                ["entityPath"] = entityPath
            }
        };

        await AddCustomTileAsync(tile);
        _newServiceBusEntityPath = string.Empty;
        _newServiceBusTileTitle = string.Empty;
        _customizerMessage = $"Added {title}.";
    }

    private async Task AddAksNamespaceTileAsync()
    {
        if (!_dashboardPreferencesReady)
        {
            return;
        }

        var @namespace = _newAksNamespace.Trim();
        if (string.IsNullOrWhiteSpace(@namespace))
        {
            _customizerMessage = "Enter an AKS namespace.";
            return;
        }

        var title = string.IsNullOrWhiteSpace(_newAksTileTitle) ? @namespace : _newAksTileTitle.Trim();
        var context = _newAksContext.Trim();
        var settings = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["title"] = title,
            ["namespace"] = @namespace
        };

        if (!string.IsNullOrWhiteSpace(context))
        {
            settings["context"] = context;
        }

        var tile = new DashboardTilePreference
        {
            TileId = DashboardTileRegistry.CreateInstanceId(DashboardTileRegistry.AksNamespaceWatch),
            IsVisible = true,
            Size = NormalizeWidgetSize(_newAksTileSize),
            Settings = settings
        };

        await AddCustomTileAsync(tile);
        _newAksContext = AppState.Config.AksConfig?.KubeconfigContext ?? string.Empty;
        _newAksNamespace = GetConfiguredAksNamespace();
        _newAksTileTitle = string.Empty;
        _customizerMessage = $"Added {title}.";
    }

    private async Task AddCustomTileAsync(DashboardTilePreference tile)
    {
        var tiles = GetActiveTilePreferences().ToList();
        var insertIndex = tiles.FindIndex(preference => DashboardTileRegistry.GetTemplateId(preference.TileId) is
DashboardTileRegistry.RecentResources or DashboardTileRegistry.Favorites);
        if (insertIndex < 0)
        {
            tiles.Add(tile);
        }
        else
        {
            tiles.Insert(insertIndex, tile);
        }

        await UpdateActiveDashboardViewAsync(view => view with { Tiles = tiles });
        RefreshAll();
    }

    private void SetNewServiceBusNamespaceId(string? value) => _newServiceBusNamespaceId = value ?? string.Empty;

    private void SetEditServiceBusNamespaceId(string? value) => _editServiceBusNamespaceId = value ?? string.Empty;

    private async Task RefreshServiceBusEntityWatchAsync(DashboardTilePreference preference, CancellationToken ct)
    {
        var tileId = preference.TileId;
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(HealthRefreshBudget);

        if (!ct.IsCancellationRequested)
        {
            await InvokeAsync(() =>
            {
                SetServiceBusEntityTileLoading(tileId, true);
                RequestTileRender();
            });
        }

        try
        {
            var metric = await Task.Run(async () =>
            {
                var namespaceId = GetSetting(preference, "namespaceId");
                var entityPath = GetSetting(preference, "entityPath");
                if (string.IsNullOrWhiteSpace(namespaceId) || string.IsNullOrWhiteSpace(entityPath))
                {
                    return new DashboardServiceBusEntityMetric(0, 0, 0, DateTimeOffset.Now, "Tile target is incomplete.");
                }

                var client = CreateServiceBusClient(namespaceId);
                if (client is null)
                {
                    return new DashboardServiceBusEntityMetric(0, 0, 0, DateTimeOffset.Now, "Namespace is not available.");
                }

                try
                {
                    var stats = await client.GetEntityStatsAsync(entityPath, timeoutCts.Token);
                    return new DashboardServiceBusEntityMetric(
                        stats.ActiveMessageCount,
                        stats.DeadLetterMessageCount,
                        stats.ScheduledMessageCount,
                        stats.UpdatedAt ?? DateTimeOffset.Now);
                }
                finally
                {
                    if (client is IAsyncDisposable disposable)
                    {
                        await disposable.DisposeAsync();
                    }
                }
            }, timeoutCts.Token);

            if (!ct.IsCancellationRequested)
            {
                await InvokeAsync(() => SetServiceBusEntityMetric(tileId, metric));
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
        }
        catch (OperationCanceledException)
        {
            if (!ct.IsCancellationRequested)
            {
                await InvokeAsync(() =>
                    SetServiceBusEntityMetric(tileId,
                        new DashboardServiceBusEntityMetric(0, 0, 0, DateTimeOffset.Now,
                            "Timed out while refreshing Service Bus entity tile.")));
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            if (!ct.IsCancellationRequested)
            {
                await InvokeAsync(() =>
                    SetServiceBusEntityMetric(tileId,
                        new DashboardServiceBusEntityMetric(0, 0, 0, DateTimeOffset.Now, ex.Message)));
            }
        }
        finally
        {
            if (!ct.IsCancellationRequested)
            {
                await InvokeAsync(() =>
                {
                    SetServiceBusEntityTileLoading(tileId, false);
                    RequestTileRender();
                });
            }
        }
    }

    private async Task RefreshAksNamespaceWatchAsync(DashboardTilePreference preference, CancellationToken ct)
    {
        var tileId = preference.TileId;
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(HealthRefreshBudget);

        if (!ct.IsCancellationRequested)
        {
            await InvokeAsync(() =>
            {
                SetAksNamespaceTileLoading(tileId, true);
                RequestTileRender();
            });
        }

        try
        {
            var metric = await Task.Run(async () =>
            {
                var @namespace = GetSetting(preference, "namespace", GetConfiguredAksNamespace());
                if (string.IsNullOrWhiteSpace(@namespace))
                {
                    return new DashboardAksNamespaceMetric(0, 0, 0, DateTimeOffset.Now, "Namespace is not configured.");
                }

                var context = GetSetting(preference, "context", AppState.Config.AksConfig?.KubeconfigContext ?? string.Empty);
                IAksClient client = AppState.UseDemoData
                    ? DemoAksClient
                    : AksClientFactory.Create(string.IsNullOrWhiteSpace(context) ? null : context,
                        AppState.Config.AksConfig!.KubeconfigPath);

                var pods = await client.GetPodsAsync(@namespace, null, timeoutCts.Token);
                var unhealthyPods = pods.Count(static pod => !pod.Ready || pod.Status is not ("Running" or "Succeeded" or "Completed"));
                var restartCount = pods.Sum(static pod => pod.RestartCount);

                return new DashboardAksNamespaceMetric(
                    pods.Count,
                    unhealthyPods,
                    restartCount,
                    DateTimeOffset.Now);
            }, timeoutCts.Token);

            if (!ct.IsCancellationRequested)
            {
                await InvokeAsync(() => SetAksNamespaceMetric(tileId, metric));
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
        }
        catch (OperationCanceledException)
        {
            if (!ct.IsCancellationRequested)
            {
                await InvokeAsync(() =>
                    SetAksNamespaceMetric(tileId,
                        new DashboardAksNamespaceMetric(0, 0, 0, DateTimeOffset.Now,
                            "Timed out while refreshing AKS namespace tile.")));
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            if (!ct.IsCancellationRequested)
            {
                await InvokeAsync(() =>
                    SetAksNamespaceMetric(tileId,
                        new DashboardAksNamespaceMetric(0, 0, 0, DateTimeOffset.Now, ex.Message)));
            }
        }
        finally
        {
            if (!ct.IsCancellationRequested)
            {
                await InvokeAsync(() =>
                {
                    SetAksNamespaceTileLoading(tileId, false);
                    RequestTileRender();
                });
            }
        }
    }

    private IServiceBusClient? CreateServiceBusClient(string namespaceId)
    {
        if (AppState.UseDemoData)
        {
            return Guid.TryParse(namespaceId, out var demoId) && demoId == DemoPaymentsNamespaceId
                ? DemoServiceBusClient.PaymentsDev()
                : DemoServiceBusClient.OrdersDev();
        }

        if (!Guid.TryParse(namespaceId, out var parsedNamespaceId))
        {
            return null;
        }

        var serviceBusNamespace = AppState.ServiceBusNamespaces.FirstOrDefault(ns => ns.Id == parsedNamespaceId);
        if (serviceBusNamespace is null)
        {
            return null;
        }

        var connectionString = CredentialStore.Get(serviceBusNamespace.CredentialKey);
        return string.IsNullOrWhiteSpace(connectionString) ? null : SbClientFactory.Create(connectionString);
    }

    private void SetServiceBusEntityMetric(string tileId, DashboardServiceBusEntityMetric metric)
    {
        lock (_customTileMetricsLock)
        {
            _serviceBusEntityMetrics[tileId] = metric;
        }
    }

    private void SetServiceBusEntityTileLoading(string tileId, bool isLoading)
    {
        lock (_customTileMetricsLock)
        {
            if (isLoading)
            {
                _serviceBusEntityTilesLoading.Add(tileId);
            }
            else
            {
                _serviceBusEntityTilesLoading.Remove(tileId);
            }
        }
    }

    private void SetAksNamespaceMetric(string tileId, DashboardAksNamespaceMetric metric)
    {
        lock (_customTileMetricsLock)
        {
            _aksNamespaceMetrics[tileId] = metric;
        }
    }

    private void SetAksNamespaceTileLoading(string tileId, bool isLoading)
    {
        lock (_customTileMetricsLock)
        {
            if (isLoading)
            {
                _aksNamespaceTilesLoading.Add(tileId);
            }
            else
            {
                _aksNamespaceTilesLoading.Remove(tileId);
            }
        }
    }

    private DashboardServiceBusEntityMetric? GetServiceBusEntityMetric(string tileId)
    {
        lock (_customTileMetricsLock)
        {
            return _serviceBusEntityMetrics.GetValueOrDefault(tileId);
        }
    }

    private bool IsServiceBusEntityTileLoading(string tileId)
    {
        lock (_customTileMetricsLock)
        {
            return _serviceBusEntityTilesLoading.Contains(tileId);
        }
    }

    private DashboardAksNamespaceMetric? GetAksNamespaceMetric(string tileId)
    {
        lock (_customTileMetricsLock)
        {
            return _aksNamespaceMetrics.GetValueOrDefault(tileId);
        }
    }

    private bool IsAksNamespaceTileLoading(string tileId)
    {
        lock (_customTileMetricsLock)
        {
            return _aksNamespaceTilesLoading.Contains(tileId);
        }
    }

    private Task OpenServiceBusEntityTileAsync(DashboardTilePreference? preference)
    {
        if (preference is null)
        {
            return Task.CompletedTask;
        }

        var namespaceId = GetSetting(preference, "namespaceId");
        var entityPath = GetSetting(preference, "entityPath");
        if (string.IsNullOrWhiteSpace(namespaceId) || string.IsNullOrWhiteSpace(entityPath))
        {
            return Task.CompletedTask;
        }

        var snapshot = new WorkspaceSnapshot
        {
            Resource = new OperatorResourceReference
            {
                Key = $"service-bus:{namespaceId}:{entityPath}",
                Area = "service-bus",
                Kind = "entity",
                DisplayName = entityPath.Split('/', StringSplitOptions.RemoveEmptyEntries |
StringSplitOptions.TrimEntries).LastOrDefault() ?? entityPath,
                DisplayPath = $"{GetSetting(preference, "namespaceLabel", "Service Bus")}/{entityPath}",
                Summary = GetSetting(preference, "namespaceLabel"),
                Icon = "⇄",
                Metadata = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["namespaceId"] = namespaceId,
                    ["entityPath"] = entityPath,
                },
            },
            RestoreState = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["namespaceId"] = namespaceId,
                ["entityPath"] = entityPath,
                ["mode"] = "active",
                ["tabType"] = "entity",
            },
        };

        return Workspaces.OpenSnapshotAsync(snapshot, recordRecent: true);
    }

    private Task OpenAksNamespaceTile(DashboardTilePreference? preference)
    {
        var @namespace = GetSetting(preference, "namespace", AppState.Config.AksConfig?.DefaultNamespace ?? "default");
        if (string.IsNullOrWhiteSpace(@namespace))
        {
            return Task.CompletedTask;
        }

        var context = GetSetting(preference, "context", AppState.Config.AksConfig?.KubeconfigContext ?? string.Empty);
        var displayPath = string.IsNullOrWhiteSpace(context) ? @namespace : $"{context}/{@namespace}";
        var snapshot = new WorkspaceSnapshot
        {
            Resource = new OperatorResourceReference
            {
                Key = $"aks:cluster:{context}:{@namespace}",
                Area = "aks",
                Kind = "cluster",
                DisplayName = string.IsNullOrWhiteSpace(context) ? @namespace : context,
                DisplayPath = displayPath,
                Summary = "Pods",
                Icon = "☁",
                Metadata = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["context"] = context,
                    ["namespace"] = @namespace,
                },
            },
            RestoreState = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["context"] = context,
                ["namespace"] = @namespace,
                ["resourceType"] = "Pods",
                ["filter"] = string.Empty,
                ["showEvents"] = "False",
                ["showPortForwardSessions"] = "False",
            },
        };

        return Workspaces.OpenSnapshotAsync(snapshot, recordRecent: true);
    }

    private IReadOnlyList<DashboardServiceBusNamespaceOption> GetServiceBusNamespaceOptions()
    {
        var options = AppState.ServiceBusNamespaces
            .Select(static ns => new DashboardServiceBusNamespaceOption(ns.Id.ToString("D"), ns.Alias, ns.FullyQualifiedNamespace))
            .ToList();

        if (AppState.UseDemoData)
        {
            AddDemoNamespaceOption(options, DemoOrdersNamespaceId, "orders-dev", "orders-dev.servicebus.windows.net");
            AddDemoNamespaceOption(options, DemoPaymentsNamespaceId, "payments-dev", "payments-dev.servicebus.windows.net");
        }

        return options;
    }

    private static void AddDemoNamespaceOption(
        List<DashboardServiceBusNamespaceOption> options,
        Guid id,
        string label,
        string fullyQualifiedNamespace)
    {
        if (options.Any(option => string.Equals(option.Id, id.ToString("D"), StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        options.Add(new DashboardServiceBusNamespaceOption(id.ToString("D"), label, fullyQualifiedNamespace));
    }

    private string GetAksTileTargetLabel(DashboardTilePreference? preference)
    {
        var @namespace = GetSetting(preference, "namespace", GetConfiguredAksNamespace());
        var context = GetSetting(preference, "context", AppState.Config.AksConfig?.KubeconfigContext ?? string.Empty);
        if (string.IsNullOrWhiteSpace(@namespace))
        {
            return string.IsNullOrWhiteSpace(context)
                ? "Namespace not configured"
                : $"{context} / namespace not configured";
        }

        return string.IsNullOrWhiteSpace(context) ? @namespace : $"{context} / {@namespace}";
    }

    private static string GetServiceBusEntityTileTargetLabel(DashboardTilePreference? preference)
    {
        var namespaceLabel = GetSetting(preference, "namespaceLabel", "Namespace not configured");
        var entityPath = GetSetting(preference, "entityPath", "entity path not configured");
        return $"{namespaceLabel} / {entityPath}";
    }

    private static IReadOnlyList<DashboardStatItem>? GetServiceBusEntityStats(DashboardServiceBusEntityMetric? metric)
    {
        if (metric is null || !string.IsNullOrWhiteSpace(metric.Error))
        {
            return null;
        }

        return
        [
            new(metric.ActiveMessages.ToString(), "active"),
            new(metric.DeadLetterMessages.ToString(), "dead-letter"),
            new(metric.ScheduledMessages.ToString(), "scheduled")
        ];
    }

    private IReadOnlyList<DashboardStatItem>? GetAksNamespaceStats(DashboardAksNamespaceMetric? metric)
    {
        if (metric is null)
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(metric.Error) && !IsDeploymentPermissionError(metric.Error))
        {
            return null;
        }

        return
        [
            new(metric.PodCount.ToString(), "pods"),
            new(metric.UnhealthyPods.ToString(), "unhealthy"),
            new(metric.RestartCount.ToString(), "restarts")
        ];
    }

    private string? GetAksNamespaceTileError(DashboardAksNamespaceMetric? metric) =>
        metric is null || string.IsNullOrWhiteSpace(metric.Error) || IsDeploymentPermissionError(metric.Error)
            ? null
            : metric.Error;

    private string? GetAksNamespaceTileNote(DashboardAksNamespaceMetric? metric) =>
        metric is not null && IsDeploymentPermissionError(metric.Error)
            ? "Ignoring deployment permissions; this tile uses pod data."
            : null;
}
