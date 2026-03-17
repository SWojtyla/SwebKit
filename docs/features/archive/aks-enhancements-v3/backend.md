# Backend Plan — AKS New Capabilities

---

title: "Backend Plan - AKS New Capabilities"
owner: ""
status: "Planned"

---

## Goal

Extend the AKS backend with 9 new interface methods covering multi-pod log streaming, StatefulSet management, ConfigMap/Secret inspection, container detail resolution, and HPA querying. Implement in both `KubernetesAksClient` and `DemoAksClient`.

## Impacted areas

- `src/SwebKit.Core/Abstractions/IAksClient.cs`
- `src/SwebKit.Core/Models/AksModels.cs`
- `src/SwebKit.Core/Services/DemoAksClient.cs`
- `src/SwebKit.Kubernetes/AksClient/KubernetesAksClient.cs`
- `tests/SwebKit.Kubernetes.Tests/`
- `tests/SwebKit.Core.Tests/`

## Design

All new methods follow the existing interface-first pattern: signatures defined in `IAksClient`, real implementation in `KubernetesAksClient`, and demo data in `DemoAksClient`. No new projects or layers are introduced.

`GetResourceYamlAsync` uses a `switch` on `kind` — each new resource type (StatefulSet, ConfigMap, Secret, HPA) requires a new case. `ApplyResourceYamlAsync` already uses `kubectl apply -f` and requires no change.

## API / Contracts

### New methods — `IAksClient.cs`

```csharp
// Feature 1 — multi-pod log aggregation
IAsyncEnumerable<AggregatedLogLine> StreamDeploymentLogsAsync(
    string ns, string deploymentName, LogStreamOptions opts, CancellationToken ct = default);

// Feature 2 — StatefulSets
Task<IReadOnlyList<StatefulSetInfo>> GetStatefulSetsAsync(string ns, CancellationToken ct = default);
Task RestartStatefulSetAsync(string ns, string name, CancellationToken ct = default);
Task ScaleStatefulSetAsync(string ns, string name, int replicas, CancellationToken ct = default);

// Feature 3 — ConfigMaps and Secrets
Task<IReadOnlyList<ConfigMapInfo>> GetConfigMapsAsync(string ns, CancellationToken ct = default);
Task<IReadOnlyList<SecretInfo>> GetSecretsAsync(string ns, CancellationToken ct = default);
Task<Dictionary<string, string>> GetSecretValuesAsync(string ns, string name, CancellationToken ct = default);

// Feature 4 — container details
Task<IReadOnlyList<ContainerDetail>> GetContainerDetailsAsync(
    string ns, string podName, CancellationToken ct = default);

// Feature 5 — HPA
Task<IReadOnlyList<HpaInfo>> GetHpasAsync(string ns, CancellationToken ct = default);
```

Multi-namespace default overloads for `GetStatefulSetsAsync` follow the same pattern as the existing `GetDeploymentsAsync` overload in the interface.

### New models — `AksModels.cs`

**Feature 1**
```csharp
public class AggregatedLogLine
{
    public required string PodName { get; set; }
    public required string Line { get; set; }
    public DateTimeOffset ReceivedAt { get; set; } = DateTimeOffset.UtcNow;
}
```
Color index (pod → color) is derived in the UI from `PodName`, not stored in the model.

**Feature 2**
```csharp
public class StatefulSetInfo
{
    public required string Name { get; set; }
    public required string Namespace { get; set; }
    public int Replicas { get; set; }
    public int ReadyReplicas { get; set; }
    public string? CurrentRevision { get; set; }
    public string? UpdateRevision { get; set; }
    public Dictionary<string, string> Labels { get; set; } = [];
}
```

**Feature 3**
```csharp
public class ConfigMapInfo
{
    public required string Name { get; set; }
    public required string Namespace { get; set; }
    public Dictionary<string, string> Data { get; set; } = [];
    public Dictionary<string, string> Labels { get; set; } = [];
}

public class SecretInfo
{
    public required string Name { get; set; }
    public required string Namespace { get; set; }
    public string Type { get; set; } = "Opaque";
    public List<string> Keys { get; set; } = [];   // key names only — values never in model
    public Dictionary<string, string> Labels { get; set; } = [];
}
```

