import { Download, Upload, File, Folder, ArrowUp, ArrowDown } from "lucide-react";
import { useStoragePageContext } from "./StoragePageContext";
import { formatBytes } from "@/lib/format-bytes";
import { useVirtualizer } from "@tanstack/react-virtual";
import { buildStorageBreadcrumbAncestors, storageCurrentPrefixLabel } from "@/lib/storage-breadcrumb";
import type { StorageBlobSortKey } from "@/lib/storage-blob-sort";
import { LastRefreshed } from "@/components/shared/LastRefreshed";
import { ConfirmBar } from "@/components/shared/ConfirmBar";

export function BlobBrowserPanel() {
  const ctx = useStoragePageContext();

  // Owned here rather than in the page context: `useVirtualizer` returns a stable
  // instance whose internals mutate on scroll, so a memoized context value holding it
  // would keep handing consumers the same object and the list would stop re-rendering
  // as you scroll.
  const blobVirtualizer = useVirtualizer({
    count: ctx.filteredItems.length,
    getScrollElement: () => ctx.blobListRef.current,
    estimateSize: () => 30,
    getItemKey: (index) => ctx.filteredItems[index].name,
    measureElement: (el) => el?.getBoundingClientRect().height ?? 30,
  });

  // 6.2 fix: labels used to be computed by string-replacing each ancestor prefix against
  // `ctx.currentPrefix` (the deepest prefix, a constant across the whole loop) instead of
  // against that ancestor's own immediate parent — see lib/storage-breadcrumb.ts for the
  // full explanation and lib/storage-breadcrumb.test.ts for regression coverage.
  const breadcrumbAncestors = buildStorageBreadcrumbAncestors(ctx.prefixHistory);
  const currentSegmentLabel = storageCurrentPrefixLabel(ctx.prefixHistory, ctx.currentPrefix);

  const hasMoreBlobs = !!ctx.blobs.data?.continuationToken;
  const isFilterActive = ctx.blobFilter.trim().length > 0;

  return (
    <div className="flex h-full w-full flex-col overflow-hidden" data-testid="storage-blob-browser">
      {!ctx.selectedContainer ? (
        <div className="flex h-full items-center justify-center text-muted-foreground" data-testid="storage-no-container">
          Select a container
        </div>
      ) : (
        <>
          {/* Breadcrumbs + filter */}
          <div className="px-3 py-2 border-b">
            <div className="flex items-center gap-1 text-sm">
              <button
                data-testid="storage-breadcrumb-0"
                onClick={() => ctx.handleBreadcrumb(0)}
                className="text-primary hover:underline"
              >
                {ctx.selectedContainer}
              </button>
              {breadcrumbAncestors.map((ancestor) => (
                <span key={ancestor.prefix} className="flex items-center gap-1">
                  <span className="text-muted-foreground">/</span>
                  <button
                    data-testid={`storage-breadcrumb-${ancestor.navigateIndex}`}
                    onClick={() => ctx.handleBreadcrumb(ancestor.navigateIndex)}
                    className="text-primary hover:underline"
                  >
                    {ancestor.label}
                  </button>
                </span>
              ))}
              {currentSegmentLabel && (
                <span className="text-muted-foreground">/ {currentSegmentLabel}</span>
              )}
            </div>
            <div className="mt-2 flex items-center gap-2">
              <input
                type="text"
                placeholder="Filter blobs..."
                value={ctx.blobFilter}
                onChange={(e) => ctx.setBlobFilter(e.target.value)}
                className="flex-1 rounded border bg-card px-2 py-1 text-xs"
                data-testid="storage-blob-filter"
              />
              <select
                value={ctx.blobSortKey}
                onChange={(e) => ctx.setBlobSortKey(e.target.value as StorageBlobSortKey)}
                className="rounded border bg-card px-2 py-1 text-xs"
                data-testid="storage-sort-key"
                title="Sort by"
              >
                <option value="name">Name</option>
                <option value="size">Size</option>
                <option value="modified">Modified</option>
              </select>
              <button
                onClick={() => ctx.setBlobSortDir(ctx.blobSortDir === "asc" ? "desc" : "asc")}
                className="rounded border px-1.5 py-1 hover:bg-accent"
                data-testid="storage-sort-dir"
                title={ctx.blobSortDir === "asc" ? "Ascending — click for descending" : "Descending — click for ascending"}
              >
                {ctx.blobSortDir === "asc" ? <ArrowUp className="h-3 w-3" /> : <ArrowDown className="h-3 w-3" />}
              </button>
              <button
                onClick={() => { ctx.setMultiSelectMode(!ctx.multiSelectMode); ctx.setSelectedBlobs(new Set()); }}
                className={`rounded border px-2 py-1 text-xs ${ctx.multiSelectMode ? "bg-primary text-primary-foreground" : "hover:bg-accent"}`}
                data-testid="storage-multi-select-toggle"
              >
                {ctx.multiSelectMode ? "Exit Multi" : "Multi-Select"}
              </button>
              <span title={ctx.allowMutations ? "Upload a new blob" : "Mutations are disabled for this storage account. Enable allowMutations in Settings."}>
                <button
                  onClick={() => ctx.setShowUpload(!ctx.showUpload)}
                  disabled={!ctx.allowMutations}
                  className={`flex items-center gap-1 rounded border px-2 py-1 text-xs disabled:cursor-not-allowed disabled:opacity-50 ${ctx.showUpload ? "bg-primary text-primary-foreground" : "hover:bg-accent"}`}
                  data-testid="storage-upload-toggle"
                >
                  <Upload className="h-3 w-3" /> Upload
                </button>
              </span>
              {ctx.multiSelectMode && ctx.selectedBlobs.size > 0 && (
                <>
                  <span className="text-xs text-muted-foreground" data-testid="storage-batch-count">{ctx.selectedBlobs.size} selected</span>
                  <button onClick={() => ctx.handleBatchDownloadBlobs([...ctx.selectedBlobs])} className="flex items-center gap-1 rounded border px-2 py-1 text-xs hover:bg-accent" data-testid="storage-batch-download">
                    <Download className="h-3 w-3" /> Download
                  </button>
                  <button onClick={() => ctx.setSelectedBlobs(new Set())} className="text-xs text-muted-foreground" data-testid="storage-batch-clear">Clear</button>
                </>
              )}
              <LastRefreshed
                at={ctx.blobs.dataUpdatedAt || null}
                isFetching={ctx.blobs.isFetching}
                testId="storage-last-refreshed"
              />
            </div>
            {isFilterActive && hasMoreBlobs && (
              <div className="mt-1 text-xs text-muted-foreground" data-testid="storage-filter-scope-notice">
                {ctx.filteredItems.length === 0
                  ? `No matches in the ${ctx.displayItems.length} blob(s) loaded so far — loading more automatically…`
                  : `Showing ${ctx.filteredItems.length} of ${ctx.displayItems.length} loaded (total unknown) — Load more to search further.`}
              </div>
            )}
            {ctx.showUpload && (
              <div className="border-b p-3" data-testid="storage-upload-panel">
                <h4 className="mb-2 text-xs font-semibold">Upload Blob</h4>
                <div className="space-y-2">
                  <div
                    {...ctx.uploadDropzone.getRootProps()}
                    className={`cursor-pointer rounded border border-dashed px-3 py-4 text-center text-xs ${
                      ctx.uploadDropzone.isDragActive ? "border-primary bg-primary/10" : "hover:bg-accent"
                    }`}
                    data-testid="storage-upload-dropzone"
                  >
                    <input {...ctx.uploadDropzone.getInputProps()} data-testid="storage-upload-file" />
                    {ctx.uploadFile ? (
                      <span className="font-mono">{ctx.uploadFile.name} ({formatBytes(ctx.uploadFile.size)})</span>
                    ) : ctx.uploadDropzone.isDragActive ? (
                      <span>Drop the file here</span>
                    ) : (
                      <span>Drop a file here or click to browse</span>
                    )}
                  </div>
                  <input
                    type="text"
                    value={ctx.uploadBlobName}
                    onChange={(e) => ctx.setUploadBlobName(e.target.value)}
                    placeholder="Blob name (e.g. folder/file.json)"
                    className="w-full rounded border bg-card px-2 py-1 text-xs"
                    data-testid="storage-upload-name"
                  />
                  {ctx.uploadBlob.isPending && (
                    <div className="space-y-1" data-testid="storage-upload-progress">
                      <div className="h-1.5 overflow-hidden rounded bg-muted">
                        <div className="h-full bg-primary transition-all" style={{ width: `${ctx.uploadProgress}%` }} />
                      </div>
                      <div className="text-right text-xs text-muted-foreground">{ctx.uploadProgress}%</div>
                    </div>
                  )}
                  <button
                    onClick={ctx.handleUploadConfirm}
                    disabled={!ctx.uploadBlobName.trim() || !ctx.uploadFile || ctx.uploadBlob.isPending || ctx.uploadCheckingOverwrite}
                    title={ctx.uploadBlob.isPending ? "Uploading…" : ctx.uploadCheckingOverwrite ? "Checking for an existing blob…" : !ctx.uploadFile || !ctx.uploadBlobName.trim() ? "Choose a file and blob name first" : undefined}
                    className="rounded bg-primary px-3 py-1 text-xs text-primary-foreground disabled:opacity-50"
                    data-testid="storage-upload-confirm"
                  >
                    {ctx.uploadBlob.isPending ? "Uploading..." : ctx.uploadCheckingOverwrite ? "Checking..." : "Upload"}
                  </button>
                </div>
                {/* 6.3: Upload used to silently overwrite an existing blob of the same name
                    with no warning at all, unlike Copy's "Allow overwrite" + confirm guard.
                    `handleUploadConfirm` checks for a name collision first and populates
                    `uploadOverwriteConfirm` instead of uploading immediately when one exists. */}
                {ctx.uploadOverwriteConfirm && (
                  <ConfirmBar
                    message={`"${ctx.uploadOverwriteConfirm.blobName}" already exists in this container. Uploading will overwrite it.`}
                    confirmLabel="Overwrite"
                    onConfirm={ctx.handleUploadOverwriteConfirm}
                    onCancel={() => ctx.setUploadOverwriteConfirm(null)}
                    confirmDisabled={ctx.uploadBlob.isPending}
                    testId="storage-upload-overwrite-confirm"
                    confirmTestId="storage-upload-overwrite-confirm-yes"
                    cancelTestId="storage-upload-overwrite-confirm-cancel"
                  />
                )}
              </div>
            )}
          </div>

          {/* Blob list */}
          <div ref={ctx.blobListRef} className="flex-1 overflow-auto" data-testid="storage-blob-list-scroll">
            {ctx.blobs.isLoading && (
              <div className="px-3 py-2 text-sm text-muted-foreground">Loading blobs...</div>
            )}
            {ctx.blobs.error && (
              <div className="px-3 py-2 text-sm text-destructive" data-testid="storage-blob-error">
                Error: {ctx.blobs.error.message}
              </div>
            )}
            {ctx.displayItems.length === 0 && !ctx.blobs.isLoading && (
              <div className="px-3 py-2 text-sm text-muted-foreground">No blobs found</div>
            )}
            {ctx.filteredItems.length > 0 && (
              <div
                style={{ height: `${blobVirtualizer.getTotalSize()}px`, position: "relative", width: "100%" }}
                data-testid="storage-blob-list-virtualizer"
              >
                {blobVirtualizer.getVirtualItems().map((virtualItem) => {
                  const item = ctx.filteredItems[virtualItem.index];
                  return (
                    <div
                      key={virtualItem.key}
                      data-index={virtualItem.index}
                      ref={blobVirtualizer.measureElement}
                      style={{
                        position: "absolute",
                        top: 0,
                        left: 0,
                        width: "100%",
                        transform: `translateY(${virtualItem.start}px)`,
                      }}
                    >
                      <div
                        data-testid={`storage-item-${item.name}`}
                        onClick={() => ctx.multiSelectMode && !item.isPrefix ? ctx.toggleBlobSelection(item.name) : item.isPrefix ? ctx.handleNavigatePrefix(item.name) : ctx.handleSelectBlob(item.name)}
                        className={`flex w-full items-start gap-2 px-3 py-1.5 text-left text-sm transition-colors hover:bg-accent cursor-pointer ${
                          ctx.multiSelectMode && !item.isPrefix ? ctx.selectedBlobs.has(item.name) ? "bg-primary/20" : "" : !item.isPrefix && ctx.selectedBlob === item.name ? "bg-accent" : ""
                        }`}
                      >
                        {ctx.multiSelectMode && !item.isPrefix && (
                          <input
                            type="checkbox"
                            checked={ctx.selectedBlobs.has(item.name)}
                            onChange={() => ctx.toggleBlobSelection(item.name)}
                            onClick={(e) => e.stopPropagation()}
                            className="mt-0.5 h-3.5 w-3.5 shrink-0"
                            data-testid={`storage-blob-checkbox-${item.name}`}
                          />
                        )}
                        {item.isPrefix ? (
                          <Folder className="mt-0.5 h-3.5 w-3.5 shrink-0 text-blue-400" />
                        ) : (
                          <File className="mt-0.5 h-3.5 w-3.5 shrink-0 text-muted-foreground" />
                        )}
                        {/* Wraps rather than truncates: blob names here run to 80+ characters
                            and the distinguishing part sits at the end. The virtualizer measures
                            each row, so taller rows lay out correctly. */}
                        <span className="min-w-0 flex-1 break-all font-mono" title={item.name}>
                          {item.name.replace(ctx.currentPrefix, "")}
                        </span>
                        {!item.isPrefix && item.lastModified && (
                          <span className="w-32 shrink-0 self-start text-right text-xs text-muted-foreground" data-testid={`storage-item-modified-${item.name}`}>
                            {new Date(item.lastModified).toLocaleString()}
                          </span>
                        )}
                        {!item.isPrefix && (
                          <span className="shrink-0 self-start text-xs text-muted-foreground">{formatBytes(item.sizeBytes)}</span>
                        )}
                      </div>
                    </div>
                  );
                })}
              </div>
            )}
            {ctx.blobs.data?.continuationToken && (
              <button
                data-testid="storage-load-more"
                onClick={ctx.handleLoadMore}
                className="w-full px-3 py-2 text-sm text-primary hover:bg-accent"
              >
                Load more...
              </button>
            )}
          </div>
        </>
      )}
    </div>
  );
}
