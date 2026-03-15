# Decisions — AKS New Capabilities

---

title: "Decisions - AKS New Capabilities"
owner: ""
status: "Planned"

---

## Decision 001 — Multi-pod log fan-out uses `Channel<T>`, not `IAsyncEnumerable` merge

**Status:** Accepted
**Date:** 2026-03-15

### Context

`StreamDeploymentLogsAsync` must merge N concurrent pod log streams into a single ordered output. The existing `StreamPodLogsAsync` returns `IAsyncEnumerable<string>`. Several merge strategies were considered.

### Decision

Use `System.Threading.Channels.Channel<AggregatedLogLine>` (unbounded). Each per-pod `Task.Run` writes into the channel. The method reads from the channel as an `IAsyncEnumerable<AggregatedLogLine>` via `ChannelReader.ReadAllAsync`. All per-pod tasks are tracked with `Task.WhenAll`; the channel is marked complete when all tasks finish. A linked `CancellationTokenSource` ensures the outer consumer cancellation propagates to all per-pod readers.

### Consequences

- Clean cancellation: outer `ct` cancels the linked source, which cancels all inner `StreamPodLogsAsync` calls.
- No ordering guarantees — lines arrive as they are produced. This is expected and desirable for live log tailing.
- Slightly more memory than a purely lazy approach, but fully bounded by the channel's consumption rate.

### Alternatives considered

- `Merge` via `System.Reactive` (Rx.NET) — rejected to avoid adding a dependency for one use case.
- Async enumerable composition library — no suitable stdlib option; Channel is the idiomatic .NET approach.

---

## Decision 002 — `SecretInfo` stores only key names, not decoded values

**Status:** Accepted
**Date:** 2026-03-15

### Context

Secrets listed via `GetSecretsAsync` contain sensitive values. Storing decoded values in the `SecretInfo` model (passed to the UI grid) would mean secret values are always loaded in memory even when the user never reveals them.

### Decision

`SecretInfo.Keys` contains only key names. Values are fetched on demand via a separate `GetSecretValuesAsync` call, triggered by the user's reveal action in `SecretDetailPanel`. The panel caches the result for the lifetime of the panel instance.

### Consequences

- Secret values are never in the list model; they only exist in component-scoped state after explicit user action.
- One extra API call on first reveal per secret (not per key — the full map is fetched once and cached).
- Aligns with the principle of least privilege — no accidental logging or serialization of secret values.

### Alternatives considered

- Fetch all values in `GetSecretsAsync` and mask in the UI — rejected; values would be in memory even if never revealed, and any accidental logging would expose them.

---

## Decision 003 — Feature 6 reuses `OpenShellAsync` rather than adding a new interface method

**Status:** Accepted
**Date:** 2026-03-15

### Context

The new "Open shell in pod" context menu action needs to `kubectl exec -it` into a specific pod and container. The existing `OpenShellAsync(string ns, string podName, string container)` on `IAksClient` already has the correct signature and implementation.

### Decision

Call the existing `OpenShellAsync` from the new context menu handler. No new interface method is added. The distinction is purely in the UI trigger point (Pod context menu item with sidecar-skip heuristic), not in the backend behavior.

### Consequences

- Zero interface churn for this feature.
- If shell behavior ever needs to diverge (e.g. shell binary detection, embedded terminal), a new method can be added at that point.

### Alternatives considered

- Add `OpenPodShellAsync` as a semantically distinct interface method — rejected as premature; the behavior is identical and the interface should not grow without reason.

---

## Decision 004 — `envFrom` resolution deferred for MVP

**Status:** Accepted
**Date:** 2026-03-15

### Context

Kubernetes containers can import all keys from a ConfigMap or Secret via `envFrom` (a bulk import). Resolving this requires an additional API call per source and produces an unbounded number of env var rows.

### Decision

For the MVP, detect non-empty `container.EnvFrom` and emit one synthetic `EnvVarDetail` row per source with `Name = "<all keys from configmap: {name}>"` (or `secret:`). Full key-by-key resolution is a follow-up.

### Consequences

- Users can see that a bulk import exists and which source it comes from, but cannot see individual keys in the panel.
- No additional API calls from `envFrom` sources at MVP.
- Follow-up work: resolve `envFrom` sources into individual rows, respecting `prefix` if set.

---

## Decision 005 — Container picker for multi-container pods uses a heuristic, not a dialog

**Status:** Accepted
**Date:** 2026-03-15

### Context

"Open shell in pod" and "Container Details" both need to target a specific container. Many pods have more than one container (application + sidecar). Presenting a picker dialog for every action adds friction for the common case where there is only one application container.

### Decision

Skip well-known sidecar containers (`istio-proxy`, `linkerd-proxy`) and use the first remaining container. If no containers remain after filtering, fall back to the first container in the list. Document this as a known limitation. A container picker can be added as a follow-up if the heuristic proves insufficient.

### Consequences

- Zero extra interaction for the majority case (single app container or Istio sidecar).
- Rare edge cases (custom sidecars, init containers masquerading as app containers) may target the wrong container. The user can work around this via `kubectl exec` directly.

### Alternatives considered

- Picker dialog on every action — rejected as too disruptive for the common case.
- Picker only when more than one non-sidecar container exists — viable follow-up.

---

## Decision 006 — HPA API version falls back silently to `autoscaling/v1`

**Status:** Accepted
**Date:** 2026-03-15

### Context

`autoscaling/v2` is available on Kubernetes 1.23+. Older clusters may return 404 for the v2 endpoint. The v1 API only exposes CPU utilization, not custom metrics.

### Decision

Try `AutoscalingV2` first. If an `HttpOperationException` with `HttpStatusCode.NotFound` is thrown, fall back to `AutoscalingV1` without surfacing an error to the user. The HPA badge will show CPU data only for v1 clusters; the metrics detail panel will show an empty custom metrics list.

### Consequences

- The feature works transparently on both old and new clusters.
- Users on v1 clusters see CPU data only — this is the most common metric and covers most use cases.
- No user-visible error for a cluster capability difference that is not actionable.

### Alternatives considered

- Always require v2 and show an error on older clusters — rejected; degrades experience unnecessarily.
- Detect cluster version at startup and select API accordingly — over-engineered for a single feature.
