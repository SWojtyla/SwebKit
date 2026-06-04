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
            return await BuildConnectedResultAsync(_demoAksClient, request.RequestedContext, request.RequestedNamespace, request.Config, ct);
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
            var client = _factory.Create(
                string.IsNullOrWhiteSpace(request.RequestedContext) ? null : request.RequestedContext,
                string.IsNullOrWhiteSpace(request.Config.KubeconfigPath) ? null : request.Config.KubeconfigPath);

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
        CancellationToken ct)
    {
        var contexts = await TryLoadContextsAsync(client, ct);
        var activeContext = ResolveContext(contexts, requestedContext, config);

        var namespaces = await TryLoadNamespacesAsync(client, ct);
        var currentNamespace = ResolveNamespace(namespaces, requestedNamespace, config);

        return new AksClientBootstrapResult(
            AksClientBootstrapStatus.Connected,
            client,
            contexts,
            namespaces,
            activeContext,
            currentNamespace,
            ErrorMessage: null);
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

    private async Task<IReadOnlyList<string>> TryLoadNamespacesAsync(IAksClient client, CancellationToken ct)
    {
        try
        {
            return (await client.GetNamespacesAsync(ct)).ToList();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to load AKS namespaces during bootstrap");
            return [];
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
        AksConfig? config)
    {
        var resolvedNamespace = NormalizeRequestedNamespace(requestedNamespace, config);
        var fallbackNamespace = string.IsNullOrWhiteSpace(config?.DefaultNamespace)
            ? string.Empty
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