import { useQuery, useMutation, useQueryClient } from "@tanstack/react-query";
import { apiFetch, apiSend, apiUpload } from "../api";
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

export function useStorageContainers(accountId: string | null) {
  return useQuery({
    queryKey: ["storage", accountId, "containers"],
    queryFn: () => apiFetch<StorageContainerItem[]>(`/api/storage/${accountId}/containers`),
    enabled: !!accountId,
  });
}

export function useStorageBlobs(accountId: string | null, container: string | null, prefix: string, continuationToken: string | null) {
  return useQuery({
    queryKey: ["storage", accountId, "containers", container, "blobs", prefix, continuationToken],
    queryFn: () => {
      const params = new URLSearchParams({ prefix });
      if (continuationToken) params.set("continuationToken", continuationToken);
      return apiFetch<StorageBlobPage>(`/api/storage/${accountId}/containers/${encodeURIComponent(container!)}/blobs?${params}`);
    },
    enabled: !!accountId && !!container,
  });
}

export function useBlobProperties(accountId: string | null, container: string | null, blobName: string | null) {
  return useQuery({
    queryKey: ["storage", accountId, "containers", container, "blobs", blobName, "properties"],
    queryFn: () => {
      const params = new URLSearchParams({ blobName: blobName! });
      return apiFetch<BlobProperties>(`/api/storage/${accountId}/containers/${encodeURIComponent(container!)}/blobs/properties?${params}`);
    },
    enabled: !!accountId && !!container && !!blobName,
  });
}

export function useBlobContent(accountId: string | null, container: string | null, blobName: string | null) {
  return useQuery({
    queryKey: ["storage", accountId, "containers", container, "blobs", blobName, "content"],
    queryFn: () => {
      const params = new URLSearchParams({ blobName: blobName! });
      return apiFetch<StorageBlobContent>(`/api/storage/${accountId}/containers/${encodeURIComponent(container!)}/blobs/content?${params}`);
    },
    enabled: !!accountId && !!container && !!blobName,
  });
}

export function useBlobSasUrl(accountId: string | null, container: string | null, blobName: string | null, expiryMinutes: number = 60) {
  return useQuery({
    queryKey: ["storage", accountId, "containers", container, "blobs", blobName, "sas", expiryMinutes],
    queryFn: () => {
      const params = new URLSearchParams({ blobName: blobName!, expiryMinutes: String(expiryMinutes) });
      return apiFetch<{ sasUrl: string }>(`/api/storage/${accountId}/containers/${encodeURIComponent(container!)}/blobs/sas?${params}`);
    },
    enabled: !!accountId && !!container && !!blobName,
  });
}

export function useBlobVersions(accountId: string | null, container: string | null, blobName: string | null) {
  return useQuery({
    queryKey: ["storage", accountId, "containers", container, "blobs", blobName, "versions"],
    queryFn: () => {
      const params = new URLSearchParams({ blobName: blobName! });
      return apiFetch<{ versionId: string; lastModified: string; sizeBytes: number; isCurrent: boolean }[]>(`/api/storage/${accountId}/containers/${encodeURIComponent(container!)}/blobs/versions?${params}`);
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
    queryFn: () => {
      const params = new URLSearchParams({ blobName: blobName!, baseVersionId: baseVersionId! });
      if (compareVersionId) params.set("compareVersionId", compareVersionId);
      return apiFetch<BlobVersionComparison>(
        `/api/storage/${accountId}/containers/${encodeURIComponent(container!)}/blobs/versions/compare?${params}`,
      );
    },
    enabled: enabled && !!accountId && !!container && !!blobName && !!baseVersionId,
  });
}

export function useUploadBlob(accountId: string | null, container: string | null) {
  const qc = useQueryClient();
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
  });
}

export function useCopyBlob(accountId: string | null) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: ({ sourceContainer, sourceBlob, destContainer, destBlob, overwrite }: { sourceContainer: string; sourceBlob: string; destContainer: string; destBlob: string; overwrite: boolean }) =>
      apiSend(`/api/storage/${accountId}/copy`, "POST", { sourceContainer, sourceBlob, destContainer, destBlob, overwrite }),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ["storage", accountId] });
    },
  });
}

export function useRestoreBlobVersion(accountId: string | null, container: string | null, blobName: string | null) {
  const qc = useQueryClient();
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
  });
}

export function useDeletedBlobs(accountId: string | null, container: string | null) {
  return useQuery({
    queryKey: ["storage", accountId, "containers", container, "deleted-blobs"],
    queryFn: () => apiFetch<{ name: string; deletedOn: string; remainingDays: number }[]>(`/api/storage/${accountId}/containers/${encodeURIComponent(container!)}/deleted-blobs`),
    enabled: !!accountId && !!container,
  });
}

export function useSetBlobMetadata(accountId: string | null, container: string | null, blobName: string | null) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (metadata: Record<string, string>) =>
      apiSend<BlobMutationResult>(`/api/storage/${accountId}/containers/${encodeURIComponent(container!)}/blobs/metadata?${new URLSearchParams({ blobName: blobName! })}`, "POST", metadata),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ["storage", accountId, "containers", container, "blobs", blobName, "properties"] });
    },
  });
}

export function useUndeleteBlob(accountId: string | null, container: string | null) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (blobName: string) =>
      apiSend<BlobRecoveryResult>(`/api/storage/${accountId}/containers/${encodeURIComponent(container!)}/blobs/undelete?${new URLSearchParams({ blobName })}`, "POST"),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ["storage", accountId, "containers", container, "deleted-blobs"] });
      qc.invalidateQueries({ queryKey: ["storage", accountId, "containers", container, "blobs"] });
    },
  });
}
