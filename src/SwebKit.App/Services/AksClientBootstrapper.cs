using Microsoft.Extensions.Logging;
using SwebKit.Core.Abstractions;
using SwebKit.Core.Domain;
using SwebKit.Core.Models;
using SwebKit.Core.Services;

namespace SwebKit.App.Services;

public sealed class AksClientBootstrapper : IAksClientBootstrapper
{
    private readonly IAksClientFactory _factory;
    private readonly DemoAksClient _demoAksClient;
    private readonly ILogger<AksClientBootstrapper> _logger;

    public AksClientBootstrapper(
        IAksClientFactory factory,
        DemoAksClient demoAksClient,
        ILogger<AksClientBootstrapper> logger)
    {
        _factory = factory;
        _demoAksClient = demoAksClient;
        _logger = logger;
    }

    public async Task<AksClientBootstrapResult> BootstrapAsync(AksClientBootstrapRequest request, CancellationToken ct = default)
    {
        if (request.ClientOverride is not null)
        {
            return await BuildConnectedResultAsync(request.ClientOverride, request.RequestedContext, request.RequestedNamespace, request.Config, ct);
        }

        if (request.UseDemoData)
        {
            return await BuildConnectedResultAsync(_demoAksClient, request.RequestedContext, request.RequestedNamespace, request.Config, ct, isDemo: true);
        }

        if (request.Config is null)
        {
            return new AksClientBootstrapResult(
                AksClientBootstrapStatus.NotConfigured,
                Client: null,
                Contexts: [],
                Namespaces: [],
                ActiveContext: request.RequestedContext ?? string.Empty,
                CurrentNamespace: NormalizeRequestedNamespace(request.RequestedNamespace, request.Config),
                ErrorMessage: null);
        }

        try
        {
            // UI freeze root-cause: k8s client construction parses kubeconfig and acquires tokens
            // before any await. Offload it to the thread pool so the Blazor UI thread stays fluid.
            var client = await Task.Run(() => _factory.Create(
                string.IsNullOrWhiteSpace(request.RequestedContext) ? null : request.RequestedContext,
                string.IsNullOrWhiteSpace(request.Config.KubeconfigPath) ? null : request.Config.KubeconfigPath), ct);

            return await BuildConnectedResultAsync(client, request.RequestedContext, request.RequestedNamespace, request.Config, ct);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new AksClientBootstrapResult(
                AksClientBootstrapStatus.Error,
                Client: null,
                Contexts: [],
                Namespaces: [],
                ActiveContext: request.RequestedContext ?? string.Empty,
                CurrentNamespace: NormalizeRequestedNamespace(request.RequestedNamespace, request.Config),
                ErrorMessage: ex.Message);
        }
    }

    private async Task<AksClientBootstrapResult> BuildConnectedResultAsync(
        IAksClient client,
        string? requestedContext,
        string? requestedNamespace,
        AksConfig? config,
        CancellationToken ct,
        bool isDemo = false)
    {
        var contexts = await TryLoadContextsAsync(client, ct);
        var activeContext = ResolveContext(contexts, requestedContext, config);

        var (namespaces, namespacesWarning) = await TryLoadNamespacesAsync(client, ct);
        var currentNamespace = ResolveNamespace(namespaces, requestedNamespace, config, isDemo);

        return new AksClientBootstrapResult(
            AksClientBootstrapStatus.Connected,
            client,
            contexts,
            namespaces,
            activeContext,
            currentNamespace,
            ErrorMessage: null)
        {
            NamespacesWarning = namespacesWarning
        };
    }

    private async Task<IReadOnlyList<KubeContextInfo>> TryLoadContextsAsync(IAksClient client, CancellationToken ct)
    {
        try
        {
            return (await client.GetContextsAsync(ct)).ToList();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to load AKS contexts during bootstrap");
            return [];
        }
    }

    private async Task<(IReadOnlyList<string> Namespaces, string? Warning)> TryLoadNamespacesAsync(IAksClient client, CancellationToken ct)
    {
        try
        {
            return ((await client.GetNamespacesAsync(ct)).ToList(), null);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (AksAccessDeniedException ex)
        {
            // Having a RoleBinding scoped to specific namespaces does not grant the cluster-wide
            // "list namespaces" permission, so this 403 is a common, expected RBAC shape rather than
            // an actual absence of namespaces — surface it distinctly instead of silently returning
            // an empty list indistinguishable from "this cluster has no namespaces".
            _logger.LogWarning(ex, "Access denied listing AKS namespaces during bootstrap");
            return ([], "Cannot list namespaces in this cluster (access denied). You may still have access to specific namespaces directly — ask your cluster administrator about a ClusterRole granting `list` on `namespaces`.");
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to load AKS namespaces during bootstrap");
            return ([], null);
        }
    }

    private static string ResolveContext(
        IReadOnlyList<KubeContextInfo> contexts,
        string? requestedContext,
        AksConfig? config)
    {
        if (!string.IsNullOrWhiteSpace(requestedContext))
        {
            return requestedContext;
        }

        var current = contexts.FirstOrDefault(context => context.IsCurrent);
        if (current is not null)
        {
            return current.Name;
        }

        return config?.KubeconfigContext ?? string.Empty;
    }

    private static string ResolveNamespace(
        IReadOnlyList<string> namespaces,
        string? requestedNamespace,
        AksConfig? config,
        bool isDemo = false)
    {
        var resolvedNamespace = NormalizeRequestedNamespace(requestedNamespace, config);
        var fallbackNamespace = string.IsNullOrWhiteSpace(config?.DefaultNamespace)
            ? (isDemo && namespaces.Count > 0 ? namespaces[0] : string.Empty)
            : config.DefaultNamespace.Trim();
        return AksNamespaceScope.NormalizeSelection(resolvedNamespace, namespaces, fallbackNamespace);
    }

    private static string NormalizeRequestedNamespace(string? requestedNamespace, AksConfig? config)
    {
        if (!string.IsNullOrWhiteSpace(requestedNamespace))
        {
            return requestedNamespace.Trim();
        }

        return string.IsNullOrWhiteSpace(config?.DefaultNamespace) ? string.Empty : config.DefaultNamespace.Trim();
    }
}