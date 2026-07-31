# AKS Internal Refactor — Technical Plan

## Control and data flow

1. `AksPage` owns the URL (`tab`, `ns`, `pod`, `yaml`, `helm`, `container`, `logs`, `logsNs`) and renders the workspace provider.
2. `AksWorkspaceProvider` exposes navigation/action callbacks (`openYaml`, `openLogs`, `openMultiPodLogs`, `openContainerDetails`, `setHelmRelease`, `setSelectedSecret`, `requestConfirm`, `setPodKey`).
3. Each tab fetches its own data via existing `useAksXxx(ns)` hooks, defines its columns, and builds row actions using `useAksWorkspace` and `useNotifyMutation`.
4. `ResourceTable<T>` renders the shared table (header, namespace column, empty state, hover, row testids, context menu event).
5. When a user right-clicks a row, the tab returns `ContextMenuItem[]`; `AksPage` sets the global `ContextMenu` state and renders the menu.
6. Mutations always route through `useNotifyMutation`, which toasts success/error and invalidates the configured base query keys.

## New files and symbols

### `web/src/components/aks/shared/AksWorkspaceContext.tsx`

```tsx
interface AksWorkspaceContextValue {
  openYaml(kind: string, name: string, namespace: string): void;
  openLogs(pod: PodInfo): void;
  openMultiPodLogs(pods: PodInfo[]): void;
  openContainerDetails(podName: string, namespace: string): void;
  setHelmRelease(rel: HelmReleaseInfo | null): void;
  setSelectedSecret(secret: SecretInfo | null): void;
  setPodKey(pod: PodInfo | null, options?: { clearOthers?: boolean }): void;
  requestConfirm(message: string, resourceName: string, onConfirm: () => void): void;
  resolvePodsForSelector(namespace: string, selectorLabels: Record<string, string>): Promise<PodInfo[]>;
}

export const AksWorkspaceContext = createContext<AksWorkspaceContextValue | null>(null);
export function AksWorkspaceProvider({ children }: { children: ReactNode }): JSX.Element;
export function useAksWorkspace(): AksWorkspaceContextValue;
```

### `web/src/components/aks/shared/ResourceTable.tsx`

```tsx
export interface Column<T extends { name: string; namespace: string }> {
  header: React.ReactNode;
  cell: (row: T) => React.ReactNode;
  className?: string;
}

export interface ResourceTableProps<T extends { name: string; namespace: string }> {
  data?: T[];
  isLoading: boolean;
  isMulti?: boolean;
  columns: Column<T>[];
  keyExtractor?: (row: T) => string;
  onRowClick?: (row: T) => void;
  onRowContextMenu?: (e: React.MouseEvent, row: T) => void;
  emptyMessage: string;
  testIdPrefix: string;
}

export function ResourceTable<T extends { name: string; namespace: string }>(
  props: ResourceTableProps<T>,
): JSX.Element;
```

### `web/src/lib/useNotifyMutation.ts` (new file, also re-exported from `web/src/lib/hooks.ts` for discoverability)

```tsx
interface NotifyMutationOptions<TData, TVariables> {
  mutationFn: (vars: TVariables) => Promise<TData>;
  successMessage: string | ((data: TData, vars: TVariables) => string);
  errorPrefix: string;
  invalidateKeys?: string[][];
}

export function useNotifyMutation<TData = unknown, TVariables = void>(
  options: NotifyMutationOptions<TData, TVariables>,
): UseMutationResult<TData, Error, TVariables>;
```

## Existing files to modify

### `web/src/components/aks/AksPage.tsx`

- Remove all `show*Menu` builders (~250 lines). Keep only the `ContextMenu` rendering and a generic `handleContextMenu(items, x, y)`.
- Move `openYaml`, `openLogs`, `openMultiPodLogs`, `openContainerDetails`, `setHelmRelease`, `setSelectedSecret`, `setPodKey`, `requestConfirm`, `resolvePodsForSelector` into `AksWorkspaceProvider`.
- Render `<AksWorkspaceProvider>` around the page content.
- Keep header, tab bar, namespace/context selectors, detail panels, confirm bar, and context menu portal.

### All `web/src/components/aks/*Tab.tsx` files

- Replace inline `<table>` markup with `ResourceTable`.
- Use `useAksWorkspace` for navigation callbacks.
- Use `useNotifyMutation` for restart/scale/delete/suspend/toggle actions.
- Provide `onRowContextMenu` that returns `ContextMenuItem[]`.
- `PodsTab` and `HpaTab` keep their custom columns and inline forms, but still use `ResourceTable` for the grid.

### `web/src/lib/hooks.ts`

- Add `export { useNotifyMutation } from "./useNotifyMutation";`.
- Refactor `useAksRestartDeployment`, `useAksScaleDeployment`, `useAksDeletePod`, `useAksScaleHpa`, `useAksDeleteHpa`, `useAksSetHpaScalingEnabled`, `useAksSuspendCronJob` to delegate to `useNotifyMutation` with the correct messages and `invalidateKeys`.
- Keep the query hooks (`useAksDeployments`, `useAksPods`, etc.) unchanged except for moving invalidation out into `useNotifyMutation`.

### `web/src/components/aks/ContextMenu.tsx`

- No interface changes needed; `ContextMenuItem` is already exported.

## Step-by-step implementation order

1. Create `useNotifyMutation` and update the AKS mutation hooks in `hooks.ts`.
2. Create `AksWorkspaceContext` and move navigation callbacks out of `AksPage`.
3. Create `ResourceTable` and migrate one low-risk tab first (`SecretsTab` or `JobsTab`).
4. Migrate remaining read-only tabs (`ServicesTab`, `IngressesTab`, `GatewaysTab`, `HttpRoutesTab`, `GatewayClassesTab`, `StatefulSetsTab`, `EventsTab`, `HelmTab`).
5. Migrate action tabs (`DeploymentsTab`, `CronJobsTab`, `HpaTab`).
6. Migrate `PodsTab` (custom metrics/age/hide-completed logic stays but table markup uses `ResourceTable`).
7. Remove `show*Menu` builders from `AksPage` and switch tabs to emit `ContextMenuItem[]`.
8. Run `npm run build` and the full Playwright suite. Fix only TypeScript/build errors and failing tests.

## Backward compatibility

- `data-testid` attributes must match the current values (`deployments-table-body`, `deployment-row-${name}`, etc.).
- URL query parameters (`tab`, `ns`, `pod`, `yaml`, `helm`, `container`, `logs`, `logsNs`) keep the same format.
- `ContextMenuItem` shape (label, icon, onClick, destructive, disabled, separator) is preserved.
