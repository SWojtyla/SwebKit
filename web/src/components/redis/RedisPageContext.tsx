import {
  useCallback,
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
  useRedisDeleteKeys,
  useRedisRenameKey,
  useRedisSetTtl,
  useRedisSetValue,
  useRedisExportKeys,
  useRedisKeyspaceHealth,
  useRedisPrefixMemory,
  useUpdateSearchParams,
} from "@/lib/hooks";
import { loadViewPreference, saveViewPreference } from "@/lib/stores/panel-preferences";
import {
  useInfiniteQueryFacade,
  useMutationFacade,
  useQueryFacade,
} from "@/lib/queryFacade";

import {
  buildNamespaceTree,
  collectAllNamespacePaths,
  collectSubtreeKeys,
  flattenNamespaceTree,
  type NamespaceNode,
} from "./redis-namespace-tree";
import {
  mainTabs,
  RedisBrowserContext,
  RedisConnectionContext,
  RedisEditorContext,
  RedisNavContext,
  RedisOpsContext,
  RedisQueriesContext,
  type PendingConfirm,
  type RedisBrowserValue,
  type RedisConnectionValue,
  type RedisEditorValue,
  type RedisNavValue,
  type RedisOpsValue,
  type RedisQueriesValue,
  type TabId,
} from "./redis-context";

