import { useState } from "react";
import { useCollections, useUpdateCollections, useExecuteRequest } from "@/lib/hooks";
import { CollectionTree } from "./CollectionTree";
import { RequestEditor } from "./RequestEditor";
import { ResponseViewer } from "./ResponseViewer";
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
): ApiCollection[] {
  return collections.map((c) =>
    c.id === collectionId ? { ...c, nodes: [...c.nodes, node] } : c,
  );
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

export function ApiClientPage() {
  const { data: collections = [], isLoading } = useCollections();
  const updateCollections = useUpdateCollections();
  const executeRequest = useExecuteRequest();

  const [selectedNodeId, setSelectedNodeId] = useState<string | null>(null);
  const [selectedCollectionId, setSelectedCollectionId] = useState<string | null>(null);
  const [draftRequest, setDraftRequest] = useState<HttpRequestEntry | null>(null);
  const [response, setResponse] = useState<ApiClientExecutionResponse | null>(null);
  const [sending, setSending] = useState(false);

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
    const name = window.prompt("Collection name");
    if (!name?.trim()) return;
    const collection: ApiCollection = {
      id: newId(),
      name: name.trim(),
      nodes: [],
      variables: [],
      defaultAuth: null,
      createdAt: now(),
      updatedAt: now(),
    };
    updateCollections.mutate([...collections, collection]);
  };

  const handleAddRequest = () => {
    const collectionId = selectedCollectionId;
    if (!collectionId) return;
    const request = emptyRequest();
    const name = window.prompt("Request name", request.name);
    if (name) request.name = name;
    const node: ApiCollectionNode = {
      id: request.id,
      type: "Request",
      name: request.name,
      isExpanded: true,
      children: [],
      defaultAuth: null,
      request,
    };
    const next = insertIntoCollection(collections, collectionId, node);
    updateCollections.mutate(next, {
      onSuccess: () => {
        setSelectedNodeId(node.id);
        setSelectedCollectionId(collectionId);
        setDraftRequest(deepClone(request));
      },
    });
  };

  const handleAddFolder = () => {
    const collectionId = selectedCollectionId;
    if (!collectionId) return;
    const name = window.prompt("Folder name");
    if (!name?.trim()) return;
    const node: ApiCollectionNode = {
      id: newId(),
      type: "Folder",
      name: name.trim(),
      isExpanded: true,
      children: [],
      defaultAuth: null,
      request: null,
    };
    const next = insertIntoCollection(collections, collectionId, node);
    updateCollections.mutate(next);
  };

  const handleDeleteNode = (nodeId: string, collectionId: string) => {
    if (!window.confirm("Delete this item?")) return;
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
        <ResponseViewer response={response} sending={sending} />
      </div>
    </div>
  );
}
