import {
  createContext,
  useContext,
  useState,
  useMemo,
  useCallback,
  useEffect,
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

function findRequestNode(nodes: ApiCollectionNode[], nodeId: string): ApiCollectionNode | null {
  for (const node of nodes) {
    if (node.id === nodeId) return node;
    if (node.children) {
      const found = findRequestNode(node.children, nodeId);
      if (found) return found;
    }
  }
  return null;
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

export interface NameDialogState {
  title: string;
  label: string;
  defaultValue: string;
  confirmText: string;
  onConfirm: (name: string) => void;
}

export interface ConfirmDialogState {
  message: string;
  onConfirm: () => void;
}

export interface ApiClientPageContextValue {
  collections: ApiCollection[];
  isLoading: boolean;

  environments: ApiEnvironment[];
  activeEnvironmentId: string | null;
  activeEnvironment: ApiEnvironment | null;
  handleSetActiveEnvironment: (envId: string | null) => void;
  handleSaveEnvironments: (envs: ApiEnvironment[], activeId: string | null) => void;

  selectedNodeId: string | null;
  selectedCollectionId: string | null;
  selectedCollection: ApiCollection | null;
  handleSelectNode: (node: ApiCollectionNode, collectionId: string) => void;

  tabs: RequestTab[];
  activeTabId: string | null;
  setActiveTabId: (tabId: string | null) => void;
  tabStates: Record<string, TabState>;
  closeTab: (tabId: string) => void;
  updateTabDraft: (tabId: string, draft: HttpRequestEntry) => void;

  activeTab: RequestTab | null;
  activeCollection: ApiCollection | null | undefined;
  variableScope: Record<string, string | null>;

  handleAddCollection: () => void;
  handleAddRequest: (collectionId: string, parentId?: string) => void;
  handleAddFolder: (collectionId: string, parentId?: string) => void;
  handleDeleteNode: (nodeId: string, collectionId: string) => void;
  handleRenameNode: (nodeId: string, collectionId: string, newName: string) => void;

  handleSave: () => Promise<boolean>;
  handleSend: () => Promise<void>;
  handleSaveExample: (name: string, response: ApiClientExecutionResponse) => Promise<void>;

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

const ApiClientPageContext = createContext<ApiClientPageContextValue | null>(null);

export function useApiClientPageContext(): ApiClientPageContextValue {
  const ctx = useContext(ApiClientPageContext);
  if (!ctx) throw new Error("useApiClientPageContext must be used within ApiClientPageProvider");
  return ctx;
}

export function ApiClientPageProvider({ children }: { children: ReactNode }): JSX.Element {
  const { data: collections = [], isLoading } = useCollections();
  const updateCollections = useUpdateCollections();
  const executeRequest = useExecuteRequest();
  const { data: envData } = useEnvironments();
  const updateEnvironments = useUpdateEnvironments();
  const location = useLocation();
  const navigate = useNavigate();
  const qc = useQueryClient();

  const environments = envData?.environments ?? [];
  const uiState = envData?.uiState;
  const activeEnvironmentId = uiState?.activeEnvironmentId ?? null;

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

  const openTab = useCallback((node: ApiCollectionNode, collectionId: string) => {
    if (node.type !== "Request" || !node.request) return;
    const existingTab = tabs.find((t) => t.nodeId === node.id);
    if (existingTab) {
      setActiveTabId(existingTab.id);
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
    };
    setTabs((prev) => [...prev, tab]);
    setTabStates((prev) => ({
      ...prev,
      [tabId]: emptyTabState(deepClone(node.request!)),
    }));
    setActiveTabId(tabId);
  }, [tabs]);

  useEffect(() => {
    const state = location.state as { collectionId?: string; nodeId?: string } | null;
    if (!state?.collectionId || !state?.nodeId) return;
    const collection = collections.find((c) => c.id === state.collectionId);
    const node = collection ? findRequestNode(collection.nodes, state.nodeId) : null;
    if (node?.type === "Request" && node.request) {
      setSelectedCollectionId(state.collectionId);
      setSelectedNodeId(state.nodeId);
      openTab(node, state.collectionId);
    }
    navigate(location.pathname, { replace: true, state: null });
  }, [location, collections, navigate, openTab]);

  const closeTab = useCallback((tabId: string) => {
    const tabState = tabStates[tabId];
    if (tabState?.dirty) {
      setConfirmDialog({
        message: `Close "${tabs.find((t) => t.id === tabId)?.name}" with unsaved changes?`,
        onConfirm: () => {
          setTabs((prev) => prev.filter((t) => t.id !== tabId));
          setTabStates((prev) => { const next = { ...prev }; delete next[tabId]; return next; });
          if (activeTabId === tabId) setActiveTabId(null);
          setConfirmDialog(null);
        },
      });
      return;
    }
    setTabs((prev) => prev.filter((t) => t.id !== tabId));
    setTabStates((prev) => { const next = { ...prev }; delete next[tabId]; return next; });
    if (activeTabId === tabId) setActiveTabId(null);
  }, [tabStates, tabs, activeTabId]);

  const updateTabDraft = useCallback((tabId: string, draft: HttpRequestEntry) => {
    setTabStates((prev) => ({
      ...prev,
      [tabId]: { ...prev[tabId], draft, dirty: true },
    }));
    setTabs((prev) => prev.map((t) => t.id === tabId ? { ...t, name: draft.name, method: draft.method, dirty: true } : t));
  }, []);

  const handleSelectNode = (node: ApiCollectionNode, collectionId: string) => {
    setSelectedNodeId(node.id);
    setSelectedCollectionId(collectionId);
    if (node.type === "Request" && node.request) {
      openTab(node, collectionId);
    }
  };

  const handleAddCollection = () => {
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
        updateCollections.mutate([...collections, collection]);
        setNameDialog(null);
      },
    });
  };

  const handleAddRequest = (collectionId: string, parentId?: string) => {
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
        const next = insertIntoCollection(collections, collectionId, node, parentId);
        updateCollections.mutate(next, {
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
  };

  const handleAddFolder = (collectionId: string, parentId?: string) => {
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
          isExpanded: true,
          children: [],
          defaultAuth: null,
          request: null,
        };
        const next = insertIntoCollection(collections, collectionId, node, parentId);
        updateCollections.mutate(next);
        setNameDialog(null);
      },
    });
  };

  const handleDeleteNode = (nodeId: string, collectionId: string) => {
    setConfirmDialog({
      message: "Delete this item? This cannot be undone.",
      onConfirm: () => {
        const next = removeNode(collections, nodeId);
        updateCollections.mutate(next, {
          onSuccess: () => {
            if (selectedNodeId === nodeId) {
              setSelectedNodeId(null);
              // Close tab for deleted node
              const tabToClose = tabs.find((t) => t.nodeId === nodeId);
              if (tabToClose) {
                setTabs((prev) => prev.filter((t) => t.id !== tabToClose.id));
                setTabStates((prev) => { const next = { ...prev }; delete next[tabToClose.id]; return next; });
                if (activeTabId === tabToClose.id) setActiveTabId(null);
              }
              setSelectedCollectionId(collectionId === nodeId ? null : collectionId);
            }
          },
        });
        setConfirmDialog(null);
      },
    });
  };

  const handleRenameNode = (_nodeId: string, _collectionId: string, newName: string) => {
    const next = renameNodeInCollections(collections, _nodeId, newName);
    updateCollections.mutate(next);
    // Update tab name if open
    setTabs((prev) => prev.map((t) => t.nodeId === _nodeId ? { ...t, name: newName } : t));
  };

  const saveActiveTab = async (baseCollections?: ApiCollection[]): Promise<boolean> => {
    if (!activeTabId) return false;
    const tabState = tabStates[activeTabId];
    if (!tabState) return false;
    const tab = tabs.find((t) => t.id === activeTabId);
    if (!tab) return false;
    // The transient credentialSecret must never be written to collections.json.
    const draftForSave = deepClone(tabState.draft);
    if (draftForSave.auth) {
      draftForSave.auth = { ...draftForSave.auth, credentialSecret: null };
    }
    const base = baseCollections ?? collections;
    const next = updateRequestInCollections(base, tab.nodeId, draftForSave);
    try {
      await updateCollections.mutateAsync(next);
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
  };

  const handleSave = async () => saveActiveTab();

  const handleSend = async () => {
    if (!activeTabId) return;
    const tabState = tabStates[activeTabId];
    if (!tabState) return;
    const tab = tabs.find((t) => t.id === activeTabId);
    if (!tab) return;
    const saved = await handleSave();
    if (!saved) return;
    setTabStates((prev) => ({ ...prev, [activeTabId]: { ...prev[activeTabId], sending: true, response: null } }));
    try {
      // Resolve the secret from the persisted store if the editor has not already loaded it.
      const request = deepClone(tabState.draft);
      if (request.auth?.credentialKey && !request.auth.credentialSecret) {
        const secret = await getSecret(request.auth.credentialKey);
        if (secret) {
          request.auth = { ...request.auth, credentialSecret: secret };
        }
      }
      const result = await executeRequest.mutateAsync({
        request,
        collectionId: tab.collectionId ?? undefined,
        environmentId: activeEnvironmentId ?? undefined,
      });
      setTabStates((prev) => ({
        ...prev,
        [activeTabId]: { ...prev[activeTabId], response: result, sending: false, history: appendHistory(prev[activeTabId], result) },
      }));
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
  };

  /** Saves a scrubbed example onto the active request and persists it. */
  const handleSaveExample = async (name: string, response: ApiClientExecutionResponse) => {
    if (!activeTabId) return;
    const tabState = tabStates[activeTabId];
    const tab = tabs.find((t) => t.id === activeTabId);
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
    const next = updateRequestInCollections(collections, tab.nodeId, draftForSave);
    try {
      await updateCollections.mutateAsync(next);
    } catch (err) {
      console.error("Failed to save response example", err);
    }
  };

  const getLatestCollections = () =>
    qc.getQueryData<CollectionsStoreResponse>(["collections"])?.collections ?? collections;

  const handleReloadConflict = async () => {
    setConflict(null);
    await qc.refetchQueries({ queryKey: ["collections"] });
    const latest = getLatestCollections();
    if (!activeTabId) return;
    const tab = tabs.find((t) => t.id === activeTabId);
    if (!tab) return;
    for (const col of latest) {
      const node = findRequestNode(col.nodes, tab.nodeId);
      if (node?.type === "Request" && node.request) {
        setTabStates((prev) => ({ ...prev, [activeTabId]: { ...prev[activeTabId], draft: deepClone(node.request!), dirty: false } }));
        setTabs((prev) => prev.map((t) => (t.id === activeTabId ? { ...t, name: node.request!.name, method: node.request!.method, dirty: false } : t)));
        break;
      }
    }
  };

  const handleOverwriteConflict = async () => {
    setConflict(null);
    await qc.refetchQueries({ queryKey: ["collections"] });
    const latest = getLatestCollections();
    await saveActiveTab(latest);
  };

  const handleSaveAsCopy = async () => {
    setConflict(null);
    await qc.refetchQueries({ queryKey: ["collections"] });
    const latest = getLatestCollections();
    const tab = tabs.find((t) => t.id === activeTabId);
    const tabState = activeTabId ? tabStates[activeTabId] : null;
    if (!tab || !tabState) return;
    const collection = latest.find((c) => c.id === tab.collectionId);
    if (!collection) return;
    const copy = deepClone(tabState.draft);
    copy.id = newId();
    copy.name = `${copy.name} (copy)`;
    copy.createdAt = now();
    copy.updatedAt = now();
    const node: ApiCollectionNode = { id: copy.id, type: "Request", name: copy.name, isExpanded: true, children: [], defaultAuth: null, request: copy };
    const next = insertIntoCollection(latest, collection.id, node);
    try {
      await updateCollections.mutateAsync(next);
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
  };

  const handleSaveEnvironments = (envs: ApiEnvironment[], activeId: string | null) => {
    updateEnvironments.mutate({
      schemaVersion: 1,
      environments: envs,
      uiState: {
        activeEnvironmentId: activeId,
        activeEnvironmentIdByCollection: uiState?.activeEnvironmentIdByCollection ?? {},
        lastSelectedRequestIdByCollection: uiState?.lastSelectedRequestIdByCollection ?? {},
      },
    });
  };

  const handleSetActiveEnvironment = (envId: string | null) => {
    updateEnvironments.mutate({
      schemaVersion: 1,
      environments,
      uiState: {
        activeEnvironmentId: envId,
        activeEnvironmentIdByCollection: uiState?.activeEnvironmentIdByCollection ?? {},
        lastSelectedRequestIdByCollection: uiState?.lastSelectedRequestIdByCollection ?? {},
      },
    });
  };

  const handleSaveCollectionVariables = (variables: CollectionVariable[]) => {
    if (!selectedCollectionId) return;
    const next = collections.map((c) =>
      c.id === selectedCollectionId ? { ...c, variables } : c,
    );
    updateCollections.mutate(next);
  };

  const selectedCollection = useMemo(
    () => collections.find((c) => c.id === selectedCollectionId) ?? null,
    [collections, selectedCollectionId],
  );

  const exportCollection = useMemo(
    () => collections.find((c) => c.id === exportCollectionId) ?? null,
    [collections, exportCollectionId],
  );

  const activeEnvironment = environments.find((e) => e.id === activeEnvironmentId) ?? null;

  const activeTab = activeTabId ? tabs.find((t) => t.id === activeTabId) ?? null : null;
  const activeCollection = activeTab
    ? collections.find((c) => c.id === activeTab.collectionId)
    : null;
  const variableScope = buildVariableScope(
    activeCollection?.variables ?? [],
    activeEnvironment,
  );

  const dismissConflict = () => setConflict(null);

  const dismissLegacyNotice = () => {
    localStorage.setItem("swokit-legacy-secret-notice", "dismissed");
    setLegacyNoticeDismissed(true);
  };

  const value: ApiClientPageContextValue = {
    collections,
    isLoading,

    environments,
    activeEnvironmentId,
    activeEnvironment,
    handleSetActiveEnvironment,
    handleSaveEnvironments,

    selectedNodeId,
    selectedCollectionId,
    selectedCollection,
    handleSelectNode,

    tabs,
    activeTabId,
    setActiveTabId,
    tabStates,
    closeTab,
    updateTabDraft,

    activeTab,
    activeCollection,
    variableScope,

    handleAddCollection,
    handleAddRequest,
    handleAddFolder,
    handleDeleteNode,
    handleRenameNode,

    handleSave,
    handleSend,
    handleSaveExample,

    conflict,
    dismissConflict,
    handleReloadConflict,
    handleOverwriteConflict,
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
  };

  return <ApiClientPageContext.Provider value={value}>{children}</ApiClientPageContext.Provider>;
}
