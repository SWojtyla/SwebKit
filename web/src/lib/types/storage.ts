export interface StorageConfig {
    id: string;
    displayName: string;
    accountName: string;
    connectionStringRef: string | null;
    useAad: boolean;
    allowMutations: boolean;
}

export interface StorageContainerItem {
    name: string;
    lastModified: string | null;
    publicAccess: string | null;
    leaseStatus: string | null;
}

export interface StorageBlobItem {
    name: string;
    isPrefix: boolean;
    sizeBytes: number | null;
    contentType: string | null;
    lastModified: string | null;
    etag: string | null;
}

export interface StorageBlobPage {
    items: StorageBlobItem[];
    continuationToken: string | null;
}

export interface BlobProperties {
    name: string;
    sizeBytes: number;
    contentType: string;
    lastModified: string;
    etag: string;
    leaseStatus: string | null;
    leaseState: string | null;
    accessTier: string | null;
    accessTierInferred: boolean | null;
    contentEncoding: string | null;
    contentLanguage: string | null;
    cacheControl: string | null;
    metadata: Record<string, string>;
    tags: Record<string, string>;
}

export interface StorageBlobContent {
    containerName: string;
    blobName: string;
    content: string;
    contentType: string | null;
    totalSizeBytes: number;
    wasTruncated: boolean;
    isBinary: boolean;
}

export interface BlobVersionComparison {
    baseVersionId: string;
    compareVersionId: string | null;
    metadataDiff: {
        before: Record<string, string | null>;
        after: Record<string, string | null>;
        addedKeys: string[];
        removedKeys: string[];
        changedKeys: string[];
    };
    contentComparePossible: boolean;
    baseSizeBytes: number | null;
    compareSizeBytes: number | null;
    textDiff: string | null;
}

export interface BlobMutationResult {
    success: boolean;
    errorMessage?: string | null;
    resultBlobPath?: string | null;
}

// ── Azure Files (file shares) ────────────────────────────────────────────────

export interface StorageShareItem {
    name: string;
    quotaGiB: number | null;
    accessTier: string | null;
    lastModified: string | null;
}

export interface StorageShareEntryItem {
    /** Path relative to the share root ("dir/sub/file.txt"). */
    name: string;
    isDirectory: boolean;
    sizeBytes: number | null;
    lastModified: string | null;
}

export interface StorageShareEntryPage {
    items: StorageShareEntryItem[];
    continuationToken: string | null;
}

export interface ShareFileProperties {
    name: string;
    sizeBytes: number;
    contentType: string | null;
    lastModified: string | null;
    eTag: string | null;
    metadata: Record<string, string>;
}

export interface ShareFileContent {
    shareName: string;
    path: string;
    content: string;
    contentType: string | null;
    totalSizeBytes: number;
    wasTruncated: boolean;
    isBinary: boolean;
}

export type BlobRecoveryState =
    | "Restored"
    | "Undeleted"
    | "Unsupported"
    | "Failed";

export interface BlobRecoveryResult {
    state: BlobRecoveryState;
    resultBlobPath?: string | null;
    errorMessage?: string | null;
}

// ── Agent ─────────────────────────────────────────────────────────────────────
