import { createContext, useContext, type MutableRefObject } from "react";
import type {
  useRedisServerInfo,
  useRedisScanKeys,
  useRedisKeyInfo,
  useRedisKeyValue,
  useRedisHashFields,
  useRedisListItemsPaginated,
  useRedisSetMembersPaginated,
  useRedisSortedSetMembers,
  useRedisSetHashField,
  useRedisDeleteHashField,
  useRedisUpdateSortedSetScore,
  useRedisSlowLog,
  useRedisDeleteKey,
  useRedisRenameKey,
  useRedisSetTtl,
  useRedisSetValue,
  useRedisExportKeys,
  useRedisKeyspaceHealth,
  useRedisPrefixMemory,
} from "@/lib/hooks";
import type {
  InfiniteQueryFacade,
  MutationFacade,
  QueryFacade,
} from "@/lib/queryFacade";
import type { RedisCacheEntry } from "@/lib/types";
import type { FlatRedisRow, NamespaceNode } from "./redis-namespace-tree";

export const mainTabs = [
  { id: "keys", label: "Keys" },
  { id: "info", label: "Server Info" },
  { id: "slowlog", label: "Slow Log" },
  { id: "keyspace", label: "Keyspace" },
  { id: "prefix", label: "Prefixes" },
  { id: "ops", label: "Ops" },
  { id: "pubsub", label: "Pub/Sub" },
] as const;
export type TabId = (typeof mainTabs)[number]["id"];

export interface PendingConfirm {
  message: string;
  onConfirm: () => void;
  /** Defaults to "Delete" — most `pendingConfirm` actions are deletions, but a non-deleting one
   * (e.g. Remove TTL) should say what it actually does instead of borrowing that label. */
  confirmLabel?: string;
}

/**
 * The page state is split into six contexts grouped by churn rate, so a change in one
 * bucket only re-renders the components that actually display it. Before the split a
 * single ~85-field context meant every keystroke in a detail-panel editor re-rendered
 * the whole virtualized key tree, and every auto-refresh tick re-rendered everything.
 *
 * Queries travel as facades (lib/queryFacade): `useQuery`/`useMutation` hand back a
 * fresh result object each render, so raw results would defeat every `useMemo` below.
 */
export interface RedisConnectionValue {
  caches: RedisCacheEntry[];
  activeCacheId: string | null;
  resolvedCacheId: string | null;
  handleCacheChange: (cacheId: string) => void;
}

export interface RedisNavValue {
  selectedKey: string | null;
  setSelectedKey: (key: string | null) => void;
  activeTab: TabId;
  setActiveTab: (tab: TabId) => void;
}

export interface RedisQueriesValue {
  serverInfo: QueryFacade<ReturnType<typeof useRedisServerInfo>>;
  scanResult: QueryFacade<ReturnType<typeof useRedisScanKeys>>;
  keyInfo: QueryFacade<ReturnType<typeof useRedisKeyInfo>>;
  keyValue: QueryFacade<ReturnType<typeof useRedisKeyValue>>;
  hashFields: QueryFacade<ReturnType<typeof useRedisHashFields>>;
  listItemsQuery: InfiniteQueryFacade<ReturnType<typeof useRedisListItemsPaginated>>;
  listItems: string[];
  setMembersQuery: InfiniteQueryFacade<ReturnType<typeof useRedisSetMembersPaginated>>;
  setMembers: string[];
  sortedSetMembers: QueryFacade<ReturnType<typeof useRedisSortedSetMembers>>;
  slowLog: QueryFacade<ReturnType<typeof useRedisSlowLog>>;
  health: QueryFacade<ReturnType<typeof useRedisKeyspaceHealth>>;
  prefixMemory: QueryFacade<ReturnType<typeof useRedisPrefixMemory>>;

  deleteKey: MutationFacade<ReturnType<typeof useRedisDeleteKey>>;
  renameKey: MutationFacade<ReturnType<typeof useRedisRenameKey>>;
  setTtl: MutationFacade<ReturnType<typeof useRedisSetTtl>>;
  setValue: MutationFacade<ReturnType<typeof useRedisSetValue>>;
  exportKeys: MutationFacade<ReturnType<typeof useRedisExportKeys>>;
  setHashField: MutationFacade<ReturnType<typeof useRedisSetHashField>>;
  deleteHashField: MutationFacade<ReturnType<typeof useRedisDeleteHashField>>;
  updateZsetScore: MutationFacade<ReturnType<typeof useRedisUpdateSortedSetScore>>;
}

export interface RedisBrowserValue {
  pattern: string;
  searchInput: string;
  setSearchInput: (v: string) => void;
  cursor: number;
  handleSearch: () => void;
  handleLoadMore: () => void;
  handleLoadAll: () => void;
  loadAllActive: boolean;
  /** Sets a new search pattern (updating both the input and the applied pattern) and switches
   * to the Keys tab — the drill-through target used by Prefix/Ops panels. */
  openPrefixInKeys: (prefix: string) => void;

