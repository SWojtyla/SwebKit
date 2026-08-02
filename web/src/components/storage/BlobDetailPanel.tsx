import { useState } from "react";
import { Download, Link as LinkIcon, Check, Plus, Trash2, Copy as CopyIcon, Sparkles } from "lucide-react";
import { ConfirmBar } from "@/components/shared/ConfirmBar";
import { useStoragePageContext } from "./StoragePageContext";
import { ContextualAssistant } from "@/components/agent/ContextualAssistant";

function formatBytes(bytes: number | null | undefined): string {
  if (bytes == null) return "-";
  if (bytes < 1024) return `${bytes}B`;
  if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)}K`;
  return `${(bytes / (1024 * 1024)).toFixed(1)}M`;
}

function formatDate(date: string | null | undefined): string {
  if (!date) return "-";
  try {
    return new Date(date).toLocaleString();
  } catch {
    return String(date);
  }
}

export function BlobDetailPanel() {
  const ctx = useStoragePageContext();
  const [askAiOpen, setAskAiOpen] = useState(false);

  return (
    <div className="flex-1 overflow-auto" data-testid="storage-blob-detail">
      {!ctx.selectedBlob ? (
        <div className="flex h-full items-center justify-center text-muted-foreground" data-testid="storage-no-blob-selected">
          Select a blob to view details
        </div>
      ) : (
        <div className="p-6 space-y-4">
          {ctx.blobProps.isLoading && <div className="text-sm text-muted-foreground">Loading blob properties...</div>}
          {ctx.blobProps.error && <div className="text-sm text-destructive">Error: {ctx.blobProps.error.message}</div>}
          {ctx.blobProps.data && (
            <>
              {/* Blob detail tabs */}
              <div className="flex border-b">
                {(["properties", "versions", "content"] as const).map((tab) => (
                  <button
                    key={tab}
                    onClick={() => ctx.setBlobDetailTab(tab)}
                    className={`px-3 py-1.5 text-xs font-medium capitalize ${ctx.blobDetailTab === tab ? "border-b-2 border-primary text-foreground" : "text-muted-foreground"}`}
                    data-testid={`storage-blob-tab-${tab}`}
                  >
                    {tab}
                  </button>
                ))}
              </div>

              {ctx.blobDetailTab === "properties" && (
              <>
                <div className="flex items-center gap-2">
                  <div className="text-lg font-mono font-semibold break-all" data-testid="storage-blob-name">
                    {ctx.blobProps.data.name}
                  </div>
                  <button onClick={() => ctx.handleCopyUrl(ctx.blobProps.data!.name)} className="text-muted-foreground hover:text-foreground" data-testid="storage-copy-url-btn" title="Copy URL">
                    {ctx.copiedUrl ? <Check className="h-3.5 w-3.5 text-success" /> : <LinkIcon className="h-3.5 w-3.5" />}
                  </button>
                  <button onClick={() => ctx.handleDownloadBlob(ctx.blobProps.data!.name)} className="text-muted-foreground hover:text-foreground" data-testid="storage-download-btn" title="Download blob">
                    <Download className="h-3.5 w-3.5" />
                  </button>
                  <button onClick={() => ctx.setShowSasUrl(!ctx.showSasUrl)} className="text-muted-foreground hover:text-foreground" data-testid="storage-sas-url-btn" title="Generate SAS URL">
                    <LinkIcon className="h-3.5 w-3.5" />
                  </button>
                  <span title={ctx.allowMutations ? "Copy blob" : "Mutations are disabled for this storage account. Enable allowMutations in Settings."}>
                    <button
                      onClick={() => {
                        ctx.setCopyDestContainer(ctx.selectedContainer ?? "");
                        ctx.setCopyDestBlob(ctx.blobProps.data!.name);
                        ctx.setCopyOverwrite(false);
                        ctx.setCopyConfirming(false);
                        ctx.setCopyStatus(null);
                        ctx.setShowCopyDialog(true);
                      }}
                      disabled={!ctx.allowMutations}
                      className="text-muted-foreground hover:text-foreground disabled:cursor-not-allowed disabled:opacity-50"
                      data-testid="storage-copy-blob-btn"
                    >
                      <CopyIcon className="h-3.5 w-3.5" />
                    </button>
                  </span>
                  <button
                    onClick={() => setAskAiOpen(true)}
                    className="text-muted-foreground hover:text-foreground"
                    title="Ask AI about this blob"
                    data-testid="storage-ask-ai-btn"
                  >
                    <Sparkles className="h-3.5 w-3.5" />
                  </button>
                </div>
                {askAiOpen && (
                  <ContextualAssistant
                    featureArea="Storage"
                    title={`blob ${ctx.blobProps.data.name}`}
                    selection={{
                      container: ctx.selectedContainer ?? "",
                      blob: ctx.blobProps.data.name,
                      ...(ctx.activeAccountId ? { account_id: ctx.activeAccountId } : {}),
                    }}
                    onClose={() => setAskAiOpen(false)}
                  />
                )}
                {ctx.showSasUrl && (
                  <div className="mt-2 rounded border bg-muted/30 p-2" data-testid="storage-sas-url-display">
                    <div className="flex items-center gap-2">
                      <input
                        type="text"
                        readOnly
                        value={ctx.sasUrl.data?.sasUrl ?? "Loading..."}
                        className="flex-1 rounded border bg-card px-2 py-1 text-xs font-mono"
                        data-testid="storage-sas-url-input"
                      />
                      <button
                        onClick={ctx.handleCopySasUrl}
                        className="rounded border px-2 py-1 text-xs hover:bg-accent"
                        data-testid="storage-sas-url-copy"
                      >
                        {ctx.copiedUrl ? <Check className="h-3 w-3" /> : "Copy"}
                      </button>
                    </div>
                    <p className="mt-1 text-xs text-muted-foreground">Valid for 60 minutes</p>
                  </div>
                )}
                <div className="mt-1 flex flex-wrap items-center gap-3 text-sm">
                  <span className="text-muted-foreground" data-testid="storage-blob-size">
                    {formatBytes(ctx.blobProps.data.sizeBytes)}
                  </span>
                  <span className="text-muted-foreground" data-testid="storage-blob-type">
                    {ctx.blobProps.data.contentType}
                  </span>
                  <span className="text-muted-foreground" data-testid="storage-blob-modified">
                    {formatDate(ctx.blobProps.data.lastModified)}
                  </span>
                  {ctx.blobProps.data.accessTier && (
                    <span className="text-muted-foreground">Tier: {ctx.blobProps.data.accessTier}</span>
                  )}
                </div>

              {/* Metadata */}
              <div>
                <div className="mb-2 flex items-center justify-between">
                  <h3 className="text-sm font-semibold">Metadata</h3>
                  {ctx.metadataEditing ? (
                    <div className="flex items-center gap-2">
                      <button onClick={ctx.handleMetadataSave} disabled={ctx.setBlobMetadata.isPending} className="rounded bg-primary px-2 py-1 text-xs text-primary-foreground disabled:opacity-50" data-testid="storage-metadata-save">{ctx.setBlobMetadata.isPending ? "Saving..." : "Save"}</button>
                      <button onClick={() => { ctx.setMetadataEditing(false); ctx.setMetadataDraft({}); }} className="rounded border px-2 py-1 text-xs" data-testid="storage-metadata-cancel">Cancel</button>
                    </div>
                  ) : (
                    <button onClick={() => { ctx.setMetadataEditing(true); ctx.setMetadataDraft(ctx.blobProps.data!.metadata); }} className="flex items-center gap-1 text-xs text-muted-foreground hover:text-foreground" data-testid="storage-metadata-edit-btn">
                      <Plus className="h-3 w-3" /> Edit
                    </button>
                  )}
                </div>
                {ctx.metadataEditing ? (
                  <div className="rounded-md border p-3 space-y-2" data-testid="storage-metadata-editor">
                    {Object.entries(ctx.metadataDraft).map(([k, v]) => (
                      <div key={k} className="flex items-center gap-2">
                        <input
                          type="text"
                          value={k}
                          readOnly
                          className="flex-1 rounded border bg-muted px-2 py-1 text-xs font-mono"
                        />
                        <input
                          type="text"
                          value={v}
                          onChange={(e) => ctx.setMetadataDraft((prev) => ({ ...prev, [k]: e.target.value }))}
                          className="flex-1 rounded border bg-card px-2 py-1 text-xs font-mono"
                        />
                        <button onClick={() => { const next = { ...ctx.metadataDraft }; delete next[k]; ctx.setMetadataDraft(next); }} className="text-destructive hover:bg-destructive/10 rounded p-1">
                          <Trash2 className="h-3 w-3" />
                        </button>
                      </div>
                    ))}
                    <button
                      onClick={() => ctx.setMetadataDraft((prev) => ({ ...prev, "new-key": "" }))}
                      className="flex items-center gap-1 text-xs text-primary hover:underline"
                      data-testid="storage-metadata-add-key"
                    >
                      <Plus className="h-3 w-3" /> Add key
                    </button>
                  </div>
                ) : Object.keys(ctx.blobProps.data.metadata).length > 0 ? (
                  <div className="rounded-md border overflow-hidden">
                    <table className="w-full table-fixed text-sm">
                      <thead className="bg-muted">
                        <tr>
                          <th className="w-1/3 px-3 py-2 text-left font-medium">Key</th>
                          <th className="px-3 py-2 text-left font-medium">Value</th>
                        </tr>
                      </thead>
                      <tbody>
                        {Object.entries(ctx.blobProps.data.metadata).map(([k, v]) => (
                          <tr key={k} className="border-t">
                            <td className="px-3 py-2 font-mono break-all">{k}</td>
                            <td className="px-3 py-2 font-mono break-all">{v}</td>
                          </tr>
                        ))}
                      </tbody>
                    </table>
                  </div>
                ) : (
                  <div className="text-xs text-muted-foreground">No metadata</div>
                )}
              </div>
              </>
              )}

              {ctx.blobDetailTab === "versions" && (
                <div data-testid="storage-blob-versions">
                  {ctx.blobVersions.isLoading && <div className="text-sm text-muted-foreground">Loading versions...</div>}
                  {ctx.blobVersions.error && <div className="text-sm text-destructive">Error: {ctx.blobVersions.error.message}</div>}
                  {ctx.blobVersions.data && (
                    <>
                      <div className="mb-3 rounded-md border bg-muted/20 p-3" data-testid="storage-version-compare-controls">
                        <div className="grid gap-2 md:grid-cols-[1fr_1fr_auto] md:items-end">
                          <label className="text-xs">
                            <span className="mb-1 block text-muted-foreground">Base version</span>
                            <select
                              value={ctx.versionBaseId ?? ""}
                              onChange={(e) => { ctx.setVersionBaseId(e.target.value || null); ctx.setVersionCompareRequested(false); }}
                              className="w-full rounded border bg-card px-2 py-1.5 font-mono text-xs"
                              data-testid="storage-version-base"
                            >
                              <option value="">Select a version</option>
                              {ctx.blobVersions.data.map((v) => <option key={v.versionId} value={v.versionId}>{v.versionId}{v.isCurrent ? " (current)" : ""}</option>)}
                            </select>
                          </label>
                          <label className="text-xs">
                            <span className="mb-1 block text-muted-foreground">Compare with</span>
                            <select
                              value={ctx.versionCompareId ?? ""}
                              onChange={(e) => { ctx.setVersionCompareId(e.target.value || null); ctx.setVersionCompareRequested(false); }}
                              className="w-full rounded border bg-card px-2 py-1.5 font-mono text-xs"
                              data-testid="storage-version-compare"
                            >
                              <option value="">Current version</option>
                              {ctx.blobVersions.data.map((v) => <option key={v.versionId} value={v.versionId}>{v.versionId}{v.isCurrent ? " (current)" : ""}</option>)}
                            </select>
                          </label>
                          <button
                            onClick={() => ctx.setVersionCompareRequested(true)}
                            disabled={!ctx.versionBaseId || ctx.versionComparison.isFetching}
                            className="rounded bg-primary px-3 py-1.5 text-xs text-primary-foreground disabled:opacity-50"
                            data-testid="storage-version-compare-btn"
                          >
                            {ctx.versionComparison.isFetching ? "Comparing..." : "Compare"}
                          </button>
                        </div>
                      </div>
                      <div className="rounded-md border overflow-hidden">
                        <table className="w-full table-fixed text-sm">
                          <thead className="bg-muted">
                            <tr>
                              <th className="px-3 py-2 text-left font-medium">Version ID</th>
                              <th className="px-3 py-2 text-left font-medium">Modified</th>
                              <th className="px-3 py-2 text-right font-medium">Size</th>
                              <th className="px-3 py-2 text-center font-medium">Current</th>
                              <th className="px-3 py-2 text-right font-medium">Actions</th>
                            </tr>
                          </thead>
                          <tbody>
                            {ctx.blobVersions.data.map((v) => (
                              <tr key={v.versionId} className="border-t">
                                <td className="px-3 py-2 font-mono text-xs break-all">{v.versionId}</td>
                                <td className="px-3 py-2 text-xs">{formatDate(v.lastModified)}</td>
                                <td className="px-3 py-2 text-right text-xs">{formatBytes(v.sizeBytes)}</td>
                                <td className="px-3 py-2 text-center">{v.isCurrent && <Check className="inline h-3 w-3 text-success" />}</td>
                                <td className="px-3 py-2 text-right">
                                  {!v.isCurrent && ctx.allowMutations && (
                                    <button
                                      onClick={() => ctx.setVersionRestoreId(v.versionId)}
                                      className="rounded border px-2 py-1 text-xs hover:bg-accent"
                                      data-testid={`storage-version-restore-${v.versionId}`}
                                    >
                                      Restore
                                    </button>
                                  )}
                                </td>
                              </tr>
                            ))}
                            {ctx.blobVersions.data.length === 0 && (
                              <tr><td colSpan={5} className="px-3 py-4 text-center text-muted-foreground">No versions available</td></tr>
                            )}
                          </tbody>
                        </table>
                      </div>
                      {ctx.versionComparison.error && <div className="mt-3 text-sm text-destructive">Error: {ctx.versionComparison.error.message}</div>}
                      {ctx.versionComparison.data && (
                        <div className="mt-3 space-y-3 rounded-md border p-3" data-testid="storage-version-diff-pane">
                          <div className="flex flex-wrap gap-3 text-xs text-muted-foreground">
                            <span className="break-all">Base: <strong className="font-mono text-foreground">{ctx.versionComparison.data.baseVersionId}</strong></span>
                            <span className="break-all">Compare: <strong className="font-mono text-foreground">{ctx.versionComparison.data.compareVersionId ?? "current"}</strong></span>
                            <span>Size: {formatBytes(ctx.versionComparison.data.baseSizeBytes)} → {formatBytes(ctx.versionComparison.data.compareSizeBytes)}</span>
                          </div>
                          <div>
                            <h4 className="mb-1 text-xs font-semibold">Metadata changes</h4>
                            <div className="text-xs text-muted-foreground">
                              Added: {ctx.versionComparison.data.metadataDiff.addedKeys.join(", ") || "none"} · Removed: {ctx.versionComparison.data.metadataDiff.removedKeys.join(", ") || "none"} · Changed: {ctx.versionComparison.data.metadataDiff.changedKeys.join(", ") || "none"}
                            </div>
                          </div>
                          {ctx.versionComparison.data.contentComparePossible && ctx.versionComparison.data.textDiff && (
                            <pre className="max-h-60 overflow-y-auto whitespace-pre-wrap break-words rounded bg-black p-3 text-xs text-green-400" data-testid="storage-version-text-diff">
                              {ctx.versionComparison.data.textDiff}
                            </pre>
                          )}
                        </div>
                      )}
                      {ctx.versionRestoreId && (
                        <ConfirmBar
                          message={`Restore version ${ctx.versionRestoreId} to the current blob?`}
                          confirmLabel="Restore"
                          onConfirm={ctx.handleVersionRestoreConfirm}
                          onCancel={() => ctx.setVersionRestoreId(null)}
                          testId="storage-version-restore-confirm"
                          confirmTestId="storage-version-restore-confirm-yes"
                          cancelTestId="storage-version-restore-confirm-cancel"
                        />
                      )}
                    </>
                  )}
                </div>
              )}

              {ctx.blobDetailTab === "content" && (
                <div data-testid="storage-blob-content-tab">
                  {ctx.blobContent.isLoading && <div className="text-sm text-muted-foreground">Loading content...</div>}
                  {ctx.blobContent.error && <div className="text-sm text-destructive">Error: {ctx.blobContent.error.message}</div>}
                  {ctx.blobContent.data && (
                    <div>
                      {ctx.blobContent.data.isBinary ? (
                        <div className="rounded-md border bg-muted p-4 text-sm text-muted-foreground" data-testid="storage-blob-binary">
                          Binary content ({formatBytes(ctx.blobContent.data.totalSizeBytes)})
                        </div>
                      ) : (
                        <>
                          <pre
                            className="rounded-md border bg-black p-4 text-sm font-mono overflow-y-auto max-h-96 whitespace-pre-wrap break-words text-green-400"
                            data-testid="storage-blob-content"
                          >
                            {ctx.blobContent.data.content}
                          </pre>
                          {ctx.blobContent.data.wasTruncated && (
                            <div className="mt-2 text-xs text-muted-foreground">
                              Content truncated at {formatBytes(ctx.blobContent.data.totalSizeBytes)}
                            </div>
                          )}
                        </>
                      )}
                    </div>
                  )}
                </div>
              )}
            </>
          )}
        </div>
      )}

      {/* Copy blob dialog */}
      {ctx.showCopyDialog && ctx.selectedBlob && (
        <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/50" data-testid="storage-copy-dialog">
          <div className="rounded-lg border bg-card p-6 shadow-lg w-96">
            <h3 className="mb-4 text-lg font-semibold">Copy Blob</h3>
            <div className="space-y-3">
              <div>
                <label className="mb-1 block text-xs text-muted-foreground">Source</label>
                <div className="rounded border bg-muted px-3 py-2 text-xs font-mono">
                  {ctx.selectedContainer}/{ctx.selectedBlob}
                </div>
              </div>
              <div>
                <label className="mb-1 block text-xs text-muted-foreground">Destination Container</label>
                <select
                  value={ctx.copyDestContainer}
                  onChange={(e) => ctx.setCopyDestContainer(e.target.value)}
                  className="w-full rounded border bg-card px-3 py-2 text-sm"
                  data-testid="storage-copy-dest-container"
                >
                  <option value="">Select a container</option>
                  {(ctx.containers.data ?? []).map((container) => (
                    <option key={container.name} value={container.name}>{container.name}</option>
                  ))}
                </select>
              </div>
              <div>
                <label className="mb-1 block text-xs text-muted-foreground">Destination Blob Name</label>
                <input
                  type="text"
                  value={ctx.copyDestBlob}
                  onChange={(e) => ctx.setCopyDestBlob(e.target.value)}
                  placeholder="blob-name"
                  className="w-full rounded border bg-card px-3 py-2 text-sm"
                  data-testid="storage-copy-dest-blob"
                />
              </div>
              <label className="flex items-start gap-2 text-xs">
                <input
                  type="checkbox"
                  checked={ctx.copyOverwrite}
                  onChange={(e) => { ctx.setCopyOverwrite(e.target.checked); ctx.setCopyConfirming(false); }}
                  data-testid="storage-copy-overwrite"
                />
                <span>
                  <span className="block font-medium">Allow overwrite</span>
                  <span className="text-muted-foreground">Only enable this when replacing an existing destination is intentional.</span>
                </span>
              </label>
              {ctx.copyStatus && (
                <p className="text-xs text-muted-foreground" data-testid="storage-copy-status">{ctx.copyStatus}</p>
              )}
              <div className="flex justify-end gap-2">
                <button
                  onClick={() => { ctx.setShowCopyDialog(false); ctx.setCopyStatus(null); }}
                  className="rounded border px-3 py-1.5 text-sm hover:bg-accent"
                >
                  Cancel
                </button>
                <button
                  onClick={ctx.handleCopyConfirm}
                  disabled={!ctx.copyDestContainer.trim() || !ctx.copyDestBlob.trim() || ctx.copyBlob.isPending}
                  className="rounded bg-primary px-3 py-1.5 text-sm text-primary-foreground disabled:opacity-50"
                  data-testid="storage-copy-confirm"
                >
                  {ctx.copyBlob.isPending ? "Copying..." : "Copy"}
                </button>
              </div>
              {ctx.copyConfirming && (
                <ConfirmBar
                  message={`Overwrite ${ctx.copyDestContainer}/${ctx.copyDestBlob} if it already exists?`}
                  confirmLabel="Overwrite"
                  requireTypedName={`${ctx.copyDestContainer}/${ctx.copyDestBlob}`}
                  onConfirm={ctx.handleCopyOverwriteConfirm}
                  onCancel={() => ctx.setCopyConfirming(false)}
                  testId="storage-copy-overwrite-confirm"
                  confirmTestId="storage-copy-overwrite-confirm-yes"
                  cancelTestId="storage-copy-overwrite-confirm-cancel"
                  typedNameTestId="storage-copy-overwrite-confirm-name"
                />
              )}
            </div>
          </div>
        </div>
      )}
    </div>
  );
}
