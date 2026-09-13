import type { StorageBlobContent } from "./types";

/**
 * The result of deciding how a fetched blob's content may be downloaded.
 *
 * - `"text"` — safe to hand `content` straight to a text-file writer (`downloadText`).
 * - `"blocked-binary"` — `content` is not real file bytes (see below) and must not be
 *   written to disk; the caller should redirect the user to a binary-safe path (the
 *   existing signed "Generate SAS URL" flow) instead.
 */
export type BlobDownloadPlan =
  | { kind: "text"; filename: string; content: string; mimeType: string }
  | { kind: "blocked-binary"; blobName: string };

/**
 * Single decision point for whether a blob's fetched content can be downloaded as a text
 * file, shared by `handleDownloadBlob` and `handleBatchDownloadBlobs`.
 *
 * P0 fix (unit 6.1 of the ux-interaction-consistency plan): Download used to hand
 * `data.content` straight to a text-file writer regardless of `data.isBinary`. For a binary
 * blob the sidecar returns an *empty* `content` (see `AzureStorageClient.GetBlobContentAsync`
 * on the .NET side), so this silently produced a corrupted (empty) file with no warning. The
 * Content tab already trusts `isBinary` to decide whether `content` is safe to render
 * (`BlobDetailPanel.tsx`); this function makes Download trust the same flag, so the decision
 * lives in one tested place instead of being re-derived (and potentially re-broken) at each
 * call site.
 */
export function planBlobDownload(blobName: string, data: StorageBlobContent): BlobDownloadPlan {
  if (data.isBinary) {
    return { kind: "blocked-binary", blobName };
  }
  return {
    kind: "text",
    filename: blobName.split("/").pop() || blobName,
    content: data.content,
    mimeType: data.contentType || "text/plain",
  };
}
