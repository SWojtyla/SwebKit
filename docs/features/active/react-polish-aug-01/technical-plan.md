# Technical plan: React/Tauri UX follow-up

## 1. Redis – single namespace-tree key list

### Goal
Bring back the namespace tree and make it the only key list, driven by the editable separator, while keeping the load-more/load-all/count features from the current flat list.

### Symbols to modify

- `web/src/components/redis/RedisPage.tsx`
  - Add `NamespaceNode` type and `buildNamespaceTree(keys: string[], separator: string): NamespaceNode[]`.
  - Add state:
    - `namespaceFilter: string | null`
    - `expandedNamespaces: Set<string>`
  - Compute `namespaceTree` with `useMemo` over `displayKeys` and `separator`.
  - Compute `filteredKeys`:
    - if `namespaceFilter` is set, include keys that equal the filter or start with `filter + separator`;
    - otherwise include all `displayKeys`.
  - Add `renderNamespaceNode(node, depth)` recursive helper:
    - chevron toggle for expansion;
    - namespace button that sets `namespaceFilter` (or clears it when already selected);
    - leaf key rows with the existing `data-testid="redis-key-${key}"` and `data-testid="redis-key-checkbox-${key}"` in batch mode.
  - Remove the separate flat `displayKeys.map(...)` list and replace it with the tree render.
  - Keep separator input, key count, and `Load more` / `Load all` controls at the bottom.

### Notes
- Default tree expansion will include all namespace nodes so the existing e2e selectors (`redis-key-user:1001`, etc.) remain visible.
- Keys without a separator will be rendered as top-level leaves.

## 2. Storage – blob-name routing fix

### Problem
Blob names like `env/prod.json` contain `/`. The sidecar route `/api/storage/{accountId}/containers/{container}/blobs/{blobName}/properties` treats the slash as a route separator and returns 404.

### Fix
Move `blobName` out of the route path and into a query/body parameter for all blob-scoped endpoints.

### Sidecar endpoints (`src-sidecar/Endpoints/StorageEndpoints.cs`)

Change the following from route `/{blobName}/...` to operation-specific paths with `?blobName=...`:

- `GET /api/storage/{accountId}/containers/{container}/blobs/properties?blobName=...`
- `GET /api/storage/{accountId}/containers/{container}/blobs/content?blobName=...`
- `GET /api/storage/{accountId}/containers/{container}/blobs/sas?blobName=...&expiryMinutes=...`
- `GET /api/storage/{accountId}/containers/{container}/blobs/versions?blobName=...`
- `GET /api/storage/{accountId}/containers/{container}/blobs/versions/compare?blobName=...&baseVersionId=...&compareVersionId=...`
- `POST /api/storage/{accountId}/containers/{container}/blobs/versions/restore?blobName=...&versionId=...`
- `POST /api/storage/{accountId}/containers/{container}/blobs/upload?blobName=...` (multipart)
- `POST /api/storage/{accountId}/containers/{container}/blobs/metadata?blobName=...` (body still metadata)
- `POST /api/storage/{accountId}/containers/{container}/blobs/undelete?blobName=...`

The `container` stays in the route; only `blobName` moves to the query string or form body.

### Frontend updates

- `web/src/lib/hooks.ts`
  - `useBlobProperties`, `useBlobContent`, `useBlobSasUrl`, `useBlobVersions`, `useBlobVersionComparison`, `useUploadBlob`, `useSetBlobMetadata`, `useRestoreBlobVersion`, `useUndeleteBlob` – update URL builders to pass `blobName` as a query parameter.
- `web/src/components/storage/StoragePage.tsx`
  - Update `handleDownloadBlob` to use the new `/content?blobName=...` URL.

## 3. App feedback / notifications

Use the existing in-app `useNotification()` toast system from `web/src/components/layout/NotificationSystem.tsx`.

### Components to add toasts to

- `web/src/components/settings/GeneralSettings.tsx`
  - Export success: `"Settings exported"`
  - Export error: `"Export failed"` with error body.
  - Import success: `"Settings imported"` (keep the restart note as body).
  - Import error: `"Import failed"` with error body.
- `web/src/components/aks/DeploymentsTab.tsx`
  - Restart success/error.
  - Scale success/error.
- `web/src/components/aks/HpaTab.tsx`
  - Scale, delete, enable/disable success/error.
- `web/src/components/aks/CronJobsTab.tsx`
  - Suspend/resume success/error.
