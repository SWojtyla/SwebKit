import {
  createContext,
  useCallback,
  useContext,
  useEffect,
  useMemo,
  useRef,
  useState,
  type ReactNode,
  type JSX,
} from "react";
import { useLocation, useNavigate } from "react-router";
import { useDropzone } from "react-dropzone";
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
import type { BlobProperties, StorageBlobContent, StorageBlobItem, StorageConfig } from "@/lib/types";
import { apiFetch } from "@/lib/api";
import { buildZip } from "@/lib/zip";
import { downloadBlob, downloadText } from "@/lib/download";
import { planBlobDownload } from "@/lib/storage-blob-download";
import { sortStorageBlobItems, type StorageBlobSortDir, type StorageBlobSortKey } from "@/lib/storage-blob-sort";
import { useNotification } from "@/components/layout/NotificationSystem";

export interface StoragePageContextValue {
  accounts: StorageConfig[];
  activeAccountId: string | null;
  resolvedAccountId: string | null;
  activeAccount: StorageConfig | undefined;
  allowMutations: boolean;
  handleSelectAccount: (id: string) => void;

  blobListRef: React.MutableRefObject<HTMLDivElement | null>;
  selectedContainer: string | null;
  handleSelectContainer: (name: string) => void;

  currentPrefix: string;
  prefixHistory: string[];
  handleNavigatePrefix: (prefix: string) => void;
  handleBreadcrumb: (index: number) => void;

  selectedBlob: string | null;
  handleSelectBlob: (name: string) => void;

  continuationToken: string | null;
  handleLoadMore: () => void;

  blobFilter: string;
  setBlobFilter: (v: string) => void;
  blobSortKey: StorageBlobSortKey;
  setBlobSortKey: (v: StorageBlobSortKey) => void;
  blobSortDir: StorageBlobSortDir;
  setBlobSortDir: (v: StorageBlobSortDir) => void;
  displayItems: StorageBlobItem[];
  filteredItems: StorageBlobItem[];

  multiSelectMode: boolean;
  setMultiSelectMode: (v: boolean) => void;
  selectedBlobs: Set<string>;
  setSelectedBlobs: (v: Set<string>) => void;
  toggleBlobSelection: (name: string) => void;

  copiedUrl: boolean;
  handleCopyUrl: (blobName: string) => void;
  handleCopySasUrl: () => void;
  handleDownloadBlob: (blobName: string) => Promise<void>;
  handleBatchDownloadBlobs: (blobNames: string[]) => Promise<void>;

  metadataEditing: boolean;
  setMetadataEditing: (v: boolean) => void;
  metadataDraft: Record<string, string>;
  setMetadataDraft: React.Dispatch<React.SetStateAction<Record<string, string>>>;

  storageViewMode: "browser" | "recovery";
  setStorageViewMode: (v: "browser" | "recovery") => void;
  blobDetailTab: "properties" | "versions" | "content";
  setBlobDetailTab: (v: "properties" | "versions" | "content") => void;

  showSasUrl: boolean;
  setShowSasUrl: (v: boolean) => void;

  showUpload: boolean;
  setShowUpload: (v: boolean) => void;
  uploadBlobName: string;
  setUploadBlobName: (v: string) => void;
  uploadFile: File | null;
  setUploadFile: (v: File | null) => void;
  uploadProgress: number;
  setUploadProgress: (v: number) => void;
  uploadDropzone: ReturnType<typeof useDropzone>;
  handleUploadConfirm: () => void;
  uploadCheckingOverwrite: boolean;
  uploadOverwriteConfirm: { blobName: string; file: File } | null;
  setUploadOverwriteConfirm: (v: { blobName: string; file: File } | null) => void;
  handleUploadOverwriteConfirm: () => void;
  checkBlobExists: (blobName: string) => Promise<boolean>;

  showCopyDialog: boolean;
  setShowCopyDialog: (v: boolean) => void;
  copyDestContainer: string;
  setCopyDestContainer: (v: string) => void;
  copyDestBlob: string;
  setCopyDestBlob: (v: string) => void;
  copyOverwrite: boolean;
  setCopyOverwrite: (v: boolean) => void;
  copyConfirming: boolean;
  setCopyConfirming: (v: boolean) => void;
  copyStatus: string | null;
  setCopyStatus: (v: string | null) => void;
  handleCopyConfirm: () => void;
  handleCopyOverwriteConfirm: () => void;

