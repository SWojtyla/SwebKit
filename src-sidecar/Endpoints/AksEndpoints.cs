using SwebKit.Core.Abstractions;
using SwebKit.Core.Configuration;
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

    public static void MapAksEndpoints(this WebApplication app)
    {
        // ── Connection ─────────────────────────────────────────────────────────

        app.MapGet("/api/aks/test", async (ProfileRepository profile, DemoModeService demo) =>
        {
            try
            {
                var client = GetClient(profile, demo);
                var ok = await client.TestConnectionAsync();
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

        app.MapGet("/api/aks/namespaces", async (ProfileRepository profile, DemoModeService demo) =>
        {
            var client = GetClient(profile, demo);
            var namespaces = await client.GetNamespacesAsync();
            return Results.Ok(namespaces);
        });

        // ── Workloads ──────────────────────────────────────────────────────────

        app.MapGet("/api/aks/{ns}/deployments", async (string ns, ProfileRepository profile, DemoModeService demo) =>
        {
            var client = GetClient(profile, demo);
            var deployments = await client.GetDeploymentsAsync(ns);
            return Results.Ok(deployments);
        });

        app.MapGet("/api/aks/{ns}/pods", async (string ns, string? labelSelector, ProfileRepository profile, DemoModeService demo) =>
        {
            var client = GetClient(profile, demo);
            var pods = await client.GetPodsAsync(ns, labelSelector);
            return Results.Ok(pods);
        });

        app.MapGet("/api/aks/{ns}/statefulsets", async (string ns, ProfileRepository profile, DemoModeService demo) =>
        {
            var client = GetClient(profile, demo);
            var sts = await client.GetStatefulSetsAsync(ns);
            return Results.Ok(sts);
        });

        app.MapGet("/api/aks/{ns}/services", async (string ns, ProfileRepository profile, DemoModeService demo) =>
        {
            var client = GetClient(profile, demo);
            var services = await client.GetServicesAsync(ns);
            return Results.Ok(services);
        });

        app.MapGet("/api/aks/{ns}/ingresses", async (string ns, ProfileRepository profile, DemoModeService demo) =>
        {
            var client = GetClient(profile, demo);
            var ingresses = await client.GetIngressesAsync(ns);
            return Results.Ok(ingresses);
        });

        app.MapGet("/api/aks/{ns}/events", async (string ns, int? limit, string? involvedObject, ProfileRepository profile, DemoModeService demo) =>
        {
            var client = GetClient(profile, demo);
            var events = limit.HasValue
                ? await client.GetEventsAsync(ns, limit.Value)
                : await client.GetEventsAsync(ns, involvedObject);
            return Results.Ok(events);
        });

        // ── Helm ───────────────────────────────────────────────────────────────

        app.MapGet("/api/aks/{ns}/helm-releases", async (string ns, ProfileRepository profile, DemoModeService demo) =>
        {
            var client = GetClient(profile, demo);
            var releases = await client.GetHelmReleasesAsync(ns);
            return Results.Ok(releases);
        });

        app.MapGet("/api/aks/{ns}/helm-releases/{release}/history", async (string ns, string release, ProfileRepository profile, DemoModeService demo) =>
        {
            var client = GetClient(profile, demo);
            var history = await client.GetHelmReleaseHistoryAsync(ns, release);
            return Results.Ok(history);
        });

        app.MapGet("/api/aks/{ns}/helm-releases/{release}/values", async (string ns, string release, ProfileRepository profile, DemoModeService demo) =>
        {
            var client = GetClient(profile, demo);
            var values = await client.GetHelmReleaseValuesAsync(ns, release);
            return Results.Text(values, "text/yaml");
        });

        // ── ConfigMaps & Secrets ───────────────────────────────────────────────

        app.MapGet("/api/aks/{ns}/configmaps", async (string ns, ProfileRepository profile, DemoModeService demo) =>
        {
            var client = GetClient(profile, demo);
            var configMaps = await client.GetConfigMapsAsync(ns);
            return Results.Ok(configMaps);
        });

        app.MapGet("/api/aks/{ns}/secrets", async (string ns, ProfileRepository profile, DemoModeService demo) =>
        {
            var client = GetClient(profile, demo);
            var (secrets, _) = await client.GetSecretsAndHelmReleasesAsync(ns);
            return Results.Ok(secrets);
        });

        app.MapGet("/api/aks/{ns}/secrets/{name}/values", async (string ns, string name, ProfileRepository profile, DemoModeService demo) =>
        {
            var client = GetClient(profile, demo);
            var values = await client.GetSecretValuesAsync(ns, name);
            return Results.Ok(values);
        });

        // ── YAML ───────────────────────────────────────────────────────────────

        app.MapGet("/api/aks/{ns}/yaml/{kind}/{name}", async (string ns, string kind, string name, ProfileRepository profile, DemoModeService demo) =>
        {
            var client = GetClient(profile, demo);
            var yaml = await client.GetResourceYamlAsync(ns, kind, name);
            return Results.Text(yaml, "text/yaml");
        });

        // ── Actions ────────────────────────────────────────────────────────────

        app.MapPost("/api/aks/{ns}/deployments/{name}/restart", async (string ns, string name, ProfileRepository profile, DemoModeService demo) =>
        {
            var client = GetClient(profile, demo);
            await client.RestartDeploymentAsync(ns, name);
            return Results.Ok();
        });

        app.MapPost("/api/aks/{ns}/deployments/{name}/scale", async (string ns, string name, int replicas, ProfileRepository profile, DemoModeService demo) =>
        {
            var client = GetClient(profile, demo);
            await client.ScaleDeploymentAsync(ns, name, replicas);
            return Results.Ok();
        });

        app.MapPost("/api/aks/{ns}/pods/{name}/delete", async (string ns, string name, ProfileRepository profile, DemoModeService demo) =>
        {
            var client = GetClient(profile, demo);
            await client.DeletePodAsync(ns, name);
            return Results.Ok();
        });

        app.MapPost("/api/aks/{ns}/statefulsets/{name}/restart", async (string ns, string name, ProfileRepository profile, DemoModeService demo) =>
        {
            var client = GetClient(profile, demo);
            await client.RestartStatefulSetAsync(ns, name);
            return Results.Ok();
        });

        app.MapPost("/api/aks/{ns}/statefulsets/{name}/scale", async (string ns, string name, int replicas, ProfileRepository profile, DemoModeService demo) =>
        {
            var client = GetClient(profile, demo);
            await client.ScaleStatefulSetAsync(ns, name, replicas);
            return Results.Ok();
        });

        app.MapDelete("/api/aks/{ns}/ingresses/{name}", async (string ns, string name, ProfileRepository profile, DemoModeService demo) =>
        {
            var client = GetClient(profile, demo);
            await client.DeleteIngressAsync(ns, name);
            return Results.Ok();
        });

        // ── HPA ────────────────────────────────────────────────────────────────

        app.MapGet("/api/aks/{ns}/hpas", async (string ns, ProfileRepository profile, DemoModeService demo) =>
        {
            var client = GetClient(profile, demo);
            var hpas = await client.GetHpasAsync(ns);
            return Results.Ok(hpas);
        });

        // ── Jobs & CronJobs ────────────────────────────────────────────────────

        app.MapGet("/api/aks/{ns}/cronjobs", async (string ns, ProfileRepository profile, DemoModeService demo) =>
        {
            var client = GetClient(profile, demo);
            var cronJobs = await client.GetCronJobsAsync(ns);
            return Results.Ok(cronJobs);
        });

        app.MapGet("/api/aks/{ns}/jobs", async (string ns, ProfileRepository profile, DemoModeService demo) =>
        {
            var client = GetClient(profile, demo);
            var jobs = await client.GetJobsAsync(ns);
            return Results.Ok(jobs);
        });

        // ── Gateway API ────────────────────────────────────────────────────────

        app.MapGet("/api/aks/{ns}/httproutes", async (string ns, ProfileRepository profile, DemoModeService demo) =>
        {
            try
            {
                var client = GetClient(profile, demo);
                var routes = await client.GetHttpRoutesAsync(ns);
                return Results.Ok(routes);
            }
            catch
            {
                return Results.Ok(Array.Empty<object>());
            }
        });

        app.MapDelete("/api/aks/{ns}/httproutes/{name}", async (string ns, string name, ProfileRepository profile, DemoModeService demo) =>
        {
            var client = GetClient(profile, demo);
            await client.DeleteHttpRouteAsync(ns, name);
            return Results.Ok();
        });

        app.MapGet("/api/aks/gatewayclasses", async (ProfileRepository profile, DemoModeService demo) =>
        {
            var client = GetClient(profile, demo);
            var classes = await client.GetGatewayClassesAsync();
            return Results.Ok(classes);
        });

        app.MapGet("/api/aks/{ns}/gateways", async (string ns, ProfileRepository profile, DemoModeService demo) =>
        {
            var client = GetClient(profile, demo);
            var gateways = await client.GetGatewaysAsync(ns);
            return Results.Ok(gateways);
        });

        // ── Container details ──────────────────────────────────────────────────

        app.MapGet("/api/aks/{ns}/pods/{podName}/containers", async (string ns, string podName, ProfileRepository profile, DemoModeService demo) =>
        {
            var client = GetClient(profile, demo);
            var containers = await client.GetContainerDetailsAsync(ns, podName);
            return Results.Ok(containers);
        });

        // ── Pod metrics ────────────────────────────────────────────────────────

        app.MapGet("/api/aks/{ns}/pod-metrics", async (string ns, ProfileRepository profile, DemoModeService demo) =>
        {
            var client = GetClient(profile, demo);
            var metrics = await client.GetPodMetricsAsync(ns);
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