- `web/src/components/aks/PodsTab.tsx`
  - Delete pod success/error.
- `web/src/components/storage/StoragePage.tsx`
  - Upload, copy, metadata save, version restore, undelete success/error.
- `web/src/components/aks/AksPage.tsx`
  - Context-menu `fetch` actions (ingress delete, HTTPRoute delete, statefulset restart/scale, helm rollback, cronjob trigger) should use `apiSend` and show a toast or error.

## 4. AKS action refresh

### Problem
Mutations such as `useAksRestartDeployment` invalidate `['aks-deployments', dep.namespace]` and `['aks-pods', dep.namespace]`, but the active table uses `['aks-deployments', namespaceToken]`. The table does not refresh.

### Fix
Update the AKS mutation hooks in `web/src/lib/hooks.ts` to invalidate the base query key so every active table refetches:

- `useAksRestartDeployment`: invalidate `['aks-deployments']` and `['aks-pods']`.
- `useAksScaleDeployment`: invalidate `['aks-deployments']`.
- `useAksDeletePod`: invalidate `['aks-pods']` (and `['aks-deployments']` since pod deletion can affect readiness counts).
- `useAksScaleHpa`: invalidate `['aks-hpas']`.
- `useAksDeleteHpa`: invalidate `['aks-hpas']`.
- `useAksSetHpaScalingEnabled`: invalidate `['aks-hpas']`.
- `useAksSuspendCronJob`: invalidate `['aks-cronjobs']`.

In the UI components, switch from `mutate()` to `mutateAsync()` wrapped in `try/catch` so the toast can report the actual error.

## 5. AKS performance improvements

### 5.1 Cache namespace list

- `src/SwebKit.Kubernetes/AksClient/KubernetesAksClient.cs`
  - Add a short-lived namespace cache:
    ```csharp
    private readonly Lock _namespaceCacheLock = new();
    private (IReadOnlyList<string> Namespaces, DateTimeOffset CachedAt)? _namespaceCache;
    ```
  - In `GetNamespacesAsync`, return cached result if younger than ~30 seconds; otherwise list and cache.
  - The cache is per client instance; context switches create a new client, so stale data is naturally discarded.

### 5.2 Cluster-scoped list fast path for `*`

- `src/SwebKit.Kubernetes/AksClient/KubernetesAksClient.cs`
  - Override the multi-namespace overloads (or branch inside `FanOutNamespacesAsync`) to detect the `"*"` sentinel.
  - When `"*"` is requested, try the Kubernetes `List...ForAllNamespacesAsync` variants first:
    - `ListPodForAllNamespacesAsync`
    - `ListDeploymentForAllNamespacesAsync`
    - `ListServiceForAllNamespacesAsync`
    - `ListConfigMapForAllNamespacesAsync`
    - `ListSecretForAllNamespacesAsync` (also used for Helm releases)
    - `ListHorizontalPodAutoscalerForAllNamespacesAsync`
    - `ListStatefulSetForAllNamespacesAsync`
    - `ListJobForAllNamespacesAsync`
    - `ListCronJobForAllNamespacesAsync`
    - `ListEventForAllNamespacesAsync`
    - `ListIngressForAllNamespacesAsync`
  - Filter results to the selected namespaces client-side and fall back to per-namespace fan-out on `Forbidden`/`Unauthorized`/`NotFound`.

### 5.3 Reduce unconditional pod fetch on `AksPage`

- `web/src/components/aks/AksPage.tsx`
  - Change `useAksPods(namespaceToken)` so it is only enabled when a pod is selected in the URL (`podParam`) or the active tab is `pods`.
  - This avoids listing pods on every other tab just for the Multi-Pod Logs button.

## 6. Tests

- Update `web/e2e/redis.spec.ts` comments and any selectors that rely on a flat list instead of the tree (key testids stay the same; only remove the "Namespace tree removed" comment).
- Ensure `web/e2e/storage.spec.ts` virtual-folder test still passes after moving `blobName` to query parameters.
- Re-run `npm run build`, `dotnet build src-sidecar/SwebKit.Sidecar.csproj`, `dotnet test tests/SwebKit.Sidecar.Tests`, and `npx playwright test`.

## 7. Out of scope

- No new MAUI/Blazor features; this is React/Tauri parity only.
- No large redesign of the AKS resource views; only action feedback and performance.
