using SwebKit.Core.Models;

namespace SwebKit.Core.Services;

public partial class DemoAksClient
{
    public async Task<IReadOnlyList<HelmReleaseInfo>> GetHelmReleasesAsync(string ns, CancellationToken ct = default)
    {
        await Task.Delay(200, ct).ConfigureAwait(false);
        var now = DateTimeOffset.UtcNow;
        return new List<HelmReleaseInfo>
        {
            new() { Name = "order-api", Namespace = ns, Chart = "order-api-1.8.3", AppVersion = "1.8.3", ChartVersion = "1.8.3", Status = "deployed", Revision = 12, Updated = now.AddHours(-6) },
            new() { Name = "product-catalog", Namespace = ns, Chart = "product-catalog-2.1.0", AppVersion = "2.1.0", ChartVersion = "2.1.0", Status = "deployed", Revision = 8, Updated = now.AddDays(-1) },
            new() { Name = "user-service", Namespace = ns, Chart = "user-service-1.4.7", AppVersion = "1.4.7", ChartVersion = "1.4.7", Status = "deployed", Revision = 15, Updated = now.AddHours(-2) },
            new() { Name = "payment-gateway", Namespace = ns, Chart = "payment-gateway-3.0.1", AppVersion = "3.0.1", ChartVersion = "3.0.1", Status = "deployed", Revision = 5, Updated = now.AddDays(-3) },
            new() { Name = "ingress-nginx", Namespace = ns, Chart = "ingress-nginx-4.9.1", AppVersion = "1.9.6", ChartVersion = "4.9.1", Status = "deployed", Revision = 3, Updated = now.AddDays(-14) },
            new() { Name = "cert-manager", Namespace = ns, Chart = "cert-manager-1.14.4", AppVersion = "1.14.4", ChartVersion = "1.14.4", Status = "deployed", Revision = 2, Updated = now.AddDays(-30) },
            new() { Name = "search-indexer", Namespace = ns, Chart = "search-indexer-0.9.2", AppVersion = "0.9.2", ChartVersion = "0.9.2", Status = "failed", Revision = 4, Updated = now.AddMinutes(-45) },
            new() { Name = "istio-base", Namespace = ns, Chart = "base-1.20.3", AppVersion = "1.20.3", ChartVersion = "1.20.3", Status = "deployed", Revision = 1, Updated = now.AddDays(-60) },
        };
    }

    public async Task<IReadOnlyList<HelmRevisionInfo>> GetHelmReleaseHistoryAsync(string ns, string releaseName, CancellationToken ct = default)
    {
        await Task.Delay(300, ct).ConfigureAwait(false);
        var now = DateTimeOffset.UtcNow;
        return new List<HelmRevisionInfo>
        {
            new() { Revision = 1, Status = "superseded", Chart = $"{releaseName}-1.0.0", AppVersion = "1.0.0", Updated = now.AddDays(-30), Description = "Install complete" },
            new() { Revision = 2, Status = "superseded", Chart = $"{releaseName}-1.1.0", AppVersion = "1.1.0", Updated = now.AddDays(-20), Description = "Upgrade complete" },
            new() { Revision = 3, Status = "superseded", Chart = $"{releaseName}-1.2.0", AppVersion = "1.2.0", Updated = now.AddDays(-10), Description = "Upgrade complete" },
            new() { Revision = 4, Status = "deployed", Chart = $"{releaseName}-1.3.0", AppVersion = "1.3.0", Updated = now.AddDays(-2), Description = "Upgrade complete" },
        };
    }

    public async Task<HelmReleaseValues> GetHelmReleaseValuesAsync(string ns, string releaseName, CancellationToken ct = default)
    {
        await Task.Delay(250, ct).ConfigureAwait(false);

        // User values: only what someone actually overrode at install/upgrade time.
        var userValues = $"""
            replicaCount: 3
            image:
              tag: "1.3.0"
            ingress:
              hosts:
                - host: {releaseName}.example.com
            """;

        // Computed values: those overrides merged onto the chart's own defaults — what the
        // release is actually running with (matches `helm get values --all`).
        var computedValues = $"""
            replicaCount: 3
            image:
              repository: acr.azurecr.io/{releaseName}
              tag: "1.3.0"
              pullPolicy: IfNotPresent
            service:
              type: ClusterIP
              port: 80
            resources:
              requests:
                cpu: 100m
                memory: 128Mi
              limits:
                cpu: 500m
                memory: 512Mi
            ingress:
              enabled: true
              className: nginx
              hosts:
                - host: {releaseName}.example.com
                  paths:
                    - path: /
                      pathType: Prefix
            autoscaling:
              enabled: true
              minReplicas: 2
              maxReplicas: 10
              targetCPUUtilizationPercentage: 75
            """;

        return new HelmReleaseValues { UserValues = userValues, ComputedValues = computedValues };
    }

    public async Task<string> GetHelmReleaseNotesAsync(string ns, string releaseName, CancellationToken ct = default)
    {
        await Task.Delay(250, ct).ConfigureAwait(false);
        return $"# {releaseName}\n\nSample release notes for the '{releaseName}' Helm release in namespace '{ns}'.";
    }

    public async Task<string> GetHelmReleaseManifestAsync(string ns, string releaseName, CancellationToken ct = default)
    {
        await Task.Delay(300, ct).ConfigureAwait(false);
        return $"# Manifest: {releaseName}\n---\napiVersion: v1\nkind: ConfigMap\nmetadata:\n  name: {releaseName}-config\n  namespace: {ns}\ndata:\n  release: \"{releaseName}\"\n";
    }

    public async Task RollbackHelmReleaseAsync(string ns, string releaseName, int targetRevision, CancellationToken ct = default)
    {
        await Task.Delay(800, ct).ConfigureAwait(false); // simulate rollback
    }

    public async Task<HelmDiffPreview> PreviewHelmUpgradeAsync(
        string ns,
        string releaseName,
        CancellationToken ct = default)
    {
        await Task.Delay(50, ct).ConfigureAwait(false);

        return new HelmDiffPreview
        {
            Namespace = ns,
            ReleaseName = releaseName,
            Capability = HelmPreviewCapability.Unsupported,
            CapabilityNote = "Demo mode does not support Helm diff preview.",
            Findings = ["Helm diff preview is not available in demo mode."]
        };
    }

    public async Task<HelmDiffPreview> PreviewHelmRollbackAsync(
        string ns,
        string releaseName,
        int revision,
        CancellationToken ct = default)
    {
        await Task.Delay(50, ct).ConfigureAwait(false);

        return new HelmDiffPreview
        {
            Namespace = ns,
            ReleaseName = releaseName,
            Capability = HelmPreviewCapability.Unsupported,
            CapabilityNote = "Demo mode does not support Helm rollback diff preview.",
            Findings = ["Helm rollback diff preview is not available in demo mode."]
        };
    }
}
