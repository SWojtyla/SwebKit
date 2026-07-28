import { useState, useMemo, useEffect, useCallback } from "react";
import { useLocation, useNavigate } from "react-router-dom";
import { useQueryClient } from "@tanstack/react-query";
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
} from "@/lib/hooks";
import { formatTtl, parseTtl, getTtlColorClass } from "@/lib/redis-format";
import { ConfirmBar } from "@/components/shared/ConfirmBar";
import { Copy, Pencil, Check, X, Clock, Trash2, RefreshCw, ChevronRight, ChevronDown, Plus } from "lucide-react";
import { KeyspaceHealthPanel, PrefixMemoryPanel, OpsInsightsPanel } from "./AdvancedPanels";
import { PubSubPanel } from "./PubSubPanel";

const typeColors: Record<string, string> = {
  string: "text-green-400",
  hash: "text-blue-400",
  list: "text-yellow-400",
  set: "text-purple-400",
  zset: "text-orange-400",
  none: "text-muted-foreground",
};

const mainTabs = [
  { id: "keys", label: "Keys" },
  { id: "info", label: "Server Info" },
  { id: "slowlog", label: "Slow Log" },
  { id: "keyspace", label: "Keyspace" },
  { id: "prefix", label: "Prefixes" },
  { id: "ops", label: "Ops" },
  { id: "pubsub", label: "Pub/Sub" },
] as const;
type TabId = (typeof mainTabs)[number]["id"];

