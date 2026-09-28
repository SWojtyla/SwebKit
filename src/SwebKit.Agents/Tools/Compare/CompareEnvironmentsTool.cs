using System.Text.Json;
using SwebKit.Agents.Tools.Redis;
using SwebKit.Agents.Tools.Sql;
using SwebKit.Core.Abstractions;
using SwebKit.Core.Configuration;
using SwebKit.Core.Domain;
using SwebKit.Core.Models;
using SwebKit.Core.Security;
using SwebKit.Core.Serialization;
using SwebKit.Core.Services;

namespace SwebKit.Agents.Tools.Compare;

/// <summary>
/// <c>compare_environments</c> — agent-colleague item 7 (cross-environment compare). Two
/// workspace maps are the environments (the map <see cref="WorkspaceMap.Name"/> is the env tag);
/// nodes on different maps carrying the same <see cref="WorkspaceResourceNode.LogicalName"/> are
/// the same logical service.
///
/// Result shape: one <c>comparisons[]</c> entry per resource area present on either side, with a
/// first-class status per side (<c>ok</c>/<c>missing</c>/<c>access_denied</c>/<c>error</c>) — a
/// missing or denied environment is an explicit outcome, never a silent gap.
///
/// Deliberately NOT <see cref="IAccessAwareTool"/>: the interface itself warns a tool mixing
/// capabilities must not implement it, and this composite touches up to four connections per
/// call. <see cref="FetchSideAsync"/> replicates the registry's per-side behavior instead —
/// the known-denial short-circuit and the observed-denial feed run per (area, connection,
/// capability) so a denied Redis side can't be recorded against a Service Bus row.
/// </summary>
public sealed class CompareEnvironmentsTool : IAgentTool
{
    /// <summary>Cap on emitted diff rows per area — a schema drift can be hundreds of columns.</summary>
    private const int MaxDifferences = 100;

    private readonly ProfileRepository _profiles;
    private readonly AppStateService _appState;
    private readonly IAksClientFactory _aksFactory;
    private readonly DemoAksClient _demoAks;
    private readonly IServiceBusConnectionPool _sbPool;
    private readonly ISqlClientFactory _sqlFactory;
    private readonly IRedisClientFactory _redisFactory;
    private readonly IAccessReportService? _accessReport;

    public CompareEnvironmentsTool(
        ProfileRepository profiles,
        AppStateService appState,
        IAksClientFactory aksFactory,
        DemoAksClient demoAks,
        IServiceBusConnectionPool sbPool,
        ISqlClientFactory sqlFactory,
        IRedisClientFactory redisFactory,
        IAccessReportService? accessReport = null)
    {
        _profiles = profiles;
        _appState = appState;
        _aksFactory = aksFactory;
        _demoAks = demoAks;
        _sbPool = sbPool;
        _sqlFactory = sqlFactory;
        _redisFactory = redisFactory;
        _accessReport = accessReport;
    }

    public string Name => "compare_environments";

    public string Description =>
        "Compares one logical service across two environments (workspace maps). Nodes on " +
        "different maps sharing the same LogicalName — set in Settings → Map inspector — are the " +
        "same service; the map name is the env tag. Per area compares AKS deployment " +
        "image/replicas/status, Service Bus queue flags and message counts, SQL schema catalog, " +
        "and Redis server info. Each side reports its own status — a missing or access-denied " +
        "environment is an explicit result, not a failure.";

    public FeatureArea FeatureArea => FeatureArea.Workspace;

    public JsonElement ParametersSchema { get; } = AgentToolSchema.Parse("""
        {
          "type": "object",
          "properties": {
            "logical_name": {
              "type": "string",
              "description": "The shared LogicalName marking the same logical service on different workspace maps (Settings → Map inspector)."
            },
            "env_a": {
              "type": "string",
              "description": "First environment — a workspace map name (or id)."
            },
            "env_b": {
              "type": "string",
              "description": "Second environment — a workspace map name (or id)."
            }
          },
          "required": ["logical_name", "env_a", "env_b"]
        }
        """);

