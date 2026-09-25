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
import { useLocation, useNavigate, useSearchParams } from "react-router";
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
    useStorageFileShares,
    useShareEntries,
    useShareFileProperties,
    useShareFileContent,
    useShareFileSasUrl,
    useUpdateSearchParams,
} from "@/lib/hooks";
import {
    loadViewPreference,
    saveViewPreference,
} from "@/lib/stores/panel-preferences";
import {
    useMutationFacade,
    useQueryFacade,
    type MutationFacade,
    type QueryFacade,
} from "@/lib/queryFacade";
import type {
    BlobProperties,
    StorageBlobContent,
    StorageBlobItem,
    StorageConfig,
    StorageShareEntryItem,
} from "@/lib/types";
import { apiFetch } from "@/lib/api";
import { buildZip } from "@/lib/zip";
import { downloadBlob, downloadText } from "@/lib/download";
import { planBlobDownload } from "@/lib/storage-blob-download";
import {
    sortStorageBlobItems,
    type StorageBlobSortDir,
    type StorageBlobSortKey,
} from "@/lib/storage-blob-sort";
import { useNotification } from "@/components/layout/NotificationSystem";

// The page state is split into per-churn contexts: a storage-account switch
// re-renders account consumers only, URL navigation re-renders nav consumers
// only, and the detail panel's copy/metadata/upload state can't churn the
// virtualized blob list. Query/mutation results travel through `queryFacade`
// so their fresh-per-render object identity can't defeat the memos.

export interface StorageAccountContextValue {
    accounts: StorageConfig[];
    activeAccountId: string | null;
    resolvedAccountId: string | null;
    activeAccount: StorageConfig | undefined;
    allowMutations: boolean;
    handleSelectAccount: (id: string) => void;
}

export interface StorageNavContextValue {
    blobListRef: React.MutableRefObject<HTMLDivElement | null>;
    selectedContainer: string | null;
    handleSelectContainer: (name: string) => void;
    currentPrefix: string;
    prefixHistory: string[];
    handleNavigatePrefix: (prefix: string) => void;
    handleBreadcrumb: (index: number) => void;
    selectedBlob: string | null;
    handleSelectBlob: (name: string) => void;
    storageViewMode: "browser" | "recovery";
    setStorageViewMode: (v: "browser" | "recovery") => void;
}

export interface StorageShareContextValue {
    selectedShare: string | null;
    shareDir: string;
    shareDirHistory: string[];
    selectedShareFile: string | null;
    shareFilter: string;
    setShareFilter: (v: string) => void;
    filteredShareEntries: StorageShareEntryItem[];
    handleSelectShare: (name: string) => void;
    handleNavigateShareDir: (dir: string) => void;
    handleShareBreadcrumb: (index: number) => void;
    handleSelectShareFile: (path: string) => void;
}

export interface StorageQueriesContextValue {
    containers: QueryFacade<ReturnType<typeof useStorageContainers>>;
    blobs: QueryFacade<ReturnType<typeof useStorageBlobs>>;
    blobProps: QueryFacade<ReturnType<typeof useBlobProperties>>;
    blobContent: QueryFacade<ReturnType<typeof useBlobContent>>;
    sasUrl: QueryFacade<ReturnType<typeof useBlobSasUrl>>;
    blobVersions: QueryFacade<ReturnType<typeof useBlobVersions>>;
    versionComparison: QueryFacade<ReturnType<typeof useBlobVersionComparison>>;
    deletedBlobs: QueryFacade<ReturnType<typeof useDeletedBlobs>>;
    fileShares: QueryFacade<ReturnType<typeof useStorageFileShares>>;
    shareEntries: QueryFacade<ReturnType<typeof useShareEntries>>;
    shareFileProps: QueryFacade<ReturnType<typeof useShareFileProperties>>;
    shareFileContent: QueryFacade<ReturnType<typeof useShareFileContent>>;
    shareFileSasUrl: QueryFacade<ReturnType<typeof useShareFileSasUrl>>;
    uploadBlob: MutationFacade<ReturnType<typeof useUploadBlob>>;
    copyBlob: MutationFacade<ReturnType<typeof useCopyBlob>>;
    restoreBlobVersion: MutationFacade<ReturnType<typeof useRestoreBlobVersion>>;
    setBlobMetadata: MutationFacade<ReturnType<typeof useSetBlobMetadata>>;
}

export interface StorageBrowserContextValue {
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

