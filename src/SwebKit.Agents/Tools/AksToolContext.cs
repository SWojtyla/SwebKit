using System.Text.Json;
using SwebKit.Core.Abstractions;
using SwebKit.Core.Services;

namespace SwebKit.Agents.Tools;

/// <summary>
/// Shared kubeconfig-context plumbing for the read-only AKS agent tools — the same "use the
/// requested context, else the globally configured one, else demo data" resolution every tool in
/// this folder needs, mirroring <c>SqlToolContext</c>/<c>RedisToolContext</c>. Multi-context
/// workspaces (multi-map alerting, cross-cluster map nodes) mean the configured context is no
/// longer the only cluster an investigation may legitimately target: a fired rule pinned to
/// <c>AksPodAlertParams.KubeconfigContext</c> must interrogate that cluster, not whichever one the
/// settings page happens to point at.
/// </summary>
internal static class AksToolContext
{
    /// <summary>The fixed connection key the access report uses for the configured cluster —
    /// matches <c>AksAccessProbes.ConnectionKey</c> in the sidecar (a single configured cluster
    /// gets one row).</summary>
    public const string ConfiguredClusterKey = "aks";

    /// <summary>Reads the optional <c>context</c> tool argument; null when absent, non-string, or
    /// blank (blank falls back to the configured context rather than erroring — the model emits
    /// empty strings often enough that failing on one punishes the user for the model's quirk).</summary>
    public static string? GetContext(JsonElement arguments) =>
        arguments.TryGetProperty("context", out var el) && el.ValueKind == JsonValueKind.String
            ? el.GetString()
            : null;

    /// <summary>
    /// Maps a call's resolved target to the access report's connection key:
    /// <see cref="ConfiguredClusterKey"/> in demo mode (the report probes the demo client under
    /// the same key) and when the call targets the configured cluster (no explicit context, or one
    /// matching <c>AksConfig.KubeconfigContext</c>); null when the call targets a different,
    /// un-probed cluster — a denial recorded against the configured cluster must not pre-empt
    /// calls to another kubeconfig context.
    /// </summary>
    public static string? ResolveConnectionKey(AppStateService appState, string? context)
    {
        if (appState.UseDemoData)
            return ConfiguredClusterKey;
        if (appState.Config.AksConfig is not { } aks)
            return null;
        return string.IsNullOrWhiteSpace(context)
            || string.Equals(context, aks.KubeconfigContext, StringComparison.OrdinalIgnoreCase)
            ? ConfiguredClusterKey
            : null;
    }

    /// <summary>Resolves the client for one tool call: the demo client in demo mode (demo tools
    /// only ever see demo data — a requested context does not escape it), otherwise a client for
    /// <paramref name="context"/> when given, or the configured context when not.</summary>
    public static IAksClient ResolveClient(
        IAksClientFactory factory, DemoAksClient demoClient, AppStateService appState, string? context) =>
        appState.UseDemoData
            ? demoClient
            : factory.Create(
                string.IsNullOrWhiteSpace(context) ? appState.Config.AksConfig?.KubeconfigContext : context,
                appState.Config.AksConfig?.KubeconfigPath);
}
