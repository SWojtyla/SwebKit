using SwebKit.Core.Configuration;
using SwebKit.Core.Domain;
using SwebKit.Sidecar.Services;

namespace SwebKit.Sidecar.Endpoints;

/// <summary>
/// Read-only support for the workspace topology "Map" settings tab. Nodes and relationships
/// themselves are NOT exposed here — they're plain fields on <see cref="SwebKit.Core.Domain.AppConfig.Topology"/>
/// and round-trip through the existing whole-profile <c>GET/PUT /api/config/profiles</c> endpoints,
/// the same way <c>RedisConfig</c>/<c>StorageAccounts</c> already do (see <see cref="ConfigEndpoints"/>).
/// The only thing that genuinely needs a dedicated endpoint is this candidate list, since it's
/// computed on demand from config that isn't all on <c>AppConfig</c> itself (Service Bus namespaces
/// live separately, per <see cref="ProfileRepository.ServiceBusNamespaces"/>) and from demo-mode
/// overlays — it has nothing to persist, so a plain CRUD resource would be the wrong shape for it.
/// </summary>
public static class WorkspaceTopologyEndpoints
{
    public static void MapWorkspaceTopologyEndpoints(this WebApplication app)
    {
        app.MapGet("/api/workspace/topology/candidates", GetCandidates);
        app.MapGet("/api/workspace/topology/suggestions", GetSuggestionsAsync);
    }

    /// <summary>Workspace-intelligence Module 2's heuristic relationship scan — see
    /// <see cref="WorkspaceRelationshipSuggestionService"/> for what it actually does and why it's
    /// explicitly a best-effort suggestion, never an auto-added fact.</summary>
    internal static async Task<IResult> GetSuggestionsAsync(WorkspaceRelationshipSuggestionService suggestions, CancellationToken ct)
    {
        var result = await suggestions.GetSuggestionsAsync(ct);
        return Results.Ok(result);
    }

    internal static IResult GetCandidates(ProfileRepository profile, DemoModeService demo)
    {
        var config = profile.Config;
        var candidates = new List<WorkspaceResourceCandidate>();

        candidates.AddRange(BuildAksCandidates(config.AksConfig));

        var namespaces = demo.IsDemoMode ? demo.GetDemoNamespaces() : profile.ServiceBusNamespaces;
        candidates.AddRange(namespaces.Select(ns => new WorkspaceResourceCandidate
        {
            Area = WorkspaceResourceArea.ServiceBus,
            ResourceKey = ns.FullyQualifiedNamespace,
            DisplayLabel = ns.Alias,
        }));

        candidates.AddRange(BuildRedisCandidates(config.RedisConfig, demo));
        candidates.AddRange(BuildStorageCandidates(config.StorageAccounts, demo));

        return Results.Ok(candidates);
    }

    private static IEnumerable<WorkspaceResourceCandidate> BuildAksCandidates(AksConfig? aks)
    {
        if (aks is null)
            yield break;

        var namespaces = aks.MonitoredNamespaces.Count > 0
            ? aks.MonitoredNamespaces
            : (string.IsNullOrWhiteSpace(aks.DefaultNamespace) ? [] : [aks.DefaultNamespace]);

        foreach (var ns in namespaces)
        {
            foreach (var deployment in aks.WatchedDeployments)
            {
                yield return new WorkspaceResourceCandidate
                {
                    Area = WorkspaceResourceArea.Aks,
                    ResourceKey = $"{ns}/{deployment}",
                    DisplayLabel = $"{deployment} ({ns})",
                };
            }
        }
    }

    private static List<RedisCacheEntry> ResolveRedisCaches(RedisConfig? redis, DemoModeService demo)
    {
        if (demo.IsDemoMode)
        {
            var demoCache = demo.GetDemoRedisCache(DemoModeService.DemoRedisCacheId);
            return demoCache is null ? [] : [demoCache];
        }

        return redis?.Caches ?? [];
    }

    private static IEnumerable<WorkspaceResourceCandidate> BuildRedisCandidates(RedisConfig? redis, DemoModeService demo) =>
        ResolveRedisCaches(redis, demo).Select(cache => new WorkspaceResourceCandidate
        {
            Area = WorkspaceResourceArea.Redis,
            ResourceKey = cache.Id,
            DisplayLabel = cache.DisplayName,
        });

    private static List<StorageConfig> ResolveStorageAccounts(List<StorageConfig> storageAccounts, DemoModeService demo)
    {
        if (demo.IsDemoMode)
        {
            var demoStorage = demo.GetDemoStorageConfig();
            return demoStorage is null ? [] : [demoStorage];
        }

        return storageAccounts;
    }

    private static IEnumerable<WorkspaceResourceCandidate> BuildStorageCandidates(List<StorageConfig> storageAccounts, DemoModeService demo) =>
        ResolveStorageAccounts(storageAccounts, demo).Select(sa => new WorkspaceResourceCandidate
        {
            Area = WorkspaceResourceArea.Storage,
            ResourceKey = sa.AccountName,
            DisplayLabel = sa.DisplayName,
        });
}
