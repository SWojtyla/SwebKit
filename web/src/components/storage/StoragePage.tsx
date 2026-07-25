import { useState } from "react";
import {
  useProfile,
  useStorageContainers,
  useStorageBlobs,
  useBlobProperties,
  useBlobContent,
} from "@/lib/hooks";
import type { StorageBlobItem } from "@/lib/types";

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
              {/* Breadcrumbs */}
              <div className="flex items-center gap-1 px-3 py-2 border-b text-sm">
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
                {displayItems.map((item) => (
                  <button
                    key={item.name}
                    data-testid={`storage-item-${item.name}`}
                    onClick={() => item.isPrefix ? handleNavigatePrefix(item.name) : handleSelectBlob(item.name)}
                    className={`flex w-full items-center gap-2 px-3 py-1.5 text-left text-sm transition-colors hover:bg-accent ${
                      !item.isPrefix && selectedBlob === item.name ? "bg-accent" : ""
                    }`}
                  >
                    <span className={item.isPrefix ? "text-blue-400" : "text-muted-foreground"}>
                      {item.isPrefix ? "📁" : "📄"}
                    </span>
                    <span className="truncate font-mono">
                      {item.isPrefix ? item.name.replace(currentPrefix, "") : item.name.replace(currentPrefix, "")}
                    </span>
                    {!item.isPrefix && (
                      <span className="ml-auto text-xs text-muted-foreground">{formatBytes(item.sizeBytes)}</span>
                    )}
                  </button>
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
                    <div className="text-lg font-mono font-semibold" data-testid="storage-blob-name">
                      {blobProps.data.name}
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
                  {Object.keys(blobProps.data.metadata).length > 0 && (
                    <div>
                      <h3 className="mb-2 text-sm font-semibold">Metadata</h3>
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
                    </div>
                  )}
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

