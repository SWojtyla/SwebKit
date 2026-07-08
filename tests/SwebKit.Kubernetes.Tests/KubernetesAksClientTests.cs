using System.Reflection;
using System.Text.Json;
using k8s;
using k8s.Models;
using SwebKit.Core.Constants;
using SwebKit.Core.Models;
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

    [Fact]
    public void BuildCliKubeconfigArgs_UsesRequestedContextFlag()
    {
        var helmArgs = KubernetesAksClient.BuildCliKubeconfigArgs(
            @"C:\temp\config",
            "prod-aks",
            "--kube-context");

        var kubectlArgs = KubernetesAksClient.BuildCliKubeconfigArgs(
            @"C:\temp\config",
            "prod-aks",
            "--context");

        Assert.Contains("--kubeconfig \"C:\\temp\\config\"", helmArgs, StringComparison.Ordinal);
        Assert.Contains("--kube-context prod-aks", helmArgs, StringComparison.Ordinal);
        Assert.DoesNotContain("--context prod-aks", helmArgs, StringComparison.Ordinal);
        Assert.Contains("--context prod-aks", kubectlArgs, StringComparison.Ordinal);
    }

    [Fact]
    public void CleanEditableYaml_StripsStatusAndServerManagedMetadataFields()
    {
        const string rawYaml = """
                                apiVersion: apps/v1
                                kind: Deployment
                                metadata:
                                  name: boa-brioengine
                                  namespace: prd-boa
                                  generation: 733
                                  resourceVersion: "2597455352"
                                  uid: cb88ddef-1491-41df-bc5d-4e8a4ac9ed41
                                  creationTimestamp: "2025-08-12T20:14:51Z"
                                  managedFields:
                                  - manager: kube-controller-manager
                                    operation: Update
                                  labels:
                                    app.kubernetes.io/name: boa-brioengine
                                spec:
                                  replicas: 4
                                status:
                                  availableReplicas: 4
                                  readyReplicas: 4
                                """;

        var cleaned = KubernetesAksClient.CleanEditableYaml(rawYaml);

        Assert.DoesNotContain("status:", cleaned, StringComparison.Ordinal);
        Assert.DoesNotContain("resourceVersion", cleaned, StringComparison.Ordinal);
        Assert.DoesNotContain("managedFields", cleaned, StringComparison.Ordinal);
        Assert.DoesNotContain("generation:", cleaned, StringComparison.Ordinal);
        Assert.DoesNotContain("uid:", cleaned, StringComparison.Ordinal);
        Assert.DoesNotContain("creationTimestamp", cleaned, StringComparison.Ordinal);

        // Editable content must survive the cleanup untouched.
        Assert.Contains("name: boa-brioengine", cleaned, StringComparison.Ordinal);
        Assert.Contains("namespace: prd-boa", cleaned, StringComparison.Ordinal);
        Assert.Contains("replicas: 4", cleaned, StringComparison.Ordinal);
        Assert.Contains("app.kubernetes.io/name: boa-brioengine", cleaned, StringComparison.Ordinal);
    }

    [Fact]
    public void CleanEditableYaml_ReturnsOriginal_WhenYamlIsEmpty()
    {
        Assert.Equal(string.Empty, KubernetesAksClient.CleanEditableYaml(string.Empty));
    }

    [Fact]
    public void CleanEditableYaml_PreservesScalarTypeFidelity_ForNumericAnnotationsAndRealNumbers()
    {
        // Regression test: metadata.annotations is map[string]string on the Kubernetes side, but
        // spec.replicas is a genuine int32. CleanEditableYaml must not blur that distinction —
        // it previously round-tripped through an untyped Dictionary<object, object>, and
        // YamlDotNet's default deserializer reads *every* plain scalar into that shape as a
        // string. Re-serializing then either (a) left a numeric-looking annotation value like
        // "251" unquoted — kubectl rejects that with "json: cannot unmarshal number into Go
        // struct field ObjectMeta.metadata.annotations of type string" — or, once quoting was
        // added to fix that, (b) incorrectly quoted genuinely-numeric fields like
        // spec.replicas, which Kubernetes then rejects the opposite way (string into *int32).
        // Operating on the YAML node tree instead of a boxed dictionary avoids both failure
        // modes because scalar style/type is never inferred or lost for untouched nodes.
        const string rawYaml = """
                                apiVersion: apps/v1
                                kind: Deployment
                                metadata:
                                  name: boa-brioengine
                                  namespace: prd-boa
                                  annotations:
                                    deployment.kubernetes.io/revision: "251"
                                    some.other/flag-like-annotation: "true"
                                spec:
                                  replicas: 4
                                """;

        var cleaned = KubernetesAksClient.CleanEditableYaml(rawYaml);

        Assert.Contains("deployment.kubernetes.io/revision: \"251\"", cleaned, StringComparison.Ordinal);
        Assert.Contains("some.other/flag-like-annotation: \"true\"", cleaned, StringComparison.Ordinal);
        Assert.Contains("replicas: 4", cleaned, StringComparison.Ordinal);
        Assert.DoesNotContain("replicas: \"4\"", cleaned, StringComparison.Ordinal);
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
    public void MapGateways_MapsGatewayApiCustomObjects()
    {
        const string json = """
                        {
                            "items": [
                                {
                                    "metadata": {
                                        "name": "payments-edge",
                                        "namespace": "payments",
                                        "labels": {
                                            "gateway.envoyproxy.io/managed": "true"
                                        }
                                    },
                                    "spec": {
                                        "gatewayClassName": "envoy-gateway",
                                        "listeners": [
                                            {
                                                "name": "https",
                                                "protocol": "HTTPS",
                                                "port": 443,
                                                "hostname": "payments.example.com"
                                            }
                                        ]
                                    },
                                    "status": {
                                        "addresses": [
                                            {
                                                "value": "20.10.0.11"
                                            }
                                        ],
                                        "conditions": [
                                            {
                                                "type": "Programmed",
                                                "status": "True"
                                            }
                                        ],
                                        "listeners": [
                                            {
                                                "name": "https",
                                                "attachedRoutes": 2
                                            }
                                        ]
                                    }
                                }
                            ]
                        }
                        """;

        using var doc = JsonDocument.Parse(json);
        var method = typeof(KubernetesAksClient).GetMethod("MapGateways", BindingFlags.Static | BindingFlags.NonPublic);

        Assert.NotNull(method);

        var result = Assert.IsAssignableFrom<IReadOnlyList<GatewayInfo>>(method!.Invoke(null, [doc.RootElement, "default"]));
        var gateway = Assert.Single(result);

        Assert.Equal("payments-edge", gateway.Name);
        Assert.Equal("payments", gateway.Namespace);
        Assert.Equal("envoy-gateway", gateway.GatewayClassName);
        Assert.Equal("Programmed", gateway.Status);
        Assert.Equal(2, gateway.AttachedRoutes);
        Assert.Equal("20.10.0.11", Assert.Single(gateway.Addresses));
        Assert.Equal("payments.example.com", Assert.Single(gateway.Listeners).Hostname);
    }

    [Fact]
    public void MapGatewayClasses_MapsGatewayApiCustomObjects()
    {
        const string json = """
                        {
                            "items": [
                                {
                                    "metadata": {
                                        "name": "envoy-gateway",
                                        "annotations": {
                                            "gateway.networking.k8s.io/default-gatewayclass": "true"
                                        }
                                    },
                                    "spec": {
                                        "controllerName": "gateway.envoyproxy.io/gatewayclass-controller",
                                        "description": "Default Envoy Gateway class.",
                                        "parametersRef": {
                                            "group": "gateway.envoyproxy.io",
                                            "kind": "EnvoyProxy",
                                            "namespace": "infrastructure",
                                            "name": "envoy-gateway-config"
                                        }
                                    },
                                    "status": {
                                        "conditions": [
                                            {
                                                "type": "Accepted",
                                                "status": "True"
                                            }
                                        ]
                                    }
                                }
                            ]
                        }
                        """;

        using var doc = JsonDocument.Parse(json);
        var method = typeof(KubernetesAksClient).GetMethod("MapGatewayClasses", BindingFlags.Static | BindingFlags.NonPublic);

        Assert.NotNull(method);

        var result = Assert.IsAssignableFrom<IReadOnlyList<GatewayClassInfo>>(method!.Invoke(null, [doc.RootElement]));
        var gatewayClass = Assert.Single(result);

        Assert.Equal("envoy-gateway", gatewayClass.Name);
        Assert.Equal("gateway.envoyproxy.io/gatewayclass-controller", gatewayClass.ControllerName);
        Assert.Equal("Accepted", gatewayClass.Status);
        Assert.Equal("Default Envoy Gateway class.", gatewayClass.Description);
        Assert.Equal("gateway.envoyproxy.io/EnvoyProxy infrastructure/envoy-gateway-config", gatewayClass.ParametersReference);
        Assert.True(gatewayClass.IsDefault);
    }

    [Fact]
    public void MapHttpRoutes_MapsGatewayApiCustomObjects()
    {
        const string json = """
                        {
                            "items": [
                                {
                                    "metadata": {
                                        "name": "payments-api-route",
                                        "namespace": "payments"
                                    },
                                    "spec": {
                                        "hostnames": ["payments.example.com"],
                                        "parentRefs": [
                                            {
                                                "name": "payments-edge",
                                                "sectionName": "https"
                                            }
                                        ],
                                        "rules": [
                                            {
                                                "backendRefs": [
                                                    {
                                                        "name": "payment-gateway",
                                                        "port": 80
                                                    }
                                                ]
                                            }
                                        ]
                                    },
                                    "status": {
                                        "parents": [
                                            {
                                                "conditions": [
                                                    {
                                                        "type": "Accepted",
                                                        "status": "True"
                                                    }
                                                ]
                                            }
                                        ]
                                    }
                                }
                            ]
                        }
                        """;

        using var doc = JsonDocument.Parse(json);
        var method = typeof(KubernetesAksClient).GetMethod("MapHttpRoutes", BindingFlags.Static | BindingFlags.NonPublic);

        Assert.NotNull(method);

        var result = Assert.IsAssignableFrom<IReadOnlyList<HttpRouteInfo>>(method!.Invoke(null, [doc.RootElement, "default"]));
        var route = Assert.Single(result);

        Assert.Equal("payments-api-route", route.Name);
        Assert.Equal("payments", route.Namespace);
        Assert.Equal("Accepted", route.Status);
        Assert.Equal("payments.example.com", Assert.Single(route.Hostnames));
        Assert.Equal("payments-edge#https", Assert.Single(route.ParentRefs));
        Assert.Equal("payment-gateway:80", Assert.Single(route.BackendRefs));
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

    [Fact]
    public void BuildClientConfiguration_WithAksExecAuth_DoesNotExecuteBrokenAuthPlugin()
    {
        using var kubeconfig = CreateTempKubeconfig("https://cluster.region.azmk8s.io:443");

        var ex = Record.Exception(() => KubernetesAksClient.BuildClientConfiguration(null, kubeconfig.Path));

        Assert.Null(ex);

        var config = KubernetesAksClient.BuildClientConfiguration(null, kubeconfig.Path);
        Assert.Equal("https://cluster.region.azmk8s.io:443", config.Host);
        Assert.True(string.IsNullOrWhiteSpace(config.AccessToken));
    }

    [Fact]
    public void BuildClientConfiguration_WithWorkingAksExecAuth_PreservesExecProviderToken()
    {
        using var kubeconfig = CreateTempKubeconfig(
            "https://cluster.region.azmk8s.io:443",
            GetWorkingExecCommandYaml("test-token"));

        var config = KubernetesAksClient.BuildClientConfiguration(null, kubeconfig.Path);

        Assert.Equal("https://cluster.region.azmk8s.io:443", config.Host);
        Assert.Equal("test-token", config.AccessToken);
    }

    [Fact]
    public void BuildClientConfiguration_WithNonAksExecAuth_StillUsesConfiguredExecPlugin()
    {
        using var kubeconfig = CreateTempKubeconfig("https://example.local:443");

        var ex = Record.Exception(() => KubernetesAksClient.BuildClientConfiguration(null, kubeconfig.Path));

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

    private static TempKubeconfig CreateTempKubeconfig(string server, string? execCommandYaml = null)
    {
        var directory = Directory.CreateTempSubdirectory();
        var path = Path.Combine(directory.FullName, "config");
        var kubeconfig = string.Join(
                        "\n",
                        [
                                        "apiVersion: v1",
                                                                "kind: Config",
                                                                "clusters:",
                                                                "- cluster:",
                                                                $"    server: {server}",
                                                                "  name: test-cluster",
                                                                "contexts:",
                                                                "- context:",
                                                                "    cluster: test-cluster",
                                                                "    user: test-user",
                                                                "  name: test-context",
                                                                "current-context: test-context",
                                                                "preferences: {}",
                                                                "users:",
                                                                "- name: test-user",
                                                                "  user:",
                                                                "    exec:",
                                                                "      apiVersion: client.authentication.k8s.io/v1beta1",
                                                                execCommandYaml ?? $"      command: {GetBrokenExecCommand()}"
                        ]) + "\n";

        File.WriteAllText(path, kubeconfig);
        return new TempKubeconfig(directory, path);
    }

    private static string GetBrokenExecCommand()
    {
        return OperatingSystem.IsWindows()
                ? "__definitely_missing_exec_command__"
                : "__definitely_missing_exec_command__";
    }

    private static string GetWorkingExecCommandYaml(string token)
    {
        if (OperatingSystem.IsWindows())
        {
            var payload = $"{{\"apiVersion\":\"client.authentication.k8s.io/v1beta1\",\"kind\":\"ExecCredential\",\"status\":{{\"token\":\"{token}\"}}}}";
            return string.Join(
                "\n",
                [
                    "      command: cmd.exe",
                    "      args:",
                    "      - /c",
                    $"      - echo {payload}"
                ]);
        }

        return string.Join(
            "\n",
            [
                "      command: /bin/sh",
                "      args:",
                "      - -c",
                $"      - printf '%s' '{{\"apiVersion\":\"client.authentication.k8s.io/v1beta1\",\"kind\":\"ExecCredential\",\"status\":{{\"token\":\"{token}\"}}}}'"
            ]);
    }

    private sealed class TempKubeconfig(DirectoryInfo directory, string path) : IDisposable
    {
        public string Path { get; } = path;

        public void Dispose()
        {
            if (directory.Exists)
                directory.Delete(recursive: true);
        }
    }
}