  separator: string;
  setSeparator: (v: string) => void;
  expandedNamespaces: Set<string>;
  toggleNamespace: (path: string) => void;
  collapseAllNamespaces: () => void;
  expandAllNamespaces: () => void;

  displayKeys: string[];
  namespaceTree: NamespaceNode[];
  flatRedisRows: FlatRedisRow[];
  redisTreeRef: MutableRefObject<HTMLDivElement | null>;

  selectedKeys: Set<string>;
  setSelectedKeys: (v: Set<string>) => void;
  toggleKeySelection: (key: string) => void;
  /** True when every currently loaded key is selected — drives the header checkbox. */
  allLoadedSelected: boolean;
  /** True when some but not all loaded keys are selected — drives the indeterminate state. */
  someLoadedSelected: boolean;
  /** "Select all loaded" toggle: selects every loaded key, or clears the selection when all are. */
  toggleSelectAllLoaded: () => void;
  /** Keys per namespace path, precomputed once per tree so rows don't each re-walk their subtree. */
  subtreeKeysByPath: Map<string, string[]>;
  /** Namespace-row checkbox toggle: selects or clears the node's whole subtree. */
  toggleSubtreeSelection: (node: NamespaceNode) => void;
  handleBatchDelete: () => void;
  handleExportSelected: () => Promise<void>;
}

export interface RedisEditorValue {
  renaming: boolean;
  setRenaming: (v: boolean) => void;
  renameValue: string;
  setRenameValue: (v: string) => void;
  handleRenameKey: (oldKey: string) => void;

  editingValue: boolean;
  setEditingValue: (v: boolean) => void;
  stringValue: string;
  setStringValue: (v: string) => void;
  handleSaveStringValue: (key: string) => void;

  showTtlEditor: boolean;
  setShowTtlEditor: (v: boolean) => void;
  ttlSeconds: number;
  setTtlSeconds: (v: number) => void;
  handleSetTtl: (key: string) => void;
  handleRemoveTtl: (key: string) => void;
  requestRemoveTtl: (key: string) => void;

  hashAdding: boolean;
  setHashAdding: (v: boolean) => void;
  newHashField: string;
  setNewHashField: (v: string) => void;
  newHashValue: string;
  setNewHashValue: (v: string) => void;
  hashEditingField: string | null;
  setHashEditingField: (v: string | null) => void;
  hashEditFieldName: string;
  setHashEditFieldName: (v: string) => void;
  hashEditValue: string;
  setHashEditValue: (v: string) => void;
  handleAddHashField: (key: string) => void;
  handleSaveHashField: (key: string, originalField: string) => void;
  requestDeleteHashField: (key: string, field: string) => void;

  zsetEditingMember: string | null;
  setZsetEditingMember: (v: string | null) => void;
  zsetEditScore: string;
  setZsetEditScore: (v: string) => void;
  handleSaveZsetScore: (key: string, member: string) => void;

  handleCopyKey: (key: string) => void;
  requestDeleteKey: (key: string) => void;
  handleDeleteKey: (key: string) => void;
}

export interface RedisOpsValue {
  autoRefresh: boolean;
  setAutoRefresh: (v: boolean) => void;
  refreshInterval: number;
  setRefreshInterval: (v: number) => void;
  handleManualRefresh: () => void;
  /** `Date.now()` of the last completed Redis fetch, or null before the first one — feeds the
   * shared `LastRefreshed` indicator. */
  lastRefreshedAt: number | null;
  /** True while any Redis query is in flight. */
  isFetching: boolean;

  pendingConfirm: PendingConfirm | null;
  setPendingConfirm: (v: PendingConfirm | null) => void;
}

export const RedisConnectionContext = createContext<RedisConnectionValue | null>(null);
export const RedisNavContext = createContext<RedisNavValue | null>(null);
export const RedisQueriesContext = createContext<RedisQueriesValue | null>(null);
export const RedisBrowserContext = createContext<RedisBrowserValue | null>(null);
export const RedisEditorContext = createContext<RedisEditorValue | null>(null);
export const RedisOpsContext = createContext<RedisOpsValue | null>(null);

function useRequired<T>(ctx: T | null, name: string): T {
  if (ctx === null) throw new Error(`${name} must be used within RedisPageProvider`);
  return ctx;
}

export function useRedisConnection(): RedisConnectionValue {
  return useRequired(useContext(RedisConnectionContext), "useRedisConnection");
}
export function useRedisNav(): RedisNavValue {
  return useRequired(useContext(RedisNavContext), "useRedisNav");
}
export function useRedisQueries(): RedisQueriesValue {
  return useRequired(useContext(RedisQueriesContext), "useRedisQueries");
}
export function useRedisBrowser(): RedisBrowserValue {
  return useRequired(useContext(RedisBrowserContext), "useRedisBrowser");
}
export function useRedisEditor(): RedisEditorValue {
  return useRequired(useContext(RedisEditorContext), "useRedisEditor");
}
export function useRedisOps(): RedisOpsValue {
  return useRequired(useContext(RedisOpsContext), "useRedisOps");
}
