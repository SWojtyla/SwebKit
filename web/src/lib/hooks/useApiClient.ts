import { useQuery, useMutation, useQueryClient } from "@tanstack/react-query";
import {
  apiFetch,
  apiSend,
  importCollection,
  getLinkedRoots,
  addLinkedRoot,
  updateLinkedRoot,
  removeLinkedRoot,
  reloadLinkedRoots,
} from "../api";
import { useNotification } from "@/components/layout/NotificationSystem";
import type {
  ApiCollection,
  CollectionsStoreResponse,
  ApiClientExecutionResponse,
  HttpRequestEntry,
  CollectionImportResult,
  LinkedRootSummary,
} from "../types";

// ── API Client ───────────────────────────────────────────────────────────────

export function useCollections(enabled = true) {
  return useQuery<CollectionsStoreResponse, Error, ApiCollection[]>({
    queryKey: ["collections"],
    queryFn: ({ signal }) => apiFetch<CollectionsStoreResponse>("/api/config/collections/store", { signal }),
    select: (data) => data.collections ?? [],
    enabled,
  });
}

/**
 * Either the full collections array, or a function deriving it from the freshest
 * stored collections. Prefer the function form for anything that edits what is
 * already there: a component's `collections` variable is a render snapshot, and
 * a PUT sends the *whole* store, so deriving from a snapshot taken before an
 * in-flight save landed silently discards that save's changes.
 */
export type CollectionsUpdate = ApiCollection[] | ((previous: ApiCollection[]) => ApiCollection[]);

export function useUpdateCollections() {
  const qc = useQueryClient();
  const { notify } = useNotification();
  return useMutation<CollectionsStoreResponse, Error, CollectionsUpdate | { update: CollectionsUpdate; force: true }>({
    // Serialized on one scope, so two saves are never in flight at once. Without
    // this, creating two requests in quick succession had each PUT a full snapshot
    // computed before the other landed and the first request vanished — and the
    // same race dropped one of two quick drag-reorders.
    scope: { id: "api-client-collections" },
    mutationFn: async (input) => {
      // Read inside `mutationFn`, which the scope defers until the previous save
      // has settled and written its response back to the cache.
      const force = typeof input === "object" && "force" in input;
      const update = force ? input.update : input;
      const current = qc.getQueryData<CollectionsStoreResponse>(["collections"]);
      const collections = typeof update === "function" ? update(current?.collections ?? []) : update;
      const token = current?.concurrencyToken;
      const params = new URLSearchParams();
      if (token) params.set("concurrencyToken", token);
      if (force) params.set("force", "true");
      const query = params.toString();
      return apiSend<CollectionsStoreResponse>(
        `/api/config/collections${query ? `?${query}` : ""}`,
        "PUT",
        { schemaVersion: 1, collections },
      );
    },
    onSuccess: (data) => {
      qc.setQueryData(["collections"], data);
      // A linked-file write may have moved/renamed files on disk — refresh the
      // roots panel so paths, counts and the git badge stay honest.
      qc.invalidateQueries({ queryKey: ["linked-roots"] });
    },
    onError: (error) => {
      // 409 conflicts are handled by the caller's banner (reload / overwrite /
      // save-as-copy) — a toast on top of that would just be noise.
      if (error.name !== "ConflictError") {
        notify("error", "Couldn't save collections", String(error));
      }
    },
  });
}

export function useExecuteRequest() {
  const { notify } = useNotification();
  return useMutation({
    mutationFn: (vars: {
      request: HttpRequestEntry;
      collectionId?: string;
      /** The collection-scoped layer — overrides the global one on a key clash. */
      environmentId?: string;
      /** The global layer, applied underneath `environmentId`. */
      globalEnvironmentId?: string;
    }) => apiSend<ApiClientExecutionResponse>("/api/api-client/execute", "POST", vars),
    onError: (error) => notify("error", "Couldn't execute request", String(error)),
  });
}

export function useImportCollection() {
  const qc = useQueryClient();
  const { notify } = useNotification();
  return useMutation<CollectionImportResult, Error, { folderPath?: string | null; payloadBase64?: string | null }>({
    mutationFn: importCollection,
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ["collections"] });
      qc.invalidateQueries({ queryKey: ["environments"] });
    },
    onError: (error) => notify("error", "Couldn't import collection", String(error)),
  });
}

// ── Linked API projects ──────────────────────────────────────────────────────

/** The folders the user linked as API projects — each holding `.swebkit-api/` files. */
export function useLinkedRoots() {
  return useQuery<LinkedRootSummary[]>({
    queryKey: ["linked-roots"],
    queryFn: ({ signal }) => getLinkedRoots(signal),
  });
}

function useLinkedRootMutation<TArgs>(
  mutationFn: (args: TArgs) => Promise<LinkedRootSummary[]>,
  errorTitle: string,
) {
  const qc = useQueryClient();
  const { notify } = useNotification();
  return useMutation<LinkedRootSummary[], Error, TArgs>({
    mutationFn,
    onSuccess: (roots) => {
      qc.setQueryData(["linked-roots"], roots);
      // Linked collections/environments appear or disappear with the roots.
      qc.invalidateQueries({ queryKey: ["collections"] });
      qc.invalidateQueries({ queryKey: ["environments"] });
    },
    onError: (error) => notify("error", errorTitle, String(error)),
  });
}

export function useAddLinkedRoot() {
  return useLinkedRootMutation(
    (args: { path: string; name?: string | null; brunoFolderPath?: string | null }) => addLinkedRoot(args),
    "Couldn't link folder",
  );
}

export function useUpdateLinkedRoot() {
  return useLinkedRootMutation(
    (args: {
      id: string;
      name?: string | null;
      isEnabled?: boolean;
      brunoSyncFolderPath?: string | null;
      brunoSyncEnabled?: boolean;
    }) => updateLinkedRoot(args.id, args),
    "Couldn't update linked folder",
  );
}

export function useRemoveLinkedRoot() {
  return useLinkedRootMutation(
    (id: string) => removeLinkedRoot(id),
    "Couldn't unlink folder",
  );
}

export function useReloadLinkedRoots() {
  return useLinkedRootMutation<void>(
    () => reloadLinkedRoots(),
    "Couldn't reload linked folders",
  );
}
