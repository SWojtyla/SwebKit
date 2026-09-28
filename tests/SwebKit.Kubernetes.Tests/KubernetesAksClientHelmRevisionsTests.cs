using k8s.Models;
using SwebKit.Kubernetes.AksClient;

namespace SwebKit.Kubernetes.Tests;

/// <summary>Tests for <see cref="KubernetesAksClient.MapHelmRevisions"/> — the namespace-wide
/// mapper behind <see cref="KubernetesAksClient.GetHelmRevisionsAsync"/> that keeps EVERY
/// revision (unlike <c>MapHelmReleases</c>, which collapses to the latest per release).</summary>
public sealed class KubernetesAksClientHelmRevisionsTests
{
    [Fact]
    public void MapHelmRevisions_KeepsEveryRevisionPerRelease()
    {
        var secrets = new[]
        {
            HelmSecret("sh.helm.release.v1.orders-api.v2", "orders", releaseName: "orders-api", revision: 2, status: "superseded"),
            HelmSecret("sh.helm.release.v1.orders-api.v3", "orders", releaseName: "orders-api", revision: 3, status: "deployed"),
        };

        var result = KubernetesAksClient.MapHelmRevisions(secrets, "orders");

        Assert.Equal(2, result.Count);
        Assert.All(result, r => Assert.Equal("orders-api", r.ReleaseName));
        Assert.Equal([2, 3], result.Select(r => r.Revision).ToArray());
        Assert.Equal("superseded", result[0].Status);
        Assert.Equal("deployed", result[1].Status);
    }

    [Fact]
    public void MapHelmRevisions_UsesSecretCreationTimestampAsRevisionTime()
    {
        var stamp = new DateTime(2026, 3, 14, 14, 32, 0, DateTimeKind.Utc);
        var secret = HelmSecret("sh.helm.release.v1.api.v1", "orders", releaseName: "api", revision: 1);
        secret.Metadata.CreationTimestamp = stamp;

        var result = KubernetesAksClient.MapHelmRevisions([secret], "orders");

        Assert.Equal(new DateTimeOffset(stamp), Assert.Single(result).Updated);
    }

    [Fact]
    public void MapHelmRevisions_IgnoresNonHelmSecrets_WhenGivenAnUnfilteredList()
    {
        var secrets = new[]
        {
            new V1Secret
            {
                Metadata = new V1ObjectMeta { Name = "db-credentials", NamespaceProperty = "orders" },
                Type = "Opaque",
            },
            HelmSecret("sh.helm.release.v1.api.v1", "orders", releaseName: "api", revision: 1),
        };

        var result = KubernetesAksClient.MapHelmRevisions(secrets, "orders");

        Assert.Single(result);
        Assert.Equal("api", result[0].ReleaseName);
    }

    private static V1Secret HelmSecret(
        string name, string ns, string releaseName, int revision, string status = "deployed") => new()
    {
        Metadata = new V1ObjectMeta
        {
            Name = name,
            NamespaceProperty = ns,
            Labels = new Dictionary<string, string>
            {
                ["owner"] = "helm",
                ["name"] = releaseName,
                ["version"] = revision.ToString(),
                ["status"] = status,
            },
            CreationTimestamp = DateTime.UtcNow,
        },
        Type = "helm.sh/release.v1",
    };
}