function formatBytes(bytes: number | null | undefined): string {
  if (bytes == null) return "-";
  if (bytes < 1024) return `${bytes}B`;
  if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)}K`;
  return `${(bytes / (1024 * 1024)).toFixed(1)}M`;
}

export function RedisPage() {
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
  const [namespaceFilter, setNamespaceFilter] = useState<string | null>(null);
  const [autoRefresh, setAutoRefresh] = useState(false);
  const [refreshInterval, setRefreshInterval] = useState(10);
  const [expandedNamespaces, setExpandedNamespaces] = useState<Set<string>>(new Set());
  const [namespaceVisibleCounts, setNamespaceVisibleCounts] = useState<Record<string, number>>({});
  const [pendingConfirm, setPendingConfirm] = useState<{ message: string; onConfirm: () => void } | null>(null);
  const [hashAdding, setHashAdding] = useState(false);
  const [newHashField, setNewHashField] = useState("");
  const [newHashValue, setNewHashValue] = useState("");
  const [hashEditingField, setHashEditingField] = useState<string | null>(null);
  const [hashEditFieldName, setHashEditFieldName] = useState("");
  const [hashEditValue, setHashEditValue] = useState("");
  const [zsetEditingMember, setZsetEditingMember] = useState<string | null>(null);
  const [zsetEditScore, setZsetEditScore] = useState("");

  const namespaceSeparator = redisConfig?.namespaceSeparator?.trim() || ":";
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
  };

  const handleLoadMore = () => {
    if (scanResult.data && !scanResult.data.isComplete) {
      setAllKeys((prev) => [...prev, ...scanResult.data!.keys]);
      setCursor(scanResult.data.cursor);
    }
  };

  const scanKeys = scanResult.data?.keys ?? [];
  const displayKeys = cursor === 0 ? scanKeys : allKeys.length > 0 ? [...allKeys, ...scanKeys] : scanKeys;

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

  // Build namespace tree from displayed keys using the configured separator
  const namespaceTree = useMemo(() => {
    const tree: Record<string, string[]> = {};
    if (!namespaceSeparator) return tree;
    for (const key of displayKeys) {
      const idx = key.indexOf(namespaceSeparator);
      if (idx !== -1) {
        const ns = key.slice(0, idx);
        if (!tree[ns]) tree[ns] = [];
        tree[ns].push(key);
      }
    }
    return tree;
  }, [displayKeys, namespaceSeparator]);

  const getNamespaceVisibleCount = (ns: string) => namespaceVisibleCounts[ns] ?? 20;
  const showMoreNamespace = (ns: string) => {
    setNamespaceVisibleCounts((prev) => ({
      ...prev,
      [ns]: (prev[ns] ?? 20) + 20,
    }));
  };

  const filteredKeys = namespaceFilter
    ? displayKeys.filter((k) => k === namespaceFilter || k.startsWith(namespaceFilter + namespaceSeparator))
    : displayKeys;

  const toggleNamespace = (ns: string) => {
    setExpandedNamespaces((prev) => {
      const next = new Set(prev);
      if (next.has(ns)) next.delete(ns);
      else next.add(ns);
      return next;
    });
  };

  if (!resolvedCacheId) {
    return (
      <div className="p-6" data-testid="redis-page">
        <h1 className="text-2xl font-bold" data-testid="redis-title">Redis</h1>
        <p className="mt-4 text-muted-foreground" data-testid="redis-no-cache">
          No Redis cache configured. Add one in Settings.
        </p>
      </div>
    );
  }

  return (
    <div className="flex h-full flex-col" data-testid="redis-page">
      {/* Connection bar */}
      <div className="flex items-center gap-4 border-b px-6 py-2">
        <h1 className="text-lg font-bold shrink-0" data-testid="redis-title">Redis</h1>
        {caches.length > 0 && (
          <select
            data-testid="redis-cache-select"
            className="rounded-md border bg-card px-3 py-1.5 text-sm"
            value={resolvedCacheId}
            onChange={(e) => {
              setActiveCacheId(e.target.value);
              setCursor(0);
              setAllKeys([]);
              setSelectedKey(null);
            }}
          >
            {caches.map((c) => (
              <option key={c.id} value={c.id}>{c.displayName}</option>
            ))}
          </select>
        )}
        {serverInfo.data && (
          <span className="flex items-center gap-1.5 text-xs text-green-500" data-testid="redis-connection-status">
            <span className="h-2 w-2 rounded-full bg-green-500" />
            Connected
          </span>
        )}
        <div className="ml-auto flex items-center gap-3">
          <label className="flex items-center gap-1.5 text-xs" data-testid="redis-auto-refresh">
            <Clock className="h-3.5 w-3.5" />
            <input
              type="checkbox"
              checked={autoRefresh}
              onChange={(e) => setAutoRefresh(e.target.checked)}
              data-testid="redis-auto-refresh-checkbox"
            />
            <span>Auto</span>
          </label>
          {autoRefresh && (
            <select
              value={refreshInterval}
              onChange={(e) => setRefreshInterval(Number(e.target.value))}
              className="rounded-md border bg-card px-2 py-1 text-xs"
              data-testid="redis-refresh-interval"
            >
              <option value={5}>5s</option>
              <option value={10}>10s</option>
              <option value={30}>30s</option>
              <option value={60}>60s</option>
            </select>
          )}
          <button
            onClick={handleManualRefresh}
            className="flex items-center gap-1 rounded-md border px-2 py-1 text-xs hover:bg-accent"
            data-testid="redis-refresh-btn"
          >
            <RefreshCw className="h-3.5 w-3.5" />
            Refresh
          </button>
        </div>
      </div>

      <div className="flex gap-1 border-b px-6" data-testid="redis-tabs">
        {mainTabs.map((tab) => (
          <button
            key={tab.id}
            data-testid={`redis-tab-${tab.id}`}
            onClick={() => setActiveTab(tab.id)}
            className={`px-4 py-2 text-sm font-medium transition-colors ${
              activeTab === tab.id
                ? "border-b-2 border-primary text-primary"
                : "text-muted-foreground hover:text-foreground"
            }`}
          >
            {tab.label}
          </button>
        ))}
      </div>

      {pendingConfirm && (
        <ConfirmBar
          message={pendingConfirm.message}
          onConfirm={() => {
            pendingConfirm.onConfirm();
            setPendingConfirm(null);
          }}
          onCancel={() => setPendingConfirm(null)}
          confirmLabel="Delete"
          testId="redis-confirm-bar"
          confirmTestId="redis-confirm-yes"
          cancelTestId="redis-confirm-cancel"
        />
      )}

      <div className="flex flex-1 overflow-hidden">
        {activeTab === "keys" && (
          <>
            <div className="w-1/3 border-r overflow-hidden flex flex-col" data-testid="redis-key-browser">
              <div className="p-3 border-b">
                <div className="flex gap-2">
                  <input
                    type="text"
                    data-testid="redis-key-search"
                    value={searchInput}
                    onChange={(e) => setSearchInput(e.target.value)}
                    onKeyDown={(e) => e.key === "Enter" && handleSearch()}
                    placeholder="Pattern (e.g. user:*)"
                    className="flex-1 rounded-md border bg-card px-3 py-1.5 text-sm"
                  />
                  <button
                    data-testid="redis-key-search-btn"
                    onClick={handleSearch}
                    className="rounded-md bg-primary px-3 py-1.5 text-sm text-primary-foreground hover:opacity-90"
                  >
                    Search
                  </button>
                </div>
                <div className="mt-2 flex items-center gap-2">
                  <button
                    onClick={() => { setBatchMode(!batchMode); setSelectedKeys(new Set()); }}
                    className={`rounded border px-2 py-1 text-xs ${batchMode ? "bg-primary text-primary-foreground" : "hover:bg-accent"}`}
                    data-testid="redis-batch-toggle"
                  >
                    {batchMode ? "Exit Batch" : "Batch Select"}
                  </button>
                  {batchMode && selectedKeys.size > 0 && (
                    <>
                      <span className="text-xs text-muted-foreground" data-testid="redis-batch-count">{selectedKeys.size} selected</span>
                      <button onClick={handleExportSelected} className="rounded border px-2 py-1 text-xs hover:bg-accent" data-testid="redis-batch-export">Export JSON</button>
                      <button onClick={handleBatchDelete} className="rounded border border-destructive px-2 py-1 text-xs text-destructive hover:bg-destructive/10" data-testid="redis-batch-delete">Delete</button>
                      <button onClick={() => setSelectedKeys(new Set())} className="text-xs text-muted-foreground" data-testid="redis-batch-clear">Clear</button>
                    </>
                  )}
                </div>
              </div>

              {/* Namespace tree with expand/collapse */}
              {Object.keys(namespaceTree).length > 0 && (
                <div className="border-b p-2" data-testid="redis-namespace-tree">
                  <div className="mb-1 text-xs font-medium text-muted-foreground">Namespaces</div>
                  <div className="space-y-0.5">
                    <button
                      onClick={() => setNamespaceFilter(null)}
                      className={`flex w-full items-center rounded px-2 py-0.5 text-xs ${!namespaceFilter ? "bg-primary text-primary-foreground" : "hover:bg-accent"}`}
                    >
                      All ({displayKeys.length})
                    </button>
                    {Object.entries(namespaceTree).map(([ns, keys]) => {
                      const visibleCount = getNamespaceVisibleCount(ns);
                      return (
                        <div key={ns}>
                          <div className="flex items-center">
                            <button
                              onClick={() => toggleNamespace(ns)}
                              className="p-0.5 text-muted-foreground hover:text-foreground"
                              data-testid={`redis-namespace-toggle-${ns}`}
                            >
                              {expandedNamespaces.has(ns) ? <ChevronDown className="h-3 w-3" /> : <ChevronRight className="h-3 w-3" />}
                            </button>
                            <button
                              onClick={() => setNamespaceFilter(namespaceFilter === ns ? null : ns)}
                              className={`flex-1 rounded px-2 py-0.5 text-left text-xs font-mono ${namespaceFilter === ns ? "bg-primary text-primary-foreground" : "hover:bg-accent"}`}
                              data-testid={`redis-namespace-${ns}`}
                            >
                              {ns} ({keys.length})
                            </button>
                          </div>
                          {expandedNamespaces.has(ns) && (
                            <div className="ml-5 border-l pl-1" data-testid={`redis-namespace-children-${ns}`}>
                              {keys.slice(0, visibleCount).map((k) => (
                                <button
                                  key={k}
                                  onClick={() => setSelectedKey(k)}
                                  className={`block w-full truncate rounded px-2 py-0.5 text-left text-xs font-mono ${selectedKey === k ? "bg-accent" : "hover:bg-accent"}`}
                                  data-testid={`redis-namespace-key-${k}`}
                                >
                                  {k.slice(k.indexOf(namespaceSeparator) + namespaceSeparator.length) || k}
                                </button>
                              ))}
                              {keys.length > visibleCount && (
                                <button
                                  onClick={() => showMoreNamespace(ns)}
                                  className="px-2 py-0.5 text-left text-xs text-primary hover:text-foreground"
                                  data-testid={`redis-namespace-show-more-${ns}`}
                                >
                                  +{Math.min(20, keys.length - visibleCount)} more
                                </button>
                              )}
                            </div>
                          )}
                        </div>
                      );
                    })}
                  </div>
                </div>
              )}

              <div className="flex-1 overflow-auto">
                {scanResult.isLoading && (
                  <div className="p-3 text-sm text-muted-foreground">Loading keys...</div>
                )}
                {scanResult.error && (
                  <div className="p-3 text-sm text-destructive" data-testid="redis-key-error">
                    Error: {scanResult.error.message}
                  </div>
                )}
                {displayKeys.length === 0 && !scanResult.isLoading && (
                  <div className="p-3 text-sm text-muted-foreground">No keys found</div>
                )}
                {filteredKeys.map((key) => (
                  <div
                    key={key}
                    data-testid={`redis-key-${key}`}
                    onClick={() => batchMode ? toggleKeySelection(key) : setSelectedKey(key)}
                    className={`flex w-full items-center gap-2 px-3 py-1.5 text-left text-sm transition-colors hover:bg-accent cursor-pointer ${
                      batchMode ? selectedKeys.has(key) ? "bg-primary/20" : "" : selectedKey === key ? "bg-accent" : ""
                    }`}
                  >
                    {batchMode && (
                      <input
                        type="checkbox"
                        checked={selectedKeys.has(key)}
                        onChange={() => toggleKeySelection(key)}
                        onClick={(e) => e.stopPropagation()}
                        className="h-3.5 w-3.5"
                        data-testid={`redis-key-checkbox-${key}`}
                      />
                    )}
                    <span className="truncate font-mono">{key}</span>
                  </div>
                ))}
                {scanResult.data && !scanResult.data.isComplete && (
                  <button
                    data-testid="redis-load-more"
                    onClick={handleLoadMore}
                    className="w-full px-3 py-2 text-sm text-primary hover:bg-accent"
                  >
                    Load more...
                  </button>
                )}
              </div>
            </div>

            <div className="flex-1 overflow-auto" data-testid="redis-key-detail">
              {!selectedKey ? (
                <div className="flex h-full items-center justify-center text-muted-foreground" data-testid="redis-no-key-selected">
                  Select a key to view details
                </div>
              ) : keyInfo.isLoading ? (
                <div className="p-6 text-sm text-muted-foreground">Loading key info...</div>
              ) : keyInfo.error ? (
                <div className="p-6 text-sm text-destructive">Error: {keyInfo.error.message}</div>
              ) : keyInfo.data ? (
                <div className="p-6 space-y-4">
                  <div className="flex items-center justify-between">
                    <div className="flex-1 min-w-0">
                      {renaming ? (
                        <div className="flex items-center gap-2">
                          <input
                            type="text"
                            value={renameValue}
                            onChange={(e) => setRenameValue(e.target.value)}
                            onKeyDown={(e) => e.key === "Enter" && handleRenameKey(keyInfo.data.key)}
                            className="rounded border bg-card px-2 py-1 text-sm font-mono flex-1"
                            autoFocus
                            data-testid="redis-rename-input"
                          />
                          <button onClick={() => handleRenameKey(keyInfo.data.key)} className="rounded bg-primary p-1 text-primary-foreground" data-testid="redis-rename-confirm">
                            <Check className="h-3.5 w-3.5" />
                          </button>
                          <button onClick={() => setRenaming(false)} className="rounded border p-1" data-testid="redis-rename-cancel">
                            <X className="h-3.5 w-3.5" />
                          </button>
                        </div>
                      ) : (
                        <div className="flex items-center gap-2">
                          <div className="text-lg font-mono font-semibold truncate" data-testid="redis-detail-key-name">
                            {keyInfo.data.key}
                          </div>
                          <button onClick={() => handleCopyKey(keyInfo.data.key)} className="text-muted-foreground hover:text-foreground" data-testid="redis-copy-key-btn" title="Copy key name">
                            <Copy className="h-3.5 w-3.5" />
                          </button>
                          <button onClick={() => { setRenaming(true); setRenameValue(keyInfo.data.key); }} className="text-muted-foreground hover:text-foreground" data-testid="redis-rename-btn" title="Rename key">
                            <Pencil className="h-3.5 w-3.5" />
                          </button>
                        </div>
                      )}
                      <div className="mt-1 flex items-center gap-3 text-sm">
                        <span className={`font-medium ${typeColors[keyInfo.data.type] ?? ""}`} data-testid="redis-detail-key-type">
                          {keyInfo.data.type}
                        </span>
                        <span className="text-muted-foreground" data-testid="redis-detail-key-ttl">
                          TTL: {formatTtl(keyInfo.data.ttl)}
                        </span>
                        <span className="text-muted-foreground" data-testid="redis-detail-key-memory">
                          {formatBytes(keyInfo.data.memoryBytes)}
                        </span>
                        {keyInfo.data.encoding && (
                          <span className="text-muted-foreground">enc: {keyInfo.data.encoding}</span>
                        )}
                      </div>
                      {/* TTL bar */}
                      {keyInfo.data.ttl && keyInfo.data.type !== "none" && (
                        <TtlBar ttl={keyInfo.data.ttl} />
                      )}
                      {/* TTL controls */}
                      {showTtlEditor ? (
                        <div className="mt-2 flex items-center gap-2" data-testid="redis-ttl-editor">
                          <input
                            type="number"
                            value={ttlSeconds}
                            onChange={(e) => setTtlSeconds(parseInt(e.target.value) || 0)}
                            placeholder="seconds"
                            className="w-24 rounded border bg-card px-2 py-1 text-xs"
                            autoFocus
                          />
                          <button onClick={() => handleSetTtl(keyInfo.data.key)} className="rounded bg-primary px-2 py-1 text-xs text-primary-foreground" data-testid="redis-ttl-set-btn">Set TTL</button>
                          <button onClick={() => handleRemoveTtl(keyInfo.data.key)} className="rounded border px-2 py-1 text-xs" data-testid="redis-ttl-remove-btn">Remove TTL</button>
                          <button onClick={() => setShowTtlEditor(false)} className="text-xs text-muted-foreground">Cancel</button>
                        </div>
                      ) : (
                        <button onClick={() => { setShowTtlEditor(true); setTtlSeconds(3600); }} className="mt-2 flex items-center gap-1 text-xs text-muted-foreground hover:text-foreground" data-testid="redis-ttl-edit-btn">
                          <Clock className="h-3 w-3" /> Set TTL
                        </button>
                      )}
                    </div>
                    <button
                      data-testid="redis-delete-key-btn"
                      onClick={() => requestDeleteKey(keyInfo.data.key)}
                      disabled={deleteKey.isPending}
                      className="flex items-center gap-1 rounded-md border border-destructive px-3 py-1.5 text-sm text-destructive hover:bg-destructive/10"
                    >
                      <Trash2 className="h-3.5 w-3.5" /> Delete
                    </button>
                  </div>

                  {keyInfo.data.type === "string" && (
                    <div>
                      <div className="mb-2 flex items-center justify-between">
                        <h3 className="text-sm font-semibold">Value</h3>
                        {editingValue ? (
                          <div className="flex items-center gap-2">
                            <button onClick={() => handleSaveStringValue(keyInfo.data.key)} disabled={setValue.isPending} className="rounded bg-primary px-2 py-1 text-xs text-primary-foreground" data-testid="redis-string-save-btn">
                              Save
                            </button>
                            <button onClick={() => setEditingValue(false)} className="rounded border px-2 py-1 text-xs" data-testid="redis-string-cancel-btn">
                              Cancel
                            </button>
                          </div>
                        ) : (
                          <button onClick={() => { setEditingValue(true); setStringValue(keyValue.data?.value ?? ""); }} className="flex items-center gap-1 text-xs text-muted-foreground hover:text-foreground" data-testid="redis-string-edit-btn">
                            <Pencil className="h-3 w-3" /> Edit
                          </button>
                        )}
                      </div>
                      {editingValue ? (
                        <textarea
                          value={stringValue}
                          onChange={(e) => setStringValue(e.target.value)}
                          className="w-full rounded-md border bg-card p-4 text-sm font-mono max-h-96 min-h-48"
                          data-testid="redis-detail-string-edit"
                          autoFocus
                        />
                      ) : (
                        <pre
                          className="rounded-md border bg-muted p-4 text-sm font-mono overflow-auto max-h-96"
                          data-testid="redis-detail-string-value"
                        >
                          {keyValue.data?.value ?? "(empty)"}
                        </pre>
                      )}
                    </div>
                  )}

                  {keyInfo.data.type === "hash" && (
                    <div>
                      <div className="mb-2 flex items-center justify-between">
                        <h3 className="text-sm font-semibold">Hash Fields</h3>
                        {!hashAdding && (
                          <button
                            onClick={() => setHashAdding(true)}
                            className="flex items-center gap-1 rounded border px-2 py-1 text-xs hover:bg-accent"
                            data-testid="redis-hash-add-btn"
                          >
                            <Plus className="h-3 w-3" /> Add field
                          </button>
                        )}
                      </div>
                      <div className="rounded-md border overflow-hidden" data-testid="redis-detail-hash-fields">
                        <table className="w-full text-sm">
                          <thead className="bg-muted">
                            <tr>
                              <th className="px-3 py-2 text-left font-medium">Field</th>
                              <th className="px-3 py-2 text-left font-medium">Value</th>
                              <th className="px-3 py-2 text-left font-medium w-24">Actions</th>
                            </tr>
                          </thead>
                          <tbody>
                            {hashAdding && (
                              <tr className="border-t">
                                <td className="px-3 py-2">
                                  <input
                                    type="text"
                                    value={newHashField}
                                    onChange={(e) => setNewHashField(e.target.value)}
                                    placeholder="field"
                                    className="w-full rounded border bg-card px-2 py-1 text-xs font-mono"
                                    data-testid="redis-hash-new-field"
                                    autoFocus
                                  />
                                </td>
                                <td className="px-3 py-2">
                                  <input
                                    type="text"
                                    value={newHashValue}
                                    onChange={(e) => setNewHashValue(e.target.value)}
                                    placeholder="value"
                                    className="w-full rounded border bg-card px-2 py-1 text-xs font-mono"
                                    data-testid="redis-hash-new-value"
                                  />
                                </td>
                                <td className="px-3 py-2">
                                  <div className="flex items-center gap-1">
                                    <button
                                      onClick={() => handleAddHashField(keyInfo.data.key)}
                                      className="rounded bg-primary p-1 text-primary-foreground"
                                      data-testid="redis-hash-new-save"
                                    >
                                      <Check className="h-3 w-3" />
                                    </button>
                                    <button
                                      onClick={() => setHashAdding(false)}
                                      className="rounded border p-1"
                                      data-testid="redis-hash-new-cancel"
                                    >
                                      <X className="h-3 w-3" />
                                    </button>
                                  </div>
                                </td>
                              </tr>
                            )}
                            {hashFields.data?.map((f) => (
                              <tr key={f.field} className="border-t">
                                {hashEditingField === f.field ? (
                                  <>
                                    <td className="px-3 py-2">
                                      <input
                                        type="text"
                                        value={hashEditFieldName}
                                        onChange={(e) => setHashEditFieldName(e.target.value)}
                                        onKeyDown={(e) => e.key === "Enter" && handleSaveHashField(keyInfo.data.key, f.field)}
                                        className="w-full rounded border bg-card px-2 py-1 text-xs font-mono"
                                        data-testid={`redis-hash-edit-field-${f.field}`}
                                        autoFocus
                                      />
                                    </td>
                                    <td className="px-3 py-2">
                                      <input
                                        type="text"
                                        value={hashEditValue}
                                        onChange={(e) => setHashEditValue(e.target.value)}
                                        onKeyDown={(e) => e.key === "Enter" && handleSaveHashField(keyInfo.data.key, f.field)}
                                        className="w-full rounded border bg-card px-2 py-1 text-xs font-mono"
                                        data-testid={`redis-hash-edit-value-${f.field}`}
                                      />
                                    </td>
                                    <td className="px-3 py-2">
                                      <div className="flex items-center gap-1">
                                        <button
                                          onClick={() => handleSaveHashField(keyInfo.data.key, f.field)}
                                          className="rounded bg-primary p-1 text-primary-foreground"
                                          data-testid={`redis-hash-save-${f.field}`}
                                        >
                                          <Check className="h-3 w-3" />
                                        </button>
                                        <button
                                          onClick={() => setHashEditingField(null)}
                                          className="rounded border p-1"
                                          data-testid={`redis-hash-cancel-${f.field}`}
                                        >
                                          <X className="h-3 w-3" />
                                        </button>
                                      </div>
                                    </td>
                                  </>
                                ) : (
                                  <>
                                    <td className="px-3 py-2 font-mono">{f.field}</td>
                                    <td className="px-3 py-2 font-mono break-all">{f.value}</td>
                                    <td className="px-3 py-2">
                                      <div className="flex items-center gap-1">
                                        <button
                                          onClick={() => { setHashEditingField(f.field); setHashEditFieldName(f.field); setHashEditValue(f.value); }}
                                          className="text-muted-foreground hover:text-foreground"
                                          data-testid={`redis-hash-edit-${f.field}`}
                                          title="Edit field"
                                        >
                                          <Pencil className="h-3.5 w-3.5" />
                                        </button>
                                        <button
                                          onClick={() => requestDeleteHashField(keyInfo.data.key, f.field)}
                                          className="text-destructive hover:text-destructive/80"
                                          data-testid={`redis-hash-delete-${f.field}`}
                                          title="Delete field"
                                        >
                                          <Trash2 className="h-3.5 w-3.5" />
                                        </button>
                                      </div>
                                    </td>
                                  </>
                                )}
                              </tr>
                            ))}
                            {(!hashFields.data || hashFields.data.length === 0) && !hashAdding && (
                              <tr><td colSpan={3} className="px-3 py-4 text-center text-muted-foreground">No fields</td></tr>
                            )}
                          </tbody>
                        </table>
                      </div>
                    </div>
                  )}

                  {keyInfo.data.type === "list" && (
                    <div>
                      <div className="mb-2 flex items-center justify-between">
                        <h3 className="text-sm font-semibold">List Items</h3>
                        {listItemsQuery.hasNextPage && (
                          <button
                            onClick={() => listItemsQuery.fetchNextPage()}
                            disabled={listItemsQuery.isFetchingNextPage}
                            className="flex items-center gap-1 rounded border px-2 py-1 text-xs hover:bg-accent disabled:opacity-50"
                            data-testid="redis-list-load-more"
                          >
                            {listItemsQuery.isFetchingNextPage ? "Loading..." : "Load more"}
                          </button>
                        )}
                      </div>
                      <div className="rounded-md border overflow-hidden" data-testid="redis-detail-list-items">
                        <table className="w-full text-sm">
                          <thead className="bg-muted">
                            <tr>
                              <th className="px-3 py-2 text-left font-medium w-16">#</th>
                              <th className="px-3 py-2 text-left font-medium">Value</th>
                            </tr>
                          </thead>
                          <tbody>
                            {listItems.map((item, i) => (
                              <tr key={i} className="border-t">
                                <td className="px-3 py-2 text-muted-foreground">{i}</td>
                                <td className="px-3 py-2 font-mono break-all">{item}</td>
                              </tr>
                            ))}
                            {listItems.length === 0 && (
                              <tr><td colSpan={2} className="px-3 py-4 text-center text-muted-foreground">No items</td></tr>
                            )}
                          </tbody>
                        </table>
                      </div>
                    </div>
                  )}

                  {keyInfo.data.type === "set" && (
                    <div>
                      <div className="mb-2 flex items-center justify-between">
                        <h3 className="text-sm font-semibold">Set Members</h3>
                        {setMembersQuery.hasNextPage && (
                          <button
                            onClick={() => setMembersQuery.fetchNextPage()}
                            disabled={setMembersQuery.isFetchingNextPage}
                            className="flex items-center gap-1 rounded border px-2 py-1 text-xs hover:bg-accent disabled:opacity-50"
                            data-testid="redis-set-load-more"
                          >
                            {setMembersQuery.isFetchingNextPage ? "Loading..." : "Load more"}
                          </button>
                        )}
                      </div>
                      <div className="rounded-md border p-3" data-testid="redis-detail-set-members">
                        {setMembers.length ? (
                          <div className="flex flex-wrap gap-2">
                            {setMembers.map((m) => (
                              <span key={m} className="rounded bg-muted px-2 py-1 text-sm font-mono">{m}</span>
                            ))}
                          </div>
                        ) : (
                          <span className="text-sm text-muted-foreground">No members</span>
                        )}
                      </div>
                    </div>
                  )}

                  {keyInfo.data.type === "zset" && (
                    <div>
                      <h3 className="mb-2 text-sm font-semibold">Sorted Set Members</h3>
                      <div className="rounded-md border overflow-hidden" data-testid="redis-detail-zset-members">
                        <table className="w-full text-sm">
                          <thead className="bg-muted">
                            <tr>
                              <th className="px-3 py-2 text-left font-medium">Member</th>
                              <th className="px-3 py-2 text-right font-medium w-32">Score</th>
                            </tr>
                          </thead>
                          <tbody>
                            {sortedSetMembers.data?.map((m) => (
                              <tr key={m.member} className="border-t">
                                <td className="px-3 py-2 font-mono">{m.member}</td>
                                <td className="px-3 py-2 text-right">
                                  {zsetEditingMember === m.member ? (
                                    <div className="flex items-center justify-end gap-2">
                                      <input
                                        type="number"
                                        value={zsetEditScore}
                                        onChange={(e) => setZsetEditScore(e.target.value)}
                                        onKeyDown={(e) => e.key === "Enter" && handleSaveZsetScore(keyInfo.data.key, m.member)}
                                        className="w-24 rounded border bg-card px-2 py-1 text-xs font-mono text-right"
                                        data-testid={`redis-zset-score-input-${m.member}`}
                                        autoFocus
                                      />
                                      <button
                                        onClick={() => handleSaveZsetScore(keyInfo.data.key, m.member)}
                                        className="rounded bg-primary p-1 text-primary-foreground"
                                        data-testid={`redis-zset-score-save-${m.member}`}
                                      >
                                        <Check className="h-3 w-3" />
                                      </button>
                                      <button
                                        onClick={() => setZsetEditingMember(null)}
                                        className="rounded border p-1"
                                        data-testid={`redis-zset-score-cancel-${m.member}`}
                                      >
                                        <X className="h-3 w-3" />
                                      </button>
                                    </div>
                                  ) : (
                                    <button
                                      onClick={() => { setZsetEditingMember(m.member); setZsetEditScore(String(m.score)); }}
                                      className="font-mono hover:underline"
                                      data-testid={`redis-zset-score-${m.member}`}
                                    >
                                      {m.score}
                                    </button>
                                  )}
                                </td>
                              </tr>
                            ))}
                            {(!sortedSetMembers.data || sortedSetMembers.data.length === 0) && (
                              <tr><td colSpan={2} className="px-3 py-4 text-center text-muted-foreground">No members</td></tr>
                            )}
                          </tbody>
                        </table>
                      </div>
                    </div>
                  )}

                  {keyInfo.data.type === "none" && (
                    <div className="text-sm text-muted-foreground" data-testid="redis-detail-key-not-found">
                      Key does not exist
                    </div>
                  )}
                </div>
              ) : null}
            </div>
          </>
        )}

        {activeTab === "info" && (
          <div className="flex-1 overflow-auto p-6" data-testid="redis-server-info">
            {serverInfo.isLoading && <div className="text-sm text-muted-foreground">Loading server info...</div>}
            {serverInfo.error && <div className="text-sm text-destructive">Error: {serverInfo.error.message}</div>}
            {serverInfo.data && (
              <div className="space-y-6">
                <div>
                  <h2 className="text-lg font-semibold mb-3">Server Overview</h2>
                  <div className="grid grid-cols-2 gap-4 sm:grid-cols-3">
                    <InfoCard label="Version" value={serverInfo.data.redisVersion} testId="redis-info-version" />
                    <InfoCard label="Uptime" value={`${Math.floor(serverInfo.data.uptimeSeconds / 3600)}h ${Math.floor((serverInfo.data.uptimeSeconds % 3600) / 60)}m`} testId="redis-info-uptime" />
                    <InfoCard label="Connected Clients" value={String(serverInfo.data.connectedClients)} testId="redis-info-clients" />
                    <InfoCard label="Used Memory" value={serverInfo.data.usedMemoryHuman} testId="redis-info-memory" />
                    <InfoCard label="Max Memory" value={serverInfo.data.maxMemoryBytes > 0 ? formatBytes(serverInfo.data.maxMemoryBytes) : "No limit"} testId="redis-info-maxmemory" />
                    <InfoCard label="Commands Processed" value={String(serverInfo.data.totalCommandsProcessed)} testId="redis-info-commands" />
                    <InfoCard label="Hit Ratio" value={`${(serverInfo.data.keyspaceHitRatio * 100).toFixed(1)}%`} testId="redis-info-hit-ratio" />
                  </div>
                </div>

                <div>
                  <h2 className="text-lg font-semibold mb-3">Databases</h2>
                  <div className="rounded-md border overflow-hidden">
                    <table className="w-full text-sm">
                      <thead className="bg-muted">
                        <tr>
                          <th className="px-3 py-2 text-left font-medium">DB</th>
                          <th className="px-3 py-2 text-right font-medium">Keys</th>
                          <th className="px-3 py-2 text-right font-medium">Expires</th>
                          <th className="px-3 py-2 text-right font-medium">Avg TTL</th>
                        </tr>
                      </thead>
                      <tbody>
                        {serverInfo.data.databases.map((db) => (
                          <tr key={db.index} className="border-t">
                            <td className="px-3 py-2">db{db.index}</td>
                            <td className="px-3 py-2 text-right">{db.keys}</td>
                            <td className="px-3 py-2 text-right">{db.expires}</td>
                            <td className="px-3 py-2 text-right">{db.avgTtl > 0 ? `${db.avgTtl}ms` : "-"}</td>
                          </tr>
                        ))}
                        {serverInfo.data.databases.length === 0 && (
                          <tr><td colSpan={4} className="px-3 py-4 text-center text-muted-foreground">No databases</td></tr>
                        )}
                      </tbody>
                    </table>
                  </div>
                </div>
              </div>
            )}
          </div>
        )}

        {activeTab === "slowlog" && (
          <div className="flex-1 overflow-auto p-6" data-testid="redis-slowlog">
            <SlowLogTab cacheId={resolvedCacheId} />
          </div>
        )}

        {activeTab === "keyspace" && (
          <div className="flex-1 overflow-auto p-6" data-testid="redis-keyspace">
            <KeyspaceHealthPanel info={serverInfo.data} />
          </div>
        )}

        {activeTab === "prefix" && (
          <div className="flex-1 overflow-auto p-6" data-testid="redis-prefix">
            <PrefixMemoryPanel
              keys={displayKeys}
              keyTypes={new Map(selectedKey ? [[selectedKey, keyInfo.data?.type ?? "unknown"]] : [])}
              separator={namespaceSeparator}
            />
          </div>
        )}

        {activeTab === "ops" && (
          <div className="flex-1 overflow-auto p-6" data-testid="redis-ops">
            <OpsInsightsPanel info={serverInfo.data} slowLog={slowLog.data} />
          </div>
        )}

        {activeTab === "pubsub" && (
          <div className="flex-1 overflow-auto p-6" data-testid="redis-pubsub">
            <PubSubPanel cacheId={resolvedCacheId} />
          </div>
        )}
      </div>
    </div>
  );
}

function TtlBar({ ttl }: { ttl: string | null }) {
  const ms = parseTtl(ttl);
  if (ms === null || ms <= 0) return null;

  const maxMs = 3600_000;
  const pct = Math.min(100, (ms / maxMs) * 100);
  const colorClass = getTtlColorClass(ms);

  return (
    <div className="mt-2" data-testid="redis-ttl-bar">
      <div className="h-1.5 w-full overflow-hidden rounded-full bg-muted">
        <div className={`h-full ${colorClass} transition-all`} style={{ width: `${pct}%` }} />
      </div>
    </div>
  );
}

function InfoCard({ label, value, testId }: { label: string; value: string; testId: string }) {
  return (
    <div className="rounded-lg border bg-card p-4">
      <div className="text-xs text-muted-foreground">{label}</div>
      <div className="mt-1 text-lg font-semibold" data-testid={testId}>{value}</div>
    </div>
  );
}

function SlowLogTab({ cacheId }: { cacheId: string }) {
  const slowLog = useRedisSlowLog(cacheId);

  if (slowLog.isLoading) return <div className="text-sm text-muted-foreground">Loading slow log...</div>;
  if (slowLog.error) return <div className="text-sm text-destructive">Error: {slowLog.error.message}</div>;
  if (!slowLog.data) return null;

  const entries = slowLog.data.entries ?? [];

  return (
    <div className="space-y-4">
      <div className="flex items-center gap-4 text-sm text-muted-foreground">
        <span data-testid="redis-slowlog-count">{entries.length} entries</span>
        {slowLog.data.truncated && <span>(truncated at {slowLog.data.maxReturned})</span>}
        <span className="capitalize">Capability: {slowLog.data.capability}</span>
      </div>

      {entries.length === 0 ? (
        <div className="text-sm text-muted-foreground">No slow log entries</div>
      ) : (
        <div className="rounded-md border overflow-hidden">
          <table className="w-full text-sm">
            <thead className="bg-muted">
              <tr>
                <th className="px-3 py-2 text-left font-medium">ID</th>
                <th className="px-3 py-2 text-left font-medium">Time</th>
                <th className="px-3 py-2 text-right font-medium">Duration</th>
                <th className="px-3 py-2 text-left font-medium">Command</th>
                <th className="px-3 py-2 text-left font-medium">Args</th>
                <th className="px-3 py-2 text-left font-medium">Client</th>
              </tr>
            </thead>
            <tbody>
              {entries.map((e) => (
                <tr key={e.id} className="border-t">
                  <td className="px-3 py-2 text-muted-foreground">{e.id}</td>
                  <td className="px-3 py-2">{new Date(e.executedAt).toLocaleTimeString()}</td>
                  <td className="px-3 py-2 text-right font-mono">{e.duration}</td>
                  <td className="px-3 py-2 font-mono">{e.command}</td>
                  <td className="px-3 py-2 font-mono text-muted-foreground">{e.arguments}</td>
                  <td className="px-3 py-2 text-muted-foreground">{e.clientName ?? "-"}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </div>
  );
}
