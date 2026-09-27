using SwebKit.Core.Abstractions;
using SwebKit.Core.Domain;
using SwebKit.Core.Models;

namespace SwebKit.Sidecar.Services.AccessProbes;

/// <summary>
/// Observability access probe: a trivial KQL query against the configured Application Insights
/// resource via <see cref="IObservabilityProviderFactory"/> (which picks the demo provider in
/// demo mode). <c>SelectedResourceId</c> is the only real ARM resource id among the connection
/// entries, so it's the one row that carries <c>ScopeResourceId</c>.
/// </summary>
internal static class ObservabilityAccessProbes
{
    public const string Area = "Observability";
    public const string CapabilityLogs = "observability.logs";
    private const string DemoConnectionKey = "demo-observability";

    public static AccessProbeSpec? Build(
        ObservabilityConfig? config,
        IObservabilityProviderFactory providerFactory,
        bool demoMode)
    {
        var resourceId = config?.SelectedResourceId;
        if (string.IsNullOrWhiteSpace(resourceId) && !demoMode)
            return null;

        var connectionKey = resourceId ?? DemoConnectionKey;
        var label = config?.SelectedResourceName
            ?? resourceId
            ?? "Demo Application Insights";

        return new AccessProbeSpec(Area, connectionKey, CapabilityLogs, label, resourceId, null,
            async ct =>
            {
                var provider = providerFactory.Create(resourceId ?? "demo", demoMode);
                await provider.RunQueryAsync("print 'probe'", TimeRange.LastHour, maxRows: 1, ct)
                    .ConfigureAwait(false);
                return ProbeOutcome.Ok;
            });
    }
}
