import { useState, useMemo, useCallback, useEffect } from "react";
import { useLocation, useNavigate } from "react-router-dom";
import { useQueryClient } from "@tanstack/react-query";
import { Globe, Settings2, GitBranch, AlertTriangle } from "lucide-react";
import {
  useCollections,
  useUpdateCollections,
  useExecuteRequest,
  useEnvironments,
  useUpdateEnvironments,
} from "@/lib/hooks";
import { CollectionTree } from "./CollectionTree";
import { RequestEditor } from "./RequestEditor";
import { ResponseViewer } from "./ResponseViewer";
import { NameDialog, ConfirmDialog } from "./Dialogs";
import { EnvironmentManager } from "./EnvironmentManager";
import { CollectionVariableEditor } from "./CollectionVariableEditor";
import { RequestTabStrip, type RequestTab } from "./RequestTabStrip";
import { CollectionExportDialog } from "./CollectionExportDialog";
import { GitPanel } from "./GitPanel";
import { ResizablePanels } from "@/components/ui/ResizablePanels";
import { buildVariableScope } from "@/lib/variable-utils";
import { getSecret } from "@/lib/tauri-bridge";
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
  return JSON.parse(JSON.stringify(obj));
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

interface TabState {
  draft: HttpRequestEntry;
  response: ApiClientExecutionResponse | null;
  sending: boolean;
  dirty: boolean;
}