    public async Task<string> ExecuteAsync(JsonElement arguments, CancellationToken ct)
    {
        var logicalName = Arg(arguments, "logical_name");
        var envA = Arg(arguments, "env_a");
        var envB = Arg(arguments, "env_b");
        if (string.IsNullOrWhiteSpace(logicalName))
            return Error("'logical_name' is required.");
        if (string.IsNullOrWhiteSpace(envA))
            return Error("'env_a' is required.");
        if (string.IsNullOrWhiteSpace(envB))
            return Error("'env_b' is required.");

        var maps = CompareToolContext.GetMaps(_appState, _profiles);
        var mapA = CompareToolContext.ResolveMap(maps, envA);
        if (mapA is null)
            return Error($"No workspace map named '{envA}'.", MapHint(maps));
        var mapB = CompareToolContext.ResolveMap(maps, envB);
        if (mapB is null)
            return Error($"No workspace map named '{envB}'.", MapHint(maps));
        if (ReferenceEquals(mapA, mapB))
            return Error($"'env_a' and 'env_b' resolve to the same map ('{mapA.Name}') — environments are distinct maps.", MapHint(maps));

        var nodesA = CompareToolContext.FindLogicalNodes(mapA, logicalName);
        var nodesB = CompareToolContext.FindLogicalNodes(mapB, logicalName);
        if (nodesA.Count == 0 && nodesB.Count == 0)
        {
            return Error(
                $"No resource carries the logical name '{logicalName}' on either map. Set LogicalName in Settings → Map inspector.",
                new
                {
                    logical_names_in_env_a = CompareToolContext.LogicalNamesOn(mapA),
                    logical_names_in_env_b = CompareToolContext.LogicalNamesOn(mapB),
                });
        }

        var comparisons = new List<object>();
        var notes = new List<string>();
        foreach (var area in Enum.GetValues<WorkspaceResourceArea>())
        {
            // One node per (map, area) per logical name — extras get a note rather than silently
            // deciding which one represents the service.
            var (nodeA, extraA) = Pick(nodesA, area);
            var (nodeB, extraB) = Pick(nodesB, area);
            if (nodeA is null && nodeB is null)
                continue;
            if (extraA > 0)
                notes.Add($"Map '{mapA.Name}' has {extraA + 1} {area} nodes named '{logicalName}' — compared the first.");
            if (extraB > 0)
                notes.Add($"Map '{mapB.Name}' has {extraB + 1} {area} nodes named '{logicalName}' — compared the first.");

            var outcome = area switch
            {
                WorkspaceResourceArea.Aks => await CompareAksAsync(nodeA, nodeB, ct),
                WorkspaceResourceArea.ServiceBus => await CompareServiceBusAsync(nodeA, nodeB, ct),
                WorkspaceResourceArea.Sql => await CompareSqlAsync(nodeA, nodeB, ct),
                WorkspaceResourceArea.Redis => await CompareRedisAsync(nodeA, nodeB, ct),
                _ => new CompareOutcome(
                    UnsupportedSide(nodeA), UnsupportedSide(nodeB),
                    [], ["No comparator exists for this resource area yet."]),
            };

            var truncated = outcome.Differences.Count > MaxDifferences;
            comparisons.Add(new
            {
                area = AreaName(area),
                status = outcome.Status,
                env_a = SideJson(nodeA, outcome.A),
                env_b = SideJson(nodeB, outcome.B),
                differences = truncated ? outcome.Differences.Take(MaxDifferences) : outcome.Differences,
                differences_truncated = truncated,
                notes = outcome.Notes,
            });
        }

        return JsonSerializer.Serialize(new
        {
            logical_name = logicalName.Trim(),
            env_a = new { name = mapA.Name, map_id = mapA.Id },
            env_b = new { name = mapB.Name, map_id = mapB.Id },
            comparisons,
            notes,
        }, SwebKitJsonOptions.Default);
    }

    // ── Per-area comparators ────────────────────────────────────────────────