**Feature 4**
```csharp
public class ContainerDetail
{
    public required string Name { get; set; }
    public required string Image { get; set; }      // full image:tag
    public string? ImageTag { get; set; }
    public ResourceRequirements Resources { get; set; } = new();
    public List<EnvVarDetail> EnvVars { get; set; } = [];
}

public class ResourceRequirements
{
    public string? CpuRequest { get; set; }
    public string? MemoryRequest { get; set; }
    public string? CpuLimit { get; set; }
    public string? MemoryLimit { get; set; }
}

public enum EnvVarSourceKind { Plain, ConfigMapRef, SecretRef, FieldRef }

public class EnvVarDetail
{
    public required string Name { get; set; }
    public string? Value { get; set; }              // null for unresolved SecretRef
    public EnvVarSourceKind Source { get; set; }
    public string? SourceName { get; set; }
    public string? SourceKey { get; set; }
    public bool IsResolved { get; set; }
}
```

**Feature 5**
```csharp
public class HpaInfo
{
    public required string Name { get; set; }
    public required string Namespace { get; set; }
    public required string TargetKind { get; set; }   // "Deployment" or "StatefulSet"
    public required string TargetName { get; set; }
    public int MinReplicas { get; set; }
    public int MaxReplicas { get; set; }
    public int CurrentReplicas { get; set; }
    public int DesiredReplicas { get; set; }
    public double? CurrentCpuUtilizationPercent { get; set; }
    public int? TargetCpuUtilizationPercent { get; set; }
    public List<HpaMetricStatus> Metrics { get; set; } = [];
    public List<HpaCondition> Conditions { get; set; } = [];
}

public class HpaMetricStatus
{
    public required string Name { get; set; }
    public string? Type { get; set; }       // "Resource", "Pods", "External"
    public double? CurrentValue { get; set; }
    public double? TargetValue { get; set; }
}

public class HpaCondition
{
    public required string Type { get; set; }
    public required string Status { get; set; }
    public string? Reason { get; set; }
    public string? Message { get; set; }
}
```

## `KubernetesAksClient` implementation notes

### Feature 1 — `StreamDeploymentLogsAsync`
1. Call `ReadNamespacedDeploymentAsync` to read `spec.selector.matchLabels` and build the label selector string. Do not use a name-based heuristic — this is authoritative.
2. Create one unbounded `Channel<AggregatedLogLine>`. For each pod, fire a `Task.Run` that calls the existing `StreamPodLogsAsync` and writes each line into the channel.
3. Use `CancellationTokenSource.CreateLinkedTokenSource(ct)` so cancelling the consumer cancels all per-pod streams.
4. Track fan-out tasks with `Task.WhenAll`; complete the channel when all tasks finish.
5. If `GetPodsAsync` returns zero pods, yield nothing immediately — do not hang.

### Feature 2 — StatefulSets
- `GetStatefulSetsAsync`: `_client.AppsV1.ListNamespacedStatefulSetAsync(ns)`, map to `StatefulSetInfo`.
- `RestartStatefulSetAsync`: same annotation-patch approach as `RestartDeploymentAsync` via `PatchNamespacedStatefulSetAsync`.
- `ScaleStatefulSetAsync`: strategic merge patch on `spec.replicas` via `PatchNamespacedStatefulSetAsync`.
- `GetResourceYamlAsync`: add `"statefulset"` case — `_client.AppsV1.ReadNamespacedStatefulSetAsync(name, ns)`.

### Feature 3 — ConfigMaps and Secrets
- `GetConfigMapsAsync`: `_client.CoreV1.ListNamespacedConfigMapAsync(ns)`. Map `cm.Data` directly.
- `GetSecretsAsync`: `_client.CoreV1.ListNamespacedSecretAsync(ns)`. Exclude Helm secrets (`owner=helm` label) and service-account token secrets (`kubernetes.io/service-account-token` type). Store only `s.Data?.Keys.ToList()`.
- `GetSecretValuesAsync`: `_client.CoreV1.ReadNamespacedSecretAsync(name, ns)`, iterate `.Data`, `Encoding.UTF8.GetString(value)` per key.
- `GetResourceYamlAsync`: add `"configmap"` and `"secret"` cases.
- Note: Secret YAML output contains base64-encoded values — expected K8s behavior; the UI should surface a notice.

### Feature 4 — `GetContainerDetailsAsync`
1. `ReadNamespacedPodAsync(podName, ns)`, iterate `pod.Spec.Containers`.
2. Split image on `:` to extract tag.
3. Map `container.Resources.Requests`/`.Limits` to `ResourceRequirements`.
4. For each `envVar`:
   - `envVar.Value != null` → `Plain`
   - `envVar.ValueFrom?.ConfigMapKeyRef != null` → `ConfigMapRef`; resolve value (see batching note below)
   - `envVar.ValueFrom?.SecretKeyRef != null` → `SecretRef`; set source info, `IsResolved = false`
   - `envVar.ValueFrom?.FieldRef != null` → `FieldRef`, `Value = FieldPath`
