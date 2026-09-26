import { createContext, useContext } from "react";
import type {
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
} from "@/lib/hooks";
import type { MutationFacade, QueryFacade } from "@/lib/queryFacade";
import type { StorageBlobSortDir, StorageBlobSortKey } from "@/lib/storage-blob-sort";
import type {
    StorageBlobItem,
    StorageConfig,
    StorageShareEntryItem,
} from "@/lib/types";

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

export const StorageAccountContext =
    createContext<StorageAccountContextValue | null>(null);
export const StorageNavContext = createContext<StorageNavContextValue | null>(null);
export const StorageShareContext = createContext<StorageShareContextValue | null>(
    null,
);
export const StorageQueriesContext =
    createContext<StorageQueriesContextValue | null>(null);
export const StorageBrowserContext =
    createContext<StorageBrowserContextValue | null>(null);
export const StorageDetailContext =
    createContext<StorageDetailContextValue | null>(null);
export const StorageActionsContext =
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