    /// <summary>
    /// AKS: <c>ResourceKey</c> is "namespace" (compare the whole deployment set) or
    /// "namespace/deployment". Each side honors its own <see cref="WorkspaceResourceNode.KubeconfigContext"/>.
    /// </summary>
    private async Task<CompareOutcome> CompareAksAsync(
        WorkspaceResourceNode? nodeA, WorkspaceResourceNode? nodeB, CancellationToken ct)
    {
        var a = nodeA is null ? NodeMissing() : await AksSideAsync(nodeA, ct);
        var b = nodeB is null ? NodeMissing() : await AksSideAsync(nodeB, ct);
        var diffs = new List<object>();
        var notes = new List<string>();

        if (a.Payload is DeploymentInfo da && b.Payload is DeploymentInfo db)
        {
            DiffField(diffs, "image_tag", da.ImageTag, db.ImageTag);
            DiffField(diffs, "replicas", da.Replicas, db.Replicas);
            DiffField(diffs, "ready_replicas", da.ReadyReplicas, db.ReadyReplicas);
            DiffField(diffs, "status", da.Status, db.Status);
        }
        else if (a.Payload is IReadOnlyList<DeploymentInfo> listA && b.Payload is IReadOnlyList<DeploymentInfo> listB)
        {
            DiffDeployments(diffs, listA, listB);
        }
        else if (a.Payload is not null && b.Payload is not null)
        {
            notes.Add("One side names a deployment and the other a whole namespace — align the map keys for a meaningful diff.");
        }

        return new(a, b, diffs, notes);

        static SideOutcome NodeMissing() => SideOutcome.MissingResult("No AKS node carries this logical name on this map.");
    }

    private Task<SideOutcome> AksSideAsync(WorkspaceResourceNode node, CancellationToken ct)
    {
        var slash = node.ResourceKey.IndexOf('/');
        var ns = slash < 0 ? node.ResourceKey : node.ResourceKey[..slash];
        var deployment = slash < 0 ? null : node.ResourceKey[(slash + 1)..];
        var connectionKey = AksToolContext.ResolveConnectionKey(_appState, node.KubeconfigContext);

        return FetchSideAsync("Aks", AccessCapabilities.KubernetesRead, connectionKey, async c =>
        {
            var client = AksToolContext.ResolveClient(_aksFactory, _demoAks, _appState, node.KubeconfigContext);
            var deployments = await client.GetDeploymentsAsync(ns, c).ConfigureAwait(false);
            if (deployment is null)
            {
                return SideOutcome.OkResult(new
                {
                    @namespace = ns,
                    context = node.KubeconfigContext,
                    deployments = deployments
                        .OrderBy(d => d.Name, StringComparer.OrdinalIgnoreCase)
                        .Select(DeploymentJson)
                        .ToList(),
                }, payload: deployments);
            }

            var match = deployments.FirstOrDefault(d =>
                string.Equals(d.Name, deployment, StringComparison.OrdinalIgnoreCase));
            return match is null
                ? SideOutcome.MissingResult($"Deployment '{deployment}' not found in namespace '{ns}'.")
                : SideOutcome.OkResult(new
                {
                    @namespace = ns,
                    context = node.KubeconfigContext,
                    deployment = DeploymentJson(match),
                }, payload: match);
        }, ct);
    }

    /// <summary>
    /// Service Bus: <c>ResourceKey</c> is "namespace-fqdn" (compare the queue set) or
    /// "namespace-fqdn/queue-name". Queue settings are whatever <see cref="IServiceBusClient"/>
    /// surfaces — the disabled/session flags plus runtime message counts; richer queue
    /// properties (TTL, lock duration, max delivery count) aren't on the client surface today.
    /// </summary>
    private async Task<CompareOutcome> CompareServiceBusAsync(
        WorkspaceResourceNode? nodeA, WorkspaceResourceNode? nodeB, CancellationToken ct)
    {
        var a = nodeA is null ? SideOutcome.MissingResult("No Service Bus node carries this logical name on this map.")
                              : await SbSideAsync(nodeA, ct);
        var b = nodeB is null ? SideOutcome.MissingResult("No Service Bus node carries this logical name on this map.")
                              : await SbSideAsync(nodeB, ct);
        var diffs = new List<object>();
        var notes = new List<string>();

        if (a.Payload is SbEntityInfo qa && b.Payload is SbEntityInfo qb)
        {
            DiffQueue(diffs, qa, qb);
        }
        else if (a.Payload is IReadOnlyList<SbEntityInfo> listA && b.Payload is IReadOnlyList<SbEntityInfo> listB)
        {
            var byA = listA.ToDictionary(q => q.Name, StringComparer.OrdinalIgnoreCase);
            var byB = listB.ToDictionary(q => q.Name, StringComparer.OrdinalIgnoreCase);
            foreach (var name in byA.Keys.Order(StringComparer.OrdinalIgnoreCase))
            {
                if (!byB.TryGetValue(name, out var other))
                    diffs.Add(new { field = $"queue {name}", env_a = "present", env_b = "(absent)" });
                else
                    DiffQueue(diffs, byA[name], other, prefix: $"queue {name}: ");
            }
            foreach (var name in byB.Keys.Where(n => !byA.ContainsKey(n)).Order(StringComparer.OrdinalIgnoreCase))
                diffs.Add(new { field = $"queue {name}", env_a = "(absent)", env_b = "present" });
        }
        else if (a.Payload is not null && b.Payload is not null)
        {
            notes.Add("One side names a queue and the other a whole namespace — align the map keys for a meaningful diff.");
        }

        return new(a, b, diffs, notes);
    }

