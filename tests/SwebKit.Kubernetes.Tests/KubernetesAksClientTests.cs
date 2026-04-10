using System.Reflection;
using k8s;
using k8s.Models;
using SwebKit.Core.Constants;
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

    [Fact]
    public void MapJobInfo_PrefersOwnerReferenceAndStripsControllerOwnedLabels()
    {
        var job = new V1Job
        {
            Metadata = new V1ObjectMeta
            {
                Name = "inventory-sync-29100000",
                NamespaceProperty = "ops",
                Labels = new Dictionary<string, string>
                {
                    ["app"] = "inventory-sync",
                    ["job-name"] = "inventory-sync-29100000"
                },
                OwnerReferences =
                [
                    new V1OwnerReference
                    {
                        ApiVersion = "batch/v1",
                        Kind = "CronJob",
                        Name = "inventory-sync",
                        Uid = "uid-1",
                        Controller = true
                    }
                ],
                Annotations = new Dictionary<string, string>
                {
                    [AksBatchAnnotations.SourceKind] = "Job",
                    [AksBatchAnnotations.SourceName] = "should-not-win"
                }
            },
            Spec = new V1JobSpec { Completions = 1 },
            Status = new V1JobStatus
            {
                Succeeded = 1,
                CompletionTime = DateTime.UtcNow,
                Conditions =
                [
                    new V1JobCondition { Type = "Complete", Status = "True" }
                ]
            }
        };

        var info = KubernetesAksClient.MapJobInfo(job, "fallback");

        Assert.Equal("Succeeded", info.Status);
        Assert.Equal("CronJob", info.SourceKind);
        Assert.Equal("inventory-sync", info.SourceName);
        Assert.Equal(1, info.DesiredCompletions);
        Assert.DoesNotContain("job-name", info.Labels.Keys);
    }

    [Fact]
    public void MapJobInfo_UsesAnnotationsWhenOwnerReferenceIsMissing()
    {
        var job = new V1Job
        {
            Metadata = new V1ObjectMeta
            {
                Name = "inventory-sync-manual-001",
                NamespaceProperty = "ops",
                Annotations = new Dictionary<string, string>
                {
                    [AksBatchAnnotations.SourceKind] = "CronJob",
                    [AksBatchAnnotations.SourceName] = "inventory-sync"
                }
            },
            Spec = new V1JobSpec { Completions = 3 },
            Status = new V1JobStatus
            {
                Active = 1,
                StartTime = DateTime.UtcNow
            }
        };

        var info = KubernetesAksClient.MapJobInfo(job, "fallback");

        Assert.Equal("Active", info.Status);
        Assert.Equal("CronJob", info.SourceKind);
        Assert.Equal("inventory-sync", info.SourceName);
        Assert.Equal(3, info.DesiredCompletions);
    }

    [Fact]
    public void BuildTriggeredJobFromCronJob_SanitizesTemplateAndAddsSourceAnnotations()
    {
        var cronJob = CreateCronJobForTests();

        var triggeredJob = KubernetesAksClient.BuildTriggeredJobFromCronJob(cronJob, "ops");

        Assert.Equal("batch/v1", triggeredJob.ApiVersion);
        Assert.Equal("Job", triggeredJob.Kind);
        Assert.StartsWith("nightly-cleanup-manual-", triggeredJob.Metadata!.GenerateName, StringComparison.Ordinal);
        Assert.Equal("CronJob", triggeredJob.Metadata.Annotations![AksBatchAnnotations.SourceKind]);
        Assert.Equal("nightly-cleanup", triggeredJob.Metadata.Annotations[AksBatchAnnotations.SourceName]);
        Assert.Equal("keep-me", triggeredJob.Metadata.Annotations["note"]);
        Assert.DoesNotContain("job-name", triggeredJob.Metadata.Labels!.Keys);
        Assert.DoesNotContain("controller-uid", triggeredJob.Metadata.Labels.Keys);
        Assert.Null(triggeredJob.Spec!.ManualSelector);
        Assert.Null(triggeredJob.Spec.Selector);
        Assert.DoesNotContain("job-name", triggeredJob.Spec.Template.Metadata!.Labels!.Keys);
        Assert.DoesNotContain("batch.kubernetes.io/controller-uid", triggeredJob.Spec.Template.Metadata.Labels.Keys);
    }

    [Fact]
    public void BuildTriggeredJobFromJob_SerializesBatchV1YamlAndUsesRerunPrefix()
    {
        var sourceJob = CreateJobForTests();

        var rerunJob = KubernetesAksClient.BuildTriggeredJobFromJob(sourceJob, "ops");
        var yaml = KubernetesYaml.Serialize(rerunJob);

        Assert.Equal("batch/v1", rerunJob.ApiVersion);
        Assert.Equal("Job", rerunJob.Kind);
        Assert.StartsWith("manual-cleanup-rerun-", rerunJob.Metadata!.GenerateName, StringComparison.Ordinal);
        Assert.Equal("Job", rerunJob.Metadata.Annotations![AksBatchAnnotations.SourceKind]);
        Assert.Equal("manual-cleanup", rerunJob.Metadata.Annotations[AksBatchAnnotations.SourceName]);
        Assert.Null(rerunJob.Spec!.ManualSelector);
        Assert.Null(rerunJob.Spec.Selector);
        Assert.Null(rerunJob.Spec.Template.Metadata!.NamespaceProperty);
        Assert.Contains("apiVersion: batch/v1", yaml);
        Assert.Contains("kind: Job", yaml);
    }

    private static V1CronJob CreateCronJobForTests()
    {
        return new V1CronJob
        {
            Metadata = new V1ObjectMeta
            {
                Name = "nightly-cleanup",
                NamespaceProperty = "ops"
            },
            Spec = new V1CronJobSpec
            {
                Schedule = "0 2 * * *",
                JobTemplate = new V1JobTemplateSpec
                {
                    Metadata = new V1ObjectMeta
                    {
                        Labels = new Dictionary<string, string>
                        {
                            ["app"] = "nightly-cleanup",
                            ["job-name"] = "stale-job",
                            ["controller-uid"] = "controller-1"
                        },
                        Annotations = new Dictionary<string, string>
                        {
                            ["note"] = "keep-me",
                            [AksBatchAnnotations.SourceKind] = "Old",
                            [AksBatchAnnotations.SourceName] = "OldName"
                        }
                    },
                    Spec = new V1JobSpec
                    {
                        ManualSelector = true,
                        Selector = new V1LabelSelector
                        {
                            MatchLabels = new Dictionary<string, string>
                            {
                                ["job-name"] = "stale-job"
                            }
                        },
                        Template = new V1PodTemplateSpec
                        {
                            Metadata = new V1ObjectMeta
                            {
                                Labels = new Dictionary<string, string>
                                {
                                    ["app"] = "nightly-cleanup",
                                    ["job-name"] = "stale-job",
                                    ["batch.kubernetes.io/controller-uid"] = "controller-1"
                                },
                                Annotations = new Dictionary<string, string>
                                {
                                    ["template-note"] = "keep-template"
                                }
                            },
                            Spec = new V1PodSpec
                            {
                                RestartPolicy = "Never",
                                Containers =
                                [
                                    new V1Container
                                    {
                                        Name = "cleanup",
                                        Image = "acr.azurecr.io/cleanup:1.0.0"
                                    }
                                ]
                            }
                        }
                    }
                }
            }
        };
    }

    private static V1Job CreateJobForTests()
    {
        return new V1Job
        {
            Metadata = new V1ObjectMeta
            {
                Name = "manual-cleanup",
                NamespaceProperty = "ops",
                Labels = new Dictionary<string, string>
                {
                    ["app"] = "manual-cleanup",
                    ["batch.kubernetes.io/job-name"] = "manual-cleanup"
                },
                Annotations = new Dictionary<string, string>
                {
                    ["note"] = "keep-me",
                    [AksBatchAnnotations.SourceKind] = "CronJob",
                    [AksBatchAnnotations.SourceName] = "inventory-sync"
                }
            },
            Spec = new V1JobSpec
            {
                ManualSelector = true,
                Selector = new V1LabelSelector
                {
                    MatchLabels = new Dictionary<string, string>
                    {
                        ["job-name"] = "manual-cleanup"
                    }
                },
                Completions = 2,
                Template = new V1PodTemplateSpec
                {
                    Metadata = new V1ObjectMeta
                    {
                        NamespaceProperty = "ops",
                        Uid = "template-uid",
                        Labels = new Dictionary<string, string>
                        {
                            ["app"] = "manual-cleanup",
                            ["job-name"] = "manual-cleanup"
                        }
                    },
                    Spec = new V1PodSpec
                    {
                        RestartPolicy = "Never",
                        Containers =
                        [
                            new V1Container
                            {
                                Name = "manual-cleanup",
                                Image = "acr.azurecr.io/manual-cleanup:1.0.0"
                            }
                        ]
                    }
                }
            },
            Status = new V1JobStatus
            {
                Succeeded = 1
            }
        };
    }
}
