import { useState } from "react";
import { useCollections, useUpdateCollections, useExecuteRequest } from "@/lib/hooks";
import { CollectionTree } from "./CollectionTree";
import { RequestEditor } from "./RequestEditor";
import { ResponseViewer } from "./ResponseViewer";
import { NameDialog, ConfirmDialog } from "./Dialogs";
import type {
  ApiCollection,
  ApiCollectionNode,
  HttpRequestEntry,
  ApiClientExecutionResponse,
  ApiRequestMethod,
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

export function ApiClientPage() {
  const { data: collections = [], isLoading } = useCollections();
  const updateCollections = useUpdateCollections();
  const executeRequest = useExecuteRequest();

  const [selectedNodeId, setSelectedNodeId] = useState<string | null>(null);
  const [selectedCollectionId, setSelectedCollectionId] = useState<string | null>(null);
  const [draftRequest, setDraftRequest] = useState<HttpRequestEntry | null>(null);
  const [response, setResponse] = useState<ApiClientExecutionResponse | null>(null);
  const [sending, setSending] = useState(false);
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

  const handleSelectNode = (node: ApiCollectionNode, collectionId: string) => {
    setSelectedNodeId(node.id);
    setSelectedCollectionId(collectionId);
    setResponse(null);
    if (node.type === "Request" && node.request) {
      setDraftRequest(deepClone(node.request));
    } else {
      setDraftRequest(null);
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
            setDraftRequest(deepClone(request));
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
              setDraftRequest(null);
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
    if (draftRequest && _nodeId === selectedNodeId) {
      setDraftRequest({ ...draftRequest, name: newName });
    }
  };

  const handleSave = () => {
    if (!draftRequest || !selectedNodeId) return;
    const next = updateRequestInCollections(collections, selectedNodeId, draftRequest);
    updateCollections.mutate(next);
  };

  const handleSend = async () => {
    if (!draftRequest) return;
    setSending(true);
    setResponse(null);
    try {
      const result = await executeRequest.mutateAsync({
        request: draftRequest,
        collectionId: selectedCollectionId ?? undefined,
      });
      setResponse(result);
    } catch (err) {
      setResponse({
        resolvedUrl: draftRequest.url,
        method: draftRequest.method,
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
      });
    } finally {
      setSending(false);
    }
  };

  if (isLoading) {
    return (
      <div className="flex h-full items-center justify-center" data-testid="api-client-page">
        Loading collections...
      </div>
    );
  }

  return (
    <div className="flex h-full" data-testid="api-client-page">
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
      />

      <div className="flex w-96 flex-col border-r">
        {draftRequest ? (
          <RequestEditor
            request={draftRequest}
            onChange={setDraftRequest}
            onSend={handleSend}
            onSave={handleSave}
            sending={sending}
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
          response={response}
          sending={sending}
          request={draftRequest ? { method: draftRequest.method, url: draftRequest.url } : null}
        />
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
    </div>
  );
}
