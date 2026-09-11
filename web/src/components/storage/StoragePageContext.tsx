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
import type { StorageBlobContent, StorageBlobItem, StorageConfig } from "@/lib/types";
import { apiFetch } from "@/lib/api";
import { buildZip } from "@/lib/zip";
import { downloadBlob, downloadText } from "@/lib/download";
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

  const displayItems = useMemo(
    () => (continuationToken === null ? (blobs.data?.items ?? []) : [...allItems, ...(blobs.data?.items ?? [])]),
    [continuationToken, blobs.data?.items, allItems],
  );

  const filteredItems = useMemo(
    () =>
      blobFilter
        ? displayItems.filter((item) => item.name.toLowerCase().includes(blobFilter.toLowerCase()))
        : displayItems,
    [blobFilter, displayItems],
  );

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

  const handleDownloadBlob = useCallback(async (blobName: string) => {
    try {
      const data = await fetchBlobContent(blobName);
      downloadText(
        blobName.split("/").pop() || blobName,
        data.content,
        data.contentType || "text/plain",
      );
      notify("success", "Download started", blobName);
    } catch (e) {
      console.error("Download failed:", e);
      notify("error", "Download failed", String(e));
    }
  }, [fetchBlobContent, notify]);

  // Bundles the selected blobs into a single ZIP, matching the pattern Service Bus's message
  // list already uses (lib/zip.ts) — previously this looped handleDownloadBlob per file, firing
  // N separate browser downloads instead of one archive.
  const handleBatchDownloadBlobs = useCallback(async (blobNames: string[]) => {
    if (blobNames.length === 0) return;
    try {
      const files: Record<string, string> = {};
      for (const blobName of blobNames) {
        const data = await fetchBlobContent(blobName);
        if (data.content) {
          files[blobName.split("/").pop() || blobName] = data.content;
        }
      }
      const zipped = await buildZip(files);
      const timestamp = new Date().toISOString().slice(0, 19).replace(/[T:]/g, "-");
      downloadBlob(`${selectedContainer}-blobs-${timestamp}.zip`, zipped);
      notify("success", `Downloaded ${Object.keys(files).length} blob(s) as ZIP`);
    } catch (e) {
      console.error("Batch download failed:", e);
      notify("error", "Batch download failed", String(e));
    }
  }, [fetchBlobContent, selectedContainer, notify]);

  const toggleBlobSelection = useCallback((name: string) => {
    setSelectedBlobs((prev) => {
      const next = new Set(prev);
      if (next.has(name)) next.delete(name);
      else next.add(name);
      return next;
    });
  }, []);

  const handleUploadConfirm = useCallback(() => {
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
  }, [uploadBlobName, uploadFile, uploadBlob, notify]);

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
