export interface RedisConfig {
    caches: RedisCacheEntry[];
    activeCacheId: string | null;
    namespaceSeparator: string;
}

export interface RedisCacheEntry {
    id: string;
    displayName: string;
    /** Key into the OS credential store holding the connection string — the secret itself
     * is never persisted to the profile. */
    credentialKey: string;
    /** Legacy plaintext connection string — still present in profiles written before the
     * credential migration ran; new saves leave it empty. */
    connectionString: string;
    database: number;
    useAad: boolean;
    cacheName: string;
}

// ── SQL (sql-database-explorer) ──────────────────────────────────────────────

export interface RedisKeyScanResult {
    cursor: number;
    keys: string[];
    isComplete: boolean;
    /** How many `SCAN` round trips the sidecar made to assemble `keys`. See `KeyScanResult.PagesScanned`. */
    pagesScanned: number;
}

export interface RedisKeyInfo {
    key: string;
    type: string;
    ttl: string | null;
    memoryBytes: number | null;
    encoding: string | null;
    frequency: number | null;
    idleSeconds: number | null;
}

export interface RedisHashField {
    field: string;
    value: string;
}

export interface RedisSortedSetEntry {
    member: string;
    score: number;
}

export interface RedisSetMembersPage {
    members: string[];
    cursor: number;
    isComplete: boolean;
}

export interface RedisServerInfo {
    redisVersion: string;
    uptimeSeconds: number;
    connectedClients: number;
    usedMemoryBytes: number;
    maxMemoryBytes: number;
    usedMemoryHuman: string;
    totalCommandsProcessed: number;
    keyspaceHitRatio: number;
    databases: RedisDatabaseInfo[];
}

export interface RedisDatabaseInfo {
    index: number;
    keys: number;
    expires: number;
    avgTtl: number;
}

export interface RedisSlowLogEntry {
    id: number;
    executedAt: string;
    duration: string;
    command: string;
    arguments: string;
    clientName: string | null;
}

export interface RedisSlowLogSummary {
    entries: RedisSlowLogEntry[];
    truncated: boolean;
    maxReturned: number;
    capability: string;
}

export interface RedisPrefixMemoryBucket {
    prefix: string;
    keyCount: number;
    totalBytes: number;
    percentage: number;
}

export type RedisHealthSeverity = "Info" | "Warning" | "Critical";

export interface RedisHealthFinding {
    entityType: string;
    riskType: string;
    severity: RedisHealthSeverity;
    target: string;
    reason: string;
    memoryBytes: number | null;
    keyCount: number | null;
    sharePercent: number | null;
    drillKey: string | null;
}

export interface RedisKeyspaceHealthReport {
    generatedAtUtc: string;
    loadedKeyCount: number;
    estimatedKeyCount: number | null;
    coveragePercent: number;
    isPartialCoverage: boolean;
    confidenceLabel: string;
    hotKeySignalsAvailable: boolean;
    keysWithHotKeySignal: number;
    keysWithoutHotKeySignal: number;
    criticalCount: number;
    warningCount: number;
    infoCount: number;
    keyFindingCount: number;
    prefixFindingCount: number;
    findings: RedisHealthFinding[];
}

export interface RedisPubSubChannelInfo {
    channel: string;
    subscriberCount: number;
}

export interface RedisPubSubSnapshot {
    channels: RedisPubSubChannelInfo[];
    patternSubscriptionCount: number;
    truncated: boolean;
    maxChannels: number;
    capability: string;
}

// ── Storage ───────────────────────────────────────────────────────────────────
