import { Download, Upload, File, Folder } from "lucide-react";
import { useStoragePageContext } from "./StoragePageContext";

function formatBytes(bytes: number | null | undefined): string {
  if (bytes == null) return "-";
  if (bytes < 1024) return `${bytes}B`;
  if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)}K`;
  return `${(bytes / (1024 * 1024)).toFixed(1)}M`;
}

export function BlobBrowserPanel() {
  const ctx = useStoragePageContext();

  return (
    <div className="w-1/3 border-r overflow-hidden flex flex-col" data-testid="storage-blob-browser">
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
              {ctx.prefixHistory.map((p, i) => (
                <span key={i} className="flex items-center gap-1">
                  <span className="text-muted-foreground">/</span>
                  <button
                    data-testid={`storage-breadcrumb-${i + 1}`}
                    onClick={() => ctx.handleBreadcrumb(i + 1)}
                    className="text-primary hover:underline"
                  >
                    {p.replace(ctx.currentPrefix, "").replace(/\//g, "") || p}
                  </button>
                </span>
              ))}
              {ctx.currentPrefix && (
                <span className="text-muted-foreground">/ {ctx.currentPrefix.replace(ctx.prefixHistory[ctx.prefixHistory.length - 1] ?? "", "")}</span>
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
            </div>
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
                    disabled={!ctx.uploadBlobName.trim() || !ctx.uploadFile || ctx.uploadBlob.isPending}
                    className="rounded bg-primary px-3 py-1 text-xs text-primary-foreground disabled:opacity-50"
                    data-testid="storage-upload-confirm"
                  >
                    {ctx.uploadBlob.isPending ? "Uploading..." : "Upload"}
                  </button>
                </div>
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
                style={{ height: `${ctx.blobVirtualizer.getTotalSize()}px`, position: "relative", width: "100%" }}
                data-testid="storage-blob-list-virtualizer"
              >
                {ctx.blobVirtualizer.getVirtualItems().map((virtualItem) => {
                  const item = ctx.filteredItems[virtualItem.index];
                  return (
                    <div
                      key={virtualItem.key}
                      data-index={virtualItem.index}
                      ref={ctx.blobVirtualizer.measureElement}
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
                        className={`flex w-full items-center gap-2 px-3 py-1.5 text-left text-sm transition-colors hover:bg-accent cursor-pointer ${
                          ctx.multiSelectMode && !item.isPrefix ? ctx.selectedBlobs.has(item.name) ? "bg-primary/20" : "" : !item.isPrefix && ctx.selectedBlob === item.name ? "bg-accent" : ""
                        }`}
                      >
                        {ctx.multiSelectMode && !item.isPrefix && (
                          <input
                            type="checkbox"
                            checked={ctx.selectedBlobs.has(item.name)}
                            onChange={() => ctx.toggleBlobSelection(item.name)}
                            onClick={(e) => e.stopPropagation()}
                            className="h-3.5 w-3.5"
                            data-testid={`storage-blob-checkbox-${item.name}`}
                          />
                        )}
                        {item.isPrefix ? (
                          <Folder className="h-3.5 w-3.5 shrink-0 text-blue-400" />
                        ) : (
                          <File className="h-3.5 w-3.5 shrink-0 text-muted-foreground" />
                        )}
                        <span className="truncate font-mono">
                          {item.isPrefix ? item.name.replace(ctx.currentPrefix, "") : item.name.replace(ctx.currentPrefix, "")}
                        </span>
                        {!item.isPrefix && (
                          <span className="ml-auto text-xs text-muted-foreground">{formatBytes(item.sizeBytes)}</span>
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
