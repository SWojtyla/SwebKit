# Technical Plan — UX Follow-up Batch

This plan lists the symbols to modify in control/data-flow order. No code changes should be made until the user explicitly approves this plan.

## Legend

- `BE` = backend sidecar / .NET core
- `FE` = React frontend
- `*` = new symbol

---

## 1. Settings import/export

### 1.1 Backend — register missing services

`src-sidecar/Program.cs`

```csharp
builder.Services.AddSingleton<UiStateRepository>();
builder.Services.AddSingleton<ReleaseRepository>();
builder.Services.AddSingleton<AppStateService>();
builder.Services.AddSingleton<ConfigurationBundleService>();
```

`ReleaseRepository` is also needed because `ConfigurationBundleService` depends on it.

### 1.2 Backend — new endpoints

New file: `src-sidecar/Endpoints/ConfigEndpoints.cs`

```csharp
public static class ConfigEndpoints
{
    public static void MapConfigEndpoints(this WebApplication app)
    {
        // Export the full app configuration bundle as JSON
        app.MapGet("/api/config/export", (ConfigurationBundleService svc) =>
        {
            var bundle = svc.Export();
            return Results.Text(svc.Serialize(bundle), "application/json");
        });

        // Import a previously exported bundle; replaces current config and persists
        app.MapPost("/api/config/import", async (ConfigurationBundleService svc, HttpRequest req) =>
        {
            using var reader = new StreamReader(req.Body);
            var json = await reader.ReadToEndAsync();
            var bundle = svc.Deserialize(json);
            await svc.ImportAsync(bundle);
            return Results.Ok();
        });
    }
}
```

`src-sidecar/Program.cs` — add `app.MapConfigEndpoints();` alongside the other `Map*Endpoints` calls.

> `ConfigurationBundleService` already implements `Export()`/`ImportAsync()`; this only exposes it over HTTP.

### 1.3 Frontend — API helpers

`web/src/lib/api.ts`

```ts
export async function exportSettings(): Promise<unknown> {
  return apiFetch<unknown>("/api/config/export");
}

export async function importSettings(bundle: unknown): Promise<void> {
  return apiSend("/api/config/import", "POST", bundle);
}
```

### 1.4 Frontend — hooks

`web/src/lib/hooks.ts`

```ts
export function useExportSettings() {
  return useMutation({ mutationFn: exportSettings });
}

export function useImportSettings() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: importSettings,
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ["profile"] });
      qc.invalidateQueries({ queryKey: ["user-settings"] });
      qc.invalidateQueries({ queryKey: ["config"] });
    },
  });
}
```

### 1.5 Frontend — UI

`web/src/components/settings/GeneralSettings.tsx`

- Add an **Export** button:
  - calls `exportSettings()`,
  - creates a `Blob`/`URL.createObjectURL`,
  - triggers download of `swebkit-settings-<iso-date>.json`.
- Add an **Import** button with a hidden `<input type="file" accept=".json,application/json">`:
  - on file select, `JSON.parse(text)`,
  - confirm with the user,
  - call `importSettings.mutate(bundle)`.

No profile model changes are required.

---

## 2. Service Bus active/DLQ/scheduled counts

### 2.1 Backend — populate stats during list

`src/SwebKit.Azure/ServiceBus/AzureServiceBusClient.cs`

In `ListQueuesAsync`, after the result list is built, populate `Stats` for every queue by calling `GetEntityStatsAsync` with limited parallelism:

```csharp
await Parallel.ForEachAsync(result,
    new ParallelOptions { MaxDegreeOfParallelism = 5, CancellationToken = ct },
    async (entity, token) =>
    {
        entity.Stats = await GetEntityStatsAsync(entity.EntityPath, token).ConfigureAwait(false);
    }).ConfigureAwait(false);
```

Do the same in `ListSubscriptionsAsync`. `TryAddScopedQueueAsync` should also set `Stats` when it adds the scoped queue.

`ListTopicsAsync` does not expose runtime counts via the admin SDK; leave `Stats` null.

### 2.2 Frontend — selected-entity stats

The URL-driven `selectedEntity` in `ServiceBusPage` only carries `entityPath` and `name`, so it loses stats. Add a per-entity stats query.

`web/src/lib/hooks.ts`