5. Batch ConfigMap resolution: build a `Dictionary<string, V1ConfigMap>` keyed by name, one API call per unique ConfigMap name.
6. If `container.EnvFrom` is non-empty, add a synthetic `EnvVarDetail` per source: `Name = "<all keys from configmap: {name}>"`; full resolution is deferred.

### Feature 5 — `GetHpasAsync`
- Try `_client.AutoscalingV2.ListNamespacedHorizontalPodAutoscalerAsync(ns)`.
- Catch `k8s.Autorest.HttpOperationException` with `HttpStatusCode.NotFound` and fall back to `_client.AutoscalingV1.ListNamespacedHorizontalPodAutoscalerAsync(ns)` (CPU metric only, no custom metrics).
- Join key: `hpa.Spec.ScaleTargetRef.Kind` + `.Name`.
- CPU utilization: find `hpa.Status.CurrentMetrics` where `Type == "Resource" && Resource?.Name == "cpu"`, read `CurrentAverageUtilization`.

### `GetResourceYamlAsync` additions (summary)

Add cases for: `"statefulset"`, `"configmap"`, `"secret"`, `"hpa"`.

## `DemoAksClient` implementation notes

### Feature 1
Fan-out N concurrent async tasks for the demo deployment's pods. Each task reads from `LogLines` at a different offset and writes into a `Channel<AggregatedLogLine>` with varied `Task.Delay` so lines interleave realistically.

### Feature 2
Add a static `DemoStatefulSets` array:
- `("order-queue", 3, 3, "order-queue-abc123")` — healthy
- `("session-store", 2, 1, "session-store-old")` — degraded (UpdateRevision != CurrentRevision)

### Feature 3
- `GetConfigMapsAsync`: 2–3 objects — e.g. `app-settings` with `ConnectionStrings__Redis`, `Feature__SearchEnabled`; `tracing-config` with `Otel__Endpoint`.
- `GetSecretsAsync`: 3 objects — `order-api-secret`, `db-credentials`, `acr-pull-secret` — with representative key names.
- `GetSecretValuesAsync`: return `{ ["api-key"] = "sk-demo-abc123", ["connection-string"] = "Server=demo;..." }`.

### Feature 4
For the first container of each demo pod:
- Image: `acr.azurecr.io/{deploymentName}:1.8.3`
- Mix: plain env vars (`ASPNETCORE_ENVIRONMENT=Production`), one `ConfigMapRef` (`ConnectionStrings__Redis` from `app-settings`), one `SecretRef` (`API_KEY` from `order-api-secret`)
- Include `istio-proxy` sidecar with minimal env vars

### Feature 5
3–4 `HpaInfo` objects:
- `payment-gateway`: CPU 68%, 3/5 replicas, `TargetCpuUtilizationPercent = 70`
- `order-api`: CPU 42%, 3/3 replicas
- `user-service`: CPU 28%, 2/2 replicas

Realistic conditions: `ScalingActive=True`, `AbleToScale=True`, `LimitedByMaxReplicas` where applicable.

### Feature 6
No new backend work. Feature reuses existing `OpenShellAsync`.

## Tasks

- [ ] Add all new model types to `AksModels.cs`
- [ ] Add 9 new method signatures to `IAksClient.cs` (+ multi-namespace overload for StatefulSets)
- [ ] Implement all stubs in `DemoAksClient.cs`
- [ ] Feature 2 backend: `GetStatefulSetsAsync`, `RestartStatefulSetAsync`, `ScaleStatefulSetAsync`, YAML switch case
- [ ] Feature 3 backend: `GetConfigMapsAsync`, `GetSecretsAsync`, `GetSecretValuesAsync`, YAML switch cases
- [ ] Feature 5 backend: `GetHpasAsync` with v2/v1 fallback
- [ ] Feature 4 backend: `GetContainerDetailsAsync` with ConfigMap batching and `envFrom` flag row
- [ ] Feature 1 backend: `StreamDeploymentLogsAsync` with channel fan-out and linked cancellation
- [ ] Unit tests: new `KubernetesAksClient` methods
- [ ] Unit tests: new `DemoAksClient` methods

## Concern-specific risks

- Fan-out cancellation in `StreamDeploymentLogsAsync` — must use linked token, not the outer `ct` directly
- HPA API version unavailable on older clusters — silent fallback to v1 required
- N+1 ConfigMap API calls in `GetContainerDetailsAsync` — batch by unique name within the method

## Validation

- Unit tests: Not started
- Integration tests: Deferred (requires live cluster)
- Manual checks: See `test-plan.md`