/**
 * The page state is split into six contexts grouped by churn rate, so a change in one
 * bucket only re-renders the components that actually display it. Before the split a
 * single ~85-field context meant every keystroke in a detail-panel editor re-rendered
 * the whole virtualized key tree, and every auto-refresh tick re-rendered everything.
 *
 * Queries travel as facades (lib/queryFacade): `useQuery`/`useMutation` hand back a
 * fresh result object each render, so raw results would defeat every `useMemo` below.
 */
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
      // eslint-disable-next-line react-hooks/set-state-in-effect -- one-shot location.state deep-link consumption; the paired navigate() must live in an effect anyway
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
  const deleteKeys = useRedisDeleteKeys(resolvedCacheId);
  const renameKey = useRedisRenameKey(resolvedCacheId);
  const setTtl = useRedisSetTtl(resolvedCacheId);
  const setValue = useRedisSetValue(resolvedCacheId);
  const exportKeys = useRedisExportKeys(resolvedCacheId);
  const setHashField = useRedisSetHashField(resolvedCacheId);
  const deleteHashField = useRedisDeleteHashField(resolvedCacheId);
  const updateZsetScore = useRedisUpdateSortedSetScore(resolvedCacheId);
  // `mutate`/`mutateAsync` are referentially stable across renders, so handlers can
  // depend on them without churning identity every provider render (the mutation
  // *object* is fresh each render and would defeat the context-value memos).
  const { mutate: deleteKeyMutate } = deleteKey;
  const { mutate: deleteKeysMutate } = deleteKeys;
  const { mutate: renameKeyMutate } = renameKey;
  const { mutate: setTtlMutate } = setTtl;
  const { mutate: setValueMutate } = setValue;
  const { mutateAsync: exportKeysMutateAsync } = exportKeys;
  const { mutate: setHashFieldMutate } = setHashField;
  const { mutate: deleteHashFieldMutate } = deleteHashField;
  const { mutate: updateZsetScoreMutate } = updateZsetScore;
  const { mutate: updateProfileMutate } = updateProfile;
  const slowLog = useRedisSlowLog(resolvedCacheId);

  const listItems = useMemo(() => listItemsQuery.data?.pages.flat() ?? [], [listItemsQuery.data]);
  const setMembers = useMemo(
    () => setMembersQuery.data?.pages.flatMap((p) => p.members) ?? [],
    [setMembersQuery.data],
  );

  // Facades stabilize what consumers see: raw TanStack results get a fresh identity
  // every render, so the Queries context would re-render everyone on any keystroke.
  const serverInfoFacade = useQueryFacade(serverInfo);
  const scanResultFacade = useQueryFacade(scanResult);
  const keyInfoFacade = useQueryFacade(keyInfo);
  const keyValueFacade = useQueryFacade(keyValue);
  const hashFieldsFacade = useQueryFacade(hashFields);
  const listItemsQueryFacade = useInfiniteQueryFacade(listItemsQuery);
  const setMembersQueryFacade = useInfiniteQueryFacade(setMembersQuery);
  const sortedSetMembersFacade = useQueryFacade(sortedSetMembers);
  const slowLogFacade = useQueryFacade(slowLog);
  const deleteKeyFacade = useMutationFacade(deleteKey);
  const renameKeyFacade = useMutationFacade(renameKey);
  const setTtlFacade = useMutationFacade(setTtl);
  const setValueFacade = useMutationFacade(setValue);
  const exportKeysFacade = useMutationFacade(exportKeys);
  const setHashFieldFacade = useMutationFacade(setHashField);
  const deleteHashFieldFacade = useMutationFacade(deleteHashField);
  const updateZsetScoreFacade = useMutationFacade(updateZsetScore);

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

  // Key-switch clears in-progress field edits — during render so the new key's
  // detail never paints with the previous key's edit buffers.
  const [prevSelectedKey, setPrevSelectedKey] = useState(selectedKey);
  if (prevSelectedKey !== selectedKey) {
    setPrevSelectedKey(selectedKey);
    setHashAdding(false);
    setNewHashField("");
    setNewHashValue("");
    setHashEditingField(null);
    setHashEditFieldName("");
    setHashEditValue("");
    setZsetEditingMember(null);
    setZsetEditScore("");
  }

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

  const handleSearch = useCallback(
    () => applySearchPattern(searchInput),
    [applySearchPattern, searchInput],
  );

  // Drill-through target for the Prefix/Ops panels, mirroring Keyspace's existing
  // onOpenKey-then-switch-tab pattern.
  const openPrefixInKeys = useCallback(
    (prefix: string) => {
      applySearchPattern(`${prefix}${separator}*`);
      setActiveTab("keys");
    },
    [applySearchPattern, separator, setActiveTab],
  );

  const handleLoadMore = useCallback(() => {
    if (scanResult.data && !scanResult.data.isComplete) {
      setAllKeys((prev) => [...prev, ...scanResult.data!.keys]);
      setCursor(scanResult.data.cursor);
    }
  }, [scanResult.data]);

  const handleLoadAll = useCallback(() => {
    if (scanResult.data && !scanResult.data.isComplete) {
      setLoadAllActive(true);
    }
  }, [scanResult.data]);

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
      // eslint-disable-next-line react-hooks/set-state-in-effect -- terminating a fetch-driven pagination loop; this IS synchronization with the query layer
      setLoadAllActive(false);
      return;
    }

    if (lastAdvancedCursorRef.current === scanResult.data.cursor) return;
    // eslint-disable-next-line react-hooks/immutability -- effect-time write to a useRef guard; refs are the sanctioned mutable channel
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
  const health = useQueryFacade(
    useRedisKeyspaceHealth(resolvedCacheId, displayKeys, separator, {
      enabled: activeTab === "keyspace",
    }),
  );
  const prefixMemory = useQueryFacade(
    useRedisPrefixMemory(resolvedCacheId, displayKeys, separator, {
      enabled: activeTab === "prefix",
    }),
  );

  const namespaceTree = useMemo(
    () => buildNamespaceTree(displayKeys, separator),
    [displayKeys, separator],
  );

  const flatRedisRows = useMemo(
    () => flattenNamespaceTree(namespaceTree, expandedNamespaces),
    [namespaceTree, expandedNamespaces],
  );

  const toggleNamespace = useCallback((path: string) => {
    setExpandedNamespaces((prev) => {
      const next = new Set(prev);
      if (next.has(path)) next.delete(path);
      else next.add(path);
      return next;
    });
  }, []);

  const collapseAllNamespaces = useCallback(() => setExpandedNamespaces(new Set()), []);
  const expandAllNamespaces = useCallback(
    () => setExpandedNamespaces(collectAllNamespacePaths(namespaceTree)),
    [namespaceTree],
  );

  const handleDeleteKey = useCallback(
    (key: string) => {
      deleteKeyMutate(key, {
        onSuccess: () => {
          setSelectedKey(null);
          setCursor(0);
          setAllKeys([]);
        },
      });
    },
    [deleteKeyMutate],
  );

  const handleRenameKey = useCallback(
    (oldKey: string) => {
      if (!renameValue.trim() || renameValue === oldKey) {
        setRenaming(false);
        return;
      }
      renameKeyMutate({ key: oldKey, newKey: renameValue.trim() }, {
        onSuccess: () => {
          setSelectedKey(renameValue.trim());
          setRenaming(false);
          setCursor(0);
          setAllKeys([]);
        },
      });
    },
    [renameValue, renameKeyMutate],
  );

  const handleCopyKey = useCallback((key: string) => {
    navigator.clipboard.writeText(key);
  }, []);

  const handleSetTtl = useCallback(
    (key: string) => {
      setTtlMutate({ key, ttlSeconds }, {
        onSuccess: () => setShowTtlEditor(false),
      });
    },
    [ttlSeconds, setTtlMutate],
  );

  const handleRemoveTtl = useCallback(
    (key: string) => {
      setTtlMutate({ key, removeTtl: true }, {
        onSuccess: () => setShowTtlEditor(false),
      });
    },
    [setTtlMutate],
  );

  const requestRemoveTtl = useCallback(
    (key: string) => {
      setPendingConfirm({
        message: `Remove TTL from "${key}"? It will no longer expire automatically.`,
        onConfirm: () => handleRemoveTtl(key),
        confirmLabel: "Remove TTL",
      });
    },
    [handleRemoveTtl],
  );

  const handleSaveStringValue = useCallback(
    (key: string) => {
      setValueMutate({ key, value: stringValue }, {
        onSuccess: () => setEditingValue(false),
      });
    },
    [stringValue, setValueMutate],
  );

  const requestDeleteKey = useCallback(
    (key: string) => {
      setPendingConfirm({
        message: `Delete key "${key}"?`,
        onConfirm: () => handleDeleteKey(key),
      });
    },
    [handleDeleteKey],
  );

  const handleAddHashField = useCallback(
    (key: string) => {
      const field = newHashField.trim();
      if (!field) return;
      setHashFieldMutate({ key, field, value: newHashValue }, {
        onSuccess: () => {
          setHashAdding(false);
          setNewHashField("");
          setNewHashValue("");
        },
      });
    },
    [newHashField, newHashValue, setHashFieldMutate],
  );

  const handleSaveHashField = useCallback(
    (key: string, originalField: string) => {
      const field = hashEditFieldName.trim();
      if (!field) return;
      if (field === originalField) {
        setHashFieldMutate({ key, field, value: hashEditValue }, {
          onSuccess: () => setHashEditingField(null),
        });
      } else {
        setHashFieldMutate({ key, field, value: hashEditValue }, {
          onSuccess: () => {
            deleteHashFieldMutate({ key, field: originalField }, {
              onSuccess: () => setHashEditingField(null),
            });
          },
        });
      }
    },
    [hashEditFieldName, hashEditValue, setHashFieldMutate, deleteHashFieldMutate],
  );

  const requestDeleteHashField = useCallback(
    (key: string, field: string) => {
      setPendingConfirm({
        message: `Delete field "${field}"?`,
        onConfirm: () => deleteHashFieldMutate({ key, field }),
      });
    },
    [deleteHashFieldMutate],
  );

  const handleSaveZsetScore = useCallback(
    (key: string, member: string) => {
      const score = parseFloat(zsetEditScore);
      if (Number.isNaN(score)) return;
      updateZsetScoreMutate({ key, member, score }, {
        onSuccess: () => setZsetEditingMember(null),
      });
    },
    [zsetEditScore, updateZsetScoreMutate],
  );

  const handleBatchDelete = useCallback(() => {
    const keys = Array.from(selectedKeys);
    setPendingConfirm({
      message: `Delete ${keys.length} key${keys.length === 1 ? "" : "s"}?`,
      onConfirm: () => {
        deleteKeysMutate(keys, {
          onSuccess: () => {
            setSelectedKeys(new Set());
            setCursor(0);
            setAllKeys([]);
          },
        });
      },
    });
  }, [selectedKeys, deleteKeysMutate]);

  const handleExportSelected = useCallback(async () => {
    const exportData = await exportKeysMutateAsync(Array.from(selectedKeys));
    const blob = new Blob([JSON.stringify(exportData, null, 2)], { type: "application/json" });
    const url = URL.createObjectURL(blob);
    const a = document.createElement("a");
    a.href = url;
    a.download = "redis-keys-export.json";
    a.click();
    URL.revokeObjectURL(url);
  }, [selectedKeys, exportKeysMutateAsync]);

  const toggleKeySelection = useCallback((key: string) => {
    setSelectedKeys((prev) => {
      const next = new Set(prev);
      if (next.has(key)) next.delete(key);
      else next.add(key);
      return next;
    });
  }, []);

  // MAUI toolbar parity: the header checkbox tri-states over the *loaded* key set — checked when
  // all loaded keys are selected, indeterminate for a subset — and toggling it either selects the
  // whole loaded set or clears the selection.
  const allLoadedSelected = displayKeys.length > 0 && displayKeys.every((k) => selectedKeys.has(k));
  const someLoadedSelected =
    !allLoadedSelected && displayKeys.some((k) => selectedKeys.has(k));
  const toggleSelectAllLoaded = useCallback(
    () => setSelectedKeys(allLoadedSelected ? new Set() : new Set(displayKeys)),
    [allLoadedSelected, displayKeys],
  );

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

  const toggleSubtreeSelection = useCallback(
    (node: NamespaceNode) => {
      const keys = subtreeKeysByPath.get(node.path) ?? collectSubtreeKeys(node);
      setSelectedKeys((prev) => {
        const next = new Set(prev);
        if (keys.every((k) => prev.has(k))) keys.forEach((k) => next.delete(k));
        else keys.forEach((k) => next.add(k));
        return next;
      });
    },
    [subtreeKeysByPath],
  );

  const handleCacheChange = useCallback(
    (cacheId: string) => {
      setActiveCacheId(cacheId);
      setCursor(0);
      setAllKeys([]);
      setSelectedKey(null);
      setSelectedKeys(new Set());
      // eslint-disable-next-line react-hooks/immutability -- event-time write to a useRef guard; refs are the sanctioned mutable channel
      lastAdvancedCursorRef.current = null;
      setExpandedNamespaces(new Set());
      restorePattern(cacheId);
      // The browser's selected cache IS the "active" cache: persisted so the page restores it next
      // visit, and because the agent's Redis tools fall back to it (RedisToolContext). The profile
      // PUT evicts only connection-changed caches, so healthy pooled connections survive the save.
      updateProfileMutate((prev) =>
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
    },
    [restorePattern, updateProfileMutate],
  );

  const connectionValue: RedisConnectionValue = useMemo(
    () => ({ caches, activeCacheId, resolvedCacheId, handleCacheChange }),
    [caches, activeCacheId, resolvedCacheId, handleCacheChange],
  );

  const navValue: RedisNavValue = useMemo(
    () => ({ selectedKey, setSelectedKey, activeTab, setActiveTab }),
    [selectedKey, activeTab, setActiveTab],
  );

  const queriesValue: RedisQueriesValue = useMemo(
    () => ({
      serverInfo: serverInfoFacade,
      scanResult: scanResultFacade,
      keyInfo: keyInfoFacade,
      keyValue: keyValueFacade,
      hashFields: hashFieldsFacade,
      listItemsQuery: listItemsQueryFacade,
      listItems,
      setMembersQuery: setMembersQueryFacade,
      setMembers,
      sortedSetMembers: sortedSetMembersFacade,
      slowLog: slowLogFacade,
      health,
      prefixMemory,
      deleteKey: deleteKeyFacade,
      renameKey: renameKeyFacade,
      setTtl: setTtlFacade,
      setValue: setValueFacade,
      exportKeys: exportKeysFacade,
      setHashField: setHashFieldFacade,
      deleteHashField: deleteHashFieldFacade,
      updateZsetScore: updateZsetScoreFacade,
    }),
    [
      serverInfoFacade,
      scanResultFacade,
      keyInfoFacade,
      keyValueFacade,
      hashFieldsFacade,
      listItemsQueryFacade,
      listItems,
      setMembersQueryFacade,
      setMembers,
      sortedSetMembersFacade,
      slowLogFacade,
      health,
      prefixMemory,
      deleteKeyFacade,
      renameKeyFacade,
      setTtlFacade,
      setValueFacade,
      exportKeysFacade,
      setHashFieldFacade,
      deleteHashFieldFacade,
      updateZsetScoreFacade,
    ],
  );

  const browserValue: RedisBrowserValue = useMemo(
    () => ({
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
    }),
    [
      pattern,
      searchInput,
      cursor,
      handleSearch,
      handleLoadMore,
      handleLoadAll,
      loadAllActive,
      openPrefixInKeys,
      separator,
      expandedNamespaces,
      toggleNamespace,
      collapseAllNamespaces,
      expandAllNamespaces,
      displayKeys,
      namespaceTree,
      flatRedisRows,
      selectedKeys,
      toggleKeySelection,
      allLoadedSelected,
      someLoadedSelected,
      toggleSelectAllLoaded,
      subtreeKeysByPath,
      toggleSubtreeSelection,
      handleBatchDelete,
      handleExportSelected,
    ],
  );

  const editorValue: RedisEditorValue = useMemo(
    () => ({
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
    }),
    [
      renaming,
      renameValue,
      handleRenameKey,
      editingValue,
      stringValue,
      handleSaveStringValue,
      showTtlEditor,
      ttlSeconds,
      handleSetTtl,
      handleRemoveTtl,
      requestRemoveTtl,
      hashAdding,
      newHashField,
      newHashValue,
      hashEditingField,
      hashEditFieldName,
      hashEditValue,
      handleAddHashField,
      handleSaveHashField,
      requestDeleteHashField,
      zsetEditingMember,
      zsetEditScore,
      handleSaveZsetScore,
      handleCopyKey,
      requestDeleteKey,
      handleDeleteKey,
    ],
  );

  const opsValue: RedisOpsValue = useMemo(
    () => ({
      autoRefresh,
      setAutoRefresh,
      refreshInterval,
      setRefreshInterval,
      handleManualRefresh,
      lastRefreshedAt,
      isFetching,
      pendingConfirm,
      setPendingConfirm,
    }),
    [
      autoRefresh,
      refreshInterval,
      handleManualRefresh,
      lastRefreshedAt,
      isFetching,
      pendingConfirm,
    ],
  );

  return (
    <RedisConnectionContext.Provider value={connectionValue}>
      <RedisNavContext.Provider value={navValue}>
        <RedisQueriesContext.Provider value={queriesValue}>
          <RedisBrowserContext.Provider value={browserValue}>
            <RedisEditorContext.Provider value={editorValue}>
              <RedisOpsContext.Provider value={opsValue}>
                {children}
              </RedisOpsContext.Provider>
            </RedisEditorContext.Provider>
          </RedisBrowserContext.Provider>
        </RedisQueriesContext.Provider>
      </RedisNavContext.Provider>
    </RedisConnectionContext.Provider>
  );
}
