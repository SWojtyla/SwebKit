# Test Plan — AKS New Capabilities

---

title: "Test Plan - AKS New Capabilities"
owner: ""
status: "Planned"
created: "2026-03-15"
updated: "2026-03-15"

---

## Goal

Validate that each new capability works correctly in isolation and does not regress existing AKS behavior. Focus on unit tests for backend logic and manual scenario checks for UI flows.

## Scope

- In scope: all 6 new features, `DemoAksClient` behavior, `KubernetesAksClient` method logic, UI acceptance scenarios
- Out of scope: live cluster integration tests (deferred), Playwright automation (deferred)

## Main scenarios (priority)

1. **Multi-pod logs** — Open "Logs for all pods" for a deployment with 3 replicas. Lines from all three pods appear with distinct colors and pod-name prefixes. Closing the panel cancels all streams cleanly.
2. **StatefulSets tab** — Switch to StatefulSets tab. List loads. A degraded StatefulSet (ReadyReplicas < Replicas) shows an orange badge. Restart and Scale work. YAML view and edit work.
3. **ConfigMap viewer** — Open ConfigMaps tab. Select a ConfigMap. Key/value table loads. Filter narrows the list. YAML view and edit work.
4. **Secret viewer** — Open Secrets tab. Only key names are visible initially. Click reveal on one key. Value appears. Click hide. Value clears. Switching to a different secret resets revealed state.
5. **Container details — ConfigMapRef** — Open Container Details for a pod with ConfigMapRef env vars. Values are resolved and shown inline.
6. **Container details — SecretRef** — SecretRef env vars show masked. Clicking reveal fetches and shows the value. Second click on a different key in the same secret does not make another API call (cache hit).
7. **HPA badge** — Deployments tab shows HPA badge for deployments with an HPA. Badge text reflects current/max/CPU%. Clicking opens detail panel with metrics and conditions.
8. **HPA absent** — Deployments with no HPA show no badge (no empty placeholder).
9. **Open shell in pod** — Right-clicking a pod and choosing "Open shell in pod" launches an external terminal with `kubectl exec -it` for the correct pod, namespace, and container. Sidecar containers are skipped.
10. **AutoRefresh pauses** — Auto-refresh pauses when any new panel is open. Closing the panel resumes refresh.

## Automated coverage

### Unit tests — `SwebKit.Kubernetes.Tests`

- `StreamDeploymentLogsAsync`: zero-pod case returns empty, cancellation token propagates to all per-pod streams.
- `GetStatefulSetsAsync`: maps all `StatefulSetInfo` fields correctly.
- `RestartStatefulSetAsync` / `ScaleStatefulSetAsync`: correct patch body sent.
- `GetConfigMapsAsync`: maps `Data` dictionary, excludes nothing unexpected.
- `GetSecretsAsync`: excludes Helm secrets and service-account tokens; returns only key names.
- `GetSecretValuesAsync`: base64 bytes decoded to UTF-8 strings correctly.
- `GetContainerDetailsAsync`: plain env vars, ConfigMapRef (resolved), SecretRef (unresolved), FieldRef all handled; ConfigMap calls batched by name; `envFrom` produces a synthetic flag row.
- `GetHpasAsync`: v2 mapping of `CurrentAverageUtilization`; v1 fallback on 404.

### Unit tests — `SwebKit.Core.Tests`

- `DemoAksClient.GetStatefulSetsAsync`: returns expected records; degraded record has `ReadyReplicas < Replicas`.
- `DemoAksClient.GetConfigMapsAsync` / `GetSecretsAsync`: return non-empty lists with expected key names.
- `DemoAksClient.GetSecretValuesAsync`: returns a non-empty dictionary.
- `DemoAksClient.GetContainerDetailsAsync`: includes at least one `ConfigMapRef` and one `SecretRef` entry.
- `DemoAksClient.GetHpasAsync`: all entries have `TargetName` matching a known demo deployment.
- `DemoAksClient.StreamDeploymentLogsAsync`: emits at least one line per demo pod; lines include `PodName`.

## Test data and setup

- Demo mode (`DemoAksClient`) covers all happy paths without a live cluster.
- Unit tests mock the `KubernetesClient` API responses directly.
- For HPA fallback tests: mock `AutoscalingV2` to throw `HttpOperationException(404)`.

## Manual checks

- Verify panel mutual exclusivity: open YAML, then open logs — YAML must close.
- Verify `AutoRefreshToggle` pauses on each new panel type and resumes on close.
- Verify no visible flicker when switching between ConfigMaps and Secrets tabs.
- Verify reveal cache: open `SecretDetailPanel`, reveal all keys, close and reopen the same secret — values should not be revealed again (state resets on component destroy).
- Verify HPA column renders gracefully on clusters without the HPA controller (empty column, no error).
- Verify "Open shell in pod" falls back to `cmd.exe` when `wt.exe` is not on PATH.

## Regression risks

- Existing Deployment log streaming (`PodLogView`) must not be affected by new `MultiPodLogView` component.
- Existing Deployment scale panel must work for `_scaleIsStatefulSet = false` path after the scale routing change.
- `GetResourceYamlAsync` new switch cases must not shadow or break existing `"deployment"`, `"pod"`, `"ingress"`, `"helm"` cases.

## Acceptance criteria

- All priority scenarios pass in demo mode.
- All listed unit tests pass.
- No regressions in existing Deployment/Pod/Ingress/Helm tab behavior.
- `AutoRefreshToggle` Paused condition is driven by `HasOpenPanel` and covers all new panels.

## Validation status

- Automated: Not started
- Manual: Not started

## Sign-off

- Owner:
- Date:
