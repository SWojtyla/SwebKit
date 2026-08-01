using SwebKit.Core.Abstractions;
using SwebKit.Core.Configuration;
using SwebKit.Core.Domain;
using SwebKit.Core.Models;
using SwebKit.Kubernetes.AksClient;

namespace SwebKit.Sidecar.Endpoints;

public static class AksEndpoints
{
    /// <summary>
    /// Resolves the AKS client through the shared, DI-registered <see cref="IMonitoringConnectionPool"/>
    /// instead of a static field, so it's testable (a fake pool can be substituted) and consistent
    /// with the caching pattern <c>SidecarMonitoringConnectionPool</c> already uses for Service Bus/Redis.
    /// </summary>
    private static IAksClient GetClient(IMonitoringConnectionPool pool, string? context = null)
    {
        var client = pool.GetAksClient(context);
        if (client is null)
            throw new InvalidOperationException("AKS is not configured. Set kubeconfig path/context in Settings.");

        return client;
    }

    private static IReadOnlyList<string> ParseNamespaceToken(string ns)
    {
        var trimmed = ns.Trim();
        if (trimmed == "*" || string.IsNullOrWhiteSpace(trimmed))
            return ["*"];

        if (!trimmed.Contains(','))
            return [trimmed];

        return trimmed
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(s => !string.IsNullOrWhiteSpace(s) && s != "*")
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }

    private static async Task<IReadOnlyList<string>> ResolveNamespacesAsync(IAksClient client, string ns, CancellationToken ct)
    {
        var token = ns.Trim();
        if (token == "*")
        {
            var all = await client.GetNamespacesAsync(ct);
            return all.Count > 0 ? all : ["default"];
        }

        var parsed = ParseNamespaceToken(ns);
        if (parsed.Count == 1 && parsed[0] == "*")
        {
            var all = await client.GetNamespacesAsync(ct);
            return all.Count > 0 ? all : ["default"];
        }

        return parsed;
    }

