import { useState, useMemo, useCallback } from "react";
import { Globe, Settings2 } from "lucide-react";
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
import type {
  ApiCollection,
  ApiCollectionNode,
  HttpRequestEntry,
  ApiClientExecutionResponse,
  ApiRequestMethod,
  ApiEnvironment,
  CollectionVariable,
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

function emptyRequest(): HttpRequestEntry {
  return {
    id: newId(),
    name: "New Request",
    method: "Get" as ApiRequestMethod,
    url: "",
    headers: [],
    queryParams: [],
    body: { mode: "None", rawContent: null, contentType: "application/json", formData: [], filePath: null },
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

  const handleSave = () => {
    if (!activeTabId) return;
    const tabState = tabStates[activeTabId];
    if (!tabState) return;
    const tab = tabs.find((t) => t.id === activeTabId);
    if (!tab) return;
    const next = updateRequestInCollections(collections, tab.nodeId, tabState.draft);
    updateCollections.mutate(next, {
      onSuccess: () => {
        setTabStates((prev) => ({ ...prev, [activeTabId]: { ...prev[activeTabId], dirty: false } }));
        setTabs((prev) => prev.map((t) => t.id === activeTabId ? { ...t, dirty: false } : t));
      },
    });
  };

  const handleSend = async () => {
    if (!activeTabId) return;
    const tabState = tabStates[activeTabId];
    if (!tabState) return;
    const tab = tabs.find((t) => t.id === activeTabId);
    if (!tab) return;
    setTabStates((prev) => ({ ...prev, [activeTabId]: { ...prev[activeTabId], sending: true, response: null } }));
    try {
      const result = await executeRequest.mutateAsync({
        request: tabState.draft,
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

  if (isLoading) {
    return (
      <div className="flex h-full items-center justify-center" data-testid="api-client-page">
        Loading collections...
      </div>
    );
  }

  return (
    <div className="flex h-full flex-col" data-testid="api-client-page">
      {/* Toolbar */}
      <div className="flex items-center gap-2 border-b px-3 py-1.5 bg-card">
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
      </div>

      {/* Main 3-pane layout */}
      <div className="flex flex-1 overflow-hidden">
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

        <div className="flex w-96 flex-col border-r">
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
            />
          ) : (
            <div className="flex h-full flex-col items-center justify-center gap-2 p-6 text-sm text-muted-foreground">
              <span data-testid="api-client-empty-editor">
                Select or create a request to start editing.
              </span>
            </div>
          )}
        </div>

        <div className="flex-1 overflow-hidden">
          <ResponseViewer
            response={activeTabId ? tabStates[activeTabId]?.response ?? null : null}
            sending={activeTabId ? tabStates[activeTabId]?.sending ?? false : false}
            request={activeTabId && tabStates[activeTabId] ? { method: tabStates[activeTabId].draft.method, url: tabStates[activeTabId].draft.url } : null}
          />
        </div>
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
    </div>
  );
}