export function ApiClientPage() {
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
  const [gitRepoPath, setGitRepoPath] = useState(".");
  const [conflict, setConflict] = useState<{ message: string } | null>(null);
  const [legacyNoticeDismissed, setLegacyNoticeDismissed] = useState(() =>
    typeof window !== "undefined" && localStorage.getItem("swokit-legacy-secret-notice") === "dismissed"
  );
  const legacySecretCount = useMemo(() => countLegacySecrets(collections), [collections]);
  const [nameDialog, setNameDialog] = useState<{
    title: string;
    label: string;
    defaultValue: string;
    confirmText: string;
    onConfirm: (name: string) => void;
  } | null>(null);
  const [confirmDialog, setConfirmDialog] = useState<{
    message: string;
    onConfirm: () => void;
  } | null>(null);

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
      [tabId]: { draft: deepClone(node.request!), response: null, sending: false, dirty: false },
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
              [tabId]: { draft: deepClone(request), response: null, sending: false, dirty: false },
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
      setTabStates((prev) => ({ ...prev, [activeTabId]: { ...prev[activeTabId], response: result, sending: false } }));
    } catch (err) {
      setTabStates((prev) => ({
        ...prev,
        [activeTabId]: {
          ...prev[activeTabId],
          sending: false,
          response: {
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
          },
        },
      }));
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
      setTabStates((prev) => ({ ...prev, [tabId]: { draft: deepClone(copy), response: null, sending: false, dirty: false } }));
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

  const activeTab = activeTabId ? tabs.find((t) => t.id === activeTabId) : null;
  const activeCollection = activeTab
    ? collections.find((c) => c.id === activeTab.collectionId)
    : null;
  const variableScope = buildVariableScope(
    activeCollection?.variables ?? [],
    activeEnvironment,
  );

  if (isLoading) {
    return (
      <div className="flex h-full items-center justify-center" data-testid="api-client-page">
        Loading collections...
      </div>
    );
  }

  return (
    <div className="flex h-full min-w-0 flex-col" data-testid="api-client-page">
      {/* Toolbar */}
      <div className="flex flex-wrap items-center gap-2 border-b px-3 py-1.5 bg-card">
        <Globe className="h-4 w-4 text-muted-foreground" />
        <select
          data-testid="env-selector"
          value={activeEnvironmentId ?? ""}
          onChange={(e) => handleSetActiveEnvironment(e.target.value || null)}
          className="rounded border bg-background px-2 py-1 text-xs"
        >
          <option value="">— No environment —</option>
          {environments.map((env) => (
            <option key={env.id} value={env.id}>{env.name}</option>
          ))}
        </select>
        <button
          onClick={() => setShowEnvManager(true)}
          className="flex items-center gap-1 rounded border px-2 py-1 text-xs hover:bg-accent"
          data-testid="env-manager-button"
        >
          <Settings2 className="h-3 w-3" /> Manage
        </button>
        {selectedCollection && (
          <button
            onClick={() => setShowColVarEditor(true)}
            className="rounded border px-2 py-1 text-xs hover:bg-accent"
            data-testid="col-vars-button"
          >
            Collection Variables
          </button>
        )}
        {activeEnvironment && (
          <span className="text-xs text-muted-foreground" data-testid="active-env-name">
            {activeEnvironment.name} ({activeEnvironment.variables.filter((v) => v.isEnabled).length} vars)
          </span>
        )}
        <div className="ml-auto" />
        <button
          onClick={() => setShowGitPanel(!showGitPanel)}
          className={`flex items-center gap-1 rounded border px-2 py-1 text-xs ${showGitPanel ? "bg-primary text-primary-foreground" : "hover:bg-accent"}`}
          data-testid="api-client-git-toggle"
        >
          <GitBranch className="h-3 w-3" /> Git
        </button>
      </div>

      {/* Conflict-resolution banner */}
      {conflict && (
        <div className="flex flex-wrap items-center gap-3 border-b bg-destructive/10 px-4 py-3" data-testid="conflict-banner">
          <AlertTriangle className="h-5 w-5 shrink-0 text-destructive" />
          <span className="flex-1 text-sm">{conflict.message}</span>
          <button onClick={handleReloadConflict} className="rounded border px-3 py-1.5 text-xs hover:bg-accent" data-testid="conflict-reload">Reload</button>
          <button onClick={handleOverwriteConflict} className="rounded bg-destructive px-3 py-1.5 text-xs text-destructive-foreground hover:opacity-90" data-testid="conflict-overwrite">Overwrite</button>
          <button onClick={handleSaveAsCopy} className="rounded border px-3 py-1.5 text-xs hover:bg-accent" data-testid="conflict-copy">Save as copy</button>
          <button onClick={() => setConflict(null)} className="rounded border px-3 py-1.5 text-xs hover:bg-accent" data-testid="conflict-dismiss">Dismiss</button>
        </div>
      )}

      {/* Legacy plaintext secret notice */}
      {legacySecretCount > 0 && !legacyNoticeDismissed && (
        <div className="flex items-start gap-2 border-b bg-amber-50 px-3 py-2 text-xs text-amber-900 dark:bg-amber-950 dark:text-amber-100" data-testid="legacy-secret-notice">
          <span className="flex-1">
            {legacySecretCount} API Client auth value{legacySecretCount === 1 ? "" : "s"} look{legacySecretCount === 1 ? "s" : ""} like a raw secret stored in collections.json.
            Re-enter {legacySecretCount === 1 ? "it" : "them"} to move {legacySecretCount === 1 ? "it" : "them"} to the secure store.
          </span>
          <button
            onClick={() => {
              localStorage.setItem("swokit-legacy-secret-notice", "dismissed");
              setLegacyNoticeDismissed(true);
            }}
            className="shrink-0 rounded border px-2 py-0.5 hover:bg-amber-100 dark:hover:bg-amber-900"
            data-testid="legacy-secret-notice-dismiss"
          >
            Dismiss
          </button>
        </div>
      )}

      {/* Main 3-pane layout */}
      <div className="flex min-w-0 flex-1 overflow-hidden">
        <ResizablePanels initialWidths={[260, 540, null]} minWidths={[180, 360, 260]} className="w-full min-w-0">
          <CollectionTree
            collections={collections}
            selectedNodeId={selectedNodeId}
            selectedCollectionId={selectedCollectionId}
            onSelectNode={handleSelectNode}
            onAddCollection={handleAddCollection}
            onAddRequest={handleAddRequest}
            onAddFolder={handleAddFolder}
            onDeleteNode={handleDeleteNode}
            onRenameNode={handleRenameNode}
            onExportCollection={setExportCollectionId}
          />

          <div className="flex h-full w-full flex-col border-r">
            <RequestTabStrip
              tabs={tabs}
              activeTabId={activeTabId}
              onSelectTab={setActiveTabId}
              onCloseTab={closeTab}
            />
            {activeTabId && tabStates[activeTabId] ? (
              <RequestEditor
                request={tabStates[activeTabId].draft}
                onChange={(req) => updateTabDraft(activeTabId, req)}
                onSend={handleSend}
                onSave={handleSave}
                sending={tabStates[activeTabId].sending}
                variableScope={variableScope}
                environments={environments}
                captureWarnings={tabStates[activeTabId]?.response?.captureWarnings ?? []}
              />
            ) : (
              <div className="flex h-full flex-col items-center justify-center gap-2 p-6 text-sm text-muted-foreground">
                <span data-testid="api-client-empty-editor">
                  Select or create a request to start editing.
                </span>
              </div>
            )}
          </div>

          <div className="flex h-full w-full flex-col overflow-hidden">
            <ResponseViewer
              response={activeTabId ? tabStates[activeTabId]?.response ?? null : null}
              sending={activeTabId ? tabStates[activeTabId]?.sending ?? false : false}
              request={activeTabId ? tabStates[activeTabId]?.draft ?? null : null}
            />
          </div>
        </ResizablePanels>
      </div>

      {/* Dialogs */}
      {nameDialog && (
        <NameDialog
          title={nameDialog.title}
          label={nameDialog.label}
          defaultValue={nameDialog.defaultValue}
          confirmText={nameDialog.confirmText}
          onConfirm={nameDialog.onConfirm}
          onCancel={() => setNameDialog(null)}
        />
      )}
      {confirmDialog && (
        <ConfirmDialog
          message={confirmDialog.message}
          onConfirm={confirmDialog.onConfirm}
          onCancel={() => setConfirmDialog(null)}
        />
      )}
      {showEnvManager && (
        <EnvironmentManager
          environments={environments}
          collections={collections}
          activeEnvironmentId={activeEnvironmentId}
          onSave={handleSaveEnvironments}
          onClose={() => setShowEnvManager(false)}
        />
      )}
      {showColVarEditor && selectedCollection && (
        <CollectionVariableEditor
          collection={selectedCollection}
          onSave={handleSaveCollectionVariables}
          onClose={() => setShowColVarEditor(false)}
        />
      )}
      {exportCollectionId && exportCollection && (
        <CollectionExportDialog
          collection={exportCollection}
          environments={environments}
          onClose={() => setExportCollectionId(null)}
        />
      )}

      {/* Git side panel */}
      {showGitPanel && (
        <div className="fixed right-0 top-0 bottom-0 z-40 w-96 border-l bg-card shadow-lg" data-testid="api-client-git-panel">
          <GitPanel repoPath={gitRepoPath} onRepoPathChange={setGitRepoPath} />
          <button
            onClick={() => setShowGitPanel(false)}
            className="absolute right-2 top-2 rounded p-1 text-muted-foreground hover:bg-accent"
            data-testid="api-client-git-close"
          >
            ✕
          </button>
        </div>
      )}
    </div>
  );
}