```ts
export function useSbEntityStats(nsId: string | null, entityPath: string | null) {
  return useQuery({
    queryKey: ["sb-entity-stats", nsId, entityPath],
    queryFn: () => apiFetch<SbEntityStats>(`/api/servicebus/${nsId}/entities/${encodeURIComponent(entityPath!)}/stats`),
    enabled: !!nsId && !!entityPath,
  });
}
```

`web/src/components/service-bus/ServiceBusPage.tsx`

```tsx
const entityStats = useSbEntityStats(selectedNsId, selectedEntity?.entityPath ?? null);
const selectedEntityWithStats = useMemo<SbEntityInfo | null>(() => {
  if (!selectedEntity) return null;
  return { ...selectedEntity, stats: entityStats.data ?? undefined };
}, [selectedEntity, entityStats.data]);
```

Use `selectedEntityWithStats?.stats` for the **Active**, **DLQ**, and **Scheduled** tab labels.

`EntityTree.tsx` already renders `entity.stats` once it exists; no structural change required.

---

## 3. AKS fixes and actions

### 3.1 Deployment status

`src/SwebKit.Kubernetes/AksClient/KubernetesAksClient.cs`

In `GetDeploymentsAsync`, replace:

```csharp
Status = d.Status?.Conditions?.FirstOrDefault(c => c.Type == "Available")?.Status ?? "Unknown",
```

with:

```csharp
Status = DeriveDeploymentStatus(d.Status?.Conditions),
```

Add helper:

```csharp
private static string DeriveDeploymentStatus(IEnumerable<V1DeploymentCondition>? conditions)
{
    if (conditions is null) return "Unknown";
    var byType = conditions.ToDictionary(c => c.Type, c => c, StringComparer.OrdinalIgnoreCase);
    if (byType.TryGetValue("Available", out var available)
        && string.Equals(available.Status, "True", StringComparison.OrdinalIgnoreCase))
        return "Available";
    if (byType.TryGetValue("Progressing", out var progressing)
        && string.Equals(progressing.Status, "True", StringComparison.OrdinalIgnoreCase))
        return "Progressing";
    if (byType.TryGetValue("Available", out var unavailable)
        && string.Equals(unavailable.Status, "False", StringComparison.OrdinalIgnoreCase))
        return "Unavailable";
    return "Unknown";
}
```

`web/src/components/aks/DeploymentsTab.tsx` — `StatusBadge` already maps `Available`/`Progressing`/other, so `Unavailable` falls to the red default. No change required unless the wording needs to be `unavailable`.

### 3.2 Pods — hide completed

`web/src/components/aks/PodsTab.tsx`

- New state:

```tsx
const [hideCompleted, setHideCompleted] = useState(true);
```

- Helper:

```tsx
const isCompletedPod = (pod: PodInfo) =>
  pod.phase === "Succeeded" || pod.status?.toLowerCase() === "completed";
```

- UI: add a checkbox labelled **Hide completed** near the table header.
- Filter:

```tsx
const visiblePods = hideCompleted ? pods.filter((p) => !isCompletedPod(p)) : pods;
```

Render `visiblePods` instead of `pods`.

### 3.3 HPA — scale, delete, KEDA pause, YAML

#### 3.3.1 Interface

`src/SwebKit.Core/Abstractions/IAksClient.cs`

After `SetHpaScalingEnabledAsync`, add:

```csharp
Task ScaleHpaAsync(string ns, string hpaName, int minReplicas, int maxReplicas, CancellationToken ct = default)
    => Task.FromException(new NotSupportedException("HPA scaling is not supported by this client."));

Task DeleteHpaAsync(string ns, string hpaName, CancellationToken ct = default)
    => Task.FromException(new NotSupportedException("HPA deletion is not supported by this client."));
```

#### 3.3.2 Real client

`src/SwebKit.Kubernetes/AksClient/KubernetesAksClient.Workloads.cs`

