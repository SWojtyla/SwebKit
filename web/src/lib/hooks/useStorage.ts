import { useQuery, useMutation, useQueryClient, keepPreviousData } from "@tanstack/react-query";
import { apiFetch, apiSend, apiUpload } from "../api";
import { useNotification } from "@/components/layout/NotificationSystem";
import type {
  StorageContainerItem,
  StorageBlobPage,
  BlobProperties,
  StorageBlobContent,
  BlobMutationResult,
  BlobVersionComparison,
  BlobRecoveryResult,
} from "../types";

// ── Storage hooks ─────────────────────────────────────────────────────────────

/** Mirrors `useAksTestConnection`/`useSbTestConnection` — the sidecar endpoint already
 * existed (`GET /api/storage/{accountId}/test`) but had no frontend hook until Settings
 * needed a "Test connection" button for it. */
export function useStorageTestConnection(accountId: string | null, options?: { enabled?: boolean }) {
  return useQuery({
    queryKey: ["storage", accountId, "test"],
    queryFn: ({ signal }) => apiFetch<{ connected: boolean; error?: string }>(`/api/storage/${accountId}/test`, { signal }),
    enabled: !!accountId && (options?.enabled ?? true),
  });
}

export function useStorageContainers(accountId: string | null) {
  return useQuery({
    queryKey: ["storage", accountId, "containers"],
    queryFn: ({ signal }) => apiFetch<StorageContainerItem[]>(`/api/storage/${accountId}/containers`, { signal }),
    enabled: !!accountId,
  });
}

export function useStorageBlobs(accountId: string | null, container: string | null, prefix: string, continuationToken: string | null) {
  return useQuery({
    queryKey: ["storage", accountId, "containers", container, "blobs", prefix, continuationToken],
    queryFn: ({ signal }) => {
      const params = new URLSearchParams({ prefix });
      if (continuationToken) params.set("continuationToken", continuationToken);
      return apiFetch<StorageBlobPage>(`/api/storage/${accountId}/containers/${encodeURIComponent(container!)}/blobs?${params}`, { signal });
    },
    enabled: !!accountId && !!container,
    // 6.5 fix: each "Load more" click changes `continuationToken`, which is part of the
    // query key — without this, that's a brand-new query with no data yet, so `isLoading`
    // flips true and flashes a "Loading blobs..." banner over the already-populated list
    // for the duration of the fetch. Keeping the previous page's data as placeholder data
    // means the list stays visible (and `isLoading` stays false) while the next page loads.
    placeholderData: keepPreviousData,
  });
}

export function useBlobProperties(accountId: string | null, container: string | null, blobName: string | null) {
  return useQuery({
    queryKey: ["storage", accountId, "containers", container, "blobs", blobName, "properties"],
    queryFn: ({ signal }) => {
      const params = new URLSearchParams({ blobName: blobName! });
      return apiFetch<BlobProperties>(`/api/storage/${accountId}/containers/${encodeURIComponent(container!)}/blobs/properties?${params}`, { signal });
    },
    enabled: !!accountId && !!container && !!blobName,
  });
}

export function useBlobContent(accountId: string | null, container: string | null, blobName: string | null) {
  return useQuery({
    queryKey: ["storage", accountId, "containers", container, "blobs", blobName, "content"],
    queryFn: ({ signal }) => {
      const params = new URLSearchParams({ blobName: blobName! });
      return apiFetch<StorageBlobContent>(`/api/storage/${accountId}/containers/${encodeURIComponent(container!)}/blobs/content?${params}`, { signal });
    },
    enabled: !!accountId && !!container && !!blobName,
  });
}

export function useBlobSasUrl(accountId: string | null, container: string | null, blobName: string | null, expiryMinutes: number = 60) {
  return useQuery({
    queryKey: ["storage", accountId, "containers", container, "blobs", blobName, "sas", expiryMinutes],
    queryFn: ({ signal }) => {
      const params = new URLSearchParams({ blobName: blobName!, expiryMinutes: String(expiryMinutes) });
      return apiFetch<{ sasUrl: string }>(`/api/storage/${accountId}/containers/${encodeURIComponent(container!)}/blobs/sas?${params}`, { signal });
    },
    enabled: !!accountId && !!container && !!blobName,
  });
}

export function useBlobVersions(accountId: string | null, container: string | null, blobName: string | null) {
  return useQuery({
    queryKey: ["storage", accountId, "containers", container, "blobs", blobName, "versions"],
    queryFn: ({ signal }) => {
      const params = new URLSearchParams({ blobName: blobName! });
      return apiFetch<{ versionId: string; lastModified: string; sizeBytes: number; isCurrent: boolean }[]>(`/api/storage/${accountId}/containers/${encodeURIComponent(container!)}/blobs/versions?${params}`, { signal });
    },
    enabled: !!accountId && !!container && !!blobName,
  });
}

