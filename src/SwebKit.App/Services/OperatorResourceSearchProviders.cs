using SwebKit.Core.Domain;
using SwebKit.Core.Services;

namespace SwebKit.App.Services;

public sealed class ServiceBusResourceSearchProvider : IOperatorResourceSearchProvider
{
    private readonly AppStateService _appState;

    public ServiceBusResourceSearchProvider(AppStateService appState)
    {
        _appState = appState;
    }

    public IEnumerable<WorkspaceSnapshot> GetSnapshots()
    {
        foreach (var ns in _appState.ServiceBusNamespaces)
        {
            yield return new WorkspaceSnapshot
            {
                Resource = new OperatorResourceReference
                {
                    Key = $"service-bus:namespace:{ns.Id:N}",
                    Area = "service-bus",
                    Kind = "namespace",
                    DisplayName = ns.Alias,
                    DisplayPath = ns.Alias,
                    Summary = ns.FullyQualifiedNamespace,
                    Icon = "📨",
                    Metadata = new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["namespaceId"] = ns.Id.ToString("D"),
                        ["fullyQualifiedNamespace"] = ns.FullyQualifiedNamespace,
                    },
                },
                RestoreState = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["namespaceId"] = ns.Id.ToString("D"),
                    ["tabType"] = "namespace",
                },
            };
        }

        foreach (var link in _appState.Config.ServiceBusEntityLinks)
        {
            var ns = _appState.ServiceBusNamespaces.FirstOrDefault(candidate => candidate.Id == link.NamespaceId);
            var entityName = link.EntityPath.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .LastOrDefault();
            if (string.IsNullOrWhiteSpace(entityName))
            {
                continue;
            }

            var alias = ns?.Alias ?? link.Alias ?? "Service Bus";
            yield return new WorkspaceSnapshot
            {
                Resource = new OperatorResourceReference
                {
                    Key = $"service-bus:{link.NamespaceId:N}:{link.EntityPath}",
                    Area = "service-bus",
                    Kind = "entity",
                    DisplayName = entityName,
                    DisplayPath = $"{alias}/{link.EntityPath}",
                    Summary = alias,
                    Icon = "📨",
                    Metadata = new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["namespaceId"] = link.NamespaceId.ToString("D"),
                        ["entityPath"] = link.EntityPath,
                    },
                },
                RestoreState = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["namespaceId"] = link.NamespaceId.ToString("D"),
                    ["entityPath"] = link.EntityPath,
                    ["mode"] = "active",
                    ["tabType"] = "entity",
                },
            };
        }
    }
}

public sealed class AksResourceSearchProvider : IOperatorResourceSearchProvider
{
    private readonly AppStateService _appState;

    public AksResourceSearchProvider(AppStateService appState)
    {
        _appState = appState;
    }

    public IEnumerable<WorkspaceSnapshot> GetSnapshots()
    {
        var config = _appState.Config.AksConfig;
        if (config is null)
        {
            yield break;
        }

        var context = config.KubeconfigContext ?? string.Empty;
        var @namespace = string.IsNullOrWhiteSpace(config.DefaultNamespace) ? "default" : config.DefaultNamespace;

        if (!string.IsNullOrWhiteSpace(context))
        {
            yield return new WorkspaceSnapshot
            {
                Resource = new OperatorResourceReference
                {
                    Key = $"aks:cluster:{context}",
                    Area = "aks",
                    Kind = "cluster",
                    DisplayName = context,
                    DisplayPath = context,
                    Summary = @namespace,
                    Icon = "☸",
                },
                RestoreState = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["context"] = context,
                    ["namespace"] = @namespace,
                    ["resourceType"] = "Deployments",
                },
            };
        }

        foreach (var deployment in config.WatchedDeployments.Where(static value => !string.IsNullOrWhiteSpace(value)).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            yield return new WorkspaceSnapshot
            {
                Resource = new OperatorResourceReference
                {
                    Key = $"aks:deployment:{@namespace}:{deployment}",
                    Area = "aks",
                    Kind = "deployment",
                    DisplayName = deployment,
                    DisplayPath = $"{@namespace}/{deployment}",
                    Summary = context,
                    Icon = "☸",
                },
                RestoreState = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["context"] = context,
                    ["namespace"] = @namespace,
                    ["resourceType"] = "Deployments",
                    ["resourceName"] = deployment,
                },
            };
        }
    }
}

public sealed class StorageResourceSearchProvider : IOperatorResourceSearchProvider
{
    private readonly AppStateService _appState;

