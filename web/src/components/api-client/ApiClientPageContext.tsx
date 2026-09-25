import {
  createContext,
  useContext,
  useState,
  useMemo,
  useCallback,
  useEffect,
  useRef,
  type ReactNode,
  type JSX,
} from "react";
import { useLocation, useNavigate } from "react-router";
import { useQueryClient } from "@tanstack/react-query";
import {
  useCollections,
  useUpdateCollections,
  useExecuteRequest,
  useEnvironments,
  useUpdateEnvironments,
} from "@/lib/hooks";
import type { ResponseHistoryEntry } from "./ResponseViewer";
import type { RequestTab } from "./RequestTabStrip";
import { buildVariableScope } from "@/lib/variable-utils";
import { getSecret } from "@/lib/tauri-bridge";
import { buildResponseExample } from "@/lib/response-example";
import { runRequestActions } from "@/lib/request-action-runner";
import { useNotification } from "@/components/layout/NotificationSystem";
import { useScreenStateProvider } from "@/lib/stores/screen-state";
import {
  moveNode,
  moveCollection,
  findRequestNode,
  describeNodeForDelete,
  formatDeleteMessage,
  type MoveNodeTarget,
  type MoveCollectionTarget,
} from "@/lib/collection-tree-utils";
import { pickNeighborTabId } from "@/lib/request-tab-utils";
import type {
  ApiCollection,
  ApiCollectionNode,
  HttpRequestEntry,
  ApiClientExecutionResponse,
  ApiRequestMethod,
  ApiEnvironment,
  CollectionVariable,
  AuthConfig,
  CollectionsStoreResponse,
} from "@/lib/types";

function newId() {
  return crypto.randomUUID();
}

function now() {
  return new Date().toISOString();
}

function deepClone<T>(obj: T): T {
  return typeof structuredClone === "function" ? structuredClone(obj) : JSON.parse(JSON.stringify(obj));
}

function emptyRequest(): HttpRequestEntry {
  return {
    id: newId(),
    name: "New Request",
    method: "Get" as ApiRequestMethod,
    url: "",
    headers: [],
    queryParams: [],
    body: { mode: "None", rawContent: null, contentType: null, formData: [], filePath: null },
    auth: null,
    captureRules: [],
    graphQlQuery: null,
    graphQlVariables: null,
    graphQlSelectedOperation: null,
    savedMessages: [],
    wsSubProtocol: null,
    responseExamples: [],
    createdAt: now(),
    updatedAt: now(),
    preRequestActions: [],
    postRequestActions: [],
  };
}

const CREDENTIAL_KEY_PREFIX = "sw-secret:";

function isLegacyCredentialKey(auth: AuthConfig | null | undefined): boolean {
  return !!auth?.credentialKey && !auth.credentialKey.startsWith(CREDENTIAL_KEY_PREFIX);
}

function countLegacySecrets(collections: ApiCollection[]): number {
  let count = 0;
  for (const collection of collections) {
    if (isLegacyCredentialKey(collection.defaultAuth)) count++;
    function walk(nodes: ApiCollectionNode[]) {
      for (const node of nodes) {
        if (node.type === "Folder") {
          if (isLegacyCredentialKey(node.defaultAuth)) count++;
          walk(node.children);
        } else if (node.request) {
          if (isLegacyCredentialKey(node.request.auth)) count++;
        }
      }
    }
    walk(collection.nodes);
  }
  return count;
}

function removeNode(collections: ApiCollection[], nodeId: string): ApiCollection[] {
  return collections
    .filter((c) => c.id !== nodeId)
    .map((collection) => ({
      ...collection,
      nodes: removeFromNodes(collection.nodes, nodeId),
    }));
}

function removeFromNodes(nodes: ApiCollectionNode[], nodeId: string): ApiCollectionNode[] {
  return nodes
    .filter((n) => n.id !== nodeId)
    .map((n) =>
      n.type === "Folder" ? { ...n, children: removeFromNodes(n.children, nodeId) } : n,
    );
}

function insertIntoCollection(
  collections: ApiCollection[],
  collectionId: string,
  node: ApiCollectionNode,
  parentId?: string,
): ApiCollection[] {
  if (!parentId) {
    return collections.map((c) =>
      c.id === collectionId ? { ...c, nodes: [...c.nodes, node] } : c,
    );
  }
  return collections.map((c) =>
    c.id === collectionId
      ? { ...c, nodes: insertIntoNodes(c.nodes, parentId, node) }
      : c,
  );
}

function insertIntoNodes(
  nodes: ApiCollectionNode[],
  parentId: string,
  node: ApiCollectionNode,
): ApiCollectionNode[] {
  return nodes.map((n) => {
    if (n.id === parentId && n.type === "Folder") {
      return { ...n, children: [...n.children, node] };
    }
    if (n.type === "Folder") {
      return { ...n, children: insertIntoNodes(n.children, parentId, node) };
    }
    return n;
  });
}

function updateRequestInCollections(
  collections: ApiCollection[],
  nodeId: string,
  request: HttpRequestEntry,
): ApiCollection[] {
  return collections.map((collection) => ({
    ...collection,
    nodes: updateRequestInNodes(collection.nodes, nodeId, request),
  }));
}

function updateRequestInNodes(
  nodes: ApiCollectionNode[],
  nodeId: string,
  request: HttpRequestEntry,
): ApiCollectionNode[] {
  return nodes.map((n) => {
    if (n.id === nodeId) {
      return { ...n, name: request.name, request };
    }
    if (n.type === "Folder") {
      return { ...n, children: updateRequestInNodes(n.children, nodeId, request) };
    }
    return n;
  });
}

function renameNodeInCollections(
  collections: ApiCollection[],
  nodeId: string,
  newName: string,
): ApiCollection[] {
  return collections.map((c) => {
    if (c.id === nodeId) return { ...c, name: newName };
    return { ...c, nodes: renameNodeInNodes(c.nodes, nodeId, newName) };
  });
}