    public static void MapAksEndpoints(this WebApplication app)
    {
        // ── Connection / context ─────────────────────────────────────────────────

        app.MapGet("/api/aks/test", async (ProfileRepository profile, DemoModeService demo, IMonitoringConnectionPool pool, CancellationToken ct) =>
        {
            try
            {
                var client = GetClient(pool);
                var ok = await client.TestConnectionAsync(ct);
                return Results.Ok(new { connected = ok });
            }
            catch (Exception ex)
            {
                return Results.Ok(new { connected = false, error = ex.Message });
            }
        });

        app.MapGet("/api/aks/contexts", (ProfileRepository profile, DemoModeService demo, IMonitoringConnectionPool pool) =>
        {
            var aksConfig = profile.GetProfileData().Config.AksConfig;
            var contexts = KubernetesAksClient.ReadContextsFromKubeconfig(aksConfig?.KubeconfigPath);
            return Results.Ok(contexts);
        });

        app.MapPost("/api/aks/context", async (SetContextRequest request, ProfileRepository profile, DemoModeService demo, IMonitoringConnectionPool pool, CancellationToken ct) =>
        {
            var data = profile.GetProfileData();
            data.Config.AksConfig ??= new AksConfig();
            data.Config.AksConfig.KubeconfigContext = request.Context;

            if (request.DefaultNamespace is not null)
                data.Config.AksConfig.DefaultNamespace = request.DefaultNamespace;

            await profile.SaveAsync();
            pool.InvalidateStaleConnections();

            try
            {
                var client = GetClient(pool, request.Context);
                var ok = await client.TestConnectionAsync(ct);
                return Results.Ok(new { connected = ok, context = request.Context });
            }
            catch (Exception ex)
            {
                return Results.Ok(new { connected = false, context = request.Context, error = ex.Message });
            }
        });

        app.MapGet("/api/aks/namespaces", async (ProfileRepository profile, DemoModeService demo, IMonitoringConnectionPool pool, CancellationToken ct) =>
        {
            var client = GetClient(pool);
            var namespaces = await client.GetNamespacesAsync(ct);
            return Results.Ok(namespaces);
        });

        // ── Workloads ──────────────────────────────────────────────────────────

        app.MapGet("/api/aks/{ns}/deployments", async (string ns, ProfileRepository profile, DemoModeService demo, IMonitoringConnectionPool pool, CancellationToken ct) =>
        {
            var client = GetClient(pool);
            var namespaces = await ResolveNamespacesAsync(client, ns, ct);
            var deployments = await client.GetDeploymentsAsync(namespaces, ct);
            return Results.Ok(deployments);
        });

        app.MapGet("/api/aks/{ns}/pods", async (string ns, string? labelSelector, ProfileRepository profile, DemoModeService demo, IMonitoringConnectionPool pool, CancellationToken ct) =>
        {
            var client = GetClient(pool);
            var namespaces = await ResolveNamespacesAsync(client, ns, ct);
            var pods = string.IsNullOrWhiteSpace(labelSelector)
                ? await client.GetPodsAsync(namespaces, ct)
                : await client.GetPodsAsync(namespaces, labelSelector, ct);
            return Results.Ok(pods);
        });

        app.MapGet("/api/aks/{ns}/statefulsets", async (string ns, ProfileRepository profile, DemoModeService demo, IMonitoringConnectionPool pool, CancellationToken ct) =>
        {
            var client = GetClient(pool);
            var namespaces = await ResolveNamespacesAsync(client, ns, ct);
            var sts = await client.GetStatefulSetsAsync(namespaces, ct);
            return Results.Ok(sts);
        });

        app.MapGet("/api/aks/{ns}/services", async (string ns, ProfileRepository profile, DemoModeService demo, IMonitoringConnectionPool pool, CancellationToken ct) =>
        {
            var client = GetClient(pool);
            var namespaces = await ResolveNamespacesAsync(client, ns, ct);
            var services = await client.GetServicesAsync(namespaces, ct);
            return Results.Ok(services);
        });

        app.MapGet("/api/aks/{ns}/ingresses", async (string ns, ProfileRepository profile, DemoModeService demo, IMonitoringConnectionPool pool, CancellationToken ct) =>
        {
            var client = GetClient(pool);
            var namespaces = await ResolveNamespacesAsync(client, ns, ct);
            var ingresses = await client.GetIngressesAsync(namespaces, ct);
            return Results.Ok(ingresses);
        });

        app.MapGet("/api/aks/{ns}/events", async (string ns, int? limit, string? involvedObject, ProfileRepository profile, DemoModeService demo, IMonitoringConnectionPool pool, CancellationToken ct) =>
        {
            var client = GetClient(pool);
            var namespaces = await ResolveNamespacesAsync(client, ns, ct);
            var events = limit.HasValue
                ? await client.GetEventsAsync(namespaces, limit.Value, ct)
                : await client.GetEventsAsync(namespaces, involvedObject, ct);
            return Results.Ok(events);
        });

        // ── Helm ───────────────────────────────────────────────────────────────

        app.MapGet("/api/aks/{ns}/helm-releases", async (string ns, ProfileRepository profile, DemoModeService demo, IMonitoringConnectionPool pool, CancellationToken ct) =>
        {
            var client = GetClient(pool);
            var namespaces = await ResolveNamespacesAsync(client, ns, ct);
            var releases = await client.GetHelmReleasesAsync(namespaces, ct);
            return Results.Ok(releases);
        });

        app.MapGet("/api/aks/{ns}/helm-releases/{release}/history", async (string ns, string release, ProfileRepository profile, DemoModeService demo, IMonitoringConnectionPool pool, CancellationToken ct) =>
        {
            var client = GetClient(pool);
            var history = await client.GetHelmReleaseHistoryAsync(ns, release, ct);
            return Results.Ok(history);
        });

        app.MapGet("/api/aks/{ns}/helm-releases/{release}/values", async (string ns, string release, ProfileRepository profile, DemoModeService demo, IMonitoringConnectionPool pool, CancellationToken ct) =>
        {
            var client = GetClient(pool);
            var values = await client.GetHelmReleaseValuesAsync(ns, release, ct);
            return Results.Ok(values);
        });

        app.MapGet("/api/aks/{ns}/helm-releases/{release}/notes", async (string ns, string release, ProfileRepository profile, DemoModeService demo, IMonitoringConnectionPool pool, CancellationToken ct) =>
        {
            var client = GetClient(pool);
            var notes = await client.GetHelmReleaseNotesAsync(ns, release, ct);
            return Results.Ok(new { notes });
        });

        app.MapGet("/api/aks/{ns}/helm-releases/{release}/manifest", async (string ns, string release, ProfileRepository profile, DemoModeService demo, IMonitoringConnectionPool pool, CancellationToken ct) =>
        {
            var client = GetClient(pool);
            var manifest = await client.GetHelmReleaseManifestAsync(ns, release, ct);
            return Results.Ok(new { manifest });
        });

        app.MapPost("/api/aks/{ns}/helm-releases/{release}/rollback", async (string ns, string release, int targetRevision, ProfileRepository profile, DemoModeService demo, IMonitoringConnectionPool pool, CancellationToken ct) =>
        {
            var client = GetClient(pool);
            await client.RollbackHelmReleaseAsync(ns, release, targetRevision, ct);
            return Results.Ok();
        });

        // ── ConfigMaps & Secrets ───────────────────────────────────────────────

        app.MapGet("/api/aks/{ns}/configmaps", async (string ns, ProfileRepository profile, DemoModeService demo, IMonitoringConnectionPool pool, CancellationToken ct) =>
        {
            var client = GetClient(pool);
            var namespaces = await ResolveNamespacesAsync(client, ns, ct);
            var configMaps = await client.GetConfigMapsAsync(namespaces, ct);
            return Results.Ok(configMaps);
        });

        app.MapGet("/api/aks/{ns}/secrets", async (string ns, ProfileRepository profile, DemoModeService demo, IMonitoringConnectionPool pool, CancellationToken ct) =>
        {
            var client = GetClient(pool);
            var namespaces = await ResolveNamespacesAsync(client, ns, ct);
            var (secrets, _) = await client.GetSecretsAndHelmReleasesAsync(namespaces, ct);
            return Results.Ok(secrets);
        });

        app.MapGet("/api/aks/{ns}/secrets/{name}/values", async (string ns, string name, ProfileRepository profile, DemoModeService demo, IMonitoringConnectionPool pool, CancellationToken ct) =>
        {
            var client = GetClient(pool);
            var values = await client.GetSecretValuesAsync(ns, name, ct);
            return Results.Ok(values);
        });

        // ── YAML ───────────────────────────────────────────────────────────────

        app.MapGet("/api/aks/{ns}/yaml/{kind}/{name}", async (string ns, string kind, string name, ProfileRepository profile, DemoModeService demo, IMonitoringConnectionPool pool, CancellationToken ct) =>
        {
            var client = GetClient(pool);
            var yaml = await client.GetResourceYamlAsync(ns, kind, name, ct);
            return Results.Text(yaml, "text/yaml");
        });

        app.MapPost("/api/aks/{ns}/yaml/{kind}/{name}", async (string ns, string kind, string name, YamlApplyRequest req, ProfileRepository profile, DemoModeService demo, IMonitoringConnectionPool pool, CancellationToken ct) =>
        {
            var client = GetClient(pool);
            await client.ApplyResourceYamlAsync(ns, kind, name, req.Yaml, ct);
            return Results.Ok();
        });

        app.MapPost("/api/aks/{ns}/yaml/validate", async (string ns, YamlValidateRequest req, ProfileRepository profile, DemoModeService demo, IMonitoringConnectionPool pool, CancellationToken ct) =>
        {
            var client = GetClient(pool);
            var error = await client.ValidateResourceYamlAsync(ns, req.Yaml, ct);
            return Results.Ok(new { valid = error is null, error });
        });

        // ── Actions ────────────────────────────────────────────────────────────

        app.MapPost("/api/aks/{ns}/deployments/{name}/restart", async (string ns, string name, ProfileRepository profile, DemoModeService demo, IMonitoringConnectionPool pool, CancellationToken ct) =>
        {
            var client = GetClient(pool);
            await client.RestartDeploymentAsync(ns, name, ct);
            return Results.Ok();
        });

        app.MapPost("/api/aks/{ns}/deployments/{name}/scale", async (string ns, string name, int replicas, ProfileRepository profile, DemoModeService demo, IMonitoringConnectionPool pool, CancellationToken ct) =>
        {
            var client = GetClient(pool);
            await client.ScaleDeploymentAsync(ns, name, replicas, ct);
            return Results.Ok();
        });

        app.MapPost("/api/aks/{ns}/pods/{name}/delete", async (string ns, string name, ProfileRepository profile, DemoModeService demo, IMonitoringConnectionPool pool, CancellationToken ct) =>
        {
            var client = GetClient(pool);
            await client.DeletePodAsync(ns, name, ct);
            return Results.Ok();
        });

        app.MapPost("/api/aks/{ns}/statefulsets/{name}/restart", async (string ns, string name, ProfileRepository profile, DemoModeService demo, IMonitoringConnectionPool pool, CancellationToken ct) =>
        {
            var client = GetClient(pool);
            await client.RestartStatefulSetAsync(ns, name, ct);
            return Results.Ok();
        });

        app.MapPost("/api/aks/{ns}/statefulsets/{name}/scale", async (string ns, string name, int replicas, ProfileRepository profile, DemoModeService demo, IMonitoringConnectionPool pool, CancellationToken ct) =>
        {
            var client = GetClient(pool);
            await client.ScaleStatefulSetAsync(ns, name, replicas, ct);
            return Results.Ok();
        });

        app.MapDelete("/api/aks/{ns}/ingresses/{name}", async (string ns, string name, ProfileRepository profile, DemoModeService demo, IMonitoringConnectionPool pool, CancellationToken ct) =>
        {
            var client = GetClient(pool);
            await client.DeleteIngressAsync(ns, name, ct);
            return Results.Ok();
        });

        // ── HPA ────────────────────────────────────────────────────────────────

        app.MapGet("/api/aks/{ns}/hpas", async (string ns, ProfileRepository profile, DemoModeService demo, IMonitoringConnectionPool pool, CancellationToken ct) =>
        {
            var client = GetClient(pool);
            var namespaces = await ResolveNamespacesAsync(client, ns, ct);
            var hpas = await client.GetHpasAsync(namespaces, ct);
            return Results.Ok(hpas);
        });

        app.MapPost("/api/aks/{ns}/hpas/{name}/scale", async (string ns, string name, ScaleHpaRequest dto, ProfileRepository profile, DemoModeService demo, IMonitoringConnectionPool pool, CancellationToken ct) =>
        {
            var client = GetClient(pool);
            var namespaces = await ResolveNamespacesAsync(client, ns, ct);
            await Task.WhenAll(namespaces.Select(n => client.ScaleHpaAsync(n, name, dto.MinReplicas, dto.MaxReplicas, ct)));
            return Results.Ok();
        });

        app.MapDelete("/api/aks/{ns}/hpas/{name}", async (string ns, string name, ProfileRepository profile, DemoModeService demo, IMonitoringConnectionPool pool, CancellationToken ct) =>
        {
            var client = GetClient(pool);
            var namespaces = await ResolveNamespacesAsync(client, ns, ct);
            await Task.WhenAll(namespaces.Select(n => client.DeleteHpaAsync(n, name, ct)));
            return Results.NoContent();
        });

        app.MapPost("/api/aks/{ns}/hpas/{name}/scaling-enabled", async (string ns, string name, SetScalingEnabledRequest dto, ProfileRepository profile, DemoModeService demo, IMonitoringConnectionPool pool, CancellationToken ct) =>
        {
            var client = GetClient(pool);
            var namespaces = await ResolveNamespacesAsync(client, ns, ct);
            await Task.WhenAll(namespaces.Select(n => client.SetHpaScalingEnabledAsync(n, name, dto.Enabled, ct)));
            return Results.Ok();
        });

        // ── Jobs & CronJobs ────────────────────────────────────────────────────

        app.MapGet("/api/aks/{ns}/cronjobs", async (string ns, ProfileRepository profile, DemoModeService demo, IMonitoringConnectionPool pool, CancellationToken ct) =>
        {
            var client = GetClient(pool);
            var namespaces = await ResolveNamespacesAsync(client, ns, ct);
            var cronJobs = await client.GetCronJobsAsync(namespaces, ct);
            return Results.Ok(cronJobs);
        });

        app.MapPost("/api/aks/{ns}/cronjobs/{name}/suspend", async (string ns, string name, SuspendCronJobRequest dto, ProfileRepository profile, DemoModeService demo, IMonitoringConnectionPool pool, CancellationToken ct) =>
        {
            var client = GetClient(pool);
            var namespaces = await ResolveNamespacesAsync(client, ns, ct);
            await Task.WhenAll(namespaces.Select(n => client.SuspendCronJobAsync(n, name, dto.Suspend, ct)));
            return Results.Ok();
        });

        app.MapGet("/api/aks/{ns}/jobs", async (string ns, ProfileRepository profile, DemoModeService demo, IMonitoringConnectionPool pool, CancellationToken ct) =>
        {
            var client = GetClient(pool);
            var namespaces = await ResolveNamespacesAsync(client, ns, ct);
            var jobs = await client.GetJobsAsync(namespaces, ct);
            return Results.Ok(jobs);
        });

        // ── Gateway API ────────────────────────────────────────────────────────

        app.MapGet("/api/aks/{ns}/httproutes", async (string ns, ProfileRepository profile, DemoModeService demo, IMonitoringConnectionPool pool, CancellationToken ct) =>
        {
            // The client already returns an empty list when the Gateway API CRD isn't installed
            // (see KubernetesAksClient.ListGatewayApiCustomObjectsAsync) — no need to swallow
            // exceptions here too. Doing so previously made a real auth/RBAC/connectivity failure
            // indistinguishable from "no HTTPRoutes exist," which is actively misleading for a
            // debugging tool. Let real errors propagate to the global exception handler like every
            // other AKS endpoint does.
            var client = GetClient(pool);
            var namespaces = await ResolveNamespacesAsync(client, ns, ct);
            var routes = await client.GetHttpRoutesAsync(namespaces, ct);
            return Results.Ok(routes);
        });

        app.MapDelete("/api/aks/{ns}/httproutes/{name}", async (string ns, string name, ProfileRepository profile, DemoModeService demo, IMonitoringConnectionPool pool, CancellationToken ct) =>
        {
            var client = GetClient(pool);
            await client.DeleteHttpRouteAsync(ns, name, ct);
            return Results.Ok();
        });

        app.MapGet("/api/aks/gatewayclasses", async (ProfileRepository profile, DemoModeService demo, IMonitoringConnectionPool pool, CancellationToken ct) =>
        {
            var client = GetClient(pool);
            var classes = await client.GetGatewayClassesAsync(ct);
            return Results.Ok(classes);
        });

        app.MapGet("/api/aks/{ns}/gateways", async (string ns, ProfileRepository profile, DemoModeService demo, IMonitoringConnectionPool pool, CancellationToken ct) =>
        {
            var client = GetClient(pool);
            var namespaces = await ResolveNamespacesAsync(client, ns, ct);
            var gateways = await client.GetGatewaysAsync(namespaces, ct);
            return Results.Ok(gateways);
        });

        // ── Container details ──────────────────────────────────────────────────

        app.MapGet("/api/aks/{ns}/pods/{podName}/containers", async (string ns, string podName, ProfileRepository profile, DemoModeService demo, IMonitoringConnectionPool pool, CancellationToken ct) =>
        {
            var client = GetClient(pool);
            var containers = await client.GetContainerDetailsAsync(ns, podName, ct);
            return Results.Ok(containers);
        });

        // ── Pod metrics ────────────────────────────────────────────────────────

        app.MapGet("/api/aks/{ns}/pod-metrics", async (string ns, ProfileRepository profile, DemoModeService demo, IMonitoringConnectionPool pool, CancellationToken ct) =>
        {
            var client = GetClient(pool);
            var namespaces = await ResolveNamespacesAsync(client, ns, ct);
            var metrics = await client.GetPodMetricsAsync(namespaces, ct);
            return Results.Ok(metrics);
        });

        // ── Pod Logs ───────────────────────────────────────────────────────────

        app.MapGet("/api/aks/{ns}/pods/{podName}/logs", async (string ns, string podName, string? container, int tail, ProfileRepository profile, DemoModeService demo, IMonitoringConnectionPool pool, CancellationToken ct) =>
        {
            var client = GetClient(pool);
            var opts = new LogStreamOptions { TailLines = tail, Follow = false };
            var lines = new List<string>(tail > 0 ? tail : 100);
            await foreach (var line in client.StreamPodLogsAsync(ns, podName, container ?? "", opts, ct))
            {
                lines.Add(line);
                if (tail > 0 && lines.Count >= tail) break;
            }
            return Results.Text(string.Join('\n', lines), "text/plain");
        });

        app.MapGet("/api/aks/{ns}/pods/{podName}/logs/stream", async (HttpContext ctx, string ns, string podName, string? container, int tail, bool follow, int? sinceSeconds, bool previousContainer, string? filter, ProfileRepository profile, DemoModeService demo, IMonitoringConnectionPool pool, CancellationToken ct) =>
        {
            var client = GetClient(pool);
            ctx.Response.ContentType = "text/event-stream";
            ctx.Response.Headers.CacheControl = "no-cache";
            ctx.Response.Headers.Connection = "keep-alive";

            var opts = new LogStreamOptions
            {
                TailLines = tail > 0 ? tail : 100,
                Follow = follow,
                SinceSeconds = sinceSeconds,
                PreviousContainer = previousContainer,
            };

            try
            {
                await foreach (var line in client.StreamPodLogsAsync(ns, podName, container ?? "", opts, ct))
                {
                    var output = string.IsNullOrEmpty(filter) || line.Contains(filter, StringComparison.OrdinalIgnoreCase)
                        ? line
                        : null;
                    if (output is not null)
                    {
                        await ctx.Response.WriteAsync($"data: {output}\n\n", ct);
                        await ctx.Response.Body.FlushAsync(ct);
                    }
                }
            }
            catch (OperationCanceledException)
            {
            }

            await ctx.Response.WriteAsync("event: done\ndata: \n\n", ct);
            await ctx.Response.Body.FlushAsync(ct);
        });
    }
}

public sealed record SetContextRequest(string Context, string? DefaultNamespace = null);
public sealed record ScaleHpaRequest(int MinReplicas, int MaxReplicas);
public sealed record SetScalingEnabledRequest(bool Enabled);
public sealed record SuspendCronJobRequest(bool Suspend);
public sealed record YamlApplyRequest(string Yaml);
public sealed record YamlValidateRequest(string Yaml);