    public StorageResourceSearchProvider(AppStateService appState)
    {
        _appState = appState;
    }

    public IEnumerable<WorkspaceSnapshot> GetSnapshots()
    {
        foreach (var account in _appState.Config.StorageAccounts)
        {
            yield return new WorkspaceSnapshot
            {
                Resource = new OperatorResourceReference
                {
                    Key = $"storage:{account.Id}",
                    Area = "storage",
                    Kind = "account",
                    DisplayName = account.DisplayName,
                    DisplayPath = account.DisplayName,
                    Summary = account.AccountName,
                    Icon = "📁",
                },
                RestoreState = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["accountId"] = account.Id,
                },
            };
        }
    }
}

public sealed class RedisResourceSearchProvider : IOperatorResourceSearchProvider
{
    private readonly AppStateService _appState;

    public RedisResourceSearchProvider(AppStateService appState)
    {
        _appState = appState;
    }

    public IEnumerable<WorkspaceSnapshot> GetSnapshots()
    {
        var config = _appState.Config.RedisConfig;
        if (config is null)
        {
            yield break;
        }

        config.EnsureMigrated();
        foreach (var cache in config.Caches)
        {
            yield return new WorkspaceSnapshot
            {
                Resource = new OperatorResourceReference
                {
                    Key = $"redis:{cache.Id}",
                    Area = "redis",
                    Kind = "cache",
                    DisplayName = cache.DisplayName,
                    DisplayPath = cache.DisplayName,
                    Summary = $"DB {cache.Database}",
                    Icon = "⚡",
                },
                RestoreState = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["cacheId"] = cache.Id,
                },
            };
        }
    }
}

public sealed class ObservabilityResourceSearchProvider : IOperatorResourceSearchProvider
{
    private readonly AppStateService _appState;

    public ObservabilityResourceSearchProvider(AppStateService appState)
    {
        _appState = appState;
    }

    public IEnumerable<WorkspaceSnapshot> GetSnapshots()
    {
        var config = _appState.Config.ObservabilityConfig;
        if (string.IsNullOrWhiteSpace(config?.SelectedResourceId) || string.IsNullOrWhiteSpace(config.SelectedResourceName))
        {
            yield break;
        }

        yield return new WorkspaceSnapshot
        {
            Resource = new OperatorResourceReference
            {
                Key = $"observability:{config.SelectedResourceId}",
                Area = "observability",
                Kind = "resource",
                DisplayName = config.SelectedResourceName,
                DisplayPath = config.SelectedResourceName,
                Summary = "Last selected resource",
                Icon = "📈",
            },
            RestoreState = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["resourceId"] = config.SelectedResourceId,
                ["resourceName"] = config.SelectedResourceName,
                ["tab"] = "Overview",
            },
        };
    }
}

public sealed class IncidentTimelineSearchProvider : IOperatorResourceSearchProvider
{
    private readonly AppStateService _appState;

    public IncidentTimelineSearchProvider(AppStateService appState)
    {
        _appState = appState;
    }

    public IEnumerable<WorkspaceSnapshot> GetSnapshots()
    {
        var config = _appState.Config.IncidentTimeline;
        var context = _appState.Config.AksConfig?.KubeconfigContext ?? string.Empty;

        foreach (var mapping in config.WorkloadMappings)
        {
            if (string.IsNullOrWhiteSpace(mapping.Namespace) || string.IsNullOrWhiteSpace(mapping.WorkloadName))
            {
                continue;
            }

            var displayName = string.IsNullOrWhiteSpace(mapping.DisplayName)
                ? mapping.WorkloadName
                : mapping.DisplayName;

            yield return new WorkspaceSnapshot
            {
                Resource = new OperatorResourceReference
                {
                    Key = $"incident-timeline:{mapping.Namespace}:{mapping.WorkloadKind}:{mapping.WorkloadName}",
                    Area = "incident-timeline",
                    Kind = mapping.WorkloadKind.ToString().ToLowerInvariant(),
                    DisplayName = displayName,
                    DisplayPath = $"{mapping.Namespace}/{mapping.WorkloadName}",
                    Summary = mapping.WorkloadKind.ToString(),
                    Icon = "🕒",
                },
                RestoreState = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["context"] = context,
                    ["namespace"] = mapping.Namespace,
                    ["workloadKind"] = mapping.WorkloadKind.ToString(),
                    ["workloadName"] = mapping.WorkloadName,
                },
            };
        }
    }
}