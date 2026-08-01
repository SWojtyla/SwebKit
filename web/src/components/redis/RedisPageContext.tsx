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
import { useLocation, useNavigate } from "react-router-dom";
import { useQueryClient } from "@tanstack/react-query";
import { useVirtualizer, type Virtualizer } from "@tanstack/react-virtual";
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

interface PendingConfirm {
  message: string;
  onConfirm: () => void;
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

  separator: string;
  setSeparator: (v: string) => void;
  namespaceFilter: string | null;
  setNamespaceFilter: (v: string | null) => void;
  expandedNamespaces: Set<string>;
  toggleNamespace: (path: string) => void;

  displayKeys: string[];
  filteredKeys: string[];
  namespaceTree: NamespaceNode[];
  flatRedisRows: FlatRedisRow[];
  redisTreeRef: React.MutableRefObject<HTMLDivElement | null>;
  redisVirtualizer: Virtualizer<HTMLDivElement, Element>;

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
  const caches = redisConfig?.caches ?? [];
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
  const [namespaceFilter, setNamespaceFilter] = useState<string | null>(null);
  const [expandedNamespaces, setExpandedNamespaces] = useState<Set<string>>(new Set());
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
    const id = setInterval(() => {
      queryClient.invalidateQueries({ queryKey: ["redis"] });
    }, refreshInterval * 1000);
    return () => clearInterval(id);
  }, [autoRefresh, refreshInterval, resolvedCacheId, queryClient]);

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

  const handleSearch = () => {
    setPattern(searchInput);
    setCursor(0);
    setAllKeys([]);
    setNamespaceFilter(null);
    setExpandedNamespaces(new Set());
  };

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

  const scanKeys = scanResult.data?.keys ?? [];
  const displayKeys = cursor === 0 ? scanKeys : allKeys.length > 0 ? [...allKeys, ...scanKeys] : scanKeys;
  const health = useRedisKeyspaceHealth(resolvedCacheId, displayKeys, separator);
  const prefixMemory = useRedisPrefixMemory(resolvedCacheId, displayKeys, separator);

  const filteredKeys = useMemo(() => {
    if (!namespaceFilter) return displayKeys;
    return displayKeys.filter((k) => k === namespaceFilter || k.startsWith(namespaceFilter + separator));
  }, [displayKeys, namespaceFilter, separator]);

  const namespaceTree = useMemo(
    () => buildNamespaceTree(filteredKeys, separator),
    [filteredKeys, separator],
  );

  const flatRedisRows = useMemo(
    () => flattenNamespaceTree(namespaceTree, expandedNamespaces),
    [namespaceTree, expandedNamespaces],
  );

  const redisVirtualizer = useVirtualizer({
    count: flatRedisRows.length,
    getScrollElement: () => redisTreeRef.current,
    estimateSize: () => 26,
    getItemKey: (index) => redisRowKey(flatRedisRows[index]),
    measureElement: (el) => el?.getBoundingClientRect().height ?? 26,
  });

  useEffect(() => {
    if (namespaceTree.length === 0) return;
    const allPaths = new Set<string>();
    const collect = (nodes: NamespaceNode[]) => {
      for (const node of nodes) {
        allPaths.add(node.path);
        collect([...node.children.values()]);
      }
    };
    collect(namespaceTree);
    setExpandedNamespaces((prev) => (prev.size === 0 ? allPaths : prev));
  }, [namespaceTree]);

  const toggleNamespace = (path: string) => {
    setExpandedNamespaces((prev) => {
      const next = new Set(prev);
      if (next.has(path)) next.delete(path);
      else next.add(path);
      return next;
    });
  };

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
    setNamespaceFilter(null);
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

    separator,
    setSeparator,
    namespaceFilter,
    setNamespaceFilter,
    expandedNamespaces,
    toggleNamespace,

    displayKeys,
    filteredKeys,
    namespaceTree,
    flatRedisRows,
    redisTreeRef,
    redisVirtualizer,

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
