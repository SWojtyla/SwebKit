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
import { useQueryClient, useIsFetching } from "@tanstack/react-query";
import {
  useProfile,
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
import type { RedisCacheEntry } from "@/lib/types";

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

export type NamespaceNode = {
  name: string;
  path: string;
  children: Map<string, NamespaceNode>;
  keys: string[];
  keyCount: number;
};

export function buildNamespaceTree(keys: string[], separator: string): NamespaceNode[] {
  const roots = new Map<string, NamespaceNode>();

  for (const key of keys) {
    const parts = key.split(separator);

    if (parts.length < 2) {
      let fallback = roots.get("(no prefix)");
      if (!fallback) {
        fallback = { name: "(no prefix)", path: "(no prefix)", children: new Map(), keys: [], keyCount: 0 };
        roots.set("(no prefix)", fallback);
      }
      fallback.keys.push(key);
      fallback.keyCount += 1;
      continue;
    }

    const namespaceParts = parts.slice(0, -1);
    let nodes = roots;
    let path = "";
    namespaceParts.forEach((name, index) => {
      path = index === 0 ? name : `${path}${separator}${name}`;
      let node = nodes.get(name);
      if (!node) {
        node = { name, path, children: new Map(), keys: [], keyCount: 0 };
        nodes.set(name, node);
      }
      node.keyCount += 1;
      nodes = node.children;
      if (index === namespaceParts.length - 1) {
        node.keys.push(key);
      }
    });
  }

  return [...roots.values()];
}

export type FlatRedisRow =
  | { kind: "namespace"; node: NamespaceNode; depth: number }
  | { kind: "key"; key: string; node: NamespaceNode; depth: number };

export function flattenNamespaceTree(
  nodes: NamespaceNode[],
  expandedNamespaces: Set<string>,
  depth = 0,
): FlatRedisRow[] {
  const rows: FlatRedisRow[] = [];
  for (const node of nodes) {
    rows.push({ kind: "namespace", node, depth });
    if (expandedNamespaces.has(node.path)) {
      rows.push(...flattenNamespaceTree([...node.children.values()], expandedNamespaces, depth + 1));
      for (const key of node.keys) {
        rows.push({ kind: "key", key, node, depth });
      }
    }
  }
  return rows;
}

export function redisRowKey(row: FlatRedisRow): string {
  return row.kind === "namespace" ? `ns:${row.node.path}` : `key:${row.key}`;
}

/**
 * Every namespace path in the tree, recursively. Used by "Expand all" — the deliberate,
 * user-triggered counterpart to "Collapse all" — and, previously, by the reactive effect that
 * used to re-expand everything on any `namespaceTree` identity change (the reported "not
 * collapsed by default" bug: search, pagination, cache switch and every key mutation all produce
 * a new tree, so that effect fired constantly and silently undid "Collapse all").
 */
export function collectAllNamespacePaths(nodes: NamespaceNode[]): Set<string> {
  const paths = new Set<string>();
  const walk = (list: NamespaceNode[]) => {
    for (const node of list) {
      paths.add(node.path);
      walk([...node.children.values()]);
    }
  };
  walk(nodes);
  return paths;
}

/**
 * The default expansion for a freshly loaded tree: only the root-level namespaces, not every
 * descendant. A multi-level keyspace (`user:profile:*`, `cache:search:results:*`, ...) opens
 * showing its top-level groups instead of every nested folder at once — this is what "collapsed
 * by default" means in practice for a hierarchical keyspace. Applied once per genuine
 * search/cache change (see the ref-guarded seed effect in `RedisPageProvider`), never as a
 * reaction to incidental data changes like pagination or a mutation's refetch.
 */
export function defaultExpandedNamespacePaths(nodes: NamespaceNode[]): Set<string> {
  return new Set(nodes.map((node) => node.path));
}

interface PendingConfirm {
  message: string;
  onConfirm: () => void;
  /** Defaults to "Delete" — most `pendingConfirm` actions are deletions, but a non-deleting one
   * (e.g. Remove TTL) should say what it actually does instead of borrowing that label. */
  confirmLabel?: string;
}

export interface RedisPageContextValue {
  caches: RedisCacheEntry[];
  activeCacheId: string | null;
  resolvedCacheId: string | null;
  handleCacheChange: (cacheId: string) => void;

  selectedKey: string | null;
  setSelectedKey: (key: string | null) => void;
  activeTab: TabId;
  setActiveTab: (tab: TabId) => void;

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
  redisTreeRef: React.MutableRefObject<HTMLDivElement | null>;

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

  selectedKeys: Set<string>;
  batchMode: boolean;
  setBatchMode: (v: boolean) => void;
  setSelectedKeys: (v: Set<string>) => void;
  toggleKeySelection: (key: string) => void;
  handleBatchDelete: () => void;
  handleExportSelected: () => Promise<void>;

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

  serverInfo: ReturnType<typeof useRedisServerInfo>;
  scanResult: ReturnType<typeof useRedisScanKeys>;
  keyInfo: ReturnType<typeof useRedisKeyInfo>;
  keyValue: ReturnType<typeof useRedisKeyValue>;
  hashFields: ReturnType<typeof useRedisHashFields>;
  listItemsQuery: ReturnType<typeof useRedisListItemsPaginated>;
  listItems: string[];
  setMembersQuery: ReturnType<typeof useRedisSetMembersPaginated>;
  setMembers: string[];
  sortedSetMembers: ReturnType<typeof useRedisSortedSetMembers>;
  slowLog: ReturnType<typeof useRedisSlowLog>;
  health: ReturnType<typeof useRedisKeyspaceHealth>;
  prefixMemory: ReturnType<typeof useRedisPrefixMemory>;

  deleteKey: ReturnType<typeof useRedisDeleteKey>;
  renameKey: ReturnType<typeof useRedisRenameKey>;
  setTtl: ReturnType<typeof useRedisSetTtl>;
  setValue: ReturnType<typeof useRedisSetValue>;
  exportKeys: ReturnType<typeof useRedisExportKeys>;
  setHashField: ReturnType<typeof useRedisSetHashField>;
  deleteHashField: ReturnType<typeof useRedisDeleteHashField>;
  updateZsetScore: ReturnType<typeof useRedisUpdateSortedSetScore>;
}

const RedisPageContext = createContext<RedisPageContextValue | null>(null);

export function useRedisPageContext(): RedisPageContextValue {
  const ctx = useContext(RedisPageContext);
  if (!ctx) throw new Error("useRedisPageContext must be used within RedisPageProvider");
  return ctx;
}

export function RedisPageProvider({ children }: { children: ReactNode }): JSX.Element {
  const { data: profile } = useProfile();
  const location = useLocation();
  const navigate = useNavigate();
  const redisConfig = profile?.config?.redisConfig;
  const caches = useMemo(() => redisConfig?.caches ?? [], [redisConfig]);
  const [activeCacheId, setActiveCacheId] = useState<string | null>(null);
  const resolvedCacheId = activeCacheId ?? caches[0]?.id ?? null;
  const queryClient = useQueryClient();

  useEffect(() => {
    const state = location.state as { cacheId?: string } | null;
    if (state?.cacheId && caches.some((c) => c.id === state.cacheId)) {
      setActiveCacheId(state.cacheId);
      navigate(location.pathname, { replace: true, state: null });
    }
  }, [location, caches, navigate]);

  const redisTreeRef = useRef<HTMLDivElement | null>(null);
  const [selectedKey, setSelectedKey] = useState<string | null>(null);
  const [pattern, setPattern] = useState("*");
  const [searchInput, setSearchInput] = useState("*");
  const [cursor, setCursor] = useState(0);
  const [allKeys, setAllKeys] = useState<string[]>([]);
  const [activeTab, setActiveTab] = useState<TabId>("keys");
  const [renaming, setRenaming] = useState(false);
  const [renameValue, setRenameValue] = useState("");
  const [editingValue, setEditingValue] = useState(false);
  const [stringValue, setStringValue] = useState("");
  const [showTtlEditor, setShowTtlEditor] = useState(false);
  const [ttlSeconds, setTtlSeconds] = useState(0);
  const [selectedKeys, setSelectedKeys] = useState<Set<string>>(new Set());
  const [batchMode, setBatchMode] = useState(false);
  const [autoRefresh, setAutoRefresh] = useState(false);
  const [refreshInterval, setRefreshInterval] = useState(10);
  const [pendingConfirm, setPendingConfirm] = useState<PendingConfirm | null>(null);
  const [loadAllActive, setLoadAllActive] = useState(false);
  const [expandedNamespaces, setExpandedNamespaces] = useState<Set<string>>(new Set());
  // Guards the one-time expansion seed below: true once this search/cache's tree has been
  // seeded, so later namespaceTree changes (pagination, load-more, a key mutation's refetch)
  // never re-trigger it. Reset to false only at a genuine search or cache change, which is what
  // makes "Collapse all" durable instead of silently undone by the next unrelated refresh.
  const hasSeededExpansionRef = useRef(false);
  const [lastRefreshedAt, setLastRefreshedAt] = useState<number | null>(null);
  const [hashAdding, setHashAdding] = useState(false);
  const [newHashField, setNewHashField] = useState("");
  const [newHashValue, setNewHashValue] = useState("");
  const [hashEditingField, setHashEditingField] = useState<string | null>(null);
  const [hashEditFieldName, setHashEditFieldName] = useState("");
  const [hashEditValue, setHashEditValue] = useState("");
  const [zsetEditingMember, setZsetEditingMember] = useState<string | null>(null);
  const [zsetEditScore, setZsetEditScore] = useState("");

  const [separator, setSeparator] = useState(redisConfig?.namespaceSeparator?.trim() || ":");
  const listPageSize = 5;
  const setPageSize = 2;

  const serverInfo = useRedisServerInfo(resolvedCacheId);
  const scanResult = useRedisScanKeys(resolvedCacheId, pattern, cursor, 100);
  const keyInfo = useRedisKeyInfo(resolvedCacheId, selectedKey);
  const keyValue = useRedisKeyValue(resolvedCacheId, selectedKey, keyInfo.data?.type ?? null);
  const hashFields = useRedisHashFields(resolvedCacheId, selectedKey, keyInfo.data?.type ?? null);
  const listItemsQuery = useRedisListItemsPaginated(resolvedCacheId, selectedKey, keyInfo.data?.type ?? null, listPageSize);
  const setMembersQuery = useRedisSetMembersPaginated(resolvedCacheId, selectedKey, keyInfo.data?.type ?? null, setPageSize);
  const sortedSetMembers = useRedisSortedSetMembers(resolvedCacheId, selectedKey, keyInfo.data?.type ?? null);
  const deleteKey = useRedisDeleteKey(resolvedCacheId);
  const renameKey = useRedisRenameKey(resolvedCacheId);
  const setTtl = useRedisSetTtl(resolvedCacheId);
  const setValue = useRedisSetValue(resolvedCacheId);
  const exportKeys = useRedisExportKeys(resolvedCacheId);
  const setHashField = useRedisSetHashField(resolvedCacheId);
  const deleteHashField = useRedisDeleteHashField(resolvedCacheId);
  const updateZsetScore = useRedisUpdateSortedSetScore(resolvedCacheId);
  const slowLog = useRedisSlowLog(resolvedCacheId);

  const listItems = listItemsQuery.data?.pages.flat() ?? [];
  const setMembers = setMembersQuery.data?.pages.flatMap((p) => p.members) ?? [];

  const handleManualRefresh = useCallback(() => {
    queryClient.invalidateQueries({ queryKey: ["redis"] });
  }, [queryClient]);

  useEffect(() => {
    if (!autoRefresh || !resolvedCacheId) return;
    // Toggling auto-refresh on must have an immediate, visible effect — waiting a full
    // `refreshInterval` before anything happens is indistinguishable from auto-refresh being
    // broken (see docs/pitfalls/react-frontend.md's TanStack Query section).
    queryClient.invalidateQueries({ queryKey: ["redis"] });
    const id = setInterval(() => {
      queryClient.invalidateQueries({ queryKey: ["redis"] });
    }, refreshInterval * 1000);
    return () => clearInterval(id);
  }, [autoRefresh, refreshInterval, resolvedCacheId, queryClient]);

  // Stamped whenever Redis fetching settles (first load, auto-refresh, the manual Refresh
  // button, or a mutation's invalidation), the same pattern AKS's `LastRefreshed` uses — derived
  // from fetch state rather than each call site, so a failed refetch still moves the label
  // instead of freezing it, and "updated Ns ago" always describes what's on screen.
  const isFetching = useIsFetching({ queryKey: ["redis"] }) > 0;
  const wasFetchingRef = useRef(false);
  useEffect(() => {
    if (wasFetchingRef.current && !isFetching) setLastRefreshedAt(Date.now());
    wasFetchingRef.current = isFetching;
  }, [isFetching]);

  useEffect(() => {
    setHashAdding(false);
    setNewHashField("");
    setNewHashValue("");
    setHashEditingField(null);
    setHashEditFieldName("");
    setHashEditValue("");
    setZsetEditingMember(null);
    setZsetEditScore("");
  }, [selectedKey]);

  // Shared by the Search button and the Prefix/Ops drill-through links below. Setting `pattern`
  // directly (rather than `setSearchInput` followed by a separate call reading `searchInput`)
  // avoids a stale-closure bug: a `setState` update isn't visible to code later in the same
  // handler, only on the next render (see the "searchParams in a callback is a snapshot" pitfall
  // in docs/pitfalls/react-frontend.md — the same class of bug).
  const applySearchPattern = useCallback((newPattern: string) => {
    setSearchInput(newPattern);
    setPattern(newPattern);
    setCursor(0);
    setAllKeys([]);
    hasSeededExpansionRef.current = false;
    setExpandedNamespaces(new Set());
  }, []);

  const handleSearch = () => applySearchPattern(searchInput);

  // Drill-through target for the Prefix/Ops panels, mirroring Keyspace's existing
  // onOpenKey-then-switch-tab pattern.
  const openPrefixInKeys = useCallback(
    (prefix: string) => {
      applySearchPattern(`${prefix}${separator}*`);
      setActiveTab("keys");
    },
    [applySearchPattern, separator],
  );

  const handleLoadMore = () => {
    if (scanResult.data && !scanResult.data.isComplete) {
      setAllKeys((prev) => [...prev, ...scanResult.data!.keys]);
      setCursor(scanResult.data.cursor);
    }
  };

  const handleLoadAll = () => {
    if (scanResult.data && !scanResult.data.isComplete) {
      setLoadAllActive(true);
    }
  };

  useEffect(() => {
    if (!loadAllActive || !scanResult.data || scanResult.data.isComplete) {
      setLoadAllActive(false);
      return;
    }
    handleLoadMore();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [loadAllActive, scanResult.data]);

  const scanKeys = useMemo(() => scanResult.data?.keys ?? [], [scanResult.data?.keys]);
  const displayKeys = useMemo(
    () => (cursor === 0 ? scanKeys : allKeys.length > 0 ? [...allKeys, ...scanKeys] : scanKeys),
    [cursor, scanKeys, allKeys],
  );
  const health = useRedisKeyspaceHealth(resolvedCacheId, displayKeys, separator);
  const prefixMemory = useRedisPrefixMemory(resolvedCacheId, displayKeys, separator);

  const namespaceTree = useMemo(
    () => buildNamespaceTree(displayKeys, separator),
    [displayKeys, separator],
  );

  const flatRedisRows = useMemo(
    () => flattenNamespaceTree(namespaceTree, expandedNamespaces),
    [namespaceTree, expandedNamespaces],
  );

  // One-time seed per genuine search/cache change, guarded by `hasSeededExpansionRef` (reset to
  // false only in `applySearchPattern`/`handleCacheChange`) — NOT a reactive effect on every
  // `namespaceTree` identity change. That was the reported bug: pagination, "load more", and any
  // key mutation's refetch all produce a new tree, so a plain `[namespaceTree]` effect fired
  // constantly and silently re-expanded everything, undoing "Collapse all" moments after it was
  // clicked. Firing once per genuine change instead makes "Collapse all" durable.
  useEffect(() => {
    if (hasSeededExpansionRef.current) return;
    if (namespaceTree.length === 0) return;
    hasSeededExpansionRef.current = true;
    setExpandedNamespaces(defaultExpandedNamespacePaths(namespaceTree));
  }, [namespaceTree]);

  const toggleNamespace = (path: string) => {
    setExpandedNamespaces((prev) => {
      const next = new Set(prev);
      if (next.has(path)) next.delete(path);
      else next.add(path);
      return next;
    });
  };

  const collapseAllNamespaces = () => setExpandedNamespaces(new Set());
  const expandAllNamespaces = () => setExpandedNamespaces(collectAllNamespacePaths(namespaceTree));

  const handleDeleteKey = (key: string) => {
    deleteKey.mutate(key, {
      onSuccess: () => {
        setSelectedKey(null);
        setCursor(0);
        setAllKeys([]);
      },
    });
  };

  const handleRenameKey = (oldKey: string) => {
    if (!renameValue.trim() || renameValue === oldKey) {
      setRenaming(false);
      return;
    }
    renameKey.mutate({ key: oldKey, newKey: renameValue.trim() }, {
      onSuccess: () => {
        setSelectedKey(renameValue.trim());
        setRenaming(false);
        setCursor(0);
        setAllKeys([]);
      },
    });
  };

  const handleCopyKey = (key: string) => {
    navigator.clipboard.writeText(key);
  };

  const handleSetTtl = (key: string) => {
    setTtl.mutate({ key, ttlSeconds }, {
      onSuccess: () => setShowTtlEditor(false),
    });
  };

  const handleRemoveTtl = (key: string) => {
    setTtl.mutate({ key, removeTtl: true }, {
      onSuccess: () => setShowTtlEditor(false),
    });
  };

  const requestRemoveTtl = (key: string) => {
    setPendingConfirm({
      message: `Remove TTL from "${key}"? It will no longer expire automatically.`,
      onConfirm: () => handleRemoveTtl(key),
      confirmLabel: "Remove TTL",
    });
  };

  const handleSaveStringValue = (key: string) => {
    setValue.mutate({ key, value: stringValue }, {
      onSuccess: () => setEditingValue(false),
    });
  };

  const requestDeleteKey = (key: string) => {
    setPendingConfirm({
      message: `Delete key "${key}"?`,
      onConfirm: () => handleDeleteKey(key),
    });
  };

  const handleAddHashField = (key: string) => {
    const field = newHashField.trim();
    if (!field) return;
    setHashField.mutate({ key, field, value: newHashValue }, {
      onSuccess: () => {
        setHashAdding(false);
        setNewHashField("");
        setNewHashValue("");
      },
    });
  };

  const handleSaveHashField = (key: string, originalField: string) => {
    const field = hashEditFieldName.trim();
    if (!field) return;
    if (field === originalField) {
      setHashField.mutate({ key, field, value: hashEditValue }, {
        onSuccess: () => setHashEditingField(null),
      });
    } else {
      setHashField.mutate({ key, field, value: hashEditValue }, {
        onSuccess: () => {
          deleteHashField.mutate({ key, field: originalField }, {
            onSuccess: () => setHashEditingField(null),
          });
        },
      });
    }
  };

  const requestDeleteHashField = (key: string, field: string) => {
    setPendingConfirm({
      message: `Delete field "${field}"?`,
      onConfirm: () => deleteHashField.mutate({ key, field }),
    });
  };

  const handleSaveZsetScore = (key: string, member: string) => {
    const score = parseFloat(zsetEditScore);
    if (Number.isNaN(score)) return;
    updateZsetScore.mutate({ key, member, score }, {
      onSuccess: () => setZsetEditingMember(null),
    });
  };

  const handleBatchDelete = () => {
    setPendingConfirm({
      message: `Delete ${selectedKeys.size} key${selectedKeys.size === 1 ? "" : "s"}?`,
      onConfirm: () => {
        selectedKeys.forEach((key) => deleteKey.mutate(key));
        setSelectedKeys(new Set());
        setBatchMode(false);
        setCursor(0);
        setAllKeys([]);
      },
    });
  };

  const handleExportSelected = async () => {
    const exportData = await exportKeys.mutateAsync(Array.from(selectedKeys));
    const blob = new Blob([JSON.stringify(exportData, null, 2)], { type: "application/json" });
    const url = URL.createObjectURL(blob);
    const a = document.createElement("a");
    a.href = url;
    a.download = "redis-keys-export.json";
    a.click();
    URL.revokeObjectURL(url);
  };

  const toggleKeySelection = (key: string) => {
    setSelectedKeys((prev) => {
      const next = new Set(prev);
      if (next.has(key)) next.delete(key);
      else next.add(key);
      return next;
    });
  };

  const handleCacheChange = (cacheId: string) => {
    setActiveCacheId(cacheId);
    setCursor(0);
    setAllKeys([]);
    setSelectedKey(null);
    hasSeededExpansionRef.current = false;
    setExpandedNamespaces(new Set());
  };

  const value: RedisPageContextValue = {
    caches,
    activeCacheId,
    resolvedCacheId,
    handleCacheChange,

    selectedKey,
    setSelectedKey,
    activeTab,
    setActiveTab,

    pattern,
    searchInput,
    setSearchInput,
    cursor,
    handleSearch,
    handleLoadMore,
    handleLoadAll,
    loadAllActive,
    openPrefixInKeys,

    separator,
    setSeparator,
    expandedNamespaces,
    toggleNamespace,
    collapseAllNamespaces,
    expandAllNamespaces,

    displayKeys,
    namespaceTree,
    flatRedisRows,
    redisTreeRef,

    renaming,
    setRenaming,
    renameValue,
    setRenameValue,
    handleRenameKey,

    editingValue,
    setEditingValue,
    stringValue,
    setStringValue,
    handleSaveStringValue,

    showTtlEditor,
    setShowTtlEditor,
    ttlSeconds,
    setTtlSeconds,
    handleSetTtl,
    handleRemoveTtl,
    requestRemoveTtl,

    selectedKeys,
    batchMode,
    setBatchMode,
    setSelectedKeys,
    toggleKeySelection,
    handleBatchDelete,
    handleExportSelected,

    autoRefresh,
    setAutoRefresh,
    refreshInterval,
    setRefreshInterval,
    handleManualRefresh,
    lastRefreshedAt,
    isFetching,

    pendingConfirm,
    setPendingConfirm,

    hashAdding,
    setHashAdding,
    newHashField,
    setNewHashField,
    newHashValue,
    setNewHashValue,
    hashEditingField,
    setHashEditingField,
    hashEditFieldName,
    setHashEditFieldName,
    hashEditValue,
    setHashEditValue,
    handleAddHashField,
    handleSaveHashField,
    requestDeleteHashField,

    zsetEditingMember,
    setZsetEditingMember,
    zsetEditScore,
    setZsetEditScore,
    handleSaveZsetScore,

    handleCopyKey,
    requestDeleteKey,
    handleDeleteKey,

    serverInfo,
    scanResult,
    keyInfo,
    keyValue,
    hashFields,
    listItemsQuery,
    listItems,
    setMembersQuery,
    setMembers,
    sortedSetMembers,
    slowLog,
    health,
    prefixMemory,

    deleteKey,
    renameKey,
    setTtl,
    setValue,
    exportKeys,
    setHashField,
    deleteHashField,
    updateZsetScore,
  };

  return <RedisPageContext.Provider value={value}>{children}</RedisPageContext.Provider>;
}