function renameNodeInNodes(
  nodes: ApiCollectionNode[],
  nodeId: string,
  newName: string,
): ApiCollectionNode[] {
  return nodes.map((n) => {
    if (n.id === nodeId) return { ...n, name: newName };
    if (n.type === "Folder") return { ...n, children: renameNodeInNodes(n.children, nodeId, newName) };
    return n;
  });
}

/** Newest-first cap on per-tab response history. Session-only, as documented. */
const HISTORY_LIMIT = 20;

interface TabState {
  draft: HttpRequestEntry;
  response: ApiClientExecutionResponse | null;
  sending: boolean;
  dirty: boolean;
  /**
   * Owned here rather than in `ResponseViewer` so history survives a remount and
   * is scoped per tab instead of per mounted component.
   */
  history: ResponseHistoryEntry[];
}

function emptyTabState(draft: HttpRequestEntry): TabState {
  return { draft, response: null, sending: false, dirty: false, history: [] };
}

/** Prepends a response to a tab's history, newest first, capped. */
function appendHistory(state: TabState | undefined, response: ApiClientExecutionResponse): ResponseHistoryEntry[] {
  const existing = state?.history ?? [];
  const nextId = (existing[0]?.id ?? 0) + 1;
  return [{ id: nextId, response, timestamp: Date.now() }, ...existing].slice(0, HISTORY_LIMIT);
}

interface NameDialogState {
  title: string;
  label: string;
  defaultValue: string;
  confirmText: string;
  onConfirm: (name: string) => void;
}

interface ConfirmDialogState {
  message: string;
  onConfirm: () => void;
  /**
   * The confirm button's label. Reserve "Delete" (the `ConfirmDialog` default)
   * for actual deletion — a tab close or a git revert is a different action
   * with a different consequence and should say so.
   */
  confirmText?: string;
}

export interface ApiClientPageContextValue {
  collections: ApiCollection[];
  isLoading: boolean;

  environments: ApiEnvironment[];
  activeEnvironmentId: string | null;
  /** The layer that wins — the scoped one when set, otherwise the global one. */
  activeEnvironment: ApiEnvironment | null;
  /** The global layer, applied to every collection. */
  activeGlobalEnvironment: ApiEnvironment | null;
  /** The layer scoped to the current collection, applied over the global one. */
  activeScopedEnvironment: ApiEnvironment | null;
  /** The collection the environment pickers work against: the open tab's, else the tree selection. */
  currentCollection: ApiCollection | null;
  /** Active collection-scoped environment per collection id, for display in the manager. */
  activeEnvironmentIdByCollection: Record<string, string>;
  handleSetActiveEnvironment: (envId: string | null) => void;
  handleSetScopedEnvironment: (collectionId: string, envId: string | null) => void;
  handleSaveEnvironments: (envs: ApiEnvironment[], activeId: string | null) => void;

  selectedNodeId: string | null;
  selectedCollectionId: string | null;
  selectedCollection: ApiCollection | null;
  handleSelectNode: (node: ApiCollectionNode, collectionId: string) => void;

  variableScope: Record<string, string | null>;

  handleAddCollection: () => void;
  handleAddRequest: (collectionId: string, parentId?: string) => void;
  handleAddFolder: (collectionId: string, parentId?: string) => void;
  handleDeleteNode: (nodeId: string, collectionId: string) => void;
  handleRenameNode: (nodeId: string, collectionId: string, newName: string) => void;
  handleMoveNode: (nodeId: string, sourceCollectionId: string, target: MoveNodeTarget) => void;
  handleMoveCollection: (collectionId: string, target: MoveCollectionTarget) => void;

  conflict: { message: string } | null;
  dismissConflict: () => void;
  handleReloadConflict: () => Promise<void>;
  handleOverwriteConflict: () => Promise<void>;
  handleSaveAsCopy: () => Promise<void>;

  legacySecretCount: number;
  legacyNoticeDismissed: boolean;
  dismissLegacyNotice: () => void;

  showEnvManager: boolean;
  setShowEnvManager: (v: boolean) => void;
  showColVarEditor: boolean;
  setShowColVarEditor: (v: boolean) => void;
  exportCollectionId: string | null;
  setExportCollectionId: (v: string | null) => void;
  exportCollection: ApiCollection | null;
  showGitPanel: boolean;
  setShowGitPanel: (v: boolean) => void;

  nameDialog: NameDialogState | null;
  setNameDialog: (v: NameDialogState | null) => void;
  confirmDialog: ConfirmDialogState | null;
  setConfirmDialog: (v: ConfirmDialogState | null) => void;

  handleSaveCollectionVariables: (variables: CollectionVariable[]) => void;
}

/**
 * Per-keystroke tab state, isolated from `ApiClientPageContextValue`: every
 * `updateTabDraft` bumps `tabs`/`tabStates`, so only the tab strip, request
 * editor and response viewer should subscribe — subscribing here is opting
 * into a re-render on every keystroke in the request editor.
 */
export interface ApiClientTabsContextValue {
  tabs: RequestTab[];
  activeTabId: string | null;
  setActiveTabId: (tabId: string | null) => void;
  tabStates: Record<string, TabState>;
  closeTab: (tabId: string) => void;
  closeOtherTabs: (tabId: string) => void;
  closeAllTabs: () => void;
  promoteTab: (tabId: string) => void;
  updateTabDraft: (tabId: string, draft: HttpRequestEntry) => void;

  activeTab: RequestTab | null;
  activeCollection: ApiCollection | null | undefined;

  handleSave: () => Promise<boolean>;
  handleSend: () => Promise<void>;
  handleSaveExample: (name: string, response: ApiClientExecutionResponse) => Promise<void>;
}

const ApiClientPageContext = createContext<ApiClientPageContextValue | null>(null);
const ApiClientTabsContext = createContext<ApiClientTabsContextValue | null>(null);