```csharp
public async Task ScaleHpaAsync(string ns, string hpaName, int minReplicas, int maxReplicas, CancellationToken ct = default)
{
    await WithAuthRetryAsync(async () =>
    {
        var (v2, v1) = await ReadHpaAsync(ns, hpaName, ct).ConfigureAwait(false);
        var patchJson = JsonSerializer.Serialize(new { spec = new { minReplicas, maxReplicas } });
        var patch = new V1Patch(patchJson, V1Patch.PatchType.MergePatch);
        if (v2 is not null)
            await _client.AutoscalingV2.PatchNamespacedHorizontalPodAutoscalerAsync(patch, hpaName, ns, cancellationToken: ct).ConfigureAwait(false);
        else
            await _client.AutoscalingV1.PatchNamespacedHorizontalPodAutoscalerAsync(patch, hpaName, ns, cancellationToken: ct).ConfigureAwait(false);
    }).ConfigureAwait(false);
}

public async Task DeleteHpaAsync(string ns, string hpaName, CancellationToken ct = default)
{
    await WithAuthRetryAsync(async () =>
    {
        var (v2, v1) = await ReadHpaAsync(ns, hpaName, ct).ConfigureAwait(false);
        if (v2 is not null)
            await _client.AutoscalingV2.DeleteNamespacedHorizontalPodAutoscalerAsync(hpaName, ns, cancellationToken: ct).ConfigureAwait(false);
        else
            await _client.AutoscalingV1.DeleteNamespacedHorizontalPodAutoscalerAsync(hpaName, ns, cancellationToken: ct).ConfigureAwait(false);
    }).ConfigureAwait(false);
}
```

> `SetHpaScalingEnabledAsync` already supports KEDA by toggling `autoscaling.keda.sh/paused` on the owning `ScaledObject`.

#### 3.3.3 Demo client

`src/SwebKit.Core/Services/DemoAksClient.cs`

- Implement `ScaleHpaAsync` by updating `_hpaOverrides` min/max (or add a new override dictionary).
- Implement `DeleteHpaAsync` by removing the item from `_hpas`.

#### 3.3.4 Sidecar endpoints

`src-sidecar/Endpoints/AksEndpoints.cs`

Add after the existing `/api/aks/{ns}/hpas` route:

```csharp
// Update HPA min/max replicas
app.MapPost("/api/aks/{ns}/hpas/{name}/scale", async (
    string ns,
    string name,
    ScaleHpaRequest dto,
    ProfileRepository profile,
    DemoModeService demo,
    CancellationToken ct) =>
{
    var client = GetClient(profile, demo);
    var namespaces = await ResolveNamespacesAsync(client, ns, ct);
    await Task.WhenAll(namespaces.Select(n => client.ScaleHpaAsync(n, name, dto.MinReplicas, dto.MaxReplicas, ct)));
    return Results.Ok();
});

// Delete HPA
app.MapDelete("/api/aks/{ns}/hpas/{name}", async (
    string ns,
    string name,
    ProfileRepository profile,
    DemoModeService demo,
    CancellationToken ct) =>
{
    var client = GetClient(profile, demo);
    var namespaces = await ResolveNamespacesAsync(client, ns, ct);
    await Task.WhenAll(namespaces.Select(n => client.DeleteHpaAsync(n, name, ct)));
    return Results.NoContent();
});

// Enable/disable autoscaling (plain freeze or KEDA pause)
app.MapPost("/api/aks/{ns}/hpas/{name}/scaling-enabled", async (
    string ns,
    string name,
    SetScalingEnabledRequest dto,
    ProfileRepository profile,
    DemoModeService demo,
    CancellationToken ct) =>
{
    var client = GetClient(profile, demo);
    var namespaces = await ResolveNamespacesAsync(client, ns, ct);
    await Task.WhenAll(namespaces.Select(n => client.SetHpaScalingEnabledAsync(n, name, dto.Enabled, ct)));
    return Results.Ok();
});
```

New DTOs in `SwebKit.Core.Models` or inline anonymous records:

```csharp
public sealed class ScaleHpaRequest
{
    public int MinReplicas { get; set; }
    public int MaxReplicas { get; set; }
}

public sealed class SetScalingEnabledRequest
{
    public bool Enabled { get; set; }
}
```

#### 3.3.5 Frontend hooks

`web/src/lib/api.ts`

