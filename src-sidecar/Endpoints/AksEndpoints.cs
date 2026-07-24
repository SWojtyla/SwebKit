using SwebKit.Core.Abstractions;
using SwebKit.Core.Configuration;
using SwebKit.Core.Models;
using SwebKit.Kubernetes.AksClient;

namespace SwebKit.Sidecar.Endpoints;

public static class AksEndpoints
{
    private static IAksClient? _client;
    private static readonly Lock _clientLock = new();

    private static IAksClient GetClient(ProfileRepository profile)
    {
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

        app.MapGet("/api/aks/test", async (ProfileRepository profile) =>
        {
            try
            {
                var client = GetClient(profile);
                var ok = await client.TestConnectionAsync();
                return Results.Ok(new { connected = ok });
            }
            catch (Exception ex)
            {
                return Results.Ok(new { connected = false, error = ex.Message });
            }
        });

        app.MapGet("/api/aks/contexts", (ProfileRepository profile) =>
        {
            var aksConfig = profile.GetProfileData().Config.AksConfig;
            var contexts = KubernetesAksClient.ReadContextsFromKubeconfig(aksConfig?.KubeconfigPath);
            return Results.Ok(contexts);
        });

        app.MapGet("/api/aks/namespaces", async (ProfileRepository profile) =>
        {
            var client = GetClient(profile);
            var namespaces = await client.GetNamespacesAsync();
            return Results.Ok(namespaces);
        });

        // ── Workloads ──────────────────────────────────────────────────────────

        app.MapGet("/api/aks/{ns}/deployments", async (string ns, ProfileRepository profile) =>
        {
            var client = GetClient(profile);
            var deployments = await client.GetDeploymentsAsync(ns);
            return Results.Ok(deployments);
        });

        app.MapGet("/api/aks/{ns}/pods", async (string ns, string? labelSelector, ProfileRepository profile) =>
        {
            var client = GetClient(profile);
            var pods = await client.GetPodsAsync(ns, labelSelector);
            return Results.Ok(pods);
        });

        app.MapGet("/api/aks/{ns}/statefulsets", async (string ns, ProfileRepository profile) =>
        {
            var client = GetClient(profile);
            var sts = await client.GetStatefulSetsAsync(ns);
            return Results.Ok(sts);
        });

        app.MapGet("/api/aks/{ns}/services", async (string ns, ProfileRepository profile) =>
        {
            var client = GetClient(profile);
            var services = await client.GetServicesAsync(ns);
            return Results.Ok(services);
        });

        app.MapGet("/api/aks/{ns}/ingresses", async (string ns, ProfileRepository profile) =>
        {
            var client = GetClient(profile);
            var ingresses = await client.GetIngressesAsync(ns);
            return Results.Ok(ingresses);
        });

        app.MapGet("/api/aks/{ns}/events", async (string ns, int? limit, string? involvedObject, ProfileRepository profile) =>
        {
            var client = GetClient(profile);
            var events = limit.HasValue
                ? await client.GetEventsAsync(ns, limit.Value)
                : await client.GetEventsAsync(ns, involvedObject);
            return Results.Ok(events);
        });

        // ── Helm ───────────────────────────────────────────────────────────────

        app.MapGet("/api/aks/{ns}/helm-releases", async (string ns, ProfileRepository profile) =>
        {
            var client = GetClient(profile);
            var releases = await client.GetHelmReleasesAsync(ns);
            return Results.Ok(releases);
        });

        app.MapGet("/api/aks/{ns}/helm-releases/{release}/history", async (string ns, string release, ProfileRepository profile) =>
        {
            var client = GetClient(profile);
            var history = await client.GetHelmReleaseHistoryAsync(ns, release);
            return Results.Ok(history);
        });

        app.MapGet("/api/aks/{ns}/helm-releases/{release}/values", async (string ns, string release, ProfileRepository profile) =>
        {
            var client = GetClient(profile);
            var values = await client.GetHelmReleaseValuesAsync(ns, release);
            return Results.Text(values, "text/yaml");
        });

        // ── ConfigMaps & Secrets ───────────────────────────────────────────────

        app.MapGet("/api/aks/{ns}/configmaps", async (string ns, ProfileRepository profile) =>
        {
            var client = GetClient(profile);
            var configMaps = await client.GetConfigMapsAsync(ns);
            return Results.Ok(configMaps);
        });

        app.MapGet("/api/aks/{ns}/secrets", async (string ns, ProfileRepository profile) =>
        {
            var client = GetClient(profile);
            var (secrets, _) = await client.GetSecretsAndHelmReleasesAsync(ns);
            return Results.Ok(secrets);
        });

        app.MapGet("/api/aks/{ns}/secrets/{name}/values", async (string ns, string name, ProfileRepository profile) =>
        {
            var client = GetClient(profile);
            var values = await client.GetSecretValuesAsync(ns, name);
            return Results.Ok(values);
        });

        // ── YAML ───────────────────────────────────────────────────────────────

        app.MapGet("/api/aks/{ns}/yaml/{kind}/{name}", async (string ns, string kind, string name, ProfileRepository profile) =>
        {
            var client = GetClient(profile);
            var yaml = await client.GetResourceYamlAsync(ns, kind, name);
            return Results.Text(yaml, "text/yaml");
        });

        // ── Actions ────────────────────────────────────────────────────────────

        app.MapPost("/api/aks/{ns}/deployments/{name}/restart", async (string ns, string name, ProfileRepository profile) =>
        {
            var client = GetClient(profile);
            await client.RestartDeploymentAsync(ns, name);
            return Results.Ok();
        });

        app.MapPost("/api/aks/{ns}/deployments/{name}/scale", async (string ns, string name, int replicas, ProfileRepository profile) =>
        {
            var client = GetClient(profile);
            await client.ScaleDeploymentAsync(ns, name, replicas);
            return Results.Ok();
        });

        app.MapPost("/api/aks/{ns}/pods/{name}/delete", async (string ns, string name, ProfileRepository profile) =>
        {
            var client = GetClient(profile);
            await client.DeletePodAsync(ns, name);
            return Results.Ok();
        });

        // ── HPA ────────────────────────────────────────────────────────────────

        app.MapGet("/api/aks/{ns}/hpas", async (string ns, ProfileRepository profile) =>
        {
            var client = GetClient(profile);
            var hpas = await client.GetHpasAsync(ns);
            return Results.Ok(hpas);
        });

        // ── Jobs & CronJobs ────────────────────────────────────────────────────

        app.MapGet("/api/aks/{ns}/cronjobs", async (string ns, ProfileRepository profile) =>
        {
            var client = GetClient(profile);
            var cronJobs = await client.GetCronJobsAsync(ns);
            return Results.Ok(cronJobs);
        });

        app.MapGet("/api/aks/{ns}/jobs", async (string ns, ProfileRepository profile) =>
        {
            var client = GetClient(profile);
            var jobs = await client.GetJobsAsync(ns);
            return Results.Ok(jobs);
        });

        // ── Container details ──────────────────────────────────────────────────

        app.MapGet("/api/aks/{ns}/pods/{podName}/containers", async (string ns, string podName, ProfileRepository profile) =>
        {
            var client = GetClient(profile);
            var containers = await client.GetContainerDetailsAsync(ns, podName);
            return Results.Ok(containers);
        });

        // ── Pod metrics ────────────────────────────────────────────────────────

        app.MapGet("/api/aks/{ns}/pod-metrics", async (string ns, ProfileRepository profile) =>
        {
            var client = GetClient(profile);
            var metrics = await client.GetPodMetricsAsync(ns);
            return Results.Ok(metrics);
        });
    }
}
