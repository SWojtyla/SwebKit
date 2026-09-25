using SwebKit.Sidecar.Services;
using SwebKit.Core.Abstractions;
using SwebKit.Core.Configuration;
using SwebKit.Core.Domain;
using SwebKit.Core.Models;
using SwebKit.Core.Services;
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

    /// <summary>Handler body for the Deployments list endpoint, extracted so it's unit testable against a fake pool.
    /// An explicit <paramref name="context"/> resolves a client for that kubeconfig context — the
    /// Map picker's cross-cluster add flow uses it — while omitting it falls back to the configured
    /// context as before.</summary>
    internal static async Task<IResult> GetDeploymentsAsync(string ns, string? context, ProfileRepository profile, DemoModeService demo, IMonitoringConnectionPool pool, CancellationToken ct)
    {
        var client = GetClient(pool, string.IsNullOrWhiteSpace(context) ? null : context);
        var namespaces = await ResolveNamespacesAsync(client, ns, ct);
        var deployments = await client.GetDeploymentsAsync(namespaces, ct);
        return Results.Ok(deployments);
    }

    /// <summary>Handler body for the Pods list endpoint, extracted so it's unit testable against a fake pool.</summary>
    internal static async Task<IResult> GetPodsAsync(string ns, string? labelSelector, ProfileRepository profile, DemoModeService demo, IMonitoringConnectionPool pool, CancellationToken ct)
    {
        var client = GetClient(pool);
        var namespaces = await ResolveNamespacesAsync(client, ns, ct);
        var pods = string.IsNullOrWhiteSpace(labelSelector)
            ? await client.GetPodsAsync(namespaces, ct)
            : await client.GetPodsAsync(namespaces, labelSelector, ct);
        return Results.Ok(pods);
    }

    /// <summary>Handler body for the HPA list endpoint, extracted so it's unit testable against a fake pool.</summary>
    internal static async Task<IResult> GetHpasAsync(string ns, ProfileRepository profile, DemoModeService demo, IMonitoringConnectionPool pool, CancellationToken ct)
    {
        var client = GetClient(pool);
        var namespaces = await ResolveNamespacesAsync(client, ns, ct);
        var hpas = await client.GetHpasAsync(namespaces, ct);
        return Results.Ok(hpas);
    }

    /// <summary>
    /// Handler body for the HTTPRoutes list endpoint, extracted so it's unit testable against a fake
    /// pool/client. The client already returns an empty list when the Gateway API CRD isn't installed
    /// (see KubernetesAksClient.ListGatewayApiCustomObjectsAsync) — no need to swallow exceptions here
    /// too. Doing so previously made a real auth/RBAC/connectivity failure indistinguishable from "no
    /// HTTPRoutes exist," which is actively misleading for a debugging tool. Let real errors propagate
    /// to the global exception handler like every other AKS endpoint does.
    /// </summary>
    internal static async Task<IResult> GetHttpRoutesAsync(string ns, ProfileRepository profile, DemoModeService demo, IMonitoringConnectionPool pool, CancellationToken ct)
    {
        var client = GetClient(pool);
        var namespaces = await ResolveNamespacesAsync(client, ns, ct);
        var routes = await client.GetHttpRoutesAsync(namespaces, ct);
        return Results.Ok(routes);
    }

    /// <summary>
    /// Handler body for the Envoy Gateway resources endpoint. The <c>{plural}</c> segment is
    /// validated against <see cref="EnvoyGatewayKinds.PluralToKind"/> — an arbitrary plural must
    /// never reach the CustomObjects API.
    /// </summary>
    internal static async Task<IResult> GetEnvoyResourcesAsync(string ns, string plural, ProfileRepository profile, DemoModeService demo, IMonitoringConnectionPool pool, CancellationToken ct)
    {
        var normalized = plural.ToLowerInvariant();
        if (!EnvoyGatewayKinds.PluralToKind.ContainsKey(normalized))
            return ApiErrors.BadRequest($"Unknown Envoy Gateway resource kind '{plural}'");

        var client = GetClient(pool);
        var namespaces = await ResolveNamespacesAsync(client, ns, ct);
        var resources = await client.GetEnvoyResourcesAsync(namespaces, normalized, ct);
        return Results.Ok(resources);
    }

    /// <summary>
    /// Handler body for the CronJob trigger endpoint, extracted so it's unit testable against a fake
    /// pool/client. Returns the created Job names so the UI can surface them in its success toast —
    /// the generated name is what the operator then looks for on the Jobs tab.
    /// </summary>
    internal static async Task<IResult> TriggerCronJobAsync(string ns, string name, ProfileRepository profile, DemoModeService demo, IMonitoringConnectionPool pool, CancellationToken ct)
    {
        var client = GetClient(pool);
        var namespaces = await ResolveNamespacesAsync(client, ns, ct);
        var jobNames = await Task.WhenAll(namespaces.Select(n => client.TriggerCronJobAsync(n, name, ct)));
        return Results.Ok(new { jobNames });
    }

    /// <summary>
    /// Handler body for the CronJob schedule-edit endpoint, extracted so the validation is unit
    /// testable. The shape check here is deliberately shallow (5 fields or a supported @macro) —
    /// the API server remains the authority on whether an expression is valid and rejects bad
    /// ones with a descriptive 422 that reaches the UI.
    /// </summary>
    internal static async Task<IResult> SetCronJobScheduleAsync(string ns, string name, SetCronJobScheduleRequest dto, ProfileRepository profile, DemoModeService demo, IMonitoringConnectionPool pool, CancellationToken ct)
    {
        var schedule = dto.Schedule?.Trim() ?? string.Empty;
        if (schedule.Length == 0)
            return ApiErrors.BadRequest("Schedule cannot be empty");
        if (!schedule.StartsWith('@') && schedule.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length != 5)
            return ApiErrors.BadRequest("Schedule must be a 5-field cron expression or an @macro (@hourly, @daily, @weekly, @monthly, @yearly)");

        var client = GetClient(pool);
        var namespaces = await ResolveNamespacesAsync(client, ns, ct);
        await Task.WhenAll(namespaces.Select(n => client.SetCronJobScheduleAsync(n, name, schedule, ct)));
        return Results.Ok();
    }

    /// <summary>Handler body for the connection-test endpoint, extracted so the error-sanitization
    /// behavior (never return a raw exception message) is unit testable.</summary>
    internal static async Task<IResult> TestConnectionAsync(ProfileRepository profile, DemoModeService demo, IMonitoringConnectionPool pool, ILogger<Program> logger, CancellationToken ct)
    {
        try
        {
            var client = GetClient(pool);
            var ok = await client.TestConnectionAsync(ct);
            return Results.Ok(new { connected = ok });
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "AKS connection test failed");
            return Results.Ok(new { connected = false, error = ConnectionTestError.Describe(ex) });
        }
    }

    /// <summary>
    /// Handler body for the contexts list endpoint, extracted so the demo-mode branch is unit
    /// testable. Demo contexts come from <see cref="DemoAksClient"/> via the pool — reading the
    /// real kubeconfig in demo mode either yields nothing (no kubeconfig on the machine) or
    /// leaks the user's real clusters into a demo session.
    /// </summary>
    internal static async Task<IResult> GetContextsAsync(ProfileRepository profile, DemoModeService demo, IMonitoringConnectionPool pool, CancellationToken ct)
    {
        if (demo.IsDemoMode)
            return Results.Ok(await GetClient(pool).GetContextsAsync(ct));

        var aksConfig = profile.GetProfileData().Config.AksConfig;
        var contexts = KubernetesAksClient.ReadContextsFromKubeconfig(aksConfig?.KubeconfigPath);
        return Results.Ok(contexts);
    }

    /// <summary>
    /// Handler body for the context-switch endpoint, extracted so it's unit testable. The target
    /// context is connection-tested <em>before</em> the profile is persisted: a failed switch must
    /// not leave <c>KubeconfigContext</c> pointing at an unreachable context (the next launch would
    /// restore onto it). The client resolves through the context-keyed pool, so a previously-visited
    /// context reuses its warm client — no blanket invalidation, which used to destroy every pooled
    /// client's namespace cache on each switch.
    /// </summary>
    internal static async Task<IResult> SetContextAsync(SetContextRequest request, ProfileRepository profile, DemoModeService demo, IMonitoringConnectionPool pool, ILogger<Program> logger, CancellationToken ct)
    {
        bool connected;
        try
        {
            var client = GetClient(pool, request.Context);
            connected = await client.TestConnectionAsync(ct);
        }
        catch (InvalidOperationException ex)
        {
            // GetClient's "AKS is not configured" message is already user-facing — don't run it
            // through ConnectionTestError.Describe, which rewrites raw exception text.
            return Results.Ok(new { connected = false, context = request.Context, error = ex.Message });
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "AKS context switch connection test failed for {Context}", request.Context);
            return Results.Ok(new { connected = false, context = request.Context, error = ConnectionTestError.Describe(ex) });
        }

        if (!connected)
            return Results.Ok(new { connected = false, context = request.Context, error = "The cluster did not respond to the connection test." });

        // Demo context names must not reach the real profile — they don't exist in the user's
        // kubeconfig and would restore as a broken context after demo mode ends.
        if (!demo.IsDemoMode)
        {
            var data = profile.GetProfileData();
            data.Config.AksConfig ??= new AksConfig();
            data.Config.AksConfig.KubeconfigContext = request.Context;

            if (request.DefaultNamespace is not null)
                data.Config.AksConfig.DefaultNamespace = request.DefaultNamespace;

            await profile.SaveAsync();
        }

        return Results.Ok(new { connected = true, context = request.Context });
    }

    /// <summary>
    /// Handler body for the namespaces list endpoint, extracted so it's unit testable against a
    /// fake pool. An explicit <paramref name="context"/> resolves a client for that kubeconfig
    /// context — monitoring rules can be pinned to a different cluster than the profile's
    /// configured one — while omitting it falls back to the configured context as before.
    /// </summary>
    internal static async Task<IResult> GetNamespacesAsync(string? context, ProfileRepository profile, DemoModeService demo, IMonitoringConnectionPool pool, CancellationToken ct)
    {
        var client = GetClient(pool, string.IsNullOrWhiteSpace(context) ? null : context);
        var namespaces = await client.GetNamespacesAsync(ct);
        return Results.Ok(namespaces);
    }

    public static void MapAksEndpoints(this WebApplication app)
    {
        // ── Connection / context ─────────────────────────────────────────────────

        app.MapGet("/api/aks/test", TestConnectionAsync);

        app.MapGet("/api/aks/contexts", GetContextsAsync);

        app.MapPost("/api/aks/context", SetContextAsync);

        app.MapGet("/api/aks/namespaces", GetNamespacesAsync);

        // ── Workloads ──────────────────────────────────────────────────────────

        app.MapGet("/api/aks/{ns}/deployments", GetDeploymentsAsync);

        app.MapGet("/api/aks/{ns}/pods", GetPodsAsync);

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
            // Values stripped: the list renders key names only, and a namespace's ConfigMap values can
            // run to megabytes that were previously serialized to the browser on every auto-refresh
            // tick. `/configmaps/{name}/values` serves the detail panel, mirroring Secrets.
            return Results.Ok(configMaps.Select(cm => new ConfigMapInfo
            {
                Name = cm.Name,
                Namespace = cm.Namespace,
                // Falls back to the value dictionary's keys so a client that only fills `Data` (the demo
                // client, and the interface's own default) still produces a usable list.
                Keys = cm.Keys.Count > 0 ? cm.Keys : [.. cm.Data.Keys],
                DataSizeChars = cm.DataSizeChars > 0 ? cm.DataSizeChars : cm.Data.Values.Sum(v => v.Length),
                Labels = cm.Labels,
            }));
        });

        app.MapGet("/api/aks/{ns}/configmaps/{name}/values", async (string ns, string name, ProfileRepository profile, DemoModeService demo, IMonitoringConnectionPool pool, CancellationToken ct) =>
        {
            var client = GetClient(pool);
            var values = await client.GetConfigMapValuesAsync(ns, name, ct);
            return Results.Ok(values);
        });

        app.MapGet("/api/aks/{ns}/secrets", async (string ns, ProfileRepository profile, DemoModeService demo, IMonitoringConnectionPool pool, CancellationToken ct) =>
        {
            var client = GetClient(pool);
            var namespaces = await ResolveNamespacesAsync(client, ns, ct);
            // Deliberately not the combined Secrets+Helm call: this endpoint discarded the Helm half
            // while still paying to transfer every Helm release Secret's gzipped manifest. The
            // dedicated call excludes them at the API server. `/helm-releases` has its own endpoint.
            var secrets = await client.GetSecretsAsync(namespaces, ct);
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

        app.MapGet("/api/aks/{ns}/hpas", GetHpasAsync);

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

        // ── KEDA ScaledJobs ──────────────────────────────────────────────────

        app.MapGet("/api/aks/{ns}/scaledjobs", async (string ns, ProfileRepository profile, DemoModeService demo, IMonitoringConnectionPool pool, CancellationToken ct) =>
        {
            var client = GetClient(pool);
            var namespaces = await ResolveNamespacesAsync(client, ns, ct);
            var scaledJobs = await client.GetScaledJobsAsync(namespaces, ct);
            return Results.Ok(scaledJobs);
        });

        app.MapPost("/api/aks/{ns}/scaledjobs/{name}/scaling-enabled", async (string ns, string name, SetScalingEnabledRequest dto, ProfileRepository profile, DemoModeService demo, IMonitoringConnectionPool pool, CancellationToken ct) =>
        {
            var client = GetClient(pool);
            var namespaces = await ResolveNamespacesAsync(client, ns, ct);
            await Task.WhenAll(namespaces.Select(n => client.SetScaledJobScalingEnabledAsync(n, name, dto.Enabled, ct)));
            return Results.Ok();
        });

        app.MapPost("/api/aks/{ns}/scaledjobs/{name}/scale", async (string ns, string name, ScaleHpaRequest dto, ProfileRepository profile, DemoModeService demo, IMonitoringConnectionPool pool, CancellationToken ct) =>
        {
            var client = GetClient(pool);
            var namespaces = await ResolveNamespacesAsync(client, ns, ct);
            await Task.WhenAll(namespaces.Select(n => client.ScaleScaledJobAsync(n, name, dto.MinReplicas, dto.MaxReplicas, ct)));
            return Results.Ok();
        });

        app.MapDelete("/api/aks/{ns}/scaledjobs/{name}", async (string ns, string name, ProfileRepository profile, DemoModeService demo, IMonitoringConnectionPool pool, CancellationToken ct) =>
        {
            var client = GetClient(pool);
            var namespaces = await ResolveNamespacesAsync(client, ns, ct);
            await Task.WhenAll(namespaces.Select(n => client.DeleteScaledJobAsync(n, name, ct)));
            return Results.NoContent();
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

        app.MapPost("/api/aks/{ns}/cronjobs/{name}/trigger", TriggerCronJobAsync);

        app.MapPost("/api/aks/{ns}/cronjobs/{name}/schedule", SetCronJobScheduleAsync);

        app.MapGet("/api/aks/{ns}/jobs", async (string ns, ProfileRepository profile, DemoModeService demo, IMonitoringConnectionPool pool, CancellationToken ct) =>
        {
            var client = GetClient(pool);
            var namespaces = await ResolveNamespacesAsync(client, ns, ct);
            var jobs = await client.GetJobsAsync(namespaces, ct);
            return Results.Ok(jobs);
        });

        // ── Gateway API ────────────────────────────────────────────────────────

        app.MapGet("/api/aks/{ns}/httproutes", GetHttpRoutesAsync);

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

        // ── Envoy Gateway ─────────────────────────────────────────────────────

        app.MapGet("/api/aks/{ns}/envoy/{plural}", GetEnvoyResourcesAsync);

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

        // Every bool/int parameter here has a default: a required primitive with none (the
        // shape `previousContainer` had) fails ASP.NET's minimal-API model binding outright
        // with a 400 the instant it's omitted from the query string — before any application
        // code runs — which a caller that reasonably leaves out a flag it doesn't care about
        // will hit silently. That's exactly what broke every multi-pod log request: the
        // client never sent `previousContainer` at all.
        app.MapGet("/api/aks/{ns}/pods/{podName}/logs/stream", (HttpContext ctx, string ns, string podName, string? container, ProfileRepository profile, DemoModeService demo, IMonitoringConnectionPool pool, ILogger<Program> logger, int tail = 0, bool follow = false, int? sinceSeconds = null, bool previousContainer = false, string? filter = null, bool timestamps = false, CancellationToken ct = default) =>
            StreamPodLogsAsync(
                ctx,
                GetClient(pool),
                ns,
                podName,
                container,
                new LogStreamOptions
                {
                    TailLines = tail > 0 ? tail : 100,
                    Follow = follow,
                    SinceSeconds = sinceSeconds,
                    PreviousContainer = previousContainer,
                    Timestamps = timestamps,
                },
                filter,
                ct,
                logger));
    }

    /// <summary>
    /// Handler body for the pod-log SSE endpoint, extracted so it can be unit tested against a
    /// fake client and a <c>DefaultHttpContext</c> without spinning up the ASP.NET pipeline.
    /// </summary>
    /// <remarks>
    /// The framing is a contract: one <c>data:</c> frame per line, terminated by an
    /// <c>event: done</c> frame. The browser <c>EventSource</c> in both log views depends on it,
    /// and <c>web/e2e/aks-ux.spec.ts</c> stubs exactly this shape. A failure that happens before
    /// or during streaming (e.g. the pod no longer exists, an RBAC denial) is framed as
    /// <c>event: stream-error</c> ahead of <c>done</c> instead of being left as an unhandled
    /// exception on an already-<c>text/event-stream</c>-typed response, which is indistinguishable
    /// on the wire from a stream that simply never delivers anything. Named <c>stream-error</c>
    /// rather than the reserved SSE name <c>error</c>: the browser's <c>EventSource</c> dispatches
    /// its own native connection-failure events under the type <c>"error"</c>, so a server frame
    /// named <c>event: error</c> would land on the exact same <c>onerror</c>/<c>addEventListener</c>
    /// listener as a real network failure, as a differently-shaped event object — indistinguishable
    /// without inspecting the event instance.
    /// </remarks>
    internal static async Task StreamPodLogsAsync(
        HttpContext ctx,
        IAksClient client,
        string ns,
        string podName,
        string? container,
        LogStreamOptions opts,
        string? filter,
        CancellationToken ct,
        ILogger? logger = null)
    {
        ctx.Response.ContentType = "text/event-stream";
        ctx.Response.Headers.CacheControl = "no-cache";
        ctx.Response.Headers.Connection = "keep-alive";

        try
        {
            await foreach (var line in client.StreamPodLogsAsync(ns, podName, container ?? "", opts, ct))
            {
                // Match the message, never the timestamp prefix -- otherwise a filter of
                // "2026" matches every line the moment `timestamps` is on.
                if (LogLineTimestamp.MatchesFilter(line, filter))
                {
                    await ctx.Response.WriteAsync($"data: {line}\n\n", ct);
                    await ctx.Response.Body.FlushAsync(ct);
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "Pod log stream failed for {Namespace}/{PodName}", ns, podName);
            var message = ex.Message.Replace("\r", " ").Replace("\n", " ");
            await ctx.Response.WriteAsync($"event: stream-error\ndata: {message}\n\n", ct);
            await ctx.Response.Body.FlushAsync(ct);
        }

        await ctx.Response.WriteAsync("event: done\ndata: \n\n", ct);
        await ctx.Response.Body.FlushAsync(ct);
    }
}

public sealed record SetContextRequest(string Context, string? DefaultNamespace = null);
public sealed record ScaleHpaRequest(int MinReplicas, int MaxReplicas);
public sealed record SetScalingEnabledRequest(bool Enabled);
public sealed record SuspendCronJobRequest(bool Suspend);
public sealed record SetCronJobScheduleRequest(string Schedule);
public sealed record YamlApplyRequest(string Yaml);
public sealed record YamlValidateRequest(string Yaml);
