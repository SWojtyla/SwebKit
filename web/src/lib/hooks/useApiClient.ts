import { useQuery, useMutation, useQueryClient } from "@tanstack/react-query";
import { apiFetch, apiSend } from "../api";
import type {
  ApiCollection,
  CollectionsStoreResponse,
  ApiClientExecutionResponse,
  HttpRequestEntry,
} from "../types";

// ── API Client ───────────────────────────────────────────────────────────────

export function useCollections(enabled = true) {
  return useQuery<CollectionsStoreResponse, Error, ApiCollection[]>({
    queryKey: ["collections"],
    queryFn: () => apiFetch<CollectionsStoreResponse>("/api/config/collections/store"),
    select: (data) => data.collections ?? [],
    enabled,
  });
}

export function useUpdateCollections() {
  const qc = useQueryClient();
  return useMutation<CollectionsStoreResponse, Error, ApiCollection[]>({
    mutationFn: async (collections) => {
      const current = qc.getQueryData<CollectionsStoreResponse>(["collections"]);
      const token = current?.concurrencyToken;
      const path = token
        ? `/api/config/collections?concurrencyToken=${encodeURIComponent(token)}`
        : "/api/config/collections";
      return apiSend<CollectionsStoreResponse>(path, "PUT", { schemaVersion: 1, collections });
    },
    onSuccess: (data) => {
      qc.setQueryData(["collections"], data);
    },
  });
}

export function useExecuteRequest() {
  return useMutation({
    mutationFn: (vars: {
      request: HttpRequestEntry;
      collectionId?: string;
      environmentId?: string;
    }) => apiSend<ApiClientExecutionResponse>("/api/api-client/execute", "POST", vars),
  });
}