```ts
export async function scaleHpa(ns: string, name: string, minReplicas: number, maxReplicas: number): Promise<void> {
  return apiSend(`/api/aks/${encodeURIComponent(ns)}/hpas/${encodeURIComponent(name)}/scale`, "POST", { minReplicas, maxReplicas });
}

export async function deleteHpa(ns: string, name: string): Promise<void> {
  return apiSend(`/api/aks/${encodeURIComponent(ns)}/hpas/${encodeURIComponent(name)}`, "DELETE");
}

export async function setHpaScalingEnabled(ns: string, name: string, enabled: boolean): Promise<void> {
  return apiSend(`/api/aks/${encodeURIComponent(ns)}/hpas/${encodeURIComponent(name)}/scaling-enabled`, "POST", { enabled });
}
```

`web/src/lib/hooks.ts`

```ts
export function useAksScaleHpa(ns: string) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: ({ name, minReplicas, maxReplicas }: { name: string; minReplicas: number; maxReplicas: number }) =>
      scaleHpa(ns, name, minReplicas, maxReplicas),
    onSuccess: () => qc.invalidateQueries({ queryKey: ["aks-hpas", ns] }),
  });
}

export function useAksDeleteHpa(ns: string) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (name: string) => deleteHpa(ns, name),
    onSuccess: () => qc.invalidateQueries({ queryKey: ["aks-hpas", ns] }),
  });
}

export function useAksSetHpaScalingEnabled(ns: string) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: ({ name, enabled }: { name: string; enabled: boolean }) =>
      setHpaScalingEnabled(ns, name, enabled),
    onSuccess: () => qc.invalidateQueries({ queryKey: ["aks-hpas", ns] }),
  });
}
```

#### 3.3.6 Frontend UI

`web/src/components/aks/HpaTab.tsx`

- Add `onViewYaml?: (kind: string, name: string, namespace: string) => void` prop.
- For each row, add action buttons:
  - **Edit**: open a small inline editor (or modal) with `minReplicas`/`maxReplicas` inputs and **Save**.
  - **-** / **+** quick buttons to decrement/increment `minReplicas` or `maxReplicas` (or both).
  - **Enable/Disable scaling**: call `setHpaScalingEnabled`.
    - For a KEDA-managed HPA, the label should be "Pause/Resume".
  - **Delete**: confirm, then call `deleteHpa`.
  - **View YAML**: call `onViewYaml("hpa"?, hpa.name, hpa.namespace)`.

`web/src/components/aks/AksPage.tsx`

```tsx
{activeTab === "hpa" && (
  <HpaTab
    ns={namespaceToken}
    isMulti={isMultiNamespace}
    onViewYaml={(kind, name, namespace) => openYaml(kind, name, namespace)}
  />
)}
```

> Note: `GetResourceYamlAsync` supports `kind="hpa"`/`"horizontalpodautoscaler"` already, but verify the kind string passed from the UI matches the sidecar route (`/api/aks/{ns}/yaml/{kind}/{name}`). If the backend only accepts `hpa`, pass `hpa`.

### 3.4 CronJobs — suspend

#### 3.4.1 Sidecar endpoint

`src-sidecar/Endpoints/AksEndpoints.cs`

```csharp
app.MapPost("/api/aks/{ns}/cronjobs/{name}/suspend", async (
    string ns,
    string name,
    SuspendCronJobRequest dto,
    ProfileRepository profile,
    DemoModeService demo,
    CancellationToken ct) =>
{
    var client = GetClient(profile, demo);
    var namespaces = await ResolveNamespacesAsync(client, ns, ct);
    await Task.WhenAll(namespaces.Select(n => client.SuspendCronJobAsync(n, name, dto.Suspend, ct)));
    return Results.Ok();
});
```

`SuspendCronJobRequest`:

```csharp
public sealed class SuspendCronJobRequest
{
    public bool Suspend { get; set; }
}
```

`KubernetesAksClient.Workloads.cs` already has `SuspendCronJobAsync`; `DemoAksClient` should mirror it.

#### 3.4.2 Frontend

`web/src/lib/api.ts`:

```ts
export async function suspendCronJob(ns: string, name: string, suspend: boolean): Promise<void> {
  return apiSend(`/api/aks/${encodeURIComponent(ns)}/cronjobs/${encodeURIComponent(name)}/suspend`, "POST", { suspend });
}
```

`web/src/lib/hooks.ts`:

```ts
export function useAksSuspendCronJob(ns: string) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: ({ name, suspend }: { name: string; suspend: boolean }) => suspendCronJob(ns, name, suspend),
    onSuccess: () => qc.invalidateQueries({ queryKey: ["aks-cronjobs", ns] }),
  });
}
```

