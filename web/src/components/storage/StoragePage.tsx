import { useState } from "react";
import {
  useProfile,
  useStorageContainers,
  useStorageBlobs,
  useBlobProperties,
  useBlobContent,
} from "@/lib/hooks";
import type { StorageBlobItem } from "@/lib/types";
import { Download, Link as LinkIcon, Check, Plus, Trash2 } from "lucide-react";

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

export function StoragePage() {
  const { data: profile } = useProfile();
  const accounts = profile?.config?.storageAccounts ?? [];
  const activeAccountId = accounts[0]?.id ?? null;

  const [selectedContainer, setSelectedContainer] = useState<string | null>(null);
  const [currentPrefix, setCurrentPrefix] = useState("");
  const [prefixHistory, setPrefixHistory] = useState<string[]>([]);
  const [selectedBlob, setSelectedBlob] = useState<string | null>(null);
  const [continuationToken, setContinuationToken] = useState<string | null>(null);
  const [allItems, setAllItems] = useState<StorageBlobItem[]>([]);
  const [blobFilter, setBlobFilter] = useState("");
  const [multiSelectMode, setMultiSelectMode] = useState(false);
  const [selectedBlobs, setSelectedBlobs] = useState<Set<string>>(new Set());
  const [copiedUrl, setCopiedUrl] = useState(false);
  const [metadataEditing, setMetadataEditing] = useState(false);
  const [metadataDraft, setMetadataDraft] = useState<Record<string, string>>({});

  const containers = useStorageContainers(activeAccountId);
  const blobs = useStorageBlobs(activeAccountId, selectedContainer, currentPrefix, continuationToken);
  const blobProps = useBlobProperties(activeAccountId, selectedContainer, selectedBlob);
  const blobContent = useBlobContent(activeAccountId, selectedContainer, selectedBlob);

  const handleSelectContainer = (name: string) => {
    setSelectedContainer(name);
    setCurrentPrefix("");
    setPrefixHistory([]);
    setSelectedBlob(null);
    setContinuationToken(null);
    setAllItems([]);
  };

  const handleNavigatePrefix = (prefix: string) => {
    setPrefixHistory((prev) => [...prev, currentPrefix]);
    setCurrentPrefix(prefix);
    setSelectedBlob(null);
    setContinuationToken(null);
    setAllItems([]);
  };

  const handleBreadcrumb = (index: number) => {
    const newPrefix = index === 0 ? "" : prefixHistory[index - 1] ?? "";
    setPrefixHistory((prev) => prev.slice(0, index));
    setCurrentPrefix(newPrefix);
    setSelectedBlob(null);
    setContinuationToken(null);
    setAllItems([]);
  };

  const handleLoadMore = () => {
    if (blobs.data?.continuationToken) {
      setAllItems((prev) => [...prev, ...(blobs.data?.items ?? [])]);
      setContinuationToken(blobs.data.continuationToken);
    }
  };

  const handleSelectBlob = (name: string) => {
    setSelectedBlob(name);
  };

  const displayItems = continuationToken === null ? (blobs.data?.items ?? []) : [...allItems, ...(blobs.data?.items ?? [])];

  const filteredItems = blobFilter
    ? displayItems.filter((item) => item.name.toLowerCase().includes(blobFilter.toLowerCase()))
    : displayItems;

  const handleCopyUrl = (blobName: string) => {
    const url = `https://${activeAccountId}.blob.core.windows.net/${selectedContainer}/${blobName}`;
    navigator.clipboard.writeText(url);
    setCopiedUrl(true);
    setTimeout(() => setCopiedUrl(false), 2000);
  };

  const handleDownloadBlob = async (blobName: string) => {
    try {
      const response = await fetch(`/api/storage/${activeAccountId}/containers/${selectedContainer}/blobs/${encodeURIComponent(blobName)}/content`);
      const data = await response.json();
      if (data.content) {
        const blob = new Blob([data.content], { type: data.contentType || "text/plain" });
        const url = URL.createObjectURL(blob);
        const a = document.createElement("a");
        a.href = url;
        a.download = blobName.split("/").pop() || blobName;
        a.click();
        URL.revokeObjectURL(url);
      }
    } catch (e) {
      console.error("Download failed:", e);
    }
  };

  const toggleBlobSelection = (name: string) => {
    setSelectedBlobs((prev) => {
      const next = new Set(prev);
      if (next.has(name)) next.delete(name);
      else next.add(name);
      return next;
    });
  };

  if (!activeAccountId) {
    return (
      <div className="p-6" data-testid="storage-page">
        <h1 className="text-2xl font-bold" data-testid="storage-title">Storage</h1>
        <p className="mt-4 text-muted-foreground" data-testid="storage-no-account">
          No storage account configured. Add one in Settings.
        </p>
      </div>
    );
  }

  return (
    <div className="flex h-full flex-col" data-testid="storage-page">
      <div className="border-b px-6 py-3">
        <h1 className="text-2xl font-bold" data-testid="storage-title">Storage</h1>
      </div>

      <div className="flex flex-1 overflow-hidden">
        {/* Container list */}
        <div className="w-48 border-r overflow-auto" data-testid="storage-container-list">
          <div className="px-3 py-2 text-xs font-semibold text-muted-foreground uppercase">Containers</div>
          {containers.isLoading && (
            <div className="px-3 py-2 text-sm text-muted-foreground">Loading...</div>
          )}
          {containers.error && (
            <div className="px-3 py-2 text-sm text-destructive" data-testid="storage-container-error">
              Error: {containers.error.message}
            </div>
          )}
          {containers.data?.map((c) => (
            <button
              key={c.name}
              data-testid={`storage-container-${c.name}`}
              onClick={() => handleSelectContainer(c.name)}
              className={`flex w-full items-center px-3 py-1.5 text-left text-sm transition-colors hover:bg-accent ${
                selectedContainer === c.name ? "bg-accent" : ""
              }`}
            >
              <span className="truncate font-mono">{c.name}</span>
            </button>
          ))}
          {(!containers.data || containers.data.length === 0) && !containers.isLoading && (
            <div className="px-3 py-2 text-sm text-muted-foreground">No containers</div>
          )}
        </div>

        {/* Blob browser */}
        <div className="w-1/3 border-r overflow-hidden flex flex-col" data-testid="storage-blob-browser">
          {!selectedContainer ? (
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
                    onClick={() => handleBreadcrumb(0)}
                    className="text-primary hover:underline"
                  >
                    {selectedContainer}
                  </button>
                  {prefixHistory.map((p, i) => (
                    <span key={i} className="flex items-center gap-1">
                      <span className="text-muted-foreground">/</span>
                      <button
                        data-testid={`storage-breadcrumb-${i + 1}`}
                        onClick={() => handleBreadcrumb(i + 1)}
                        className="text-primary hover:underline"
                      >
                        {p.replace(currentPrefix, "").replace(/\//g, "") || p}
                      </button>
                    </span>
                  ))}
                  {currentPrefix && (
                    <span className="text-muted-foreground">/ {currentPrefix.replace(prefixHistory[prefixHistory.length - 1] ?? "", "")}</span>
                  )}
                </div>
                <div className="mt-2 flex items-center gap-2">
                  <input
                    type="text"
                    placeholder="Filter blobs..."
                    value={blobFilter}
                    onChange={(e) => setBlobFilter(e.target.value)}
                    className="flex-1 rounded border bg-card px-2 py-1 text-xs"
                    data-testid="storage-blob-filter"
                  />
                  <button
                    onClick={() => { setMultiSelectMode(!multiSelectMode); setSelectedBlobs(new Set()); }}
                    className={`rounded border px-2 py-1 text-xs ${multiSelectMode ? "bg-primary text-primary-foreground" : "hover:bg-accent"}`}
                    data-testid="storage-multi-select-toggle"
                  >
                    {multiSelectMode ? "Exit Multi" : "Multi-Select"}
                  </button>
                  {multiSelectMode && selectedBlobs.size > 0 && (
                    <>
                      <span className="text-xs text-muted-foreground" data-testid="storage-batch-count">{selectedBlobs.size} selected</span>
                      <button onClick={() => selectedBlobs.forEach((b) => handleDownloadBlob(b))} className="flex items-center gap-1 rounded border px-2 py-1 text-xs hover:bg-accent" data-testid="storage-batch-download">
                        <Download className="h-3 w-3" /> Download
                      </button>
                      <button onClick={() => setSelectedBlobs(new Set())} className="text-xs text-muted-foreground" data-testid="storage-batch-clear">Clear</button>
                    </>
                  )}
                </div>
              </div>

              {/* Blob list */}
              <div className="flex-1 overflow-auto">
                {blobs.isLoading && (
                  <div className="px-3 py-2 text-sm text-muted-foreground">Loading blobs...</div>
                )}
                {blobs.error && (
                  <div className="px-3 py-2 text-sm text-destructive" data-testid="storage-blob-error">
                    Error: {blobs.error.message}
                  </div>
                )}
                {displayItems.length === 0 && !blobs.isLoading && (
                  <div className="px-3 py-2 text-sm text-muted-foreground">No blobs found</div>
                )}
                {filteredItems.map((item) => (
                  <div
                    key={item.name}
                    data-testid={`storage-item-${item.name}`}
                    onClick={() => multiSelectMode && !item.isPrefix ? toggleBlobSelection(item.name) : item.isPrefix ? handleNavigatePrefix(item.name) : handleSelectBlob(item.name)}
                    className={`flex w-full items-center gap-2 px-3 py-1.5 text-left text-sm transition-colors hover:bg-accent cursor-pointer ${
                      multiSelectMode && !item.isPrefix ? selectedBlobs.has(item.name) ? "bg-primary/20" : "" : !item.isPrefix && selectedBlob === item.name ? "bg-accent" : ""
                    }`}
                  >
                    {multiSelectMode && !item.isPrefix && (
                      <input
                        type="checkbox"
                        checked={selectedBlobs.has(item.name)}
                        onChange={() => toggleBlobSelection(item.name)}
                        onClick={(e) => e.stopPropagation()}
                        className="h-3.5 w-3.5"
                        data-testid={`storage-blob-checkbox-${item.name}`}
                      />
                    )}
                    <span className={item.isPrefix ? "text-blue-400" : "text-muted-foreground"}>
                      {item.isPrefix ? "📁" : "📄"}
                    </span>
                    <span className="truncate font-mono">
                      {item.isPrefix ? item.name.replace(currentPrefix, "") : item.name.replace(currentPrefix, "")}
                    </span>
                    {!item.isPrefix && (
                      <span className="ml-auto text-xs text-muted-foreground">{formatBytes(item.sizeBytes)}</span>
                    )}
                  </div>
                ))}
                {blobs.data?.continuationToken && (
                  <button
                    data-testid="storage-load-more"
                    onClick={handleLoadMore}
                    className="w-full px-3 py-2 text-sm text-primary hover:bg-accent"
                  >
                    Load more...
                  </button>
                )}
              </div>
            </>
          )}
        </div>

        {/* Blob detail */}
        <div className="flex-1 overflow-auto" data-testid="storage-blob-detail">
          {!selectedBlob ? (
            <div className="flex h-full items-center justify-center text-muted-foreground" data-testid="storage-no-blob-selected">
              Select a blob to view details
            </div>
          ) : (
            <div className="p-6 space-y-4">
              {blobProps.isLoading && <div className="text-sm text-muted-foreground">Loading blob properties...</div>}
              {blobProps.error && <div className="text-sm text-destructive">Error: {blobProps.error.message}</div>}
              {blobProps.data && (
                <>
                  <div>
                    <div className="flex items-center gap-2">
                      <div className="text-lg font-mono font-semibold" data-testid="storage-blob-name">
                        {blobProps.data.name}
                      </div>
                      <button onClick={() => handleCopyUrl(blobProps.data.name)} className="text-muted-foreground hover:text-foreground" data-testid="storage-copy-url-btn" title="Copy URL">
                        {copiedUrl ? <Check className="h-3.5 w-3.5 text-green-500" /> : <LinkIcon className="h-3.5 w-3.5" />}
                      </button>
                      <button onClick={() => handleDownloadBlob(blobProps.data.name)} className="text-muted-foreground hover:text-foreground" data-testid="storage-download-btn" title="Download blob">
                        <Download className="h-3.5 w-3.5" />
                      </button>
                    </div>
                    <div className="mt-1 flex flex-wrap items-center gap-3 text-sm">
                      <span className="text-muted-foreground" data-testid="storage-blob-size">
                        {formatBytes(blobProps.data.sizeBytes)}
                      </span>
                      <span className="text-muted-foreground" data-testid="storage-blob-type">
                        {blobProps.data.contentType}
                      </span>
                      <span className="text-muted-foreground" data-testid="storage-blob-modified">
                        {formatDate(blobProps.data.lastModified)}
                      </span>
                      {blobProps.data.accessTier && (
                        <span className="text-muted-foreground">Tier: {blobProps.data.accessTier}</span>
                      )}
                    </div>
                  </div>

                  {/* Metadata */}
                  <div>
                    <div className="mb-2 flex items-center justify-between">
                      <h3 className="text-sm font-semibold">Metadata</h3>
                      {metadataEditing ? (
                        <div className="flex items-center gap-2">
                          <button onClick={() => setMetadataEditing(false)} className="rounded bg-primary px-2 py-1 text-xs text-primary-foreground" data-testid="storage-metadata-save">Save</button>
                          <button onClick={() => { setMetadataEditing(false); setMetadataDraft({}); }} className="rounded border px-2 py-1 text-xs" data-testid="storage-metadata-cancel">Cancel</button>
                        </div>
                      ) : (
                        <button onClick={() => { setMetadataEditing(true); setMetadataDraft(blobProps.data.metadata); }} className="flex items-center gap-1 text-xs text-muted-foreground hover:text-foreground" data-testid="storage-metadata-edit-btn">
                          <Plus className="h-3 w-3" /> Edit
                        </button>
                      )}
                    </div>
                    {metadataEditing ? (
                      <div className="rounded-md border p-3 space-y-2" data-testid="storage-metadata-editor">
                        {Object.entries(metadataDraft).map(([k, v]) => (
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
                              onChange={(e) => setMetadataDraft((prev) => ({ ...prev, [k]: e.target.value }))}
                              className="flex-1 rounded border bg-card px-2 py-1 text-xs font-mono"
                            />
                            <button onClick={() => { const next = { ...metadataDraft }; delete next[k]; setMetadataDraft(next); }} className="text-destructive hover:bg-destructive/10 rounded p-1">
                              <Trash2 className="h-3 w-3" />
                            </button>
                          </div>
                        ))}
                        <button
                          onClick={() => setMetadataDraft((prev) => ({ ...prev, "new-key": "" }))}
                          className="flex items-center gap-1 text-xs text-primary hover:underline"
                          data-testid="storage-metadata-add-key"
                        >
                          <Plus className="h-3 w-3" /> Add key
                        </button>
                      </div>
                    ) : Object.keys(blobProps.data.metadata).length > 0 ? (
                      <div className="rounded-md border overflow-hidden">
                        <table className="w-full text-sm">
                          <thead className="bg-muted">
                            <tr>
                              <th className="px-3 py-2 text-left font-medium">Key</th>
                              <th className="px-3 py-2 text-left font-medium">Value</th>
                            </tr>
                          </thead>
                          <tbody>
                            {Object.entries(blobProps.data.metadata).map(([k, v]) => (
                              <tr key={k} className="border-t">
                                <td className="px-3 py-2 font-mono">{k}</td>
                                <td className="px-3 py-2 font-mono">{v}</td>
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

              {/* Content preview */}
              {blobContent.isLoading && <div className="text-sm text-muted-foreground">Loading content...</div>}
              {blobContent.error && <div className="text-sm text-destructive">Error: {blobContent.error.message}</div>}
              {blobContent.data && (
                <div>
                  <h3 className="mb-2 text-sm font-semibold">Content Preview</h3>
                  {blobContent.data.isBinary ? (
                    <div className="rounded-md border bg-muted p-4 text-sm text-muted-foreground" data-testid="storage-blob-binary">
                      Binary content ({formatBytes(blobContent.data.totalSizeBytes)})
                    </div>
                  ) : (
                    <>
                      <pre
                        className="rounded-md border bg-muted p-4 text-sm font-mono overflow-auto max-h-96"
                        data-testid="storage-blob-content"
                      >
                        {blobContent.data.content}
                      </pre>
                      {blobContent.data.wasTruncated && (
                        <div className="mt-2 text-xs text-muted-foreground">
                          Content truncated at {formatBytes(blobContent.data.totalSizeBytes)}
                        </div>
                      )}
                    </>
                  )}
                </div>
              )}
            </div>
          )}
        </div>
      </div>
    </div>
  );
}

