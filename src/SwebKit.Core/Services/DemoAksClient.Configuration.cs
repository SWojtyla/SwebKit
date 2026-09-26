using SwebKit.Core.Models;

namespace SwebKit.Core.Services;

public partial class DemoAksClient
{
    public async Task<IReadOnlyList<ConfigMapInfo>> GetConfigMapsAsync(string ns, CancellationToken ct = default)
    {
        await Task.Delay(200, ct).ConfigureAwait(false);
        return new List<ConfigMapInfo>
        {
            new()
            {
                Name = "app-settings", Namespace = ns,
                Data = new Dictionary<string, string>
                {
                    ["ConnectionStrings__Redis"] = "redis://redis-service:6379",
                    ["Feature__SearchEnabled"] = "true",
                    ["Feature__PaymentProvider"] = "stripe",
                    ["Logging__Level"] = "Information"
                },
                Labels = new Dictionary<string, string> { ["app"] = "order-api", ["team"] = "commerce" }
            },
            new()
            {
                Name = "tracing-config", Namespace = ns,
                Data = new Dictionary<string, string>
                {
                    ["Otel__Endpoint"] = "http://otel-collector:4317",
                    ["Otel__ServiceName"] = "ecommerce",
                    ["Otel__SampleRate"] = "0.1"
                },
                Labels = new Dictionary<string, string> { ["team"] = "platform" }
            },
            new()
            {
                Name = "ingress-config", Namespace = ns,
                Data = new Dictionary<string, string>
                {
                    ["proxy-connect-timeout"] = "60",
                    ["proxy-read-timeout"] = "60"
                },
                Labels = new Dictionary<string, string> { ["app.kubernetes.io/managed-by"] = "Helm" }
            }
        };
    }

    public async Task<IReadOnlyList<SecretInfo>> GetSecretsAsync(string ns, CancellationToken ct = default)
    {
        await Task.Delay(200, ct).ConfigureAwait(false);
        return new List<SecretInfo>
        {
            new()
            {
                Name = "order-api-secret", Namespace = ns, Type = "Opaque",
                Keys = ["api-key", "webhook-secret"],
                Labels = new Dictionary<string, string> { ["app"] = "order-api" }
            },
            new()
            {
                Name = "db-credentials", Namespace = ns, Type = "Opaque",
                Keys = ["connection-string", "username", "password"],
                Labels = new Dictionary<string, string> { ["team"] = "platform" }
            },
            new()
            {
                Name = "acr-pull-secret", Namespace = ns, Type = "kubernetes.io/dockerconfigjson",
                Keys = [".dockerconfigjson"],
                Labels = new Dictionary<string, string> { ["app.kubernetes.io/managed-by"] = "Helm" }
            }
        };
    }

    public Task<Dictionary<string, string>> GetSecretValuesAsync(string ns, string name, CancellationToken ct = default)
    {
        var values = name switch
        {
            "order-api-secret" => new Dictionary<string, string>
            {
                ["api-key"] = "sk-demo-abc123",
                ["webhook-secret"] = "whsec-demo-xyz789"
            },
            "db-credentials" => new Dictionary<string, string>
            {
                ["connection-string"] = "Server=demo-sql.database.windows.net;Database=ecommerce;",
                ["username"] = "app-user",
                ["password"] = "P@ssw0rd-Demo!"
            },
            "acr-pull-secret" => new Dictionary<string, string>
            {
                [".dockerconfigjson"] = "{\"auths\":{\"acr.azurecr.io\":{\"auth\":\"ZGVtbzpkZW1v\"}}}"
            },
            _ => new Dictionary<string, string>()
        };
        return Task.FromResult(values);
    }

    // ── Feature 4: Container details ─────────────────────────────────────────

    public async Task<IReadOnlyList<ResourceQuotaInfo>> GetResourceQuotasAsync(string ns, CancellationToken ct = default)
    {
        await Task.Delay(120, ct).ConfigureAwait(false);

        return
        [
            new ResourceQuotaInfo
            {
                Name = "compute-quota",
                Namespace = ns,
                HardLimits =
                [
                    new ResourceQuotaUsage { Resource = "cpu", Hard = "8" },
                    new ResourceQuotaUsage { Resource = "memory", Hard = "16Gi" },
                    new ResourceQuotaUsage { Resource = "pods", Hard = "100" }
                ],
                Used =
                [
                    new ResourceQuotaUsage { Resource = "cpu", Used = "4" },
                    new ResourceQuotaUsage { Resource = "memory", Used = "8Gi" },
                    new ResourceQuotaUsage { Resource = "pods", Used = "12" }
                ]
            },
            new ResourceQuotaInfo
            {
                Name = "storage-quota",
                Namespace = ns,
                HardLimits =
                [
                    new ResourceQuotaUsage { Resource = "persistentvolumeclaims", Hard = "10" },
                    new ResourceQuotaUsage { Resource = "requests.storage", Hard = "50Gi" }
                ],
                Used =
                [
                    new ResourceQuotaUsage { Resource = "persistentvolumeclaims", Used = "9" },
                    new ResourceQuotaUsage { Resource = "requests.storage", Used = "47Gi" }
                ]
            }
        ];
    }

    public async Task<IReadOnlyList<LimitRangeInfo>> GetLimitRangesAsync(string ns, CancellationToken ct = default)
    {
        await Task.Delay(100, ct).ConfigureAwait(false);

        return
        [
            new LimitRangeInfo
            {
                Name = "default-limits",
                Namespace = ns,
                Limits =
                [
                    new LimitRangeItem
                    {
                        Type = "Container",
                        DefaultRequests = new Dictionary<string, string>(StringComparer.Ordinal)
                        {
                            ["cpu"] = "100m",
                            ["memory"] = "128Mi"
                        },
                        DefaultLimits = new Dictionary<string, string>(StringComparer.Ordinal)
                        {
                            ["cpu"] = "500m",
                            ["memory"] = "512Mi"
                        },
                        Min = new Dictionary<string, string>(StringComparer.Ordinal)
                        {
                            ["cpu"] = "50m",
                            ["memory"] = "64Mi"
                        },
                        Max = new Dictionary<string, string>(StringComparer.Ordinal)
                        {
                            ["cpu"] = "2",
                            ["memory"] = "2Gi"
                        }
                    }
                ]
            }
        ];
    }

    public async Task<IReadOnlyList<PodDisruptionBudgetInfo>> GetPodDisruptionBudgetsAsync(string ns, CancellationToken ct = default)
    {
        await Task.Delay(100, ct).ConfigureAwait(false);

        return
        [
            new PodDisruptionBudgetInfo
            {
                Name = "order-api-pdb",
                Namespace = ns,
                MinAvailable = "1",
                DesiredHealthy = 2,
                CurrentHealthy = 3,
                ExpectedPods = 3,
                DisruptionsAllowed = true,
                AllowedDisruptions = 1,
                SelectorLabels = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["app"] = "order-api"
                }
            },
            new PodDisruptionBudgetInfo
            {
                Name = "payment-gateway-pdb",
                Namespace = ns,
                MinAvailable = "2",
                DesiredHealthy = 3,
                CurrentHealthy = 3,
                ExpectedPods = 3,
                DisruptionsAllowed = false,
                AllowedDisruptions = 0,
                SelectorLabels = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["app"] = "payment-gateway"
                }
            }
        ];
    }
}