    private async Task<SideOutcome> SbSideAsync(WorkspaceResourceNode node, CancellationToken ct)
    {
        var slash = node.ResourceKey.IndexOf('/');
        var host = slash < 0 ? node.ResourceKey : node.ResourceKey[..slash];
        var queue = slash < 0 ? null : node.ResourceKey[(slash + 1)..];

        // Resolve the connection key eagerly but defer the client itself — a known-denial
        // short-circuit must not spin up the pooled client it would never use.
        Func<IServiceBusClient> resolveClient;
        string? connectionKey;
        if (_appState.UseDemoData)
        {
            connectionKey = CompareToolContext.DemoNamespaceKey(host);
            if (CompareToolContext.DemoServiceBus(host) is not { } demo)
                return SideOutcome.MissingResult($"No demo Service Bus namespace matches '{host}'.");
            resolveClient = () => demo;
        }
        else
        {
            var resolution = ServiceBusToolContext.ResolveNamespace(_appState, host);
            if (!resolution.IsSuccess)
                return SideOutcome.MissingResult(resolution.Error!);
            connectionKey = resolution.Namespace!.Id.ToString();
            var ns = resolution.Namespace;
            resolveClient = () => _sbPool.GetOrCreate(ns!);
        }

        return await FetchSideAsync("ServiceBus", AccessCapabilities.ServiceBusManage, connectionKey, async c =>
        {
            var queues = await resolveClient().ListQueuesAsync(c).ConfigureAwait(false);
            if (queue is null)
            {
                return SideOutcome.OkResult(new
                {
                    @namespace = host,
                    queues = queues
                        .OrderBy(q => q.Name, StringComparer.OrdinalIgnoreCase)
                        .Select(QueueJson)
                        .ToList(),
                }, payload: queues);
            }

            var match = queues.FirstOrDefault(q =>
                string.Equals(q.Name, queue, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(q.EntityPath, queue, StringComparison.OrdinalIgnoreCase));
            return match is null
                ? SideOutcome.MissingResult($"Queue '{queue}' not found on '{host}'.")
                : SideOutcome.OkResult(new { @namespace = host, queue = QueueJson(match) }, payload: match);
        }, ct);
    }

    /// <summary>
    /// SQL: <c>ResourceKey</c> is "server-fqdn/database" (database falls back to the connection's
    /// configured one). Wraps <see cref="SqlSchemaComparer"/> — each side's catalog is fetched
    /// independently so a denied side can't take the whole compare down; the metadata-hidden
    /// state (data-plane grants, no VIEW DEFINITION) lands on the same access_denied outcome the
    /// access report's <c>sql.metadata</c> probe uses.
    /// </summary>
    private async Task<CompareOutcome> CompareSqlAsync(
        WorkspaceResourceNode? nodeA, WorkspaceResourceNode? nodeB, CancellationToken ct)
    {
        var a = nodeA is null ? SideOutcome.MissingResult("No SQL node carries this logical name on this map.")
                              : await SqlSideAsync(nodeA, ct);
        var b = nodeB is null ? SideOutcome.MissingResult("No SQL node carries this logical name on this map.")
                              : await SqlSideAsync(nodeB, ct);
        var diffs = new List<object>();

        if (a.Payload is SqlSchemaModel schemaA && b.Payload is SqlSchemaModel schemaB)
        {
            // env A is the comparer's "source", env B the "target" — the OnlyInSource/OnlyInTarget
            // naming maps onto env_a/env_b unchanged.
            var result = SqlSchemaComparer.Compare(schemaA, schemaB);
            foreach (var o in result.OnlyInSource)
                diffs.Add(new { field = $"{o.Kind} {o.Schema}.{o.Name}", env_a = "present", env_b = "(absent)" });
            foreach (var o in result.OnlyInTarget)
                diffs.Add(new { field = $"{o.Kind} {o.Schema}.{o.Name}", env_a = "(absent)", env_b = "present" });
            foreach (var o in result.Differing)
            foreach (var d in o.Diffs)
                diffs.Add(new { field = $"{o.Schema}.{o.Name}: {d.Property}", env_a = d.SourceValue, env_b = d.TargetValue });
        }

        return new(a, b, diffs, []);
    }

    private async Task<SideOutcome> SqlSideAsync(WorkspaceResourceNode node, CancellationToken ct)
    {
        var slash = node.ResourceKey.IndexOf('/');
        var server = slash < 0 ? node.ResourceKey : node.ResourceKey[..slash];
        var database = slash < 0 ? null : node.ResourceKey[(slash + 1)..];

        var connection = SqlToolContext.GetConnections(_appState, _profiles)
            .FirstOrDefault(c => string.Equals(c.Server, server, StringComparison.OrdinalIgnoreCase));
        if (connection is null)
            return SideOutcome.MissingResult($"No configured SQL connection matches server '{server}'.");

        return await FetchSideAsync("Sql", AccessCapabilities.SqlMetadata, connection.Id, async c =>
        {
            var resolution = await SqlToolContext.ResolveAsync(_appState, _profiles, _sqlFactory, connection.Id, c)
                .ConfigureAwait(false);
            if (resolution.Error is not null)
                return SideOutcome.ErrorResult(resolution.Error);

            await using var client = resolution.Client!;
            var db = database ?? connection.Database;
            var schema = await client.GetSchemaAsync(db, c).ConfigureAwait(false);
            IReadOnlyList<string> permissions;
            try
            {
                permissions = await client.GetMyPermissionsAsync(db, c).ConfigureAwait(false);
            }
            catch
            {
                // Same contract as SqlEndpoints.GetSchemaAsync — a failed self-permission probe
                // leaves the metadata-hidden flag off rather than denying by default.
                permissions = [];
            }
            schema.ApplyPermissionAnalysis(permissions);

            if (schema.MetadataHidden)
            {
                var denial = MetadataHiddenDenial();
                RecordDenial(denial, connection.Id);
                return SideOutcome.DeniedResult(denial, cached: false);
            }

            return SideOutcome.OkResult(new
            {
                connection = connection.DisplayName,
                server,
                database = schema.Database ?? db,
                object_count = schema.Schemas.Sum(s => s.Objects.Count),
            }, payload: schema);
        }, ct);
    }

    /// <summary>
    /// Redis: <c>ResourceKey</c> is the configured cache id. Compares the durable server
    /// facts (version, maxmemory) plus per-database key counts; point-in-time counters
    /// (connected clients, used memory, hit ratio) stay in the per-side detail — they drift
    /// second-to-second and would drown a drift report in noise.
    /// </summary>
    private async Task<CompareOutcome> CompareRedisAsync(
        WorkspaceResourceNode? nodeA, WorkspaceResourceNode? nodeB, CancellationToken ct)
    {
        var a = nodeA is null ? SideOutcome.MissingResult("No Redis node carries this logical name on this map.")
                              : await RedisSideAsync(nodeA, ct);
        var b = nodeB is null ? SideOutcome.MissingResult("No Redis node carries this logical name on this map.")
                              : await RedisSideAsync(nodeB, ct);
        var diffs = new List<object>();

        if (a.Payload is RedisServerInfo ra && b.Payload is RedisServerInfo rb)
        {
            DiffField(diffs, "redis_version", ra.RedisVersion, rb.RedisVersion);
            DiffField(diffs, "max_memory_bytes", ra.MaxMemoryBytes, rb.MaxMemoryBytes);
            var keysA = ra.Databases.ToDictionary(d => d.Index, d => d.Keys);
            var keysB = rb.Databases.ToDictionary(d => d.Index, d => d.Keys);
            foreach (var index in keysA.Keys.Union(keysB.Keys).Order())
            {
                keysA.TryGetValue(index, out var ka);
                keysB.TryGetValue(index, out var kb);
                if (ka != kb)
                    diffs.Add(new { field = $"db{index}.keys", env_a = ka, env_b = kb });
            }
        }

        return new(a, b, diffs, []);
    }

    private async Task<SideOutcome> RedisSideAsync(WorkspaceResourceNode node, CancellationToken ct)
    {
        var cache = RedisToolContext.ResolveCache(_appState, _profiles, node.ResourceKey);
        if (cache is null)
            return SideOutcome.MissingResult($"No configured Redis cache matches '{node.ResourceKey}'.");

        return await FetchSideAsync("Redis", AccessCapabilities.RedisData, cache.Id, async c =>
        {
            var resolution = await RedisToolContext.ResolveAsync(_appState, _profiles, _redisFactory, cache.Id, c)
                .ConfigureAwait(false);
            if (resolution.Error is not null)
                return SideOutcome.ErrorResult(resolution.Error);

            using var client = resolution.Client!;
            var info = await client.GetServerInfoAsync(c).ConfigureAwait(false);
            return SideOutcome.OkResult(new
            {
                cache = cache.DisplayName,
                redis_version = info.RedisVersion,
                max_memory_bytes = info.MaxMemoryBytes,
                used_memory = info.UsedMemoryHuman,
                connected_clients = info.ConnectedClients,
                keyspace_hit_ratio = info.KeyspaceHitRatio,
                databases = info.Databases.ToDictionary(d => $"db{d.Index}", d => d.Keys),
            }, payload: info);
        }, ct);
    }

    // ── Shared machinery ────────────────────────────────────────────────────

    /// <summary>
    /// Runs one side's fetch with the registry's two access behaviors applied per
    /// (area, connection, capability): a fresh known denial short-circuits before the SDK call,
    /// and an exception classified as an authorization failure becomes a structured
    /// <c>access_denied</c> side that also feeds the access report. Everything else becomes an
    /// <c>error</c> side — one environment's failure never sinks the compare.
    /// </summary>
    private async Task<SideOutcome> FetchSideAsync(
        string featureArea, string capability, string? connectionKey,
        Func<CancellationToken, Task<SideOutcome>> body, CancellationToken ct)
    {
        if (connectionKey is not null && _accessReport is not null &&
            _accessReport.TryGetKnownDenial(featureArea, connectionKey, capability, out var known))
        {
            return SideOutcome.DeniedResult(known, cached: true);
        }

        try
        {
            return await body(ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (AccessAdvisor.TryCreateDenial(ex, featureArea, out var denial))
        {
            denial = denial with { Capability = capability };
            RecordDenial(denial, connectionKey);
            return SideOutcome.DeniedResult(denial, cached: false);
        }
        catch (Exception ex)
        {
            return SideOutcome.ErrorResult(ex.Message);
        }
    }

    private void RecordDenial(AccessDenial denial, string? connectionKey)
    {
        if (_accessReport is null || connectionKey is null)
            return;
        try
        {
            _accessReport.RecordObservedDenial(denial, connectionKey);
        }
        catch
        {
            // A report-sink failure must never mask the denial result itself (registry contract).
        }
    }

    /// <summary>The sql.metadata denial the access report's probe emits — kept in sync
    /// deliberately (the canonical text lives in the sidecar, which agents can't reference).</summary>
    private static AccessDenial MetadataHiddenDenial() =>
        new("Sql", AccessCapabilities.SqlMetadata,
            "VIEW DEFINITION",
            "Ask a DBA for VIEW DEFINITION (or db_datareader) on this database so schema browsing works.",
            "The identity holds data-plane grants (SELECT/EXECUTE) but the schema catalog reads " +
            "empty — metadata is hidden without VIEW DEFINITION.",
            Remedy: new AccessRemedy(AccessRemedyKind.SqlGrant, "VIEW DEFINITION",
                "GRANT VIEW DEFINITION TO {principal};",
                "Ask a DBA for VIEW DEFINITION (or db_datareader) on this database so schema browsing works."));

    private static object SideJson(WorkspaceResourceNode? node, SideOutcome outcome)
    {
        var denial = outcome.Denial is null ? null : new
        {
            capability = outcome.Denial.Capability,
            required_access = outcome.Denial.RequiredAccess,
            guidance = outcome.Denial.Guidance,
            detail = outcome.Denial.Detail,
        };
        return new
        {
            status = outcome.Status,
            resource_key = node?.ResourceKey,
            display_label = node?.DisplayLabel,
            detail = outcome.Detail,
            message = outcome.Message,
            denial,
            // Stable bool — the registry's flat shape omits `cached` on live denials, but inside
            // a per-side object an always-present flag reads better.
            cached = outcome.CachedDenial,
        };
    }

    /// <summary>A side in an area with no comparator — missing when the map has no node,
    /// <c>unsupported</c> when it has one but nothing can be compared.</summary>
    private static SideOutcome UnsupportedSide(WorkspaceResourceNode? node) =>
        node is null
            ? SideOutcome.MissingResult("No node carries this logical name on this map.")
            : new SideOutcome { Status = SideOutcome.Unsupported };

    private static (WorkspaceResourceNode? Node, int Extra) Pick(List<WorkspaceResourceNode> nodes, WorkspaceResourceArea area)
    {
        var matches = nodes.Where(n => n.Area == area).ToList();
        return (matches.FirstOrDefault(), Math.Max(0, matches.Count - 1));
    }

    private static void DiffField(List<object> diffs, string field, object? a, object? b)
    {
        if (!Equals(a, b))
            diffs.Add(new { field, env_a = a, env_b = b });
    }

    private static void DiffDeployments(
        List<object> diffs, IReadOnlyList<DeploymentInfo> a, IReadOnlyList<DeploymentInfo> b)
    {
        var byA = a.ToDictionary(d => d.Name, StringComparer.OrdinalIgnoreCase);
        var byB = b.ToDictionary(d => d.Name, StringComparer.OrdinalIgnoreCase);
        foreach (var name in byA.Keys.Order(StringComparer.OrdinalIgnoreCase))
        {
            if (!byB.TryGetValue(name, out var other))
            {
                diffs.Add(new { field = $"deployment {name}", env_a = "present", env_b = "(absent)" });
                continue;
            }
            var mine = byA[name];
            DiffField(diffs, $"deployment {name}: image_tag", mine.ImageTag, other.ImageTag);
            DiffField(diffs, $"deployment {name}: replicas", mine.Replicas, other.Replicas);
            DiffField(diffs, $"deployment {name}: ready_replicas", mine.ReadyReplicas, other.ReadyReplicas);
            DiffField(diffs, $"deployment {name}: status", mine.Status, other.Status);
        }
        foreach (var name in byB.Keys.Where(n => !byA.ContainsKey(n)).Order(StringComparer.OrdinalIgnoreCase))
            diffs.Add(new { field = $"deployment {name}", env_a = "(absent)", env_b = "present" });
    }

    private static void DiffQueue(List<object> diffs, SbEntityInfo a, SbEntityInfo b, string prefix = "")
    {
        DiffField(diffs, $"{prefix}is_disabled", a.IsDisabled, b.IsDisabled);
        DiffField(diffs, $"{prefix}requires_session", a.RequiresSession, b.RequiresSession);
        // Runtime counts — the plan's "settings and stats". null-vs-value diffs report "unread"
        // rather than pretending the unread side had zero messages.
        DiffField(diffs, $"{prefix}active_messages", a.Stats?.ActiveMessageCount, b.Stats?.ActiveMessageCount);
        DiffField(diffs, $"{prefix}dead_letter_messages", a.Stats?.DeadLetterMessageCount, b.Stats?.DeadLetterMessageCount);
        DiffField(diffs, $"{prefix}scheduled_messages", a.Stats?.ScheduledMessageCount, b.Stats?.ScheduledMessageCount);
    }

    private static object DeploymentJson(DeploymentInfo d) => new
    {
        d.Name, image_tag = d.ImageTag, replicas = d.Replicas, ready_replicas = d.ReadyReplicas, status = d.Status,
    };

    private static object QueueJson(SbEntityInfo q) => new
    {
        q.Name, is_disabled = q.IsDisabled, requires_session = q.RequiresSession,
        active_messages = q.Stats?.ActiveMessageCount,
        dead_letter_messages = q.Stats?.DeadLetterMessageCount,
        scheduled_messages = q.Stats?.ScheduledMessageCount,
    };

    private static string AreaName(WorkspaceResourceArea area) => area.ToString().ToLowerInvariant();

    private static string? Arg(JsonElement arguments, string name) =>
        arguments.TryGetProperty(name, out var el) && el.ValueKind == JsonValueKind.String
            ? el.GetString()
            : null;

    private static object MapHint(IReadOnlyList<WorkspaceMap> maps) =>
        new { available_maps = maps.Select(m => m.Name).ToList() };

    private static string Error(string message, object? extra = null) =>
        JsonSerializer.Serialize(new { error = message, hints = extra }, SwebKitJsonOptions.Default);
}
