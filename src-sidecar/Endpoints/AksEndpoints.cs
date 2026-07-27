using SwebKit.Core.Abstractions;
using SwebKit.Core.Configuration;
using SwebKit.Core.Domain;
using SwebKit.Core.Models;
using SwebKit.Kubernetes.AksClient;

namespace SwebKit.Sidecar.Endpoints;

public static class AksEndpoints
{
    private static IAksClient? _client;
    private static readonly Lock _clientLock = new();

    private static IAksClient GetClient(ProfileRepository profile, DemoModeService demo)
    {
        if (demo.IsDemoMode)
            return demo.GetAksClient();

        lock (_clientLock)
        {
            if (_client is not null) return _client;

            var aksConfig = profile.GetProfileData().Config.AksConfig;
            if (aksConfig is null)
                throw new InvalidOperationException("AKS is not configured. Set kubeconfig path/context in Settings.");

            var factory = new AksClientFactory();
            _client = factory.Create(aksConfig.KubeconfigContext, aksConfig.KubeconfigPath);
            return _client;
        }
    }

    public static void ResetCachedClient()
    {
        lock (_clientLock)
        {
            if (_client is IAsyncDisposable asyncDisp)
            {
                try { asyncDisp.DisposeAsync().AsTask().Wait(); } catch { /* best effort */ }
            }
            else if (_client is IDisposable disp)
            {
                try { disp.Dispose(); } catch { /* best effort */ }
            }

            _client = null;
        }
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

        app.MapGet("/api/aks/test", async (ProfileRepository profile, DemoModeService demo, CancellationToken ct) =>
        {
            try
            {
                var client = GetClient(profile, demo);
                var ok = await client.TestConnectionAsync(ct);
                return Results.Ok(new { connected = ok });
            }
            catch (Exception ex)
            {
                return Results.Ok(new { connected = false, error = ex.Message });
            }
        });

        app.MapGet("/api/aks/contexts", (ProfileRepository profile, DemoModeService demo) =>
        {
            var aksConfig = profile.GetProfileData().Config.AksConfig;
            var contexts = KubernetesAksClient.ReadContextsFromKubeconfig(aksConfig?.KubeconfigPath);
            return Results.Ok(contexts);
        });

        app.MapPost("/api/aks/context", async (SetContextRequest request, ProfileRepository profile, DemoModeService demo, CancellationToken ct) =>
        {
            var data = profile.GetProfileData();
            data.Config.AksConfig ??= new AksConfig();
            data.Config.AksConfig.KubeconfigContext = request.Context;

            if (request.DefaultNamespace is not null)
                data.Config.AksConfig.DefaultNamespace = request.DefaultNamespace;

            await profile.SaveAsync();
            ResetCachedClient();

            try
            {
                var client = GetClient(profile, demo);
                var ok = await client.TestConnectionAsync(ct);
                return Results.Ok(new { connected = ok, context = request.Context });
            }
            catch (Exception ex)
            {
                return Results.Ok(new { connected = false, context = request.Context, error = ex.Message });
            }
        });

        app.MapGet("/api/aks/namespaces", async (ProfileRepository profile, DemoModeService demo, CancellationToken ct) =>
        {
            var client = GetClient(profile, demo);
            var namespaces = await client.GetNamespacesAsync(ct);
            return Results.Ok(namespaces);
        });

        // ── Workloads ──────────────────────────────────────────────────────────

        app.MapGet("/api/aks/{ns}/deployments", async (string ns, ProfileRepository profile, DemoModeService demo, CancellationToken ct) =>
        {
            var client = GetClient(profile, demo);
            var namespaces = await ResolveNamespacesAsync(client, ns, ct);
            var deployments = await client.GetDeploymentsAsync(namespaces, ct);
            return Results.Ok(deployments);
        });

        app.MapGet("/api/aks/{ns}/pods", async (string ns, string? labelSelector, ProfileRepository profile, DemoModeService demo, CancellationToken ct) =>
        {
            var client = GetClient(profile, demo);
            var namespaces = await ResolveNamespacesAsync(client, ns, ct);
            var pods = string.IsNullOrWhiteSpace(labelSelector)
                ? await client.GetPodsAsync(namespaces, ct)
                : await client.GetPodsAsync(namespaces, labelSelector, ct);
            return Results.Ok(pods);
        });

        app.MapGet("/api/aks/{ns}/statefulsets", async (string ns, ProfileRepository profile, DemoModeService demo, CancellationToken ct) =>
        {
            var client = GetClient(profile, demo);
            var namespaces = await ResolveNamespacesAsync(client, ns, ct);
            var sts = await client.GetStatefulSetsAsync(namespaces, ct);
            return Results.Ok(sts);
        });

        app.MapGet("/api/aks/{ns}/services", async (string ns, ProfileRepository profile, DemoModeService demo, CancellationToken ct) =>
        {
            var client = GetClient(profile, demo);
            var namespaces = await ResolveNamespacesAsync(client, ns, ct);
            var services = await client.GetServicesAsync(namespaces, ct);
            return Results.Ok(services);
        });

        app.MapGet("/api/aks/{ns}/ingresses", async (string ns, ProfileRepository profile, DemoModeService demo, CancellationToken ct) =>
        {
            var client = GetClient(profile, demo);
            var namespaces = await ResolveNamespacesAsync(client, ns, ct);
            var ingresses = await client.GetIngressesAsync(namespaces, ct);
            return Results.Ok(ingresses);
        });

        app.MapGet("/api/aks/{ns}/events", async (string ns, int? limit, string? involvedObject, ProfileRepository profile, DemoModeService demo, CancellationToken ct) =>
        {
            var client = GetClient(profile, demo);
            var namespaces = await ResolveNamespacesAsync(client, ns, ct);
            var events = limit.HasValue
                ? await client.GetEventsAsync(namespaces, limit.Value, ct)
                : await client.GetEventsAsync(namespaces, involvedObject, ct);
            return Results.Ok(events);
        });

        // ── Helm ───────────────────────────────────────────────────────────────

        app.MapGet("/api/aks/{ns}/helm-releases", async (string ns, ProfileRepository profile, DemoModeService demo, CancellationToken ct) =>
        {
            var client = GetClient(profile, demo);
            var namespaces = await ResolveNamespacesAsync(client, ns, ct);
            var releases = await client.GetHelmReleasesAsync(namespaces, ct);
            return Results.Ok(releases);
        });

        app.MapGet("/api/aks/{ns}/helm-releases/{release}/history", async (string ns, string release, ProfileRepository profile, DemoModeService demo, CancellationToken ct) =>
        {
            var client = GetClient(profile, demo);
            var history = await client.GetHelmReleaseHistoryAsync(ns, release, ct);
            return Results.Ok(history);
        });

        app.MapGet("/api/aks/{ns}/helm-releases/{release}/values", async (string ns, string release, ProfileRepository profile, DemoModeService demo, CancellationToken ct) =>
        {
            var client = GetClient(profile, demo);
            var values = await client.GetHelmReleaseValuesAsync(ns, release, ct);
            return Results.Text(values, "text/yaml");
        });

        // ── ConfigMaps & Secrets ───────────────────────────────────────────────

        app.MapGet("/api/aks/{ns}/configmaps", async (string ns, ProfileRepository profile, DemoModeService demo, CancellationToken ct) =>
        {
            var client = GetClient(profile, demo);
            var namespaces = await ResolveNamespacesAsync(client, ns, ct);
            var configMaps = await client.GetConfigMapsAsync(namespaces, ct);
            return Results.Ok(configMaps);
        });

        app.MapGet("/api/aks/{ns}/secrets", async (string ns, ProfileRepository profile, DemoModeService demo, CancellationToken ct) =>
        {
            var client = GetClient(profile, demo);
            var namespaces = await ResolveNamespacesAsync(client, ns, ct);
            var (secrets, _) = await client.GetSecretsAndHelmReleasesAsync(namespaces, ct);
            return Results.Ok(secrets);
        });

        app.MapGet("/api/aks/{ns}/secrets/{name}/values", async (string ns, string name, ProfileRepository profile, DemoModeService demo, CancellationToken ct) =>
        {
            var client = GetClient(profile, demo);
            var values = await client.GetSecretValuesAsync(ns, name, ct);
            return Results.Ok(values);
        });

        // ── YAML ───────────────────────────────────────────────────────────────

        app.MapGet("/api/aks/{ns}/yaml/{kind}/{name}", async (string ns, string kind, string name, ProfileRepository profile, DemoModeService demo, CancellationToken ct) =>
        {
            var client = GetClient(profile, demo);
            var yaml = await client.GetResourceYamlAsync(ns, kind, name, ct);
            return Results.Text(yaml, "text/yaml");
        });

        // ── Actions ────────────────────────────────────────────────────────────

        app.MapPost("/api/aks/{ns}/deployments/{name}/restart", async (string ns, string name, ProfileRepository profile, DemoModeService demo, CancellationToken ct) =>
        {
            var client = GetClient(profile, demo);
            await client.RestartDeploymentAsync(ns, name, ct);
            return Results.Ok();
        });

        app.MapPost("/api/aks/{ns}/deployments/{name}/scale", async (string ns, string name, int replicas, ProfileRepository profile, DemoModeService demo, CancellationToken ct) =>
        {
            var client = GetClient(profile, demo);
            await client.ScaleDeploymentAsync(ns, name, replicas, ct);
            return Results.Ok();
        });

        app.MapPost("/api/aks/{ns}/pods/{name}/delete", async (string ns, string name, ProfileRepository profile, DemoModeService demo, CancellationToken ct) =>
        {
            var client = GetClient(profile, demo);
            await client.DeletePodAsync(ns, name, ct);
            return Results.Ok();
        });

        app.MapPost("/api/aks/{ns}/statefulsets/{name}/restart", async (string ns, string name, ProfileRepository profile, DemoModeService demo, CancellationToken ct) =>
        {
            var client = GetClient(profile, demo);
            await client.RestartStatefulSetAsync(ns, name, ct);
            return Results.Ok();
        });

        app.MapPost("/api/aks/{ns}/statefulsets/{name}/scale", async (string ns, string name, int replicas, ProfileRepository profile, DemoModeService demo, CancellationToken ct) =>
        {
            var client = GetClient(profile, demo);
            await client.ScaleStatefulSetAsync(ns, name, replicas, ct);
            return Results.Ok();
        });

        app.MapDelete("/api/aks/{ns}/ingresses/{name}", async (string ns, string name, ProfileRepository profile, DemoModeService demo, CancellationToken ct) =>
        {
            var client = GetClient(profile, demo);
            await client.DeleteIngressAsync(ns, name, ct);
            return Results.Ok();
        });

        // ── HPA ────────────────────────────────────────────────────────────────

        app.MapGet("/api/aks/{ns}/hpas", async (string ns, ProfileRepository profile, DemoModeService demo, CancellationToken ct) =>
        {
            var client = GetClient(profile, demo);
            var namespaces = await ResolveNamespacesAsync(client, ns, ct);
            var hpas = await client.GetHpasAsync(namespaces, ct);
            return Results.Ok(hpas);
        });

        // ── Jobs & CronJobs ────────────────────────────────────────────────────

        app.MapGet("/api/aks/{ns}/cronjobs", async (string ns, ProfileRepository profile, DemoModeService demo, CancellationToken ct) =>
        {
            var client = GetClient(profile, demo);
            var namespaces = await ResolveNamespacesAsync(client, ns, ct);
            var cronJobs = await client.GetCronJobsAsync(namespaces, ct);
            return Results.Ok(cronJobs);
        });

        app.MapGet("/api/aks/{ns}/jobs", async (string ns, ProfileRepository profile, DemoModeService demo, CancellationToken ct) =>
        {
            var client = GetClient(profile, demo);
            var namespaces = await ResolveNamespacesAsync(client, ns, ct);
            var jobs = await client.GetJobsAsync(namespaces, ct);
            return Results.Ok(jobs);
        });

        // ── Gateway API ────────────────────────────────────────────────────────

        app.MapGet("/api/aks/{ns}/httproutes", async (string ns, ProfileRepository profile, DemoModeService demo, CancellationToken ct) =>
        {
            try
            {
                var client = GetClient(profile, demo);
                var namespaces = await ResolveNamespacesAsync(client, ns, ct);
                var routes = await client.GetHttpRoutesAsync(namespaces, ct);
                return Results.Ok(routes);
            }
            catch
            {
                return Results.Ok(Array.Empty<object>());
            }
        });

        app.MapDelete("/api/aks/{ns}/httproutes/{name}", async (string ns, string name, ProfileRepository profile, DemoModeService demo, CancellationToken ct) =>
        {
            var client = GetClient(profile, demo);
            await client.DeleteHttpRouteAsync(ns, name, ct);
            return Results.Ok();
        });

        app.MapGet("/api/aks/gatewayclasses", async (ProfileRepository profile, DemoModeService demo, CancellationToken ct) =>
        {
            var client = GetClient(profile, demo);
            var classes = await client.GetGatewayClassesAsync(ct);
            return Results.Ok(classes);
        });

        app.MapGet("/api/aks/{ns}/gateways", async (string ns, ProfileRepository profile, DemoModeService demo, CancellationToken ct) =>
        {
            var client = GetClient(profile, demo);
            var namespaces = await ResolveNamespacesAsync(client, ns, ct);
            var gateways = await client.GetGatewaysAsync(namespaces, ct);
            return Results.Ok(gateways);
        });

        // ── Container details ──────────────────────────────────────────────────

        app.MapGet("/api/aks/{ns}/pods/{podName}/containers", async (string ns, string podName, ProfileRepository profile, DemoModeService demo, CancellationToken ct) =>
        {
            var client = GetClient(profile, demo);
            var containers = await client.GetContainerDetailsAsync(ns, podName, ct);
            return Results.Ok(containers);
        });

        // ── Pod metrics ────────────────────────────────────────────────────────

        app.MapGet("/api/aks/{ns}/pod-metrics", async (string ns, ProfileRepository profile, DemoModeService demo, CancellationToken ct) =>
        {
            var client = GetClient(profile, demo);
            var namespaces = await ResolveNamespacesAsync(client, ns, ct);
            var metrics = await client.GetPodMetricsAsync(namespaces, ct);
            return Results.Ok(metrics);
        });

        // ── Pod Logs ───────────────────────────────────────────────────────────

        app.MapGet("/api/aks/{ns}/pods/{podName}/logs", async (string ns, string podName, string? container, int tail, ProfileRepository profile, DemoModeService demo, CancellationToken ct) =>
        {
            var client = GetClient(profile, demo);
            var opts = new LogStreamOptions { TailLines = tail, Follow = false };
            var lines = new List<string>(tail > 0 ? tail : 100);
            await foreach (var line in client.StreamPodLogsAsync(ns, podName, container ?? "", opts, ct))
            {
                lines.Add(line);
                if (tail > 0 && lines.Count >= tail) break;
            }
            return Results.Text(string.Join('\n', lines), "text/plain");
        });

        app.MapGet("/api/aks/{ns}/pods/{podName}/logs/stream", async (HttpContext ctx, string ns, string podName, string? container, int tail, bool follow, int? sinceSeconds, bool previousContainer, string? filter, ProfileRepository profile, DemoModeService demo, CancellationToken ct) =>
        {
            var client = GetClient(profile, demo);
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
