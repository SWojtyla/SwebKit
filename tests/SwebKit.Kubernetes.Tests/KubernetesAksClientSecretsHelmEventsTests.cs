using System.Text.Json.Nodes;
using k8s.Models;
using SwebKit.Kubernetes.AksClient;

namespace SwebKit.Kubernetes.Tests;

/// <summary>
/// Tests for the shared static mappers behind <see cref="KubernetesAksClient.GetSecretsAsync"/>,
/// <see cref="KubernetesAksClient.GetHelmReleasesAsync"/>, and
/// <see cref="KubernetesAksClient.GetSecretsAndHelmReleasesAsync"/> — verifying the combined path
/// (one Secrets list split into both Secrets and Helm releases) produces the same result as the
/// two original independent queries would have.
/// </summary>
public sealed class KubernetesAksClientSecretsHelmEventsTests
{
    // ── MapSecrets ──────────────────────────────────────────────────────────────

    [Fact]
    public void MapSecrets_ExcludesHelmOwnedAndServiceAccountTokenSecrets()
    {
        var secrets = new[]
        {
            PlainSecret("db-credentials", "orders"),
            HelmSecret("sh.helm.release.v1.orders-api.v3", "orders"),
            ServiceAccountTokenSecret("default-token-abc12", "orders"),
        };

        var result = KubernetesAksClient.MapSecrets(secrets, "orders");

        var secret = Assert.Single(result);
        Assert.Equal("db-credentials", secret.Name);
    }

    [Fact]
    public void MapSecrets_UsesFallbackNamespace_WhenMetadataNamespaceIsMissing()
    {
        var secret = PlainSecret("db-credentials", ns: null);

        var result = KubernetesAksClient.MapSecrets([secret], "orders");

        Assert.Equal("orders", Assert.Single(result).Namespace);
    }

    // ── MapHelmReleases ─────────────────────────────────────────────────────────

    [Fact]
    public void MapHelmReleases_IgnoresNonHelmSecrets_WhenGivenAnUnfilteredList()
    {
        // The combined GetSecretsAndHelmReleasesAsync path passes the FULL unfiltered secrets list
        // (unlike GetHelmReleasesAsync's own owner=helm-filtered server query) — MapHelmReleases must
        // filter defensively so it still only picks out the Helm-owned secrets.
        var secrets = new[]
        {
            PlainSecret("db-credentials", "orders"),
            HelmSecret("sh.helm.release.v1.orders-api.v3", "orders", releaseName: "orders-api", revision: 3),
        };

        var result = KubernetesAksClient.MapHelmReleases(secrets, "orders");

        var release = Assert.Single(result);
        Assert.Equal("orders-api", release.Name);
        Assert.Equal(3, release.Revision);
    }

    [Fact]
    public void MapHelmReleases_KeepsOnlyLatestRevisionPerReleaseName()
    {
        var secrets = new[]
        {
            HelmSecret("sh.helm.release.v1.orders-api.v2", "orders", releaseName: "orders-api", revision: 2, status: "superseded"),
            HelmSecret("sh.helm.release.v1.orders-api.v3", "orders", releaseName: "orders-api", revision: 3, status: "deployed"),
        };

        var result = KubernetesAksClient.MapHelmReleases(secrets, "orders");

        var release = Assert.Single(result);
        Assert.Equal(3, release.Revision);
        Assert.Equal("deployed", release.Status);
    }

    [Fact]
    public void MapSecretsAndMapHelmReleases_TogetherPartitionAFullSecretsList_LikeTheTwoOriginalQueriesWould()
    {
        var secrets = new[]
        {
            PlainSecret("db-credentials", "orders"),
            PlainSecret("api-key", "orders"),
            HelmSecret("sh.helm.release.v1.orders-api.v3", "orders", releaseName: "orders-api", revision: 3),
            ServiceAccountTokenSecret("default-token-abc12", "orders"),
        };

        var plainSecrets = KubernetesAksClient.MapSecrets(secrets, "orders");
        var helmReleases = KubernetesAksClient.MapHelmReleases(secrets, "orders");

        Assert.Equal(2, plainSecrets.Count);
        Assert.Contains(plainSecrets, s => s.Name == "db-credentials");
        Assert.Contains(plainSecrets, s => s.Name == "api-key");
        Assert.Single(helmReleases);
        Assert.Equal("orders-api", helmReleases[0].Name);
    }

    // ── MapEvents ───────────────────────────────────────────────────────────────

    [Fact]
    public void MapEvents_OrdersByLastTimestampDescending()
    {
        var older = Event("pod-restarted", "orders", DateTime.UtcNow.AddMinutes(-10));
        var newer = Event("pod-scheduled", "orders", DateTime.UtcNow.AddMinutes(-1));

        var result = KubernetesAksClient.MapEvents([older, newer], "orders");

        Assert.Equal("pod-scheduled", result[0].Name);
        Assert.Equal("pod-restarted", result[1].Name);
    }

    [Fact]
    public void MapEvents_DefaultsTypeToNormal_WhenNotSet()
    {
        var evt = Event("pod-scheduled", "orders", DateTime.UtcNow);
        evt.Type = null;

        var result = KubernetesAksClient.MapEvents([evt], "orders");

        Assert.Equal("Normal", Assert.Single(result).Type);
    }

    // ── ParseHelmReleaseValues ────────────────────────────────────────────────────