    showUpload: boolean;
    setShowUpload: (v: boolean) => void;
    uploadBlobName: string;
    setUploadBlobName: (v: string) => void;
    uploadFile: File | null;
    setUploadFile: (v: File | null) => void;
    uploadProgress: number;
    setUploadProgress: (v: number) => void;
    handleUploadDrop: (files: File[]) => void;
    handleUploadConfirm: () => void;
    uploadCheckingOverwrite: boolean;
    uploadOverwriteConfirm: { blobName: string; file: File } | null;
    setUploadOverwriteConfirm: (
        v: { blobName: string; file: File } | null,
    ) => void;
    handleUploadOverwriteConfirm: () => void;
}

export interface StorageDetailContextValue {
    metadataEditing: boolean;
    setMetadataEditing: (v: boolean) => void;
    metadataDraft: Record<string, string>;
    setMetadataDraft: React.Dispatch<
        React.SetStateAction<Record<string, string>>
    >;
    handleMetadataSave: () => void;
    blobDetailTab: "properties" | "versions" | "content";
    setBlobDetailTab: (v: "properties" | "versions" | "content") => void;
    showSasUrl: boolean;
    setShowSasUrl: (v: boolean) => void;

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
}

export interface StorageActionsContextValue {
    copiedUrl: boolean;
    handleCopyUrl: (blobName: string) => void;
    handleCopySasUrl: () => void;
    handleCopyShareSasUrl: () => void;
    handleDownloadBlob: (blobName: string) => Promise<void>;
    handleBatchDownloadBlobs: (blobNames: string[]) => Promise<void>;
    checkBlobExists: (blobName: string) => Promise<boolean>;
}

const StorageAccountContext =
    createContext<StorageAccountContextValue | null>(null);
const StorageNavContext = createContext<StorageNavContextValue | null>(null);
const StorageShareContext = createContext<StorageShareContextValue | null>(
    null,
);
const StorageQueriesContext =
    createContext<StorageQueriesContextValue | null>(null);
const StorageBrowserContext =
    createContext<StorageBrowserContextValue | null>(null);
const StorageDetailContext =
    createContext<StorageDetailContextValue | null>(null);
const StorageActionsContext =
    createContext<StorageActionsContextValue | null>(null);

function useScoped<T>(ctx: T | null, name: string): T {
    if (!ctx)
        throw new Error(`${name} must be used within StoragePageProvider`);
    return ctx;
}

export function useStorageAccount(): StorageAccountContextValue {
    return useScoped(useContext(StorageAccountContext), "useStorageAccount");
}

export function useStorageNav(): StorageNavContextValue {
    return useScoped(useContext(StorageNavContext), "useStorageNav");
}

export function useStorageShare(): StorageShareContextValue {
    return useScoped(useContext(StorageShareContext), "useStorageShare");
}

export function useStorageQueries(): StorageQueriesContextValue {
    return useScoped(useContext(StorageQueriesContext), "useStorageQueries");
}

export function useStorageBrowser(): StorageBrowserContextValue {
    return useScoped(useContext(StorageBrowserContext), "useStorageBrowser");
}

export function useStorageDetail(): StorageDetailContextValue {
    return useScoped(useContext(StorageDetailContext), "useStorageDetail");
}

export function useStorageActions(): StorageActionsContextValue {
    return useScoped(useContext(StorageActionsContext), "useStorageActions");
}