export function useApiClientPageContext(): ApiClientPageContextValue {
  const ctx = useContext(ApiClientPageContext);
  if (!ctx) throw new Error("useApiClientPageContext must be used within ApiClientPageProvider");
  return ctx;
}

export function useApiClientTabs(): ApiClientTabsContextValue {
  const ctx = useContext(ApiClientTabsContext);
  if (!ctx) throw new Error("useApiClientTabs must be used within ApiClientPageProvider");
  return ctx;
}

export function ApiClientPageProvider({ children }: { children: ReactNode }): JSX.Element {
  const { notify } = useNotification();
  const { data: collections = [], isLoading } = useCollections();
  const updateCollections = useUpdateCollections();
  const executeRequest = useExecuteRequest();
  const { data: envData } = useEnvironments();
  const updateEnvironments = useUpdateEnvironments();
  // Mutation objects are fresh each render; their mutate functions are stable.
  const { mutate: updateCollectionsMutate, mutateAsync: updateCollectionsMutateAsync } = updateCollections;
  const { mutateAsync: executeRequestMutateAsync } = executeRequest;
  const { mutate: updateEnvironmentsMutate } = updateEnvironments;
  const location = useLocation();
  const navigate = useNavigate();
  const qc = useQueryClient();

  const environments = useMemo(() => envData?.environments ?? [], [envData]);
  const uiState = envData?.uiState;
  const activeEnvironmentId = uiState?.activeEnvironmentId ?? null;

  /// Resolves the two environment layers that apply to a collection: a global one
  /// and a collection-scoped one, both active at once with the scoped one winning.
  /// Both slots already existed in the stored UI state
  /// (`activeEnvironmentId` and `activeEnvironmentIdByCollection`) but only the
  /// first was ever read, so a value shared by every `DEV (via …)` environment had
  /// to be duplicated into each of them.
  ///
  /// Used by both the preview scope and the send payload. Resolving it twice is how
  /// the preview would start describing something other than what is sent.
  const resolveEnvironmentLayers = useCallback((collectionId: string | null | undefined) => {
    const selected = environments.find((e) => e.id === activeEnvironmentId) ?? null;

    // The global slot can still hold a collection-scoped environment picked before
    // the two layers existed. Honour it as that collection's project selection
    // rather than applying an environment scoped to somewhere else.
    const global = selected && selected.collectionId === null ? selected : null;

    const scopedId = collectionId
      ? uiState?.activeEnvironmentIdByCollection?.[collectionId] ?? null
      : null;
    const scoped =
      (scopedId ? environments.find((e) => e.id === scopedId) ?? null : null) ??
      (selected && collectionId && selected.collectionId === collectionId ? selected : null);

    return { global, scoped };
  }, [environments, activeEnvironmentId, uiState?.activeEnvironmentIdByCollection]);

  const [selectedNodeId, setSelectedNodeId] = useState<string | null>(null);
  const [selectedCollectionId, setSelectedCollectionId] = useState<string | null>(null);
  const [tabs, setTabs] = useState<RequestTab[]>([]);
  const [activeTabId, setActiveTabId] = useState<string | null>(null);
  const [tabStates, setTabStates] = useState<Record<string, TabState>>({});
  const [showEnvManager, setShowEnvManager] = useState(false);
  const [showColVarEditor, setShowColVarEditor] = useState(false);
  const [exportCollectionId, setExportCollectionId] = useState<string | null>(null);
  const [showGitPanel, setShowGitPanel] = useState(false);
  const [conflict, setConflict] = useState<{ message: string } | null>(null);
  const [legacyNoticeDismissed, setLegacyNoticeDismissed] = useState(() =>
    typeof window !== "undefined" && localStorage.getItem("swokit-legacy-secret-notice") === "dismissed"
  );
  const legacySecretCount = useMemo(() => countLegacySecrets(collections), [collections]);
  const [nameDialog, setNameDialog] = useState<NameDialogState | null>(null);
  const [confirmDialog, setConfirmDialog] = useState<ConfirmDialogState | null>(null);

  // Refs mirror the per-keystroke tab state so handlers living in the *page*
  // context (delete-node, conflict resolution, select-node) can read it without
  // depending on it — otherwise their identity, and the whole page-context
  // value, would churn on every editor keystroke.
  const tabsRef = useRef(tabs);
  const tabStatesRef = useRef(tabStates);
  const activeTabIdRef = useRef(activeTabId);
  useEffect(() => {
    tabsRef.current = tabs;
    tabStatesRef.current = tabStates;
    activeTabIdRef.current = activeTabId;
  });

  /**
   * `preview: true` (single-click tree navigation) reuses the one preview tab
   * instead of opening a new permanent one — browsing a collection should not
   * accumulate a tab per row clicked. `preview: false`/omitted (Add Request,
   * deep link, double-click) always opens or promotes to a permanent tab.
   */
  const openTab = useCallback((node: ApiCollectionNode, collectionId: string, opts?: { preview?: boolean }) => {
    if (node.type !== "Request" || !node.request) return;
    const preview = opts?.preview ?? false;

    const existingTab = tabsRef.current.find((t) => t.nodeId === node.id);
    if (existingTab) {
      // Deliberately reopening an already-open tab (not from a preview click)
      // is a revisit, not a throwaway peek — promote it if it was a preview.
      if (!preview && existingTab.isPreview) {
        setTabs((prev) => prev.map((t) => (t.id === existingTab.id ? { ...t, isPreview: false } : t)));
      }
      setActiveTabId(existingTab.id);
      return;
    }

    const existingPreviewTab = preview
      ? tabsRef.current.find((t) => t.isPreview && !tabStatesRef.current[t.id]?.dirty)
      : undefined;
    if (existingPreviewTab) {
      setTabs((prev) =>
        prev.map((t) =>
          t.id === existingPreviewTab.id
            ? { ...t, nodeId: node.id, collectionId, name: node.name, method: node.request!.method, dirty: false, isPreview: true }
            : t,
        ),
      );
      setTabStates((prev) => ({
        ...prev,
        [existingPreviewTab.id]: emptyTabState(deepClone(node.request!)),
      }));
      setActiveTabId(existingPreviewTab.id);
      return;
    }

    const tabId = newId();
    const tab: RequestTab = {
      id: tabId,
      nodeId: node.id,
      collectionId,
      name: node.name,
      method: node.request.method,
      dirty: false,
      isPreview: preview,
    };
    setTabs((prev) => [...prev, tab]);
    setTabStates((prev) => ({
      ...prev,
      [tabId]: emptyTabState(deepClone(node.request!)),
    }));
    setActiveTabId(tabId);
  }, []);

  useEffect(() => {
    const state = location.state as { collectionId?: string; nodeId?: string } | null;
    if (!state?.collectionId || !state?.nodeId) return;
    const collection = collections.find((c) => c.id === state.collectionId);
    const node = collection ? findRequestNode(collection.nodes, state.nodeId) : null;
    if (node?.type === "Request" && node.request) {
      // eslint-disable-next-line react-hooks/set-state-in-effect -- one-shot location.state deep-link consumption; the paired navigate() must live in an effect anyway
      setSelectedCollectionId(state.collectionId);
      setSelectedNodeId(state.nodeId);
      openTab(node, state.collectionId);
    }
    navigate(location.pathname, { replace: true, state: null });
  }, [location, collections, navigate, openTab]);

  /** Closing the active tab activates a neighbor instead of falling back to
   *  blank — only closing the last remaining tab actually clears the editor. */
  const closeTab = useCallback((tabId: string) => {
    const performClose = () => {
      setTabs((prev) => prev.filter((t) => t.id !== tabId));
      setTabStates((prev) => { const next = { ...prev }; delete next[tabId]; return next; });
      if (activeTabId === tabId) setActiveTabId(pickNeighborTabId(tabs, tabId));
    };

    const tabState = tabStates[tabId];
    if (tabState?.dirty) {
      setConfirmDialog({
        message: `Close "${tabs.find((t) => t.id === tabId)?.name}" with unsaved changes?`,
        confirmText: "Close",
        onConfirm: () => {
          performClose();
          setConfirmDialog(null);
        },
      });
      return;
    }
    performClose();
  }, [tabStates, tabs, activeTabId]);

  /** Closes every tab except `keepTabId`, confirming first only when it would
   *  discard unsaved changes elsewhere. */
  const closeOtherTabs = useCallback((keepTabId: string) => {
    const performClose = () => {
      setTabs((prev) => prev.filter((t) => t.id === keepTabId));
      setTabStates((prev) => (prev[keepTabId] ? { [keepTabId]: prev[keepTabId] } : {}));
      setActiveTabId(keepTabId);
    };

    const others = tabs.filter((t) => t.id !== keepTabId);
    const hasDirtyOther = others.some((t) => tabStates[t.id]?.dirty);
    if (hasDirtyOther) {
      setConfirmDialog({
        message: `Close ${others.length} other tab${others.length === 1 ? "" : "s"}? Unsaved changes will be lost.`,
        confirmText: "Close",
        onConfirm: () => {
          performClose();
          setConfirmDialog(null);
        },
      });
      return;
    }
    performClose();
  }, [tabs, tabStates]);

  /** Closes every open tab, confirming first only when any holds unsaved changes. */
  const closeAllTabs = useCallback(() => {
    const performClose = () => {
      setTabs([]);
      setTabStates({});
      setActiveTabId(null);
    };

    const hasDirty = tabs.some((t) => tabStates[t.id]?.dirty);
    if (hasDirty) {
      setConfirmDialog({
        message: `Close all ${tabs.length} tabs? Unsaved changes will be lost.`,
        confirmText: "Close",
        onConfirm: () => {
          performClose();
          setConfirmDialog(null);
        },
      });
      return;
    }
    performClose();
  }, [tabs, tabStates]);

  /** Promotes a preview tab to permanent (e.g. double-clicking it in the strip). */
  const promoteTab = useCallback((tabId: string) => {
    setTabs((prev) => prev.map((t) => (t.id === tabId ? { ...t, isPreview: false } : t)));
  }, []);

  const updateTabDraft = useCallback((tabId: string, draft: HttpRequestEntry) => {
    setTabStates((prev) => ({
      ...prev,
      [tabId]: { ...prev[tabId], draft, dirty: true },
    }));
    // Editing a preview tab is a deliberate change, not a throwaway peek —
    // promote it so the next single-click preview does not replace it.
    setTabs((prev) => prev.map((t) => t.id === tabId ? { ...t, name: draft.name, method: draft.method, dirty: true, isPreview: false } : t));
  }, []);

  const handleSelectNode = useCallback((node: ApiCollectionNode, collectionId: string) => {
    setSelectedNodeId(node.id);
    setSelectedCollectionId(collectionId);
    if (node.type === "Request" && node.request) {
      openTab(node, collectionId, { preview: true });
    }
  }, [openTab]);

  const handleAddCollection = useCallback(() => {
    setNameDialog({
      title: "New Collection",
      label: "Collection name",
      defaultValue: "",
      confirmText: "Create",
      onConfirm: (name) => {
        const collection: ApiCollection = {
          id: newId(),
          name,
          nodes: [],
          variables: [],
          defaultAuth: null,
          createdAt: now(),
          updatedAt: now(),
        };
        updateCollectionsMutate((prev) => [...prev, collection]);
        setNameDialog(null);
      },
    });
  }, [updateCollectionsMutate]);

  const handleAddRequest = useCallback((collectionId: string, parentId?: string) => {
    const request = emptyRequest();
    setNameDialog({
      title: "New Request",
      label: "Request name",
      defaultValue: request.name,
      confirmText: "Create",
      onConfirm: (name) => {
        request.name = name;
        const node: ApiCollectionNode = {
          id: request.id,
          type: "Request",
          name,
          isExpanded: true,
          children: [],
          defaultAuth: null,
          request,
        };
        updateCollectionsMutate((prev) => insertIntoCollection(prev, collectionId, node, parentId), {
          onSuccess: () => {
            setSelectedNodeId(node.id);
            setSelectedCollectionId(collectionId);
            // Open tab for the new request
            const tabId = newId();
            const tab: RequestTab = {
              id: tabId,
              nodeId: node.id,
              collectionId,
              name,
              method: request.method,
              dirty: false,
            };
            setTabs((prev) => [...prev, tab]);
            setTabStates((prev) => ({
              ...prev,
              [tabId]: emptyTabState(deepClone(request)),
            }));
            setActiveTabId(tabId);
          },
        });
        setNameDialog(null);
      },
    });
  }, [updateCollectionsMutate]);

  const handleAddFolder = useCallback((collectionId: string, parentId?: string) => {
    setNameDialog({
      title: "New Folder",
      label: "Folder name",
      defaultValue: "",
      confirmText: "Create",
      onConfirm: (name) => {
        const node: ApiCollectionNode = {
          id: newId(),
          type: "Folder",
          name,
          // Collapsed by default (unit 4.2) — an ever-expanding tree of
          // "expanded forever" folders was the reported clutter bug.
          isExpanded: false,
          children: [],
          defaultAuth: null,
          request: null,
        };
        updateCollectionsMutate((prev) => insertIntoCollection(prev, collectionId, node, parentId));
        setNameDialog(null);
      },
    });
  }, [updateCollectionsMutate]);

  const handleDeleteNode = useCallback((nodeId: string, collectionId: string) => {
    const info = describeNodeForDelete(collections, nodeId, collectionId);
    setConfirmDialog({
      message: formatDeleteMessage(info),
      confirmText: "Delete",
      onConfirm: () => {
        updateCollectionsMutate((prev) => removeNode(prev, nodeId), {
          onSuccess: () => {
            if (selectedNodeId === nodeId) {
              setSelectedNodeId(null);
              // Close tab for deleted node — same neighbor-activation rule as
              // a manual tab close (unit 4.1).
              const tabToClose = tabsRef.current.find((t) => t.nodeId === nodeId);
              if (tabToClose) {
                setTabs((prev) => prev.filter((t) => t.id !== tabToClose.id));
                setTabStates((prev) => { const next = { ...prev }; delete next[tabToClose.id]; return next; });
                if (activeTabIdRef.current === tabToClose.id) setActiveTabId(pickNeighborTabId(tabsRef.current, tabToClose.id));
              }
              setSelectedCollectionId(collectionId === nodeId ? null : collectionId);
            }
          },
        });
        setConfirmDialog(null);
      },
    });
  }, [collections, selectedNodeId, updateCollectionsMutate]);

  const handleRenameNode = useCallback((_nodeId: string, _collectionId: string, newName: string) => {
    updateCollectionsMutate((prev) => renameNodeInCollections(prev, _nodeId, newName));
    // Update tab name if open
    setTabs((prev) => prev.map((t) => t.nodeId === _nodeId ? { ...t, name: newName } : t));
  }, [updateCollectionsMutate]);

  const handleMoveNode = useCallback((nodeId: string, sourceCollectionId: string, target: MoveNodeTarget) => {
    // No snapshot-based no-op guard here on purpose: a node created moments ago
    // may not be in this render's `collections` yet, `moveNode` returns its input
    // unchanged when it cannot find the source, and bailing on that swallowed the
    // move entirely. `moveNode` runs against the freshest store inside the updater
    // instead, where an impossible move is already a safe no-op.
    if (target.targetCollectionId !== sourceCollectionId) {
      setTabs((prev) => prev.map((t) => (t.nodeId === nodeId ? { ...t, collectionId: target.targetCollectionId } : t)));
      if (selectedNodeId === nodeId) {
        setSelectedCollectionId(target.targetCollectionId);
      }
    }
    updateCollectionsMutate((prev) => moveNode(prev, nodeId, target), {
      onSuccess: () => notify("success", "Moved", "Request moved."),
      onError: (err) => notify("error", "Move failed", err.message),
    });
  }, [selectedNodeId, updateCollectionsMutate, notify]);

  const handleMoveCollection = useCallback((collectionId: string, target: MoveCollectionTarget) => {
    updateCollectionsMutate((prev) => moveCollection(prev, collectionId, target), {
      onSuccess: () => notify("success", "Moved", "Collection moved."),
      onError: (err) => notify("error", "Move failed", err.message),
    });
  }, [updateCollectionsMutate, notify]);

  const saveActiveTab = useCallback(async (baseCollections?: ApiCollection[]): Promise<boolean> => {
    const activeTabId = activeTabIdRef.current;
    if (!activeTabId) return false;
    const tabState = tabStatesRef.current[activeTabId];
    if (!tabState) return false;
    const tab = tabsRef.current.find((t) => t.id === activeTabId);
    if (!tab) return false;
    // The transient credentialSecret must never be written to collections.json.
    const draftForSave = deepClone(tabState.draft);
    if (draftForSave.auth) {
      draftForSave.auth = { ...draftForSave.auth, credentialSecret: null };
    }
    try {
      // An explicit base is an overwrite-after-conflict, which must send exactly
      // what the caller resolved; otherwise derive from the freshest store.
      await updateCollectionsMutateAsync(
        baseCollections
          ? updateRequestInCollections(baseCollections, tab.nodeId, draftForSave)
          : (prev) => updateRequestInCollections(prev, tab.nodeId, draftForSave),
      );
      setConflict(null);
      setTabStates((prev) => ({ ...prev, [activeTabId]: { ...prev[activeTabId], dirty: false } }));
      setTabs((prev) => prev.map((t) => t.id === activeTabId ? { ...t, dirty: false } : t));
      return true;
    } catch (err) {
      const message = err instanceof Error ? err.message : String(err);
      if (message.includes("409") || message.toLowerCase().includes("conflict")) {
        setConflict({
          message: "The collections file changed on disk. Reload the latest version, overwrite with your changes, or save your request as a copy.",
        });
      } else {
        console.error("Failed to save collections", err);
      }
      return false;
    }
  }, [updateCollectionsMutateAsync]);

  const handleSave = useCallback(async () => saveActiveTab(), [saveActiveTab]);

  const handleSend = useCallback(async () => {
    const activeTabId = activeTabIdRef.current;
    if (!activeTabId) return;
    const tabState = tabStatesRef.current[activeTabId];
    if (!tabState) return;
    const tab = tabsRef.current.find((t) => t.id === activeTabId);
    if (!tab) return;
    const saved = await handleSave();
    if (!saved) return;
    setTabStates((prev) => ({ ...prev, [activeTabId]: { ...prev[activeTabId], sending: true, response: null } }));
    let request: HttpRequestEntry | undefined;
    try {
      // Resolve the secret from the persisted store if the editor has not already loaded it.
      request = deepClone(tabState.draft);
      if (request.auth?.credentialKey && !request.auth.credentialSecret) {
        const secret = await getSecret(request.auth.credentialKey);
        if (secret) {
          request.auth = { ...request.auth, credentialSecret: secret };
        }
      }

      await runRequestActions(
        request.preRequestActions ?? [],
        { request },
        (type, title, message) => notify(type, title, message),
      );

      const layers = resolveEnvironmentLayers(tab.collectionId);
      const result = await executeRequestMutateAsync({
        request,
        collectionId: tab.collectionId ?? undefined,
        // Both layers travel to the backend, which applies the same precedence.
        globalEnvironmentId: layers.global?.id ?? undefined,
        environmentId: layers.scoped?.id ?? undefined,
      });

      // Commit the response immediately so the UI is not blocked by post-request actions (e.g. Delay).
      setTabStates((prev) => ({
        ...prev,
        [activeTabId]: { ...prev[activeTabId], response: result, sending: false, history: appendHistory(prev[activeTabId], result) },
      }));

      try {
        await runRequestActions(
          request.postRequestActions ?? [],
          { request, response: result },
          (type, title, message) => notify(type, title, message),
        );
      } catch (postErr) {
        const message = postErr instanceof Error ? postErr.message : "Unknown error";
        notify("error", "Post-request action failed", message);
      }
    } catch (err) {
      const failure: ApiClientExecutionResponse = {
        resolvedUrl: tabState.draft.url,
        method: tabState.draft.method,
        statusCode: 0,
        statusText: "Request Failed",
        errorMessage: err instanceof Error ? err.message : "Unknown error",
        elapsedMs: 0,
        contentLength: -1,
        contentType: null,
        responseBody: null,
        responseBodyTruncated: false,
        headers: [],
        captureWarnings: [],
        graphQlErrors: null,
      };

      await runRequestActions(
        request?.postRequestActions ?? tabState.draft.postRequestActions ?? [],
        { request: request ?? tabState.draft, response: failure },
        (type, title, message) => notify(type, title, message),
      );

      setTabStates((prev) => ({
        ...prev,
        [activeTabId]: {
          ...prev[activeTabId],
          sending: false,
          response: failure,
          history: appendHistory(prev[activeTabId], failure),
        },
      }));
    }
  }, [handleSave, executeRequestMutateAsync, notify, resolveEnvironmentLayers]);

  /** Saves a scrubbed example onto the active request and persists it. */
  const handleSaveExample = useCallback(async (name: string, response: ApiClientExecutionResponse) => {
    const activeTabId = activeTabIdRef.current;
    if (!activeTabId) return;
    const tabState = tabStatesRef.current[activeTabId];
    const tab = tabsRef.current.find((t) => t.id === activeTabId);
    if (!tabState || !tab) return;

    const example = buildResponseExample(newId(), name, response, now());
    const draft: HttpRequestEntry = {
      ...tabState.draft,
      responseExamples: [...tabState.draft.responseExamples, example],
    };

    setTabStates((prev) => ({ ...prev, [activeTabId]: { ...prev[activeTabId], draft } }));

    // Same rule as saveActiveTab: the transient credentialSecret must never reach
    // collections.json.
    const draftForSave = deepClone(draft);
    if (draftForSave.auth) {
      draftForSave.auth = { ...draftForSave.auth, credentialSecret: null };
    }
    try {
      await updateCollectionsMutateAsync((prev) =>
        updateRequestInCollections(prev, tab.nodeId, draftForSave),
      );
    } catch (err) {
      console.error("Failed to save response example", err);
    }
  }, [updateCollectionsMutateAsync]);

  const getLatestCollections = useCallback(
    () =>
      qc.getQueryData<CollectionsStoreResponse>(["collections"])?.collections ?? collections,
    [qc, collections],
  );

  const handleReloadConflict = useCallback(async () => {
    setConflict(null);
    await qc.refetchQueries({ queryKey: ["collections"] });
    const latest = getLatestCollections();
    const activeTabId = activeTabIdRef.current;
    if (!activeTabId) return;
    const tab = tabsRef.current.find((t) => t.id === activeTabId);
    if (!tab) return;
    for (const col of latest) {
      const node = findRequestNode(col.nodes, tab.nodeId);
      if (node?.type === "Request" && node.request) {
        setTabStates((prev) => ({ ...prev, [activeTabId]: { ...prev[activeTabId], draft: deepClone(node.request!), dirty: false } }));
        setTabs((prev) => prev.map((t) => (t.id === activeTabId ? { ...t, name: node.request!.name, method: node.request!.method, dirty: false } : t)));
        break;
      }
    }
  }, [qc, getLatestCollections]);

  const handleOverwriteConflict = useCallback(async () => {
    setConflict(null);
    await qc.refetchQueries({ queryKey: ["collections"] });
    const latest = getLatestCollections();
    await saveActiveTab(latest);
  }, [qc, getLatestCollections, saveActiveTab]);

  const handleSaveAsCopy = useCallback(async () => {
    setConflict(null);
    await qc.refetchQueries({ queryKey: ["collections"] });
    const latest = getLatestCollections();
    const activeTabId = activeTabIdRef.current;
    const tab = tabsRef.current.find((t) => t.id === activeTabId);
    const tabState = activeTabId ? tabStatesRef.current[activeTabId] : null;
    if (!tab || !tabState) return;
    const collection = latest.find((c) => c.id === tab.collectionId);
    if (!collection) return;
    const copy = deepClone(tabState.draft);
    copy.id = newId();
    copy.name = `${copy.name} (copy)`;
    copy.createdAt = now();
    copy.updatedAt = now();
    const node: ApiCollectionNode = { id: copy.id, type: "Request", name: copy.name, isExpanded: true, children: [], defaultAuth: null, request: copy };
    try {
      await updateCollectionsMutateAsync((prev) => insertIntoCollection(prev, collection.id, node));
      const tabId = newId();
      setTabs((prev) => [...prev, { id: tabId, nodeId: node.id, collectionId: collection.id, name: node.name, method: copy.method, dirty: false }]);
      setTabStates((prev) => ({ ...prev, [tabId]: emptyTabState(deepClone(copy)) }));
      setActiveTabId(tabId);
      setSelectedNodeId(node.id);
      setSelectedCollectionId(collection.id);
    } catch (err) {
      const message = err instanceof Error ? err.message : String(err);
      if (message.includes("409") || message.toLowerCase().includes("conflict")) {
        setConflict({
          message: "The collections file changed on disk. Reload the latest version, overwrite with your changes, or save your request as a copy.",
        });
      } else {
        console.error("Failed to save as copy", err);
      }
    }
  }, [qc, getLatestCollections, updateCollectionsMutateAsync]);

  const handleSaveEnvironments = useCallback((envs: ApiEnvironment[], activeId: string | null) => {
    updateEnvironmentsMutate({
      schemaVersion: 1,
      environments: envs,
      uiState: {
        activeEnvironmentId: activeId,
        activeEnvironmentIdByCollection: uiState?.activeEnvironmentIdByCollection ?? {},
        lastSelectedRequestIdByCollection: uiState?.lastSelectedRequestIdByCollection ?? {},
      },
    });
  }, [uiState, updateEnvironmentsMutate]);

  const handleSetActiveEnvironment = useCallback((envId: string | null) => {
    updateEnvironmentsMutate({
      schemaVersion: 1,
      environments,
      uiState: {
        activeEnvironmentId: envId,
        activeEnvironmentIdByCollection: uiState?.activeEnvironmentIdByCollection ?? {},
        lastSelectedRequestIdByCollection: uiState?.lastSelectedRequestIdByCollection ?? {},
      },
    });
  }, [environments, uiState, updateEnvironmentsMutate]);

  /// Sets the collection-scoped layer. Kept separate from the global slot so
  /// switching a project's target does not disturb the shared environment, which is
  /// the whole point of having two layers.
  const handleSetScopedEnvironment = useCallback((collectionId: string, envId: string | null) => {
    const byCollection = { ...(uiState?.activeEnvironmentIdByCollection ?? {}) };
    if (envId === null) delete byCollection[collectionId];
    else byCollection[collectionId] = envId;

    updateEnvironmentsMutate({
      schemaVersion: 1,
      environments,
      uiState: {
        // A pre-existing global selection that is really collection-scoped would
        // keep overriding this one through the compatibility path, so clear it.
        activeEnvironmentId:
          activeEnvironmentId &&
          environments.find((e) => e.id === activeEnvironmentId)?.collectionId === collectionId
            ? null
            : activeEnvironmentId,
        activeEnvironmentIdByCollection: byCollection,
        lastSelectedRequestIdByCollection: uiState?.lastSelectedRequestIdByCollection ?? {},
      },
    });
  }, [environments, uiState, activeEnvironmentId, updateEnvironmentsMutate]);

  const handleSaveCollectionVariables = useCallback((variables: CollectionVariable[]) => {
    if (!selectedCollectionId) return;
    updateCollectionsMutate(
      (prev) => prev.map((c) => (c.id === selectedCollectionId ? { ...c, variables } : c)),
      {
        // Without this a rejected save (a stale concurrency token, say) was
        // swallowed, and the editor simply reopened empty — indistinguishable
        // from never having typed the variable.
        onError: (err) => notify("error", "Saving collection variables failed", err.message),
      },
    );
  }, [selectedCollectionId, updateCollectionsMutate, notify]);

  const selectedCollection = useMemo(
    () => collections.find((c) => c.id === selectedCollectionId) ?? null,
    [collections, selectedCollectionId],
  );

  const exportCollection = useMemo(
    () => collections.find((c) => c.id === exportCollectionId) ?? null,
    [collections, exportCollectionId],
  );

  const activeTab = activeTabId ? tabs.find((t) => t.id === activeTabId) ?? null : null;
  const activeCollection = activeTab
    ? collections.find((c) => c.id === activeTab.collectionId)
    : null;

  // The collection the environment pickers and the variable scope both work against.
  // Falls back to the tree selection because `activeCollection` needs an *open request
  // tab*: gating the project picker on it alone meant that before opening a request there
  // was no way to choose a collection-scoped environment at all.
  const currentCollection = activeCollection ?? selectedCollection;

  const { global: activeGlobalEnvironment, scoped: activeScopedEnvironment } =
    resolveEnvironmentLayers(currentCollection?.id);

  // Lowest priority first: the project layer overrides the global one, and the
  // global one fills in everything the project does not mention.
  const variableScope = buildVariableScope(currentCollection?.variables ?? [], [
    activeGlobalEnvironment,
    activeScopedEnvironment,
  ]);

  // The environment whose name the toolbar shows: the project one when there is
  // one, since that is the layer that wins.
  const activeEnvironment = activeScopedEnvironment ?? activeGlobalEnvironment;

  // Screen-state snapshot (agent-workspace-awareness M1) — the open request's method/URL/name.
  // Headers, auth config, and body are deliberately NOT serialized (D5: secrets live there);
  // the URL is sent without its query string/fragment since SAS signatures and api-key params
  // live there too.
  useScreenStateProvider("api-client-page", "ApiClient", () => {
    const draft = activeTabId ? tabStates[activeTabId]?.draft : null;
    if (!draft) return { collection: currentCollection?.name ?? null, openRequest: null };
    return {
      collection: activeCollection?.name ?? currentCollection?.name ?? null,
      environment: activeEnvironment?.name ?? null,
      openRequest: {
        id: draft.id,
        name: draft.name,
        method: draft.method,
        url: draft.url.split("?")[0].split("#")[0],
      },
      responseStatus: tabStates[activeTabId!]?.response?.statusCode ?? null,
      openTabCount: tabs.length,
    };
  }, [activeTabId, tabStates, activeCollection, currentCollection, activeEnvironment, tabs]);

  const dismissConflict = useCallback(() => setConflict(null), []);

  const dismissLegacyNotice = useCallback(() => {
    localStorage.setItem("swokit-legacy-secret-notice", "dismissed");
    setLegacyNoticeDismissed(true);
  }, []);

  // Tabs state churns per editor keystroke (updateTabDraft bumps `tabs` and
  // `tabStates`). It lives in its own context so the page, collection tree and
  // dialogs don't re-render on every keystroke — only the tab strip, request
  // editor and response viewer subscribe here.
  const tabsValue: ApiClientTabsContextValue = useMemo(
    () => ({
      tabs,
      activeTabId,
      setActiveTabId,
      tabStates,
      closeTab,
      closeOtherTabs,
      closeAllTabs,
      promoteTab,
      updateTabDraft,
      activeTab,
      activeCollection,
      handleSave,
      handleSend,
      handleSaveExample,
    }),
    [
      tabs,
      activeTabId,
      tabStates,
      closeTab,
      closeOtherTabs,
      closeAllTabs,
      promoteTab,
      updateTabDraft,
      activeTab,
      activeCollection,
      handleSave,
      handleSend,
      handleSaveExample,
    ],
  );

  const value: ApiClientPageContextValue = useMemo(() => ({
    collections,
    isLoading,

    environments,
    activeEnvironmentId,
    activeEnvironment,
    activeGlobalEnvironment,
    activeScopedEnvironment,
    currentCollection: currentCollection ?? null,
    activeEnvironmentIdByCollection: uiState?.activeEnvironmentIdByCollection ?? {},
    handleSetActiveEnvironment,
    handleSetScopedEnvironment,
    handleSaveEnvironments,

    selectedNodeId,
    selectedCollectionId,
    selectedCollection,
    // These handlers read the tab-state mirror refs at event time only — by
    // design (see the refs comment above); the rule flags their transitive
    // reachability from this render-built object.
    // eslint-disable-next-line react-hooks/refs
    handleSelectNode,

    variableScope,

    handleAddCollection,
    handleAddRequest,
    handleAddFolder,
    handleDeleteNode,
    handleRenameNode,
    handleMoveNode,
    handleMoveCollection,

    conflict,
    dismissConflict,
    // eslint-disable-next-line react-hooks/refs -- event-time ref reads, same pattern as handleSelectNode above
    handleReloadConflict,
    // eslint-disable-next-line react-hooks/refs -- event-time ref reads, same pattern as handleSelectNode above
    handleOverwriteConflict,
    // eslint-disable-next-line react-hooks/refs -- event-time ref reads, same pattern as handleSelectNode above
    handleSaveAsCopy,

    legacySecretCount,
    legacyNoticeDismissed,
    dismissLegacyNotice,

    showEnvManager,
    setShowEnvManager,
    showColVarEditor,
    setShowColVarEditor,
    exportCollectionId,
    setExportCollectionId,
    exportCollection,
    showGitPanel,
    setShowGitPanel,

    nameDialog,
    setNameDialog,
    confirmDialog,
    setConfirmDialog,

    handleSaveCollectionVariables,
  }), [
    collections,
    isLoading,
    environments,
    activeEnvironmentId,
    activeEnvironment,
    activeGlobalEnvironment,
    activeScopedEnvironment,
    currentCollection,
    uiState?.activeEnvironmentIdByCollection,
    handleSetActiveEnvironment,
    handleSetScopedEnvironment,
    handleSaveEnvironments,
    selectedNodeId,
    selectedCollectionId,
    selectedCollection,
    handleSelectNode,
    variableScope,
    handleAddCollection,
    handleAddRequest,
    handleAddFolder,
    handleDeleteNode,
    handleRenameNode,
    handleMoveNode,
    handleMoveCollection,
    conflict,
    dismissConflict,
    handleReloadConflict,
    handleOverwriteConflict,
    handleSaveAsCopy,
    legacySecretCount,
    legacyNoticeDismissed,
    dismissLegacyNotice,
    showEnvManager,
    showColVarEditor,
    exportCollectionId,
    exportCollection,
    showGitPanel,
    nameDialog,
    confirmDialog,
    handleSaveCollectionVariables,
  ]);

  return (
    <ApiClientPageContext.Provider value={value}>
      <ApiClientTabsContext.Provider value={tabsValue}>
        {children}
      </ApiClientTabsContext.Provider>
    </ApiClientPageContext.Provider>
  );
}