  versionBaseId: string | null;
  setVersionBaseId: (v: string | null) => void;
  versionCompareId: string | null;
  setVersionCompareId: (v: string | null) => void;
  versionCompareRequested: boolean;
  setVersionCompareRequested: (v: boolean) => void;
  versionRestoreId: string | null;
  setVersionRestoreId: (v: string | null) => void;
  handleVersionRestoreConfirm: () => void;
  handleMetadataSave: () => void;

  containers: ReturnType<typeof useStorageContainers>;
  blobs: ReturnType<typeof useStorageBlobs>;
  blobProps: ReturnType<typeof useBlobProperties>;
  blobContent: ReturnType<typeof useBlobContent>;
  sasUrl: ReturnType<typeof useBlobSasUrl>;
  blobVersions: ReturnType<typeof useBlobVersions>;
  versionComparison: ReturnType<typeof useBlobVersionComparison>;
  deletedBlobs: ReturnType<typeof useDeletedBlobs>;

  uploadBlob: ReturnType<typeof useUploadBlob>;
  copyBlob: ReturnType<typeof useCopyBlob>;
  restoreBlobVersion: ReturnType<typeof useRestoreBlobVersion>;
  setBlobMetadata: ReturnType<typeof useSetBlobMetadata>;
}

const StoragePageContext = createContext<StoragePageContextValue | null>(null);

export function useStoragePageContext(): StoragePageContextValue {
  const ctx = useContext(StoragePageContext);
  if (!ctx) throw new Error("useStoragePageContext must be used within StoragePageProvider");
  return ctx;
}

