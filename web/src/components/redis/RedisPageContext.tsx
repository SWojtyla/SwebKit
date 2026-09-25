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
import { useQueryClient, useIsFetching } from "@tanstack/react-query";
import {
  useProfile,
  useUpdateProfile,
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
  useUpdateSearchParams,
} from "@/lib/hooks";
import { loadViewPreference, saveViewPreference } from "@/lib/stores/panel-preferences";
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
type TabId = (typeof mainTabs)[number]["id"];

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

function flattenNamespaceTree(
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
 * user-triggered counterpart to "Collapse all".
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
 * Every key in a namespace's subtree — its own keys plus all descendants'. Used by the
 * namespace-row selection checkbox, which selects or clears the whole subtree in one click
 * (same behavior as the MAUI browser's namespace checkboxes).
 */
export function collectSubtreeKeys(node: NamespaceNode): string[] {
  const keys = [...node.keys];
  for (const child of node.children.values()) keys.push(...collectSubtreeKeys(child));
  return keys;
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
  const updateProfile = useUpdateProfile();
  const location = useLocation();
  const navigate = useNavigate();
  const redisConfig = profile?.config?.redisConfig;
  const caches = useMemo(() => redisConfig?.caches ?? [], [redisConfig]);
  const [activeCacheId, setActiveCacheId] = useState<string | null>(null);
  // Fallback order: this session's explicit selection → the persisted "last used" cache (written
  // on every switch — it doubles as the agent tools' default, see RedisToolContext) → first
  // configured cache.
  const configuredActive = redisConfig?.activeCacheId;
  const resolvedCacheId =
    activeCacheId ??
    (configuredActive && caches.some((c) => c.id === configuredActive) ? configuredActive : null) ??
    caches[0]?.id ??
    null;
  const queryClient = useQueryClient();

  useEffect(() => {
    const state = location.state as { cacheId?: string } | null;
    if (state?.cacheId && caches.some((c) => c.id === state.cacheId)) {
      setActiveCacheId(state.cacheId);
      navigate(location.pathname, { replace: true, state: null });
    }
  }, [location, caches, navigate]);

  const redisTreeRef = useRef<HTMLDivElement | null>(null);
  const [searchParams] = useSearchParams();
  const updateParams = useUpdateSearchParams();
  const [selectedKey, setSelectedKey] = useState<string | null>(null);
  const [pattern, setPattern] = useState("*");
  const [searchInput, setSearchInput] = useState("*");
  const [cursor, setCursor] = useState(0);
  const [allKeys, setAllKeys] = useState<string[]>([]);
  // `?tab=` is the deep-linkable form of the tab strip; anything unrecognized
  // falls back to Keys.
  const tabParam = searchParams.get("tab");
  const activeTab: TabId = mainTabs.some((t) => t.id === tabParam)
    ? (tabParam as TabId)
    : "keys";
  const setActiveTab = useCallback(
    (tab: TabId) => updateParams({ tab: tab === "keys" ? null : tab }),
    [updateParams],
  );
  const [renaming, setRenaming] = useState(false);
  const [renameValue, setRenameValue] = useState("");
  const [editingValue, setEditingValue] = useState(false);
  const [stringValue, setStringValue] = useState("");
  const [showTtlEditor, setShowTtlEditor] = useState(false);
  const [ttlSeconds, setTtlSeconds] = useState(0);
  const [selectedKeys, setSelectedKeys] = useState<Set<string>>(new Set());
  const [autoRefresh, setAutoRefresh] = useState(false);
  const [refreshInterval, setRefreshInterval] = useState(10);
  const [pendingConfirm, setPendingConfirm] = useState<PendingConfirm | null>(null);
  const [loadAllActive, setLoadAllActive] = useState(false);
  // Every namespace starts collapsed (matching the MAUI browser); the user expands explicitly
  // or via "Expand all". Nothing ever re-expands on its own — search, pagination and cache
  // switches all reset to this same empty set.
  const [expandedNamespaces, setExpandedNamespaces] = useState<Set<string>>(new Set());
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
    lastAdvancedCursorRef.current = null;
    setExpandedNamespaces(new Set());
    setSelectedKeys(new Set());
    if (resolvedCacheId) {
      saveViewPreference(`redis-last-pattern:${resolvedCacheId}`, newPattern);
    }
  }, [resolvedCacheId]);

  // Each cache remembers its own last applied pattern — switching back restores
  // the filter the operator was actually using there rather than a global "*".
  const restorePattern = useCallback((cacheId: string) => {
    const saved = loadViewPreference<string>(`redis-last-pattern:${cacheId}`, "*");
    setSearchInput(saved);
    setPattern(saved);
  }, []);

  // First resolve of the active cache (profile load, or the persisted
  // activeCacheId landing) restores that cache's pattern; explicit switches go
  // through handleCacheChange's own restore so the pattern updates in the same
  // commit as the cache id.
  const patternRestoredRef = useRef(false);
  useEffect(() => {
    if (!resolvedCacheId || patternRestoredRef.current) return;
    patternRestoredRef.current = true;
    restorePattern(resolvedCacheId);
  }, [resolvedCacheId, restorePattern]);

  const handleSearch = () => applySearchPattern(searchInput);

  // Drill-through target for the Prefix/Ops panels, mirroring Keyspace's existing
  // onOpenKey-then-switch-tab pattern.
  const openPrefixInKeys = useCallback(
    (prefix: string) => {
      applySearchPattern(`${prefix}${separator}*`);
      setActiveTab("keys");
    },
    [applySearchPattern, separator, setActiveTab],
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

  // "Load all" walks the cursor one page at a time. It used to key off `scanResult.data` identity,
  // which went `undefined` the moment `handleLoadMore` changed the cursor (and therefore the query
  // key) — so the effect immediately re-ran, hit the `!scanResult.data` branch and switched itself
  // off after exactly one extra page, while the button had already flipped back from "Loading all…".
  //
  // Now that `useRedisScanKeys` keeps the previous page's data during a fetch, `data` stays defined
  // but is briefly the *previous* page, so advancing on data identity alone would re-append the same
  // keys. Both conditions below are load-bearing: wait for the fetch to settle, then advance at most
  // once per distinct cursor.
  const lastAdvancedCursorRef = useRef<number | null>(null);
  useEffect(() => {
    if (!loadAllActive) return;
    if (scanResult.isFetching || !scanResult.data) return;

    if (scanResult.data.isComplete) {
      setLoadAllActive(false);
      return;
    }

    if (lastAdvancedCursorRef.current === scanResult.data.cursor) return;
    lastAdvancedCursorRef.current = scanResult.data.cursor;
    handleLoadMore();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [loadAllActive, scanResult.isFetching, scanResult.data]);

  const scanKeys = useMemo(() => scanResult.data?.keys ?? [], [scanResult.data?.keys]);
  const displayKeys = useMemo(
    () => (cursor === 0 ? scanKeys : allKeys.length > 0 ? [...allKeys, ...scanKeys] : scanKeys),
    [cursor, scanKeys, allKeys],
  );
  // Gated on their own tabs. Each POSTs up to 500 keys and the sidecar reads full metadata for every
  // one of them, so leaving these enabled on `displayKeys.length > 0` meant that simply browsing the
  // Keys tab fired both sweeps on every scan page and every "Load more" — thousands of Redis commands
  // for two panels nobody was looking at.
  const health = useRedisKeyspaceHealth(resolvedCacheId, displayKeys, separator, {
    enabled: activeTab === "keyspace",
  });
  const prefixMemory = useRedisPrefixMemory(resolvedCacheId, displayKeys, separator, {
    enabled: activeTab === "prefix",
  });

  const namespaceTree = useMemo(
    () => buildNamespaceTree(displayKeys, separator),
    [displayKeys, separator],
  );

  const flatRedisRows = useMemo(
    () => flattenNamespaceTree(namespaceTree, expandedNamespaces),
    [namespaceTree, expandedNamespaces],
  );

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

  // MAUI toolbar parity: the header checkbox tri-states over the *loaded* key set — checked when
  // all loaded keys are selected, indeterminate for a subset — and toggling it either selects the
  // whole loaded set or clears the selection.
  const allLoadedSelected = displayKeys.length > 0 && displayKeys.every((k) => selectedKeys.has(k));
  const someLoadedSelected =
    !allLoadedSelected && displayKeys.some((k) => selectedKeys.has(k));
  const toggleSelectAllLoaded = () =>
    setSelectedKeys(allLoadedSelected ? new Set() : new Set(displayKeys));

  // Keys per namespace path, computed once per tree — a row computing its own subtree would
  // re-walk it on every virtualizer re-render (i.e. every scroll frame).
  const subtreeKeysByPath = useMemo(() => {
    const map = new Map<string, string[]>();
    const walk = (node: NamespaceNode) => {
      map.set(node.path, collectSubtreeKeys(node));
      node.children.forEach(walk);
    };
    namespaceTree.forEach(walk);
    return map;
  }, [namespaceTree]);

  const toggleSubtreeSelection = (node: NamespaceNode) => {
    const keys = subtreeKeysByPath.get(node.path) ?? collectSubtreeKeys(node);
    setSelectedKeys((prev) => {
      const next = new Set(prev);
      if (keys.every((k) => prev.has(k))) keys.forEach((k) => next.delete(k));
      else keys.forEach((k) => next.add(k));
      return next;
    });
  };

  const handleCacheChange = (cacheId: string) => {
    setActiveCacheId(cacheId);
    setCursor(0);
    setAllKeys([]);
    setSelectedKey(null);
    setSelectedKeys(new Set());
    lastAdvancedCursorRef.current = null;
    setExpandedNamespaces(new Set());
    restorePattern(cacheId);
    // The browser's selected cache IS the "active" cache: persisted so the page restores it next
    // visit, and because the agent's Redis tools fall back to it (RedisToolContext). The profile
    // PUT evicts only connection-changed caches, so healthy pooled connections survive the save.
    updateProfile.mutate((prev) =>
      prev.config.redisConfig
        ? {
            ...prev,
            config: {
              ...prev.config,
              redisConfig: { ...prev.config.redisConfig, activeCacheId: cacheId },
            },
          }
        : prev,
    );
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
    setSelectedKeys,
    toggleKeySelection,
    allLoadedSelected,
    someLoadedSelected,
    toggleSelectAllLoaded,
    subtreeKeysByPath,
    toggleSubtreeSelection,
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