    [Fact]
    public void ParseHelmReleaseValues_UserValues_OnlyContainsConfigOverrides()
    {
        var releaseJson = ReleaseJson(
            config: """{ "replicaCount": 3 }""",
            chartValues: """{ "replicaCount": 1, "image": { "tag": "latest" } }""");

        var result = KubernetesAksClient.ParseHelmReleaseValues(releaseJson);

        Assert.Contains("replicaCount", result.UserValues);
        Assert.Contains("3", result.UserValues);
        Assert.DoesNotContain("image", result.UserValues);
    }

    [Fact]
    public void ParseHelmReleaseValues_ComputedValues_MergesChartDefaultsWithUserOverrides()
    {
        var releaseJson = ReleaseJson(
            config: """{ "replicaCount": 3 }""",
            chartValues: """{ "replicaCount": 1, "image": { "tag": "latest" } }""");

        var result = KubernetesAksClient.ParseHelmReleaseValues(releaseJson);

        // Override wins on the conflicting key...
        Assert.Contains("\"replicaCount\": 3", result.ComputedValues);
        // ...but the chart's own default the user never touched still shows up.
        Assert.Contains("image", result.ComputedValues);
        Assert.Contains("latest", result.ComputedValues);
    }

    [Fact]
    public void ParseHelmReleaseValues_ComputedValues_FallsBackToUserValues_WhenChartHasNoDefaults()
    {
        var releaseJson = ReleaseJson(config: """{ "replicaCount": 3 }""", chartValues: null);

        var result = KubernetesAksClient.ParseHelmReleaseValues(releaseJson);

        Assert.Equal(result.UserValues, result.ComputedValues);
    }

    [Fact]
    public void ParseHelmReleaseValues_UserValues_DefaultsToEmptyObject_WhenNoConfigWasSupplied()
    {
        // A release installed with no --set/-f overrides at all has no "config" key.
        var releaseJson = """{ "chart": { "values": { "replicaCount": 1 } } }""";

        var result = KubernetesAksClient.ParseHelmReleaseValues(releaseJson);

        Assert.Equal("{}", result.UserValues);
        Assert.Contains("replicaCount", result.ComputedValues);
    }

    // ── MergeJsonValues ───────────────────────────────────────────────────────────

    [Fact]
    public void MergeJsonValues_DeepMergesNestedObjects_OverrideWinsOnConflict()
    {
        var baseNode = JsonNode.Parse("""{ "a": { "x": 1, "y": 2 } }""");
        var overrideNode = JsonNode.Parse("""{ "a": { "y": 3, "z": 4 } }""");

        var merged = KubernetesAksClient.MergeJsonValues(baseNode, overrideNode);

        Assert.Equal(1, (int)merged!["a"]!["x"]!);
        Assert.Equal(3, (int)merged["a"]!["y"]!);
        Assert.Equal(4, (int)merged["a"]!["z"]!);
    }

    [Fact]
    public void MergeJsonValues_OverrideArray_ReplacesBaseArrayWholesale()
    {
        // Helm doesn't merge arrays element-by-element — an override array replaces the base
        // array entirely, it never splices/concatenates.
        var baseNode = JsonNode.Parse("""{ "list": [1, 2, 3] }""");
        var overrideNode = JsonNode.Parse("""{ "list": [9] }""");

        var merged = KubernetesAksClient.MergeJsonValues(baseNode, overrideNode);

        var list = Assert.IsType<JsonArray>(merged!["list"]);
        Assert.Single(list);
        Assert.Equal(9, (int)list[0]!);
    }

    [Fact]
    public void MergeJsonValues_ReturnsBaseUntouched_WhenOverrideIsNull()
    {
        var baseNode = JsonNode.Parse("""{ "a": 1 }""");

        var merged = KubernetesAksClient.MergeJsonValues(baseNode, null);

        Assert.Equal(1, (int)merged!["a"]!);
    }

    [Fact]
    public void MergeJsonValues_ReturnsOverride_WhenBaseIsNull()
    {
        var overrideNode = JsonNode.Parse("""{ "a": 1 }""");

        var merged = KubernetesAksClient.MergeJsonValues(null, overrideNode);

        Assert.Equal(1, (int)merged!["a"]!);
    }

    // ── Fixture helpers ───────────────────────────────────────────────────────────

    private static string ReleaseJson(string config, string? chartValues) =>
        chartValues is null
            ? $$"""{ "config": {{config}} }"""
            : $$"""{ "config": {{config}}, "chart": { "values": {{chartValues}} } }""";


    private static V1Secret PlainSecret(string name, string? ns) => new()
    {
        Metadata = new V1ObjectMeta { Name = name, NamespaceProperty = ns },
        Type = "Opaque",
        Data = new Dictionary<string, byte[]> { ["value"] = "secret"u8.ToArray() },
    };

    private static V1Secret ServiceAccountTokenSecret(string name, string ns) => new()
    {
        Metadata = new V1ObjectMeta { Name = name, NamespaceProperty = ns },
        Type = "kubernetes.io/service-account-token",
    };

    private static V1Secret HelmSecret(
        string name, string ns, string releaseName = "release", int revision = 1, string status = "deployed") => new()
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

    private static Corev1Event Event(string name, string ns, DateTime lastTimestamp) => new()
    {
        Metadata = new V1ObjectMeta { Name = name, NamespaceProperty = ns },
        Type = "Normal",
        Reason = "Scheduled",
        Message = "Successfully assigned",
        LastTimestamp = lastTimestamp,
        Count = 1,
    };
}