`web/src/components/aks/CronJobsTab.tsx`:

- Add a **Suspend** / **Resume** button per row.
- Call `useAksSuspendCronJob` and invalidate cronjob query.

`AksPage.showCronJobMenu` should also offer **Suspend/Resume** as a context-menu item.

### 3.5 Helm detail panel — manifest, notes, values

#### 3.5.1 Backend — release notes

`src/SwebKit.Core/Abstractions/IAksClient.cs`

```csharp
Task<string> GetHelmReleaseNotesAsync(string ns, string releaseName, CancellationToken ct = default)
    => Task.FromException<string>(new NotSupportedException("Helm release notes are not supported."));
```

`src/SwebKit.Kubernetes/AksClient/KubernetesAksClient.cs` (or `KubernetesAksClient.Helm.cs`):

```csharp
public async Task<string> GetHelmReleaseNotesAsync(string ns, string releaseName, CancellationToken ct = default)
{
    return await WithAuthRetryAsync(async () =>
    {
        var helmArgs = new KubectlArgumentBuilder()
            .WithHelmGlobalFlags(_kubeconfigPath, _kubeconfigContext)
            .Add("get").Add("notes").Add(releaseName)
            .Add("--namespace").Add(ns);

        var output = await KubectlExecutor.ExecuteAsync(helmArgs, ct).ConfigureAwait(false);
        return output ?? "# No release notes";
    }).ConfigureAwait(false);
}
```

> If `KubectlExecutor` is not available, reuse the `Process` pattern from `PreviewHelmUpgradeAsync`.

`DemoAksClient.cs` returns a static notes string.

#### 3.5.2 Sidecar endpoint

`src-sidecar/Endpoints/AksEndpoints.cs`

```csharp
app.MapGet("/api/aks/{ns}/helm-releases/{name}/notes", async (
    string ns,
    string name,
    ProfileRepository profile,
    DemoModeService demo,
    CancellationToken ct) =>
{
    var client = GetClient(profile, demo);
    var notes = await client.GetHelmReleaseNotesAsync(ns, name, ct);
    return Results.Ok(new { notes });
});
```

#### 3.5.3 Frontend

`web/src/lib/api.ts`:

```ts
export async function getHelmReleaseNotes(ns: string, name: string): Promise<{ notes: string }> {
  return apiFetch<{ notes: string }>(`/api/aks/${encodeURIComponent(ns)}/helm-releases/${encodeURIComponent(name)}/notes`);
}
```

`web/src/lib/hooks.ts`:

```ts
export function useAksHelmNotes(ns: string, name: string | null) {
  return useQuery({
    queryKey: ["aks-helm-notes", ns, name],
    queryFn: () => getHelmReleaseNotes(ns, name!),
    enabled: !!name,
  });
}
```

`web/src/components/aks/HelmDetailPanel.tsx`

- Change tab list to `history | values | manifest | notes`.
- **Values** tab: keep user/computed values toggle, but render with `highlightYaml` / `YamlViewer` (or a highlighted `<pre>`). Do not use the plain green-on-black block.
- **Manifest** tab: call `GET /api/aks/{ns}/yaml/helm/{name}` (which `GetResourceYamlAsync` already supports for `kind=helm`) and render with `YamlViewer` / `highlightYaml`.
- **Notes** tab: render the notes output in a `<pre>`; if it is YAML-ish, apply `highlightYaml`.
- **History** tab: unchanged.

---

## 4. Redis browser improvements

### 4.1 Single list and separator

`web/src/components/redis/RedisPage.tsx`

- Remove the duplicate flat `filteredKeys.map(...)` list below the namespace tree.
- The namespace tree becomes the single key browser.
- Fix `buildNamespaceTree` so keys without a namespace still appear:

```typescript
if (parts.length < 2) {
  // key has no separator — show it directly under a top-level "(no prefix)" node
  let fallback = roots.get("(no prefix)");
  if (!fallback) {
    fallback = { name: "(no prefix)", path: "", children: new Map(), keys: [], keyCount: 0 };
    roots.set("(no prefix)", fallback);
  }
  fallback.keys.push(key);
  fallback.keyCount += 1;
  continue;
}
```