export function StoragePageProvider({ children }: { children: ReactNode }): JSX.Element {
  const { data: profile } = useProfile();
  const location = useLocation();
  const navigate = useNavigate();
  const accounts = useMemo(() => profile?.config?.storageAccounts ?? [], [profile]);
  const [activeAccountId, setActiveAccountId] = useState<string | null>(accounts[0]?.id ?? null);
  const resolvedAccountId = activeAccountId ?? accounts[0]?.id ?? null;
  const activeAccount = accounts.find((a) => a.id === resolvedAccountId);
  const allowMutations = activeAccount?.allowMutations ?? false;
  const { notify } = useNotification();

  const blobListRef = useRef<HTMLDivElement | null>(null);
  const [selectedContainer, setSelectedContainer] = useState<string | null>(null);
  const [currentPrefix, setCurrentPrefix] = useState("");
  const [prefixHistory, setPrefixHistory] = useState<string[]>([]);
  const [selectedBlob, setSelectedBlob] = useState<string | null>(null);
  const [continuationToken, setContinuationToken] = useState<string | null>(null);
  const [allItems, setAllItems] = useState<StorageBlobItem[]>([]);
  const [blobFilter, setBlobFilter] = useState("");
  const [blobSortKey, setBlobSortKey] = useState<StorageBlobSortKey>("name");
  const [blobSortDir, setBlobSortDir] = useState<StorageBlobSortDir>("asc");
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
  const [uploadCheckingOverwrite, setUploadCheckingOverwrite] = useState(false);
  const [uploadOverwriteConfirm, setUploadOverwriteConfirm] = useState<{ blobName: string; file: File } | null>(null);
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

  const handleSelectAccount = useCallback((id: string) => {
    setActiveAccountId(id);
    setSelectedContainer(null);
    setCurrentPrefix("");
    setPrefixHistory([]);
    setSelectedBlob(null);
    setContinuationToken(null);
    setAllItems([]);
  }, []);

  const handleSelectContainer = useCallback((name: string) => {
    setSelectedContainer(name);
    setCurrentPrefix("");
    setPrefixHistory([]);
    setSelectedBlob(null);
    setContinuationToken(null);
    setAllItems([]);
  }, []);

  const handleNavigatePrefix = useCallback((prefix: string) => {
    setPrefixHistory((prev) => [...prev, currentPrefix]);
    setCurrentPrefix(prefix);
    setSelectedBlob(null);
    setContinuationToken(null);
    setAllItems([]);
  }, [currentPrefix]);

  const handleBreadcrumb = useCallback((index: number) => {
    const newPrefix = index === 0 ? "" : prefixHistory[index - 1] ?? "";
    setPrefixHistory((prev) => prev.slice(0, index));
    setCurrentPrefix(newPrefix);
    setSelectedBlob(null);
    setContinuationToken(null);
    setAllItems([]);
  }, [prefixHistory]);

  const handleLoadMore = useCallback(() => {
    if (blobs.data?.continuationToken) {
      setAllItems((prev) => [...prev, ...(blobs.data?.items ?? [])]);
      setContinuationToken(blobs.data.continuationToken);
    }
  }, [blobs.data]);

  const handleSelectBlob = useCallback((name: string) => {
    setSelectedBlob(name);
    setVersionBaseId(null);
    setVersionCompareId(null);
    setVersionCompareRequested(false);
    setVersionRestoreId(null);
  }, []);

  // Dedupes by name against `allItems`: with `useStorageBlobs`'s `placeholderData:
  // keepPreviousData` (6.5), while the next page is loading `blobs.data` still holds the
  // *previous* page's items (the ones `handleLoadMore` just copied into `allItems`) rather
  // than briefly going empty — without this filter those would render twice (and collide
  // as virtualizer keys) for the duration of that fetch.
  const displayItems = useMemo(() => {
    if (continuationToken === null) return blobs.data?.items ?? [];
    const seen = new Set(allItems.map((item) => item.name));
    const incoming = (blobs.data?.items ?? []).filter((item) => !seen.has(item.name));
    return [...allItems, ...incoming];
  }, [continuationToken, blobs.data?.items, allItems]);

  const filteredItems = useMemo(() => {
    const filtered = blobFilter
      ? displayItems.filter((item) => item.name.toLowerCase().includes(blobFilter.toLowerCase()))
      : displayItems;
    return sortStorageBlobItems(filtered, blobSortKey, blobSortDir);
  }, [blobFilter, displayItems, blobSortKey, blobSortDir]);

  // 6.5: there's no server-side search, so the filter above only ever searches whatever
  // pages `handleLoadMore` has already pulled in — stopping at zero local matches looks
  // identical to "no such blob exists." While a filter is active, has no local matches yet,
  // and more pages remain, keep paging automatically instead of leaving the user to notice
  // and click "Load more" themselves. Stops as soon as a match appears or the last page (no
  // continuation token) is reached, so it can't loop forever on a genuinely absent name.
  useEffect(() => {
    if (!blobFilter.trim()) return;
    if (filteredItems.length > 0) return;
    if (blobs.isFetching) return;
    if (!blobs.data?.continuationToken) return;
    handleLoadMore();
  }, [blobFilter, filteredItems.length, blobs.isFetching, blobs.data?.continuationToken, handleLoadMore]);

  const handleCopyUrl = useCallback((blobName: string) => {
    // The Azure host uses the storage account name; `resolvedAccountId` is SwebKit's own
    // config id (a random 8-char slug), which produced a URL pointing at nothing.
    const url = `https://${activeAccount?.accountName}.blob.core.windows.net/${selectedContainer}/${blobName}`;
    navigator.clipboard.writeText(url);
    setCopiedUrl(true);
    setTimeout(() => setCopiedUrl(false), 2000);
  }, [activeAccount?.accountName, selectedContainer]);

  const handleCopySasUrl = useCallback(() => {
    if (sasUrl.data?.sasUrl) {
      navigator.clipboard.writeText(sasUrl.data.sasUrl);
      setCopiedUrl(true);
      setTimeout(() => setCopiedUrl(false), 2000);
    }
  }, [sasUrl.data?.sasUrl]);

  // Goes through `apiFetch` rather than a bare relative `fetch`: the sidecar listens on
  // its own OS-assigned port, so "/api/..." resolved against the Tauri asset server and
  // came back as index.html, surfacing as "Unexpected token '<'" instead of a download.
  const fetchBlobContent = useCallback(
    (blobName: string) =>
      apiFetch<StorageBlobContent>(
        `/api/storage/${resolvedAccountId}/containers/${encodeURIComponent(selectedContainer!)}/blobs/content?${new URLSearchParams({ blobName })}`,
      ),
    [resolvedAccountId, selectedContainer],
  );

  // P0 fix: this used to hand `data.content` straight to a text-file writer even when
  // `data.isBinary` was true — for a binary blob the sidecar returns an *empty* `content`
  // (see AzureStorageClient.GetBlobContentAsync), so this silently wrote a 0-byte file with
  // no warning. The Content tab already trusts `isBinary` to decide whether `content` is
  // safe to render (BlobDetailPanel.tsx); Download must trust the same flag. `planBlobDownload`
  // (lib/storage-blob-download.ts, unit-tested) is the single decision point both this and the
  // batch version below go through. There's no binary-safe download endpoint, so binary blobs
  // are redirected to the existing signed "Generate SAS URL" flow instead of ever writing a
  // corrupted/empty file.
  const handleDownloadBlob = useCallback(async (blobName: string) => {
    try {
      const data = await fetchBlobContent(blobName);
      const plan = planBlobDownload(blobName, data);
      if (plan.kind === "blocked-binary") {
        notify(
          "error",
          "Can't download as text",
          `"${blobName}" isn't a text blob, so Download would write an empty/corrupted file. Use "Generate SAS URL" instead to get a direct link to the real bytes.`,
        );
        if (blobName === selectedBlob) setShowSasUrl(true);
        return;
      }
      downloadText(plan.filename, plan.content, plan.mimeType);
      notify("success", "Download started", blobName);
    } catch (e) {
      console.error("Download failed:", e);
      notify("error", "Download failed", String(e));
    }
  }, [fetchBlobContent, notify, selectedBlob]);

  // Bundles the selected blobs into a single ZIP, matching the pattern Service Bus's message
  // list already uses (lib/zip.ts) — previously this looped handleDownloadBlob per file, firing
  // N separate browser downloads instead of one archive. Binary blobs are skipped (their
  // content would be empty/corrupted, same reasoning as handleDownloadBlob above) rather than
  // silently zipped as empty files — the user is told which ones were skipped and why.
  const handleBatchDownloadBlobs = useCallback(async (blobNames: string[]) => {
    if (blobNames.length === 0) return;
    try {
      const files: Record<string, string> = {};
      const skippedBinary: string[] = [];
      for (const blobName of blobNames) {
        const data = await fetchBlobContent(blobName);
        const plan = planBlobDownload(blobName, data);
        if (plan.kind === "blocked-binary") {
          skippedBinary.push(blobName);
          continue;
        }
        if (plan.content) {
          files[plan.filename] = plan.content;
        }
      }
      const downloadedCount = Object.keys(files).length;
      if (downloadedCount > 0) {
        const zipped = await buildZip(files);
        const timestamp = new Date().toISOString().slice(0, 19).replace(/[T:]/g, "-");
        downloadBlob(`${selectedContainer}-blobs-${timestamp}.zip`, zipped);
      }
      if (skippedBinary.length > 0) {
        notify(
          "error",
          downloadedCount > 0
            ? `Downloaded ${downloadedCount} blob(s), skipped ${skippedBinary.length} binary`
            : "No blobs downloaded — all binary",
          `Binary blobs can't be zipped as text: ${skippedBinary.join(", ")}. Use "Generate SAS URL" on each to download them directly.`,
        );
      } else {
        notify("success", `Downloaded ${downloadedCount} blob(s) as ZIP`);
      }
    } catch (e) {
      console.error("Batch download failed:", e);
      notify("error", "Batch download failed", String(e));
    }
  }, [fetchBlobContent, selectedContainer, notify]);

  // Used by both Upload (6.3) and Recovery (6.3) to warn before silently overwriting/
  // colliding with an existing blob, instead of relying on whatever page of the (paginated,
  // possibly filtered) list happens to be loaded client-side.
  const checkBlobExists = useCallback(async (blobName: string): Promise<boolean> => {
    if (!resolvedAccountId || !selectedContainer) return false;
    try {
      await apiFetch<BlobProperties>(
        `/api/storage/${resolvedAccountId}/containers/${encodeURIComponent(selectedContainer)}/blobs/properties?${new URLSearchParams({ blobName })}`,
      );
      return true;
    } catch {
      return false;
    }
  }, [resolvedAccountId, selectedContainer]);

  const toggleBlobSelection = useCallback((name: string) => {
    setSelectedBlobs((prev) => {
      const next = new Set(prev);
      if (next.has(name)) next.delete(name);
      else next.add(name);
      return next;
    });
  }, []);

  const performUpload = useCallback((blobName: string, file: File) => {
    uploadBlob.mutate(
      { blobName, file, onProgress: setUploadProgress },
      {
        onSuccess: () => {
          notify("success", "Blob uploaded", blobName);
          setUploadBlobName("");
          setUploadFile(null);
          setUploadProgress(100);
          setShowUpload(false);
          setUploadOverwriteConfirm(null);
        },
        onError: (e) => notify("error", "Upload failed", String(e)),
      },
    );
  }, [uploadBlob, notify]);

  // Upload previously overwrote an existing blob of the same name with no warning at all,
  // unlike the Copy flow's explicit "Allow overwrite" + confirm guard. Since there's no
  // manual "allow overwrite" toggle for Upload (the user just types a name), an existence
  // check against the target name stands in for it.
  const handleUploadConfirm = useCallback(async () => {
    const blobName = uploadBlobName.trim();
    if (!blobName || !uploadFile) return;
    const file = uploadFile;
    setUploadCheckingOverwrite(true);
    try {
      const exists = await checkBlobExists(blobName);
      if (exists) {
        setUploadOverwriteConfirm({ blobName, file });
        return;
      }
      performUpload(blobName, file);
    } finally {
      setUploadCheckingOverwrite(false);
    }
  }, [uploadBlobName, uploadFile, checkBlobExists, performUpload]);

  const handleUploadOverwriteConfirm = useCallback(() => {
    if (!uploadOverwriteConfirm) return;
    performUpload(uploadOverwriteConfirm.blobName, uploadOverwriteConfirm.file);
  }, [uploadOverwriteConfirm, performUpload]);

  const handleMetadataSave = useCallback(() => {
    setBlobMetadata.mutate(metadataDraft, {
      onSuccess: () => {
        notify("success", "Metadata saved");
        setMetadataEditing(false);
      },
      onError: (e) => notify("error", "Metadata save failed", String(e)),
    });
  }, [setBlobMetadata, metadataDraft, notify]);

  const handleCopyConfirm = useCallback(() => {
    if (copyOverwrite) {
      setCopyConfirming(true);
      return;
    }
    copyBlob.mutate(
      { sourceContainer: selectedContainer!, sourceBlob: selectedBlob!, destContainer: copyDestContainer, destBlob: copyDestBlob, overwrite: false },
      {
        onSuccess: () => {
          notify("success", "Blob copied", `${copyDestContainer}/${copyDestBlob}`);
          setCopyStatus("Copied successfully");
          setTimeout(() => { setShowCopyDialog(false); setCopyStatus(null); }, 2000);
        },
        onError: (e) => { setCopyStatus(`Error: ${e}`); notify("error", "Copy failed", String(e)); },
      },
    );
  }, [copyOverwrite, copyBlob, selectedContainer, selectedBlob, copyDestContainer, copyDestBlob, notify]);

  const handleCopyOverwriteConfirm = useCallback(() => {
    copyBlob.mutate(
      { sourceContainer: selectedContainer!, sourceBlob: selectedBlob!, destContainer: copyDestContainer, destBlob: copyDestBlob, overwrite: true },
      {
        onSuccess: () => {
          notify("success", "Blob copied", `${copyDestContainer}/${copyDestBlob}`);
          setCopyStatus("Copied successfully");
          setCopyConfirming(false);
          setTimeout(() => { setShowCopyDialog(false); setCopyStatus(null); }, 2000);
        },
        onError: (e) => { setCopyStatus(`Error: ${e}`); notify("error", "Copy failed", String(e)); },
      },
    );
  }, [copyBlob, selectedContainer, selectedBlob, copyDestContainer, copyDestBlob, notify]);

  const handleVersionRestoreConfirm = useCallback(() => {
    if (!versionRestoreId) return;
    restoreBlobVersion.mutate(versionRestoreId, {
      onSuccess: () => {
        notify("success", "Version restored", versionRestoreId);
        setVersionRestoreId(null);
      },
      onError: (e) => notify("error", "Restore failed", String(e)),
    });
  }, [versionRestoreId, restoreBlobVersion, notify]);

  const value: StoragePageContextValue = useMemo(
    () => ({
    accounts,
    activeAccountId,
    resolvedAccountId,
    activeAccount,
    allowMutations,
    handleSelectAccount,

    blobListRef,
    selectedContainer,
    handleSelectContainer,

    currentPrefix,
    prefixHistory,
    handleNavigatePrefix,
    handleBreadcrumb,

    selectedBlob,
    handleSelectBlob,

    continuationToken,
    handleLoadMore,

    blobFilter,
    setBlobFilter,
    blobSortKey,
    setBlobSortKey,
    blobSortDir,
    setBlobSortDir,
    displayItems,
    filteredItems,

    multiSelectMode,
    setMultiSelectMode,
    selectedBlobs,
    setSelectedBlobs,
    toggleBlobSelection,

    copiedUrl,
    handleCopyUrl,
    handleCopySasUrl,
    handleDownloadBlob,
    handleBatchDownloadBlobs,

    metadataEditing,
    setMetadataEditing,
    metadataDraft,
    setMetadataDraft,
    handleMetadataSave,

    storageViewMode,
    setStorageViewMode,
    blobDetailTab,
    setBlobDetailTab,

    showSasUrl,
    setShowSasUrl,

    showUpload,
    setShowUpload,
    uploadBlobName,
    setUploadBlobName,
    uploadFile,
    setUploadFile,
    uploadProgress,
    setUploadProgress,
    uploadDropzone,
    handleUploadConfirm,
    uploadCheckingOverwrite,
    uploadOverwriteConfirm,
    setUploadOverwriteConfirm,
    handleUploadOverwriteConfirm,
    checkBlobExists,

    showCopyDialog,
    setShowCopyDialog,
    copyDestContainer,
    setCopyDestContainer,
    copyDestBlob,
    setCopyDestBlob,
    copyOverwrite,
    setCopyOverwrite,
    copyConfirming,
    setCopyConfirming,
    copyStatus,
    setCopyStatus,
    handleCopyConfirm,
    handleCopyOverwriteConfirm,

    versionBaseId,
    setVersionBaseId,
    versionCompareId,
    setVersionCompareId,
    versionCompareRequested,
    setVersionCompareRequested,
    versionRestoreId,
    setVersionRestoreId,
    handleVersionRestoreConfirm,

    containers,
    blobs,
    blobProps,
    blobContent,
    sasUrl,
    blobVersions,
    versionComparison,
    deletedBlobs,

    uploadBlob,
    copyBlob,
    restoreBlobVersion,
    setBlobMetadata,
  }),
    [
      accounts,
      activeAccountId,
      resolvedAccountId,
      activeAccount,
      allowMutations,
      handleSelectAccount,
      blobListRef,
      selectedContainer,
      handleSelectContainer,
      currentPrefix,
      prefixHistory,
      handleNavigatePrefix,
      handleBreadcrumb,
      selectedBlob,
      handleSelectBlob,
      continuationToken,
      handleLoadMore,
      blobFilter,
      setBlobFilter,
      blobSortKey,
      setBlobSortKey,
      blobSortDir,
      setBlobSortDir,
      displayItems,
      filteredItems,
      multiSelectMode,
      setMultiSelectMode,
      selectedBlobs,
      setSelectedBlobs,
      toggleBlobSelection,
      copiedUrl,
      handleCopyUrl,
      handleCopySasUrl,
      handleDownloadBlob,
      handleBatchDownloadBlobs,
      metadataEditing,
      setMetadataEditing,
      metadataDraft,
      setMetadataDraft,
      handleMetadataSave,
      storageViewMode,
      setStorageViewMode,
      blobDetailTab,
      setBlobDetailTab,
      showSasUrl,
      setShowSasUrl,
      showUpload,
      setShowUpload,
      uploadBlobName,
      setUploadBlobName,
      uploadFile,
      setUploadFile,
      uploadProgress,
      setUploadProgress,
      uploadDropzone,
      handleUploadConfirm,
      uploadCheckingOverwrite,
      uploadOverwriteConfirm,
      setUploadOverwriteConfirm,
      handleUploadOverwriteConfirm,
      checkBlobExists,
      showCopyDialog,
      setShowCopyDialog,
      copyDestContainer,
      setCopyDestContainer,
      copyDestBlob,
      setCopyDestBlob,
      copyOverwrite,
      setCopyOverwrite,
      copyConfirming,
      setCopyConfirming,
      copyStatus,
      setCopyStatus,
      handleCopyConfirm,
      handleCopyOverwriteConfirm,
      versionBaseId,
      setVersionBaseId,
      versionCompareId,
      setVersionCompareId,
      versionCompareRequested,
      setVersionCompareRequested,
      versionRestoreId,
      setVersionRestoreId,
      handleVersionRestoreConfirm,
      containers,
      blobs,
      blobProps,
      blobContent,
      sasUrl,
      blobVersions,
      versionComparison,
      deletedBlobs,
      uploadBlob,
      copyBlob,
      restoreBlobVersion,
      setBlobMetadata,
    ],
  );

  return <StoragePageContext.Provider value={value}>{children}</StoragePageContext.Provider>;
}
