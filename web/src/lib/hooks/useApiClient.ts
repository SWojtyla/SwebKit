import { useQuery, useMutation, useQueryClient } from "@tanstack/react-query";
import { apiFetch, apiSend, importCollection } from "../api";
import { useNotification } from "@/components/layout/NotificationSystem";
import type {
  ApiCollection,
  CollectionsStoreResponse,
  ApiClientExecutionResponse,
  HttpRequestEntry,
  CollectionImportResult,
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
  return useMutation<CollectionsStoreResponse, Error, CollectionsUpdate>({
    // Serialized on one scope, so two saves are never in flight at once. Without
    // this, creating two requests in quick succession had each PUT a full snapshot
    // computed before the other landed and the first request vanished — and the
    // same race dropped one of two quick drag-reorders.
    scope: { id: "api-client-collections" },
    mutationFn: async (update) => {
      // Read inside `mutationFn`, which the scope defers until the previous save
      // has settled and written its response back to the cache.
      const current = qc.getQueryData<CollectionsStoreResponse>(["collections"]);
      const collections = typeof update === "function" ? update(current?.collections ?? []) : update;
      const token = current?.concurrencyToken;
      const path = token
        ? `/api/config/collections?concurrencyToken=${encodeURIComponent(token)}`
        : "/api/config/collections";
      return apiSend<CollectionsStoreResponse>(path, "PUT", { schemaVersion: 1, collections });
    },
    onSuccess: (data) => {
      qc.setQueryData(["collections"], data);
    },
    onError: (error) => notify("error", "Couldn't save collections", String(error)),
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