### 4.2 Editable separator

Add local state:

```tsx
const [separatorInput, setSeparatorInput] = useState(redisConfig?.namespaceSeparator?.trim() || ":");
```

In the key-browser header, render an input:

```tsx
<input
  value={separatorInput}
  onChange={(e) => setSeparatorInput(e.target.value)}
  onBlur={() => {
    // Persist to the active cache's profile config
    if (!resolvedCacheId || !profile) return;
    const next = { ...profile };
    const cache = next.config.redisConfig!.caches.find((c) => c.id === resolvedCacheId)!;
    cache.namespaceSeparator = separatorInput;
    updateProfile.mutate(next);
  }}
  className="..."
  title="Namespace separator"
/>
```

Use `separatorInput` (not `namespaceSeparator`) when building the tree and filtering keys. Reset it when `resolvedCacheId` changes.

### 4.3 Key count and load-more / load-all

Display:

```tsx
<div className="text-xs text-muted-foreground">
  <strong>{displayKeys.length}</strong> loaded
  {estimatedTotalKeys !== undefined && estimatedTotalKeys > 0 && (
    <> of <strong>~{estimatedTotalKeys}</strong> in database</>
  )}
</div>
```

`estimatedTotalKeys` derived from `serverInfo.data?.databases`:

```tsx
const estimatedTotalKeys = useMemo(() => {
  const db = serverInfo.data?.databases?.find((d) => d.index === 0);
  if (db?.keys) return Number(db.keys);
  return undefined;
}, [serverInfo.data]);
```

Refactor scan pagination so `displayKeys` is an accumulated array. `handleLoadMore` advances the `cursor` to the value returned by the last scan; a `useEffect` appends the new `scanResult.data.keys` to `allKeys` whenever the cursor changes.

Add a **Load all** button visible when `estimatedTotalKeys` is known and `< 1000` (or unconditionally available). If `estimatedTotalKeys < 1000` and no keys are loaded, the first scan can be followed automatically until `isComplete`.

`handleLoadAll`:

```tsx
const handleLoadAll = () => {
  setIsLoadingAll(true);
  handleLoadMore(); // kicks off the chain
};
```

Effect:

```tsx
useEffect(() => {
  if (!scanResult.data) return;
  setAllKeys((prev) => [...prev, ...scanResult.data!.keys]);
  if (isLoadingAll && !scanResult.data!.isComplete) {
    setCursor(scanResult.data!.cursor);
  } else {
    setIsLoadingAll(false);
  }
}, [scanResult.data, isLoadingAll]);
```

### 4.4 Batch selection in tree

The current flat list has selection checkboxes. Port them to the tree:

- In `renderNamespaceNode`, add a checkbox for each key row.
- A namespace checkbox selects/deselects every key under that node (recursively).
- Keep `selectedKeys` state and `batchMode` unchanged.

No backend changes are required for the Redis browser.

---

## 5. API Client panel ratio

### 5.1 Default widths

`web/src/components/api-client/ApiClientPage.tsx`

Change:

```tsx
<ResizablePanels initialWidths={[260, 360, null]} minWidths={[180, 260, 260]} ...>
```

to:

```tsx
<ResizablePanels
  initialWidths={[260, "45%", null]}
  minWidths={[180, 380, 280]}
  ...
>
```

This makes the request editor at least 380 px wide and defaults to 45% of the width, while letting the response panel fill the remainder.

### 5.2 Request editor wrap/fit

`web/src/components/api-client/RequestEditor.tsx`

- URL / method row: add `flex-wrap` to the flex container so controls wrap when narrow.
- Params/headers rows: change from `flex items-center gap-2` to `flex flex-wrap items-center gap-2` and add `min-w-0` to value inputs:

```tsx
<div className="mb-1 flex flex-wrap items-center gap-2">
  <input ... className="w-32 ..." />
  <input ... className="min-w-0 flex-1 ..." />
</div>
```

- Tabs row: add `flex-wrap` so method/body/auth tabs do not overflow.

---

## Verification commands

Run after implementation:

```bash
cd /home/ubuntu/repos/SwebKit
npm run build
dotnet build src-sidecar/SwebKit.Sidecar.csproj
dotnet test tests/SwebKit.Sidecar.Tests
cd web && npx playwright test
```
