import type { AgentConfig } from "./agent";
import type { AksConfig } from "./aks";
import type { ObservabilityConfig } from "./observability";
import type { RedisConfig } from "./redis";
import type { SbMessageTemplate, ServiceBusNamespace } from "./serviceBus";
import type { SqlConfig } from "./sql";
import type { StorageConfig } from "./storage";
import type { WorkspaceMap, WorkspaceSnapshot, WorkspaceTopology } from "./workspace";

export interface ProfileData {
    config: AppConfig;
    serviceBusNamespaces: ServiceBusNamespace[];
    messageTemplates: SbMessageTemplate[];
    schemaVersion: number;
}

export interface AppConfig {
    name: string;
    isProduction: boolean;
    aksConfig: AksConfig | null;
    redisConfig: RedisConfig | null;
    sqlConfig: SqlConfig | null;
    storageAccounts: StorageConfig[];
    observabilityConfig: ObservabilityConfig | null;
    favoriteEntities: FavoriteEntity[];
    favoriteResources: FavoriteResource[];
    keyVaults: KeyVaultEntry[];
    /** The user-curated workspace maps (Settings → Map) — one named component
     * graph per project/environment. */
    maps: WorkspaceMap[];
    /** Legacy single-map storage — pre-maps profiles still carry nodes here and
     * the sidecar migrates them into `maps` on load/save. */
    topology: WorkspaceTopology;
}

// ── Workspace topology (workspace-intelligence Module 1) ────────────────────

export interface KeyVaultEntry {
    id: string;
    name: string;
    url: string;
}

export interface FavoriteEntity {
    namespaceId: string;
    entityPath: string;
    label: string;
}

export interface FavoriteResource {
    name: string;
    pinnedAt: string;
    snapshot: WorkspaceSnapshot;
}

export interface UserSettings {
    theme: string;
    fontSize: "small" | "medium" | "large";
    density: "comfortable" | "compact";
    warmupConnectionsOnStartup: boolean;
    restoreLastWorkspaceOnStartup: boolean;
    verifyApiClientSsl: boolean;
    apiClientRequestTabs: boolean;
    autoSaveRequests: boolean;
    agent: AgentConfig;
    logging: LoggingSettings;
    /** Incremented once per app launch by the sidecar; drives the Fathom theme's unlock progress. */
    sessionCount: number;
    /** Sticky once true — the Fathom theme, once earned, stays available even if sessionCount is later reset. */
    fathomUnlocked: boolean;
    /** Set only via the hidden six-click gesture on the status bar version number — no other UI surfaces it. */
    fathomDeveloperOverride: boolean;
    /** Port-forward pins set elsewhere in the app; surfaced here so saves from this page don't erase them. */
    pinnedPortForwards: Record<
        string,
        {
            label: string;
            namespace?: string;
            podLabelSelector?: string;
            remotePort: number;
            localPort: number;
            pinnedAt: string;
        }[]
    >;
}

/** Sessions needed before Fathom unlocks. Mirrors UserSettings.FathomUnlockThreshold (server-enforced; this constant only drives the progress bar). */
export const FATHOM_UNLOCK_THRESHOLD = 100;

export interface LoggingSettings {
    enabled: boolean;
    minimumLevel: string;
}

// ── API response helpers ─────────────────────────────────────────────────────
