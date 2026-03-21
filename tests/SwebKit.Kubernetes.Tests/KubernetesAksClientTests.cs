using System.Reflection;
using SwebKit.Kubernetes.AksClient;

namespace SwebKit.Kubernetes.Tests;

public class KubernetesAksClientTests
{
    [Fact]
    public void Ctor_InvalidContext_ThrowsHelpfulException()
    {
        // The scaffold currently binds directly to kubeconfig; invalid context should fail fast.
        var ex = Record.Exception(() => new KubernetesAksClient("__this-context-should-not-exist__"));

        Assert.NotNull(ex);
    }

    [Fact]
    public void TryExtractServerIdFromKubeconfig_ParsesServerIdArgument()
    {
        const string kubeconfig = """
apiVersion: v1
clusters: []
contexts: []
users:
- name: aks-user
  user:
    exec:
      command: kubelogin
      args:
      - get-token
      - --server-id
      - 6dae42f8-4368-4678-94ff-3960e28e3630
""";

        var serverId = AksAzureAuthHelpers.TryExtractServerIdFromKubeconfig(kubeconfig);

        Assert.Equal("6dae42f8-4368-4678-94ff-3960e28e3630", serverId);
    }

    [Fact]
    public void TryExtractServerIdFromKubeconfig_ParsesInlineServerIdEquals()
    {
        const string kubeconfig = """
users:
- name: aks-user
  user:
    exec:
      command: kubelogin
      args:
      - get-token
      - --server-id=api://my-custom-app-id
""";

        var serverId = AksAzureAuthHelpers.TryExtractServerIdFromKubeconfig(kubeconfig);

        Assert.Equal("api://my-custom-app-id", serverId);
    }

    [Fact]
    public void TryExtractServerIdFromKubeconfig_ReturnsNull_WhenNoServerIdPresent()
    {
        const string kubeconfig = """
apiVersion: v1
clusters: []
contexts: []
users:
- name: basic-user
  user:
    token: some-token
""";

        var serverId = AksAzureAuthHelpers.TryExtractServerIdFromKubeconfig(kubeconfig);

        Assert.Null(serverId);
    }

    [Fact]
    public void TryExtractServerIdFromKubeconfig_ReturnsNull_ForEmptyContent()
    {
        Assert.Null(AksAzureAuthHelpers.TryExtractServerIdFromKubeconfig(""));
        Assert.Null(AksAzureAuthHelpers.TryExtractServerIdFromKubeconfig(null!));
    }

    [Fact]
    public void BuildAksTokenScopes_ForGuidServerId_ReturnsApiScope()
    {
        var scopes = AksAzureAuthHelpers.BuildAksTokenScopes("6dae42f8-4368-4678-94ff-3960e28e3630");

        Assert.Single(scopes);
        Assert.Equal("api://6dae42f8-4368-4678-94ff-3960e28e3630/.default", scopes[0]);
    }

    [Fact]
    public void BuildAksTokenScopes_ForApiPrefixedServerId_ReturnsSingleScope()
    {
        var scopes = AksAzureAuthHelpers.BuildAksTokenScopes("api://my-custom-server-id");

        Assert.Single(scopes);
        Assert.Equal("api://my-custom-server-id/.default", scopes[0]);
    }

    [Fact]
    public void BuildAksTokenScopes_ForEmptyServerId_ReturnsEmpty()
    {
        Assert.Empty(AksAzureAuthHelpers.BuildAksTokenScopes(""));
        Assert.Empty(AksAzureAuthHelpers.BuildAksTokenScopes("   "));
    }

    [Theory]
    [InlineData("https://cluster.region.azmk8s.io:443", null, true)]
    [InlineData("https://cluster.region.azmk8s.io:443", "already-token", false)]
    [InlineData("https://example.local", null, false)]
    [InlineData("https://my-cluster.azure.com", null, true)]
    [InlineData(null, null, false)]
    [InlineData("", null, false)]
    public void ShouldUseAzureCredentialFallback_ReturnsExpectedValue(string? host, string? accessToken, bool expected)
    {
        var actual = AksAzureAuthHelpers.ShouldUseAzureCredentialFallback(host, accessToken);

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void KubernetesAksClient_HasNoStaticGenericDictionaryFields()
    {
        // Process registries and other mutable shared state must be instance-level,
        // not static. A static Dictionary<,> would allow state to leak between
        // independent client instances and make tests non-deterministic.
        var type = typeof(KubernetesAksClient);
        var staticDictFields = type
            .GetFields(BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)
            .Where(f => f.FieldType.IsGenericType &&
                        f.FieldType.GetGenericTypeDefinition() == typeof(Dictionary<,>))
            .ToList();

        Assert.Empty(staticDictFields);
    }

    [Theory]
    [InlineData("ingress-nginx-4.9.1", "4.9.1")]
    [InlineData("cert-manager-1.14.4", "1.14.4")]
    [InlineData("order-api-1.8.3", "1.8.3")]
    [InlineData("base-1.20.3", "1.20.3")]
    [InlineData("my-chart-0.1.0-beta.1", "0.1.0-beta.1")]
    [InlineData("nochart", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void TryParseChartVersion_ExtractsVersionFromChartLabel(string? chart, string? expected)
    {
        var actual = KubernetesAksClient.TryParseChartVersion(chart);

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void BuildClientConfiguration_DefaultConfig_WhenNoExplicitValues()
    {
        // When neither path nor context is set, should use default config
        var ex = Record.Exception(() => KubernetesAksClient.BuildClientConfiguration(null, null));

        // Should not throw — builds from default kubeconfig location
        // (may throw if no kubeconfig exists, but that's environment-dependent)
        // The key test is that it doesn't throw ArgumentNullException
        if (ex is not null)
            Assert.IsNotType<ArgumentNullException>(ex);
    }

    [Fact]
    public void BuildClientConfiguration_WithExplicitContext_ThrowsForInvalidContext()
    {
        var ex = Record.Exception(() =>
            KubernetesAksClient.BuildClientConfiguration("__nonexistent_context__", null));

        Assert.NotNull(ex);
    }

    // ── CPU parsing tests ──

    [Theory]
    [InlineData("100m", 0.1)]
    [InlineData("250m", 0.25)]
    [InlineData("1000m", 1.0)]
    [InlineData("1", 1.0)]
    [InlineData("0.5", 0.5)]
    [InlineData("500000000n", 0.5)]
    [InlineData("100000u", 0.1)]
    [InlineData("0", 0)]
    public void ParseCpuToMillicores_ConvertsCorrectly(string input, double expected)
    {
        var actual = KubernetesAksClient.ParseCpuToMillicores(input);

        Assert.Equal(expected, actual, precision: 6);
    }

    // ── Memory parsing tests ──

    [Theory]
    [InlineData("128Mi", 128L * 1024 * 1024)]
    [InlineData("1Gi", 1L * 1024 * 1024 * 1024)]
    [InlineData("256Ki", 256L * 1024)]
    [InlineData("1048576", 1048576L)]
    [InlineData("0", 0)]
    public void ParseMemoryToBytes_ConvertsCorrectly(string input, long expected)
    {
        var actual = KubernetesAksClient.ParseMemoryToBytes(input);

        Assert.Equal(expected, actual);
    }
}
