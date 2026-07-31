import { useState, useEffect } from "react";
import { useLocation, useNavigate } from "react-router-dom";
import {
  useProfile,
  useStorageContainers,
  useStorageBlobs,
  useBlobProperties,
  useBlobContent,
  useBlobSasUrl,
  useBlobVersions,
  useBlobVersionComparison,
  useUploadBlob,
  useCopyBlob,
  useRestoreBlobVersion,
  useDeletedBlobs,
  useSetBlobMetadata,
} from "@/lib/hooks";
import type { StorageBlobItem } from "@/lib/types";
import { Download, Link as LinkIcon, Check, Plus, Trash2, RotateCcw, Upload, Copy as CopyIcon } from "lucide-react";
import { BlobRecoveryPanel } from "./BlobRecoveryPanel";
import { ConfirmBar } from "@/components/shared/ConfirmBar";
import { useNotification } from "@/components/layout/NotificationSystem";
import { useDropzone } from "react-dropzone";

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
  const location = useLocation();
  const navigate = useNavigate();
  const accounts = profile?.config?.storageAccounts ?? [];
  const [activeAccountId, setActiveAccountId] = useState<string | null>(accounts[0]?.id ?? null);
  const resolvedAccountId = activeAccountId ?? accounts[0]?.id ?? null;
  const activeAccount = accounts.find((a) => a.id === resolvedAccountId);
  const allowMutations = activeAccount?.allowMutations ?? false;
  const { notify } = useNotification();

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
  const [storageViewMode, setStorageViewMode] = useState<"browser" | "recovery">("browser");
  const [blobDetailTab, setBlobDetailTab] = useState<"properties" | "versions" | "content">("properties");
  const [showSasUrl, setShowSasUrl] = useState(false);
  const [showUpload, setShowUpload] = useState(false);
  const [uploadBlobName, setUploadBlobName] = useState("");
  const [uploadFile, setUploadFile] = useState<File | null>(null);
  const [uploadProgress, setUploadProgress] = useState(0);
  const [showCopyDialog, setShowCopyDialog] = useState(false);
  const [copyDestContainer, setCopyDestContainer] = useState("");
  const [copyDestBlob, setCopyDestBlob] = useState("");
  const [copyOverwrite, setCopyOverwrite] = useState(false);
  const [copyConfirming, setCopyConfirming] = useState(false);
  const [copyStatus, setCopyStatus] = useState<string | null>(null);
  const [versionBaseId, setVersionBaseId] = useState<string | null>(null);
  const [versionCompareId, setVersionCompareId] = useState<string | null>(null);
  const [versionCompareRequested, setVersionCompareRequested] = useState(false);
  const [versionRestoreId, setVersionRestoreId] = useState<string | null>(null);

  useEffect(() => {
    const state = location.state as { accountId?: string } | null;
    if (state?.accountId && accounts.some((a) => a.id === state.accountId)) {
      setActiveAccountId(state.accountId);
      setSelectedContainer(null);
      navigate(location.pathname, { replace: true, state: null });
    }
  }, [location, navigate, accounts]);

  const containers = useStorageContainers(resolvedAccountId);
  const blobs = useStorageBlobs(resolvedAccountId, selectedContainer, currentPrefix, continuationToken);
  const blobProps = useBlobProperties(resolvedAccountId, selectedContainer, selectedBlob);
  const blobContent = useBlobContent(resolvedAccountId, selectedContainer, selectedBlob);
  const sasUrl = useBlobSasUrl(resolvedAccountId, selectedContainer, selectedBlob, 60);
  const blobVersions = useBlobVersions(resolvedAccountId, selectedContainer, selectedBlob);
  const versionComparison = useBlobVersionComparison(resolvedAccountId, selectedContainer, selectedBlob, versionBaseId, versionCompareId, versionCompareRequested);
  const uploadBlob = useUploadBlob(resolvedAccountId, selectedContainer);
  const copyBlob = useCopyBlob(resolvedAccountId);
  const restoreBlobVersion = useRestoreBlobVersion(resolvedAccountId, selectedContainer, selectedBlob);
  const setBlobMetadata = useSetBlobMetadata(resolvedAccountId, selectedContainer, selectedBlob);
  const deletedBlobs = useDeletedBlobs(resolvedAccountId, selectedContainer);
  const uploadDropzone = useDropzone({
    onDrop: (files: File[]) => {
      const file = files[0];
      if (!file) return;
      setUploadFile(file);
      setUploadBlobName(file.name);
      setUploadProgress(0);
    },
    multiple: false,
    disabled: !allowMutations,
  });

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
    setVersionBaseId(null);
    setVersionCompareId(null);
    setVersionCompareRequested(false);
    setVersionRestoreId(null);
  };

  const displayItems = continuationToken === null ? (blobs.data?.items ?? []) : [...allItems, ...(blobs.data?.items ?? [])];

  const filteredItems = blobFilter
    ? displayItems.filter((item) => item.name.toLowerCase().includes(blobFilter.toLowerCase()))
    : displayItems;

  const handleCopyUrl = (blobName: string) => {
    const url = `https://${resolvedAccountId}.blob.core.windows.net/${selectedContainer}/${blobName}`;
    navigator.clipboard.writeText(url);
    setCopiedUrl(true);
    setTimeout(() => setCopiedUrl(false), 2000);
  };

  const handleDownloadBlob = async (blobName: string) => {
    try {
      const params = new URLSearchParams({ blobName });
      const response = await fetch(`/api/storage/${resolvedAccountId}/containers/${selectedContainer}/blobs/content?${params}`);
      if (!response.ok) throw new Error(`API ${response.status}`);
      const data = await response.json();
      if (data.content) {
        const blob = new Blob([data.content], { type: data.contentType || "text/plain" });
        const url = URL.createObjectURL(blob);
        const a = document.createElement("a");
        a.href = url;
        a.download = blobName.split("/").pop() || blobName;
        a.click();
        URL.revokeObjectURL(url);
        notify("success", "Download started", blobName);
      }
    } catch (e) {
      console.error("Download failed:", e);
      notify("error", "Download failed", String(e));
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

  if (!resolvedAccountId) {
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
        <div className="flex items-center justify-between">
          <h1 className="text-2xl font-bold" data-testid="storage-title">Storage</h1>
          <div className="flex gap-1">
            <button
              onClick={() => setStorageViewMode("browser")}
              className={`rounded-md px-3 py-1.5 text-xs ${storageViewMode === "browser" ? "bg-primary text-primary-foreground" : "border hover:bg-accent"}`}
              data-testid="storage-view-browser"
            >
              Browser
            </button>
            <button
              onClick={() => setStorageViewMode("recovery")}
              disabled={!selectedContainer}
              className={`flex items-center gap-1 rounded-md px-3 py-1.5 text-xs disabled:opacity-50 ${storageViewMode === "recovery" ? "bg-primary text-primary-foreground" : "border hover:bg-accent"}`}
              data-testid="storage-view-recovery"
            >
              <RotateCcw className="h-3 w-3" />
              Recovery
            </button>
          </div>
        </div>
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

        {/* Recovery mode */}
        {storageViewMode === "recovery" ? (
          <div className="flex-1 overflow-auto">
            <BlobRecoveryPanel accountId={resolvedAccountId} container={selectedContainer} allowMutations={allowMutations} serverDeletedBlobs={deletedBlobs.data ?? undefined} />
          </div>
        ) : (
        <>
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
                  <span title={allowMutations ? "Upload a new blob" : "Mutations are disabled for this storage account. Enable allowMutations in Settings."}>
                    <button
                      onClick={() => setShowUpload(!showUpload)}
                      disabled={!allowMutations}
                      className={`flex items-center gap-1 rounded border px-2 py-1 text-xs disabled:cursor-not-allowed disabled:opacity-50 ${showUpload ? "bg-primary text-primary-foreground" : "hover:bg-accent"}`}
                      data-testid="storage-upload-toggle"
                    >
                      <Upload className="h-3 w-3" /> Upload
                    </button>
                  </span>
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
                {showUpload && (
                  <div className="border-b p-3" data-testid="storage-upload-panel">
                    <h4 className="mb-2 text-xs font-semibold">Upload Blob</h4>
                    <div className="space-y-2">
                      <div
                        {...uploadDropzone.getRootProps()}
                        className={`cursor-pointer rounded border border-dashed px-3 py-4 text-center text-xs ${
                          uploadDropzone.isDragActive ? "border-primary bg-primary/10" : "hover:bg-accent"
                        }`}
                        data-testid="storage-upload-dropzone"
                      >
                        <input {...uploadDropzone.getInputProps()} data-testid="storage-upload-file" />
                        {uploadFile ? (
                          <span className="font-mono">{uploadFile.name} ({formatBytes(uploadFile.size)})</span>
                        ) : uploadDropzone.isDragActive ? (
                          <span>Drop the file here</span>
                        ) : (
                          <span>Drop a file here or click to browse</span>
                        )}
                      </div>
                      <input
                        type="text"
                        value={uploadBlobName}
                        onChange={(e) => setUploadBlobName(e.target.value)}
                        placeholder="Blob name (e.g. folder/file.json)"
                        className="w-full rounded border bg-card px-2 py-1 text-xs"
                        data-testid="storage-upload-name"
                      />
                      {uploadBlob.isPending && (
                        <div className="space-y-1" data-testid="storage-upload-progress">
                          <div className="h-1.5 overflow-hidden rounded bg-muted">
                            <div className="h-full bg-primary transition-all" style={{ width: `${uploadProgress}%` }} />
                          </div>
                          <div className="text-right text-xs text-muted-foreground">{uploadProgress}%</div>
                        </div>
                      )}
                      <button
                        onClick={() => {
                          if (uploadBlobName.trim() && uploadFile) {
                            uploadBlob.mutate(
                              { blobName: uploadBlobName.trim(), file: uploadFile, onProgress: setUploadProgress },
                              {
                                onSuccess: () => {
                                  notify("success", "Blob uploaded", uploadBlobName.trim());
                                  setUploadBlobName("");
                                  setUploadFile(null);
                                  setUploadProgress(100);
                                  setShowUpload(false);
                                },
                                onError: (e) => notify("error", "Upload failed", String(e)),
                              },
                            );
                          }
                        }}
                        disabled={!uploadBlobName.trim() || !uploadFile || uploadBlob.isPending}
                        className="rounded bg-primary px-3 py-1 text-xs text-primary-foreground disabled:opacity-50"
                        data-testid="storage-upload-confirm"
                      >
                        {uploadBlob.isPending ? "Uploading..." : "Upload"}
                      </button>
                    </div>
                  </div>
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
                  {/* Blob detail tabs */}
                  <div className="flex border-b">
                    {(["properties", "versions", "content"] as const).map((tab) => (
                      <button
                        key={tab}
                        onClick={() => setBlobDetailTab(tab)}
                        className={`px-3 py-1.5 text-xs font-medium capitalize ${blobDetailTab === tab ? "border-b-2 border-primary text-foreground" : "text-muted-foreground"}`}
                        data-testid={`storage-blob-tab-${tab}`}
                      >
                        {tab}
                      </button>
                    ))}
                  </div>

                  {blobDetailTab === "properties" && (
                  <>
                    <div className="flex items-center gap-2">
                      <div className="text-lg font-mono font-semibold break-all" data-testid="storage-blob-name">
                        {blobProps.data.name}
                      </div>
                      <button onClick={() => handleCopyUrl(blobProps.data.name)} className="text-muted-foreground hover:text-foreground" data-testid="storage-copy-url-btn" title="Copy URL">
                        {copiedUrl ? <Check className="h-3.5 w-3.5 text-green-500" /> : <LinkIcon className="h-3.5 w-3.5" />}
                      </button>
                      <button onClick={() => handleDownloadBlob(blobProps.data.name)} className="text-muted-foreground hover:text-foreground" data-testid="storage-download-btn" title="Download blob">
                        <Download className="h-3.5 w-3.5" />
                      </button>
                      <button onClick={() => setShowSasUrl(!showSasUrl)} className="text-muted-foreground hover:text-foreground" data-testid="storage-sas-url-btn" title="Generate SAS URL">
                        <LinkIcon className="h-3.5 w-3.5" />
                      </button>
                      <span title={allowMutations ? "Copy blob" : "Mutations are disabled for this storage account. Enable allowMutations in Settings."}>
                        <button
                          onClick={() => {
                            setCopyDestContainer(selectedContainer ?? "");
                            setCopyDestBlob(blobProps.data.name);
                            setCopyOverwrite(false);
                            setCopyConfirming(false);
                            setCopyStatus(null);
                            setShowCopyDialog(true);
                          }}
                          disabled={!allowMutations}
                          className="text-muted-foreground hover:text-foreground disabled:cursor-not-allowed disabled:opacity-50"
                          data-testid="storage-copy-blob-btn"
                        >
                          <CopyIcon className="h-3.5 w-3.5" />
                        </button>
                      </span>
                    </div>
                    {showSasUrl && (
                      <div className="mt-2 rounded border bg-muted/30 p-2" data-testid="storage-sas-url-display">
                        <div className="flex items-center gap-2">
                          <input
                            type="text"
                            readOnly
                            value={sasUrl.data?.sasUrl ?? "Loading..."}
                            className="flex-1 rounded border bg-card px-2 py-1 text-xs font-mono"
                            data-testid="storage-sas-url-input"
                          />
                          <button
                            onClick={() => {
                              if (sasUrl.data?.sasUrl) {
                                navigator.clipboard.writeText(sasUrl.data.sasUrl);
                                setCopiedUrl(true);
                                setTimeout(() => setCopiedUrl(false), 2000);
                              }
                            }}
                            className="rounded border px-2 py-1 text-xs hover:bg-accent"
                            data-testid="storage-sas-url-copy"
                          >
                            {copiedUrl ? <Check className="h-3 w-3" /> : "Copy"}
                          </button>
                        </div>
                        <p className="mt-1 text-xs text-muted-foreground">Valid for 60 minutes</p>
                      </div>
                    )}
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

                  {/* Metadata */}
                  <div>
                    <div className="mb-2 flex items-center justify-between">
                      <h3 className="text-sm font-semibold">Metadata</h3>
                      {metadataEditing ? (
                        <div className="flex items-center gap-2">
                          <button onClick={() => { setBlobMetadata.mutate(metadataDraft, { onSuccess: () => { notify("success", "Metadata saved"); setMetadataEditing(false); }, onError: (e) => notify("error", "Metadata save failed", String(e)) }); }} disabled={setBlobMetadata.isPending} className="rounded bg-primary px-2 py-1 text-xs text-primary-foreground disabled:opacity-50" data-testid="storage-metadata-save">{setBlobMetadata.isPending ? "Saving..." : "Save"}</button>
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
                        <table className="w-full table-fixed text-sm">
                          <thead className="bg-muted">
                            <tr>
                              <th className="w-1/3 px-3 py-2 text-left font-medium">Key</th>
                              <th className="px-3 py-2 text-left font-medium">Value</th>
                            </tr>
                          </thead>
                          <tbody>
                            {Object.entries(blobProps.data.metadata).map(([k, v]) => (
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

                  {blobDetailTab === "versions" && (
                    <div data-testid="storage-blob-versions">
                      {blobVersions.isLoading && <div className="text-sm text-muted-foreground">Loading versions...</div>}
                      {blobVersions.error && <div className="text-sm text-destructive">Error: {blobVersions.error.message}</div>}
                      {blobVersions.data && (
                        <>
                          <div className="mb-3 rounded-md border bg-muted/20 p-3" data-testid="storage-version-compare-controls">
                            <div className="grid gap-2 md:grid-cols-[1fr_1fr_auto] md:items-end">
                              <label className="text-xs">
                                <span className="mb-1 block text-muted-foreground">Base version</span>
                                <select
                                  value={versionBaseId ?? ""}
                                  onChange={(e) => { setVersionBaseId(e.target.value || null); setVersionCompareRequested(false); }}
                                  className="w-full rounded border bg-card px-2 py-1.5 font-mono text-xs"
                                  data-testid="storage-version-base"
                                >
                                  <option value="">Select a version</option>
                                  {blobVersions.data.map((v) => <option key={v.versionId} value={v.versionId}>{v.versionId}{v.isCurrent ? " (current)" : ""}</option>)}
                                </select>
                              </label>
                              <label className="text-xs">
                                <span className="mb-1 block text-muted-foreground">Compare with</span>
                                <select
                                  value={versionCompareId ?? ""}
                                  onChange={(e) => { setVersionCompareId(e.target.value || null); setVersionCompareRequested(false); }}
                                  className="w-full rounded border bg-card px-2 py-1.5 font-mono text-xs"
                                  data-testid="storage-version-compare"
                                >
                                  <option value="">Current version</option>
                                  {blobVersions.data.map((v) => <option key={v.versionId} value={v.versionId}>{v.versionId}{v.isCurrent ? " (current)" : ""}</option>)}
                                </select>
                              </label>
                              <button
                                onClick={() => setVersionCompareRequested(true)}
                                disabled={!versionBaseId || versionComparison.isFetching}
                                className="rounded bg-primary px-3 py-1.5 text-xs text-primary-foreground disabled:opacity-50"
                                data-testid="storage-version-compare-btn"
                              >
                                {versionComparison.isFetching ? "Comparing..." : "Compare"}
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
                                {blobVersions.data.map((v) => (
                                  <tr key={v.versionId} className="border-t">
                                    <td className="px-3 py-2 font-mono text-xs break-all">{v.versionId}</td>
                                    <td className="px-3 py-2 text-xs">{formatDate(v.lastModified)}</td>
                                    <td className="px-3 py-2 text-right text-xs">{formatBytes(v.sizeBytes)}</td>
                                    <td className="px-3 py-2 text-center">{v.isCurrent && <Check className="inline h-3 w-3 text-green-500" />}</td>
                                    <td className="px-3 py-2 text-right">
                                      {!v.isCurrent && allowMutations && (
                                        <button
                                          onClick={() => setVersionRestoreId(v.versionId)}
                                          className="rounded border px-2 py-1 text-xs hover:bg-accent"
                                          data-testid={`storage-version-restore-${v.versionId}`}
                                        >
                                          Restore
                                        </button>
                                      )}
                                    </td>
                                  </tr>
                                ))}
                                {blobVersions.data.length === 0 && (
                                  <tr><td colSpan={5} className="px-3 py-4 text-center text-muted-foreground">No versions available</td></tr>
                                )}
                              </tbody>
                            </table>
                          </div>
                          {versionComparison.error && <div className="mt-3 text-sm text-destructive">Error: {versionComparison.error.message}</div>}
                          {versionComparison.data && (
                            <div className="mt-3 space-y-3 rounded-md border p-3" data-testid="storage-version-diff-pane">
                              <div className="flex flex-wrap gap-3 text-xs text-muted-foreground">
                                <span className="break-all">Base: <strong className="font-mono text-foreground">{versionComparison.data.baseVersionId}</strong></span>
                                <span className="break-all">Compare: <strong className="font-mono text-foreground">{versionComparison.data.compareVersionId ?? "current"}</strong></span>
                                <span>Size: {formatBytes(versionComparison.data.baseSizeBytes)} → {formatBytes(versionComparison.data.compareSizeBytes)}</span>
                              </div>
                              <div>
                                <h4 className="mb-1 text-xs font-semibold">Metadata changes</h4>
                                <div className="text-xs text-muted-foreground">
                                  Added: {versionComparison.data.metadataDiff.addedKeys.join(", ") || "none"} · Removed: {versionComparison.data.metadataDiff.removedKeys.join(", ") || "none"} · Changed: {versionComparison.data.metadataDiff.changedKeys.join(", ") || "none"}
                                </div>
                              </div>
                              {versionComparison.data.contentComparePossible && versionComparison.data.textDiff && (
                                <pre className="max-h-60 overflow-y-auto whitespace-pre-wrap break-words rounded bg-black p-3 text-xs text-green-400" data-testid="storage-version-text-diff">
                                  {versionComparison.data.textDiff}
                                </pre>
                              )}
                            </div>
                          )}
                          {versionRestoreId && (
                            <ConfirmBar
                              message={`Restore version ${versionRestoreId} to the current blob?`}
                              confirmLabel="Restore"
                              onConfirm={() => {
                                restoreBlobVersion.mutate(versionRestoreId, {
                                  onSuccess: () => {
                                    notify("success", "Version restored", versionRestoreId);
                                    setVersionRestoreId(null);
                                  },
                                  onError: (e) => notify("error", "Restore failed", String(e)),
                                });
                              }}
                              onCancel={() => setVersionRestoreId(null)}
                              testId="storage-version-restore-confirm"
                              confirmTestId="storage-version-restore-confirm-yes"
                              cancelTestId="storage-version-restore-confirm-cancel"
                            />
                          )}
                        </>
                      )}
                    </div>
                  )}

                  {blobDetailTab === "content" && (
                    <div data-testid="storage-blob-content-tab">
                      {blobContent.isLoading && <div className="text-sm text-muted-foreground">Loading content...</div>}
                      {blobContent.error && <div className="text-sm text-destructive">Error: {blobContent.error.message}</div>}
                      {blobContent.data && (
                        <div>
                          {blobContent.data.isBinary ? (
                            <div className="rounded-md border bg-muted p-4 text-sm text-muted-foreground" data-testid="storage-blob-binary">
                              Binary content ({formatBytes(blobContent.data.totalSizeBytes)})
                            </div>
                          ) : (
                            <>
                              <pre
                                className="rounded-md border bg-black p-4 text-sm font-mono overflow-y-auto max-h-96 whitespace-pre-wrap break-words text-green-400"
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
                </>
              )}
            </div>
          )}
        </div>
        </>
        )}

        {/* Copy blob dialog */}
        {showCopyDialog && selectedBlob && (
          <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/50" data-testid="storage-copy-dialog">
            <div className="rounded-lg border bg-card p-6 shadow-lg w-96">
              <h3 className="mb-4 text-lg font-semibold">Copy Blob</h3>
              <div className="space-y-3">
                <div>
                  <label className="mb-1 block text-xs text-muted-foreground">Source</label>
                  <div className="rounded border bg-muted px-3 py-2 text-xs font-mono">
                    {selectedContainer}/{selectedBlob}
                  </div>
                </div>
                <div>
                  <label className="mb-1 block text-xs text-muted-foreground">Destination Container</label>
                  <select
                    value={copyDestContainer}
                    onChange={(e) => setCopyDestContainer(e.target.value)}
                    className="w-full rounded border bg-card px-3 py-2 text-sm"
                    data-testid="storage-copy-dest-container"
                  >
                    <option value="">Select a container</option>
                    {(containers.data ?? []).map((container) => (
                      <option key={container.name} value={container.name}>{container.name}</option>
                    ))}
                  </select>
                </div>
                <div>
                  <label className="mb-1 block text-xs text-muted-foreground">Destination Blob Name</label>
                  <input
                    type="text"
                    value={copyDestBlob}
                    onChange={(e) => setCopyDestBlob(e.target.value)}
                    placeholder="blob-name"
                    className="w-full rounded border bg-card px-3 py-2 text-sm"
                    data-testid="storage-copy-dest-blob"
                  />
                </div>
                <label className="flex items-start gap-2 text-xs">
                  <input
                    type="checkbox"
                    checked={copyOverwrite}
                    onChange={(e) => { setCopyOverwrite(e.target.checked); setCopyConfirming(false); }}
                    data-testid="storage-copy-overwrite"
                  />
                  <span>
                    <span className="block font-medium">Allow overwrite</span>
                    <span className="text-muted-foreground">Only enable this when replacing an existing destination is intentional.</span>
                  </span>
                </label>
                {copyStatus && (
                  <p className="text-xs text-muted-foreground" data-testid="storage-copy-status">{copyStatus}</p>
                )}
                <div className="flex justify-end gap-2">
                  <button
                    onClick={() => { setShowCopyDialog(false); setCopyStatus(null); }}
                    className="rounded border px-3 py-1.5 text-sm hover:bg-accent"
                  >
                    Cancel
                  </button>
                  <button
                    onClick={() => {
                      if (copyOverwrite) {
                        setCopyConfirming(true);
                        return;
                      }
                      copyBlob.mutate(
                        { sourceContainer: selectedContainer!, sourceBlob: selectedBlob, destContainer: copyDestContainer, destBlob: copyDestBlob, overwrite: false },
                        {
                          onSuccess: () => {
                            notify("success", "Blob copied", `${copyDestContainer}/${copyDestBlob}`);
                            setCopyStatus("Copied successfully");
                            setTimeout(() => { setShowCopyDialog(false); setCopyStatus(null); }, 2000);
                          },
                          onError: (e) => { setCopyStatus(`Error: ${e}`); notify("error", "Copy failed", String(e)); }
                        },
                      );
                    }}
                    disabled={!copyDestContainer.trim() || !copyDestBlob.trim() || copyBlob.isPending}
                    className="rounded bg-primary px-3 py-1.5 text-sm text-primary-foreground disabled:opacity-50"
                    data-testid="storage-copy-confirm"
                  >
                    {copyBlob.isPending ? "Copying..." : "Copy"}
                  </button>
                </div>
                {copyConfirming && (
                  <ConfirmBar
                    message={`Overwrite ${copyDestContainer}/${copyDestBlob} if it already exists?`}
                    confirmLabel="Overwrite"
                    requireTypedName={`${copyDestContainer}/${copyDestBlob}`}
                    onConfirm={() => {
                      copyBlob.mutate(
                        { sourceContainer: selectedContainer!, sourceBlob: selectedBlob, destContainer: copyDestContainer, destBlob: copyDestBlob, overwrite: true },
                        {
                          onSuccess: () => {
                            notify("success", "Blob copied", `${copyDestContainer}/${copyDestBlob}`);
                            setCopyStatus("Copied successfully");
                            setCopyConfirming(false);
                            setTimeout(() => { setShowCopyDialog(false); setCopyStatus(null); }, 2000);
                          },
                          onError: (e) => { setCopyStatus(`Error: ${e}`); notify("error", "Copy failed", String(e)); }
                        },
                      );
                    }}
                    onCancel={() => setCopyConfirming(false)}
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
    </div>
  );
}