export function StoragePageProvider({
    children,
}: {
    children: ReactNode;
}): JSX.Element {
    const { data: profile } = useProfile();
    const location = useLocation();
    const navigate = useNavigate();
    const [searchParams] = useSearchParams();
    const updateParams = useUpdateSearchParams();
    const accounts = useMemo(
        () => profile?.config?.storageAccounts ?? [],
        [profile],
    );
    const { notify } = useNotification();

    // Selection state lives in the URL (?account/?container/?prefix/?blob/?view) so
    // reloads, back/forward and deep links restore the exact browsing position.
    // Persisted view-prefs only fill in what a bare visit doesn't specify.
    const accountParam = searchParams.get("account");
    const activeAccountId =
        accountParam && accounts.some((a) => a.id === accountParam)
            ? accountParam
            : null;
    const resolvedAccountId = activeAccountId ?? accounts[0]?.id ?? null;
    const activeAccount = accounts.find((a) => a.id === resolvedAccountId);
    const allowMutations = activeAccount?.allowMutations ?? false;

    const blobListRef = useRef<HTMLDivElement | null>(null);
    const selectedContainer = searchParams.get("container");
    const currentPrefix = searchParams.get("prefix") ?? "";
    const selectedBlob = searchParams.get("blob");
    // File-share browsing state lives in the same URL scheme (?share/?dir/?file) —
    // share and container selection are mutually exclusive, each clears the other.
    const selectedShare = searchParams.get("share");
    const shareDir = searchParams.get("dir") ?? "";
    const selectedShareFile = searchParams.get("file");
    const storageViewMode: "browser" | "recovery" =
        searchParams.get("view") === "recovery" ? "recovery" : "browser";
    // Derived rather than stored: navigation is strictly hierarchical, so the
    // ancestor list is reconstructable from the prefix itself — and deep links
    // into a prefix get a complete breadcrumb trail for free.
    const prefixHistory = useMemo(() => {
        const segments = currentPrefix.split("/").filter(Boolean);
        const history = [""];
        let acc = "";
        for (const segment of segments.slice(0, -1)) {
            acc += `${segment}/`;
            history.push(acc);
        }
        return history;
    }, [currentPrefix]);
    const shareDirHistory = useMemo(() => {
        const segments = shareDir.split("/").filter(Boolean);
        const history = [""];
        let acc = "";
        for (const segment of segments.slice(0, -1)) {
            acc = acc ? `${acc}/${segment}` : segment;
            history.push(acc);
        }
        return history;
    }, [shareDir]);
    const [continuationToken, setContinuationToken] = useState<string | null>(
        null,
    );
    const [allItems, setAllItems] = useState<StorageBlobItem[]>([]);
    const [blobFilter, setBlobFilter] = useState("");
    const [blobSortKey, setBlobSortKey] = useState<StorageBlobSortKey>("name");
    const [blobSortDir, setBlobSortDir] = useState<StorageBlobSortDir>("asc");
    const [multiSelectMode, setMultiSelectMode] = useState(false);
    const [selectedBlobs, setSelectedBlobs] = useState<Set<string>>(new Set());
    const [copiedUrl, setCopiedUrl] = useState(false);
    const [metadataEditing, setMetadataEditing] = useState(false);
    const [metadataDraft, setMetadataDraft] = useState<Record<string, string>>(
        {},
    );
    const [blobDetailTab, setBlobDetailTab] = useState<
        "properties" | "versions" | "content"
    >("properties");
    const [showSasUrl, setShowSasUrl] = useState(false);
    const [showUpload, setShowUpload] = useState(false);
    const [uploadBlobName, setUploadBlobName] = useState("");
    const [uploadFile, setUploadFile] = useState<File | null>(null);
    const [uploadProgress, setUploadProgress] = useState(0);
    const [uploadCheckingOverwrite, setUploadCheckingOverwrite] =
        useState(false);
    const [uploadOverwriteConfirm, setUploadOverwriteConfirm] = useState<{
        blobName: string;
        file: File;
    } | null>(null);
    const [showCopyDialog, setShowCopyDialog] = useState(false);
    const [copyDestContainer, setCopyDestContainer] = useState("");
    const [copyDestBlob, setCopyDestBlob] = useState("");
    const [copyOverwrite, setCopyOverwrite] = useState(false);
    const [copyConfirming, setCopyConfirming] = useState(false);
    const [copyStatus, setCopyStatus] = useState<string | null>(null);
    const [versionBaseId, setVersionBaseId] = useState<string | null>(null);
    const [versionCompareId, setVersionCompareId] = useState<string | null>(
        null,
    );
    const [versionCompareRequested, setVersionCompareRequested] =
        useState(false);
    const [versionRestoreId, setVersionRestoreId] = useState<string | null>(
        null,
    );
    const [shareFilter, setShareFilter] = useState("");

    // Command-palette deep links arrive as location.state.accountId — translate to
    // the account param (same as clicking the account) so the URL stays canonical.
    useEffect(() => {
        const state = location.state as { accountId?: string } | null;
        if (
            state?.accountId &&
            accounts.some((a) => a.id === state.accountId)
        ) {
            updateParams(
                {
                    account: state.accountId,
                    container: null,
                    prefix: null,
                    blob: null,
                },
                { replace: true },
            );
            navigate(location.pathname, { replace: true, state: null });
        }
    }, [location, navigate, accounts, updateParams]);

    // First visit without an explicit ?account: fall back to the persisted last
    // account (validated against the loaded profile), and always settle the param
    // on the resolved account so the URL is shareable.
    const accountRestoredRef = useRef(false);
    useEffect(() => {
        if (accountRestoredRef.current || accounts.length === 0) return;
        accountRestoredRef.current = true;
        if (accountParam) return;
        const last = loadViewPreference<string>("storage-last-account", "");
        const target = accounts.some((a) => a.id === last)
            ? last
            : resolvedAccountId;
        if (target) updateParams({ account: target }, { replace: true });
    }, [accounts, accountParam, resolvedAccountId, updateParams]);

    // Once a container-less visit has the container list, restore that account's
    // last-used container — only if it still exists.
    const containers = useStorageContainers(resolvedAccountId);
    const containerRestoredRef = useRef<string | null>(null);
    const containerList = containers.data;
    useEffect(() => {
        if (!resolvedAccountId || selectedContainer !== null) return;
        if (
            containerRestoredRef.current === resolvedAccountId ||
            !containerList
        )
            return;
        containerRestoredRef.current = resolvedAccountId;
        const last = loadViewPreference<string>(
            `storage-last-container:${resolvedAccountId}`,
            "",
        );
        if (last && containerList.some((c) => c.name === last)) {
            updateParams({ container: last }, { replace: true });
        }
    }, [resolvedAccountId, selectedContainer, containerList, updateParams]);

    // Persist the browsed location regardless of how it was reached (clicks, deep
    // links, back/forward) so a bare visit can resume it.
    useEffect(() => {
        if (!resolvedAccountId) return;
        saveViewPreference("storage-last-account", resolvedAccountId);
        if (selectedContainer) {
            saveViewPreference(
                `storage-last-container:${resolvedAccountId}`,
                selectedContainer,
            );
        }
    }, [resolvedAccountId, selectedContainer]);

    // Pagination is fetch-position state, not navigation state: it resets whenever
    // the browsed location changes (including via back/forward, which bypasses the
    // select handlers that used to clear it manually). Adjusted during render so a
    // new location never paints one frame of the previous location's page.
    const [prevLocation, setPrevLocation] = useState({ resolvedAccountId, selectedContainer, currentPrefix });
    if (
        prevLocation.resolvedAccountId !== resolvedAccountId ||
        prevLocation.selectedContainer !== selectedContainer ||
        prevLocation.currentPrefix !== currentPrefix
    ) {
        setPrevLocation({ resolvedAccountId, selectedContainer, currentPrefix });
        setContinuationToken(null);
        setAllItems([]);
    }

    // Same for the version-detail panel — a blob arriving via URL/back-forward
    // must not inherit the previous blob's compare/restore state.
    const [prevSelectedBlob, setPrevSelectedBlob] = useState(selectedBlob);
    if (prevSelectedBlob !== selectedBlob) {
        setPrevSelectedBlob(selectedBlob);
        setVersionBaseId(null);
        setVersionCompareId(null);
        setVersionCompareRequested(false);
        setVersionRestoreId(null);
    }

    const blobs = useStorageBlobs(
        resolvedAccountId,
        selectedContainer,
        currentPrefix,
        continuationToken,
    );
    const blobProps = useBlobProperties(
        resolvedAccountId,
        selectedContainer,
        selectedBlob,
    );
    const blobContent = useBlobContent(
        resolvedAccountId,
        selectedContainer,
        selectedBlob,
    );
    const sasUrl = useBlobSasUrl(
        resolvedAccountId,
        selectedContainer,
        selectedBlob,
        60,
    );
    const blobVersions = useBlobVersions(
        resolvedAccountId,
        selectedContainer,
        selectedBlob,
    );
    const versionComparison = useBlobVersionComparison(
        resolvedAccountId,
        selectedContainer,
        selectedBlob,
        versionBaseId,
        versionCompareId,
        versionCompareRequested,
    );
    const uploadBlob = useUploadBlob(resolvedAccountId, selectedContainer);
    const copyBlob = useCopyBlob(resolvedAccountId);
    const restoreBlobVersion = useRestoreBlobVersion(
        resolvedAccountId,
        selectedContainer,
        selectedBlob,
    );
    const setBlobMetadata = useSetBlobMetadata(
        resolvedAccountId,
        selectedContainer,
        selectedBlob,
    );
    // The mutation objects are fresh each render; the `mutate` functions on them
    // are referentially stable. Callbacks dep on the functions, not the objects.
    const { mutate: uploadMutate } = uploadBlob;
    const { mutate: copyMutate } = copyBlob;
    const { mutate: restoreVersionMutate } = restoreBlobVersion;
    const { mutate: setMetadataMutate } = setBlobMetadata;
    const deletedBlobs = useDeletedBlobs(resolvedAccountId, selectedContainer);
    const fileShares = useStorageFileShares(resolvedAccountId);
    const shareEntries = useShareEntries(
        resolvedAccountId,
        selectedShare,
        shareDir,
    );
    const shareFileProps = useShareFileProperties(
        resolvedAccountId,
        selectedShare,
        selectedShareFile,
    );
    const shareFileContent = useShareFileContent(
        resolvedAccountId,
        selectedShare,
        selectedShareFile,
    );
    const shareFileSasUrl = useShareFileSasUrl(
        resolvedAccountId,
        selectedShare,
        selectedShareFile,
        60,
    );
    // `useDropzone` itself lives in BlobBrowserPanel — its result is a fresh
    // object every render, which would defeat every context memo it travelled
    // through. The drop side-effect stays here as a stable callback.
    const handleUploadDrop = useCallback((files: File[]) => {
        const file = files[0];
        if (!file) return;
        setUploadFile(file);
        setUploadBlobName(file.name);
        setUploadProgress(0);
    }, []);

    const handleSelectAccount = useCallback(
        (id: string) => {
            updateParams({
                account: id,
                container: null,
                prefix: null,
                blob: null,
            });
        },
        [updateParams],
    );

    const handleSelectContainer = useCallback(
        (name: string) => {
            updateParams({
                container: name,
                prefix: null,
                blob: null,
                share: null,
                dir: null,
                file: null,
            });
        },
        [updateParams],
    );

    const handleSelectShare = useCallback(
        (name: string) => {
            updateParams({
                share: name,
                dir: null,
                file: null,
                container: null,
                prefix: null,
                blob: null,
            });
        },
        [updateParams],
    );

    const handleNavigateShareDir = useCallback(
        (dir: string) => {
            updateParams({ dir: dir || null, file: null });
        },
        [updateParams],
    );

    const handleShareBreadcrumb = useCallback(
        (index: number) => {
            const newDir =
                index === 0 ? "" : (shareDirHistory[index - 1] ?? "");
            updateParams({ dir: newDir || null, file: null });
        },
        [shareDirHistory, updateParams],
    );

    const handleSelectShareFile = useCallback(
        (path: string) => {
            updateParams({ file: path });
        },
        [updateParams],
    );

    const filteredShareEntries = useMemo(() => {
        const entries = shareEntries.data?.items ?? [];
        const filtered = shareFilter
            ? entries.filter((e) =>
                  e.name.toLowerCase().includes(shareFilter.toLowerCase()),
              )
            : entries;
        // Directories first, then alphabetical — the way a file manager orders a listing.
        return [...filtered].sort((a, b) =>
            a.isDirectory === b.isDirectory
                ? a.name.localeCompare(b.name)
                : a.isDirectory
                  ? -1
                  : 1,
        );
    }, [shareEntries.data?.items, shareFilter]);

    const handleCopyShareSasUrl = useCallback(() => {
        if (shareFileSasUrl.data?.sasUrl) {
            navigator.clipboard.writeText(shareFileSasUrl.data.sasUrl);
            setCopiedUrl(true);
            setTimeout(() => setCopiedUrl(false), 2000);
        }
    }, [shareFileSasUrl.data?.sasUrl]);

    const handleNavigatePrefix = useCallback(
        (prefix: string) => {
            updateParams({ prefix: prefix || null, blob: null });
        },
        [updateParams],
    );

    const handleBreadcrumb = useCallback(
        (index: number) => {
            const newPrefix =
                index === 0 ? "" : (prefixHistory[index - 1] ?? "");
            updateParams({ prefix: newPrefix || null, blob: null });
        },
        [prefixHistory, updateParams],
    );

    const handleLoadMore = useCallback(() => {
        if (blobs.data?.continuationToken) {
            setAllItems((prev) => [...prev, ...(blobs.data?.items ?? [])]);
            setContinuationToken(blobs.data.continuationToken);
        }
    }, [blobs.data]);

    const handleSelectBlob = useCallback(
        (name: string) => {
            updateParams({ blob: name });
        },
        [updateParams],
    );

    const setStorageViewMode = useCallback(
        (v: "browser" | "recovery") => {
            updateParams({ view: v === "browser" ? null : v });
        },
        [updateParams],
    );

    // Dedupes by name against `allItems`: with `useStorageBlobs`'s `placeholderData:
    // keepPreviousData` (6.5), while the next page is loading `blobs.data` still holds the
    // *previous* page's items (the ones `handleLoadMore` just copied into `allItems`) rather
    // than briefly going empty — without this filter those would render twice (and collide
    // as virtualizer keys) for the duration of that fetch.
    const displayItems = useMemo(() => {
        if (continuationToken === null) return blobs.data?.items ?? [];
        const seen = new Set(allItems.map((item) => item.name));
        const incoming = (blobs.data?.items ?? []).filter(
            (item) => !seen.has(item.name),
        );
        return [...allItems, ...incoming];
    }, [continuationToken, blobs.data?.items, allItems]);

    const filteredItems = useMemo(() => {
        const filtered = blobFilter
            ? displayItems.filter((item) =>
                  item.name.toLowerCase().includes(blobFilter.toLowerCase()),
              )
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
        // eslint-disable-next-line react-hooks/set-state-in-effect -- fetch-driven auto-pagination; advancing the query is the point of the effect
        handleLoadMore();
    }, [
        blobFilter,
        filteredItems.length,
        blobs.isFetching,
        blobs.data?.continuationToken,
        handleLoadMore,
    ]);

    const handleCopyUrl = useCallback(
        (blobName: string) => {
            // The Azure host uses the storage account name; `resolvedAccountId` is SwebKit's own
            // config id (a random 8-char slug), which produced a URL pointing at nothing.
            const url = `https://${activeAccount?.accountName}.blob.core.windows.net/${selectedContainer}/${blobName}`;
            navigator.clipboard.writeText(url);
            setCopiedUrl(true);
            setTimeout(() => setCopiedUrl(false), 2000);
        },
        [activeAccount?.accountName, selectedContainer],
    );

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
    const handleDownloadBlob = useCallback(
        async (blobName: string) => {
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
        },
        [fetchBlobContent, notify, selectedBlob],
    );

    // Bundles the selected blobs into a single ZIP, matching the pattern Service Bus's message
    // list already uses (lib/zip.ts) — previously this looped handleDownloadBlob per file, firing
    // N separate browser downloads instead of one archive. Binary blobs are skipped (their
    // content would be empty/corrupted, same reasoning as handleDownloadBlob above) rather than
    // silently zipped as empty files — the user is told which ones were skipped and why.
    const handleBatchDownloadBlobs = useCallback(
        async (blobNames: string[]) => {
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
                    const timestamp = new Date()
                        .toISOString()
                        .slice(0, 19)
                        .replace(/[T:]/g, "-");
                    downloadBlob(
                        `${selectedContainer}-blobs-${timestamp}.zip`,
                        zipped,
                    );
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
                    notify(
                        "success",
                        `Downloaded ${downloadedCount} blob(s) as ZIP`,
                    );
                }
            } catch (e) {
                console.error("Batch download failed:", e);
                notify("error", "Batch download failed", String(e));
            }
        },
        [fetchBlobContent, selectedContainer, notify],
    );

    // Used by both Upload (6.3) and Recovery (6.3) to warn before silently overwriting/
    // colliding with an existing blob, instead of relying on whatever page of the (paginated,
    // possibly filtered) list happens to be loaded client-side.
    const checkBlobExists = useCallback(
        async (blobName: string): Promise<boolean> => {
            if (!resolvedAccountId || !selectedContainer) return false;
            try {
                await apiFetch<BlobProperties>(
                    `/api/storage/${resolvedAccountId}/containers/${encodeURIComponent(selectedContainer)}/blobs/properties?${new URLSearchParams({ blobName })}`,
                );
                return true;
            } catch {
                return false;
            }
        },
        [resolvedAccountId, selectedContainer],
    );

    const toggleBlobSelection = useCallback((name: string) => {
        setSelectedBlobs((prev) => {
            const next = new Set(prev);
            if (next.has(name)) next.delete(name);
            else next.add(name);
            return next;
        });
    }, []);

    const performUpload = useCallback(
        (blobName: string, file: File) => {
            uploadMutate(
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
        },
        [uploadMutate, notify],
    );

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
        performUpload(
            uploadOverwriteConfirm.blobName,
            uploadOverwriteConfirm.file,
        );
    }, [uploadOverwriteConfirm, performUpload]);

    const handleMetadataSave = useCallback(() => {
        setMetadataMutate(metadataDraft, {
            onSuccess: () => {
                notify("success", "Metadata saved");
                setMetadataEditing(false);
            },
            onError: (e) => notify("error", "Metadata save failed", String(e)),
        });
    }, [setMetadataMutate, metadataDraft, notify]);

    const handleCopyConfirm = useCallback(() => {
        if (copyOverwrite) {
            setCopyConfirming(true);
            return;
        }
        copyMutate(
            {
                sourceContainer: selectedContainer!,
                sourceBlob: selectedBlob!,
                destContainer: copyDestContainer,
                destBlob: copyDestBlob,
                overwrite: false,
            },
            {
                onSuccess: () => {
                    notify(
                        "success",
                        "Blob copied",
                        `${copyDestContainer}/${copyDestBlob}`,
                    );
                    setCopyStatus("Copied successfully");
                    setTimeout(() => {
                        setShowCopyDialog(false);
                        setCopyStatus(null);
                    }, 2000);
                },
                onError: (e) => {
                    setCopyStatus(`Error: ${e}`);
                    notify("error", "Copy failed", String(e));
                },
            },
        );
    }, [
        copyOverwrite,
        copyMutate,
        selectedContainer,
        selectedBlob,
        copyDestContainer,
        copyDestBlob,
        notify,
    ]);

    const handleCopyOverwriteConfirm = useCallback(() => {
        copyMutate(
            {
                sourceContainer: selectedContainer!,
                sourceBlob: selectedBlob!,
                destContainer: copyDestContainer,
                destBlob: copyDestBlob,
                overwrite: true,
            },
            {
                onSuccess: () => {
                    notify(
                        "success",
                        "Blob copied",
                        `${copyDestContainer}/${copyDestBlob}`,
                    );
                    setCopyStatus("Copied successfully");
                    setCopyConfirming(false);
                    setTimeout(() => {
                        setShowCopyDialog(false);
                        setCopyStatus(null);
                    }, 2000);
                },
                onError: (e) => {
                    setCopyStatus(`Error: ${e}`);
                    notify("error", "Copy failed", String(e));
                },
            },
        );
    }, [
        copyMutate,
        selectedContainer,
        selectedBlob,
        copyDestContainer,
        copyDestBlob,
        notify,
    ]);

    const handleVersionRestoreConfirm = useCallback(() => {
        if (!versionRestoreId) return;
        restoreVersionMutate(versionRestoreId, {
            onSuccess: () => {
                notify("success", "Version restored", versionRestoreId);
                setVersionRestoreId(null);
            },
            onError: (e) => notify("error", "Restore failed", String(e)),
        });
    }, [versionRestoreId, restoreVersionMutate, notify]);

    // Query/mutation objects are fresh every render — facades expose stable
    // identities so the queries context only notifies on real data changes.
    const containersF = useQueryFacade(containers);
    const blobsF = useQueryFacade(blobs);
    const blobPropsF = useQueryFacade(blobProps);
    const blobContentF = useQueryFacade(blobContent);
    const sasUrlF = useQueryFacade(sasUrl);
    const blobVersionsF = useQueryFacade(blobVersions);
    const versionComparisonF = useQueryFacade(versionComparison);
    const deletedBlobsF = useQueryFacade(deletedBlobs);
    const fileSharesF = useQueryFacade(fileShares);
    const shareEntriesF = useQueryFacade(shareEntries);
    const shareFilePropsF = useQueryFacade(shareFileProps);
    const shareFileContentF = useQueryFacade(shareFileContent);
    const shareFileSasUrlF = useQueryFacade(shareFileSasUrl);
    const uploadBlobF = useMutationFacade(uploadBlob);
    const copyBlobF = useMutationFacade(copyBlob);
    const restoreBlobVersionF = useMutationFacade(restoreBlobVersion);
    const setBlobMetadataF = useMutationFacade(setBlobMetadata);

    const accountValue: StorageAccountContextValue = useMemo(
        () => ({
            accounts,
            activeAccountId,
            resolvedAccountId,
            activeAccount,
            allowMutations,
            handleSelectAccount,
        }),
        [
            accounts,
            activeAccountId,
            resolvedAccountId,
            activeAccount,
            allowMutations,
            handleSelectAccount,
        ],
    );

    const navValue: StorageNavContextValue = useMemo(
        () => ({
            blobListRef,
            selectedContainer,
            handleSelectContainer,
            currentPrefix,
            prefixHistory,
            handleNavigatePrefix,
            handleBreadcrumb,
            selectedBlob,
            handleSelectBlob,
            storageViewMode,
            setStorageViewMode,
        }),
        [
            selectedContainer,
            handleSelectContainer,
            currentPrefix,
            prefixHistory,
            handleNavigatePrefix,
            handleBreadcrumb,
            selectedBlob,
            handleSelectBlob,
            storageViewMode,
            setStorageViewMode,
        ],
    );

    const shareValue: StorageShareContextValue = useMemo(
        () => ({
            selectedShare,
            shareDir,
            shareDirHistory,
            selectedShareFile,
            shareFilter,
            setShareFilter,
            filteredShareEntries,
            handleSelectShare,
            handleNavigateShareDir,
            handleShareBreadcrumb,
            handleSelectShareFile,
        }),
        [
            selectedShare,
            shareDir,
            shareDirHistory,
            selectedShareFile,
            shareFilter,
            filteredShareEntries,
            handleSelectShare,
            handleNavigateShareDir,
            handleShareBreadcrumb,
            handleSelectShareFile,
        ],
    );

    const queriesValue: StorageQueriesContextValue = useMemo(
        () => ({
            containers: containersF,
            blobs: blobsF,
            blobProps: blobPropsF,
            blobContent: blobContentF,
            sasUrl: sasUrlF,
            blobVersions: blobVersionsF,
            versionComparison: versionComparisonF,
            deletedBlobs: deletedBlobsF,
            fileShares: fileSharesF,
            shareEntries: shareEntriesF,
            shareFileProps: shareFilePropsF,
            shareFileContent: shareFileContentF,
            shareFileSasUrl: shareFileSasUrlF,
            uploadBlob: uploadBlobF,
            copyBlob: copyBlobF,
            restoreBlobVersion: restoreBlobVersionF,
            setBlobMetadata: setBlobMetadataF,
        }),
        [
            containersF,
            blobsF,
            blobPropsF,
            blobContentF,
            sasUrlF,
            blobVersionsF,
            versionComparisonF,
            deletedBlobsF,
            fileSharesF,
            shareEntriesF,
            shareFilePropsF,
            shareFileContentF,
            shareFileSasUrlF,
            uploadBlobF,
            copyBlobF,
            restoreBlobVersionF,
            setBlobMetadataF,
        ],
    );

    const browserValue: StorageBrowserContextValue = useMemo(
        () => ({
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
            showUpload,
            setShowUpload,
            uploadBlobName,
            setUploadBlobName,
            uploadFile,
            setUploadFile,
            uploadProgress,
            setUploadProgress,
            handleUploadDrop,
            handleUploadConfirm,
            uploadCheckingOverwrite,
            uploadOverwriteConfirm,
            setUploadOverwriteConfirm,
            handleUploadOverwriteConfirm,
        }),
        [
            continuationToken,
            handleLoadMore,
            blobFilter,
            blobSortKey,
            blobSortDir,
            displayItems,
            filteredItems,
            multiSelectMode,
            selectedBlobs,
            toggleBlobSelection,
            showUpload,
            uploadBlobName,
            uploadFile,
            uploadProgress,
            handleUploadDrop,
            handleUploadConfirm,
            uploadCheckingOverwrite,
            uploadOverwriteConfirm,
            handleUploadOverwriteConfirm,
        ],
    );

    const detailValue: StorageDetailContextValue = useMemo(
        () => ({
            metadataEditing,
            setMetadataEditing,
            metadataDraft,
            setMetadataDraft,
            handleMetadataSave,
            blobDetailTab,
            setBlobDetailTab,
            showSasUrl,
            setShowSasUrl,
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
        }),
        [
            metadataEditing,
            metadataDraft,
            handleMetadataSave,
            blobDetailTab,
            showSasUrl,
            showCopyDialog,
            copyDestContainer,
            copyDestBlob,
            copyOverwrite,
            copyConfirming,
            copyStatus,
            handleCopyConfirm,
            handleCopyOverwriteConfirm,
            versionBaseId,
            versionCompareId,
            versionCompareRequested,
            versionRestoreId,
            handleVersionRestoreConfirm,
        ],
    );

    const actionsValue: StorageActionsContextValue = useMemo(
        () => ({
            copiedUrl,
            handleCopyUrl,
            handleCopySasUrl,
            handleCopyShareSasUrl,
            handleDownloadBlob,
            handleBatchDownloadBlobs,
            checkBlobExists,
        }),
        [
            copiedUrl,
            handleCopyUrl,
            handleCopySasUrl,
            handleCopyShareSasUrl,
            handleDownloadBlob,
            handleBatchDownloadBlobs,
            checkBlobExists,
        ],
    );

    return (
        <StorageAccountContext.Provider value={accountValue}>
            <StorageNavContext.Provider value={navValue}>
                <StorageShareContext.Provider value={shareValue}>
                    <StorageQueriesContext.Provider value={queriesValue}>
                        <StorageBrowserContext.Provider value={browserValue}>
                            <StorageDetailContext.Provider value={detailValue}>
                                <StorageActionsContext.Provider
                                    value={actionsValue}
                                >
                                    {children}
                                </StorageActionsContext.Provider>
                            </StorageDetailContext.Provider>
                        </StorageBrowserContext.Provider>
                    </StorageQueriesContext.Provider>
                </StorageShareContext.Provider>
            </StorageNavContext.Provider>
        </StorageAccountContext.Provider>
    );
}