export function useBlobVersionComparison(
  accountId: string | null,
  container: string | null,
  blobName: string | null,
  baseVersionId: string | null,
  compareVersionId: string | null,
  enabled = true,
) {
  return useQuery({
    queryKey: ["storage", accountId, "containers", container, "blobs", blobName, "versions", "compare", baseVersionId, compareVersionId],
    queryFn: ({ signal }) => {
      const params = new URLSearchParams({ blobName: blobName!, baseVersionId: baseVersionId! });
      if (compareVersionId) params.set("compareVersionId", compareVersionId);
      return apiFetch<BlobVersionComparison>(
        `/api/storage/${accountId}/containers/${encodeURIComponent(container!)}/blobs/versions/compare?${params}`,
        { signal },
      );
    },
    enabled: enabled && !!accountId && !!container && !!blobName && !!baseVersionId,
  });
}

export function useUploadBlob(accountId: string | null, container: string | null) {
  const qc = useQueryClient();
  const { notify } = useNotification();
  return useMutation({
    mutationFn: ({ blobName, file, onProgress }: { blobName: string; file: File; onProgress?: (percent: number) => void }) =>
      apiUpload(
        `/api/storage/${accountId}/containers/${encodeURIComponent(container!)}/blobs/upload?${new URLSearchParams({ blobName })}`,
        file,
        onProgress,
      ),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ["storage", accountId, "containers", container, "blobs"] });
    },
    onError: (error) => notify("error", "Couldn't upload blob", String(error)),
  });
}

export function useCopyBlob(accountId: string | null) {
  const qc = useQueryClient();
  const { notify } = useNotification();
  return useMutation({
    mutationFn: ({ sourceContainer, sourceBlob, destContainer, destBlob, overwrite }: { sourceContainer: string; sourceBlob: string; destContainer: string; destBlob: string; overwrite: boolean }) =>
      apiSend(`/api/storage/${accountId}/copy`, "POST", { sourceContainer, sourceBlob, destContainer, destBlob, overwrite }),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ["storage", accountId] });
    },
    onError: (error) => notify("error", "Couldn't copy blob", String(error)),
  });
}

export function useRestoreBlobVersion(accountId: string | null, container: string | null, blobName: string | null) {
  const qc = useQueryClient();
  const { notify } = useNotification();
  return useMutation({
    mutationFn: (versionId: string) =>
      apiSend<BlobRecoveryResult>(
        `/api/storage/${accountId}/containers/${encodeURIComponent(container!)}/blobs/versions/${encodeURIComponent(versionId)}/restore?${new URLSearchParams({ blobName: blobName! })}`,
        "POST",
      ),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ["storage", accountId, "containers", container, "blobs", blobName] });
      qc.invalidateQueries({ queryKey: ["storage", accountId, "containers", container, "blobs"] });
    },
    onError: (error) => notify("error", "Couldn't restore blob version", String(error)),
  });
}

export function useDeletedBlobs(accountId: string | null, container: string | null) {
  return useQuery({
    queryKey: ["storage", accountId, "containers", container, "deleted-blobs"],
    queryFn: ({ signal }) => apiFetch<{ name: string; deletedOn: string; remainingDays: number }[]>(`/api/storage/${accountId}/containers/${encodeURIComponent(container!)}/deleted-blobs`, { signal }),
    enabled: !!accountId && !!container,
  });
}

export function useSetBlobMetadata(accountId: string | null, container: string | null, blobName: string | null) {
  const qc = useQueryClient();
  const { notify } = useNotification();
  return useMutation({
    mutationFn: (metadata: Record<string, string>) =>
      apiSend<BlobMutationResult>(`/api/storage/${accountId}/containers/${encodeURIComponent(container!)}/blobs/metadata?${new URLSearchParams({ blobName: blobName! })}`, "POST", metadata),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ["storage", accountId, "containers", container, "blobs", blobName, "properties"] });
    },
    onError: (error) => notify("error", "Couldn't save blob metadata", String(error)),
  });
}

export function useUndeleteBlob(accountId: string | null, container: string | null) {
  const qc = useQueryClient();
  const { notify } = useNotification();
  return useMutation({
    mutationFn: (blobName: string) =>
      apiSend<BlobRecoveryResult>(`/api/storage/${accountId}/containers/${encodeURIComponent(container!)}/blobs/undelete?${new URLSearchParams({ blobName })}`, "POST"),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ["storage", accountId, "containers", container, "deleted-blobs"] });
      qc.invalidateQueries({ queryKey: ["storage", accountId, "containers", container, "blobs"] });
    },
    onError: (error) => notify("error", "Couldn't restore deleted blob", String(error)),
  });
}
