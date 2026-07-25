import { useState, useEffect, useRef } from "react";
import {
  Plus, Folder, FileText, Trash2, ChevronRight, ChevronDown,
  Search, MoreVertical, Pencil, FolderPlus, FilePlus,
} from "lucide-react";
import type { ApiCollection, ApiCollectionNode } from "@/lib/types";

interface CollectionTreeProps {
  collections: ApiCollection[];
  selectedNodeId: string | null;
  selectedCollectionId: string | null;
  onSelectNode: (node: ApiCollectionNode, collectionId: string) => void;
  onAddCollection: () => void;
  onAddRequest: (collectionId: string, parentId?: string) => void;
  onAddFolder: (collectionId: string, parentId?: string) => void;
  onDeleteNode: (nodeId: string, collectionId: string) => void;
  onRenameNode: (nodeId: string, collectionId: string, newName: string) => void;
}

const methodColors: Record<string, string> = {
  Get: "text-blue-500",
  Post: "text-green-500",
  Put: "text-yellow-500",
  Patch: "text-orange-500",
  Delete: "text-red-500",
  Head: "text-purple-500",
  Options: "text-gray-500",
  GraphQl: "text-pink-500",
  WebSocket: "text-cyan-500",
};

function matchesSearch(node: ApiCollectionNode, query: string): boolean {
  if (!query) return true;
  const q = query.toLowerCase();
  if (node.name.toLowerCase().includes(q)) return true;
  if (node.type === "Request" && node.request) {
    if (node.request.url.toLowerCase().includes(q)) return true;
    if (node.request.method.toLowerCase().includes(q)) return true;
  }
  if (node.type === "Folder") {
    return node.children.some((c) => matchesSearch(c, q));
  }
  return false;
}

function filterNodes(nodes: ApiCollectionNode[], query: string): ApiCollectionNode[] {
  if (!query) return nodes;
  return nodes
    .filter((n) => matchesSearch(n, query))
    .map((n) => (n.type === "Folder" ? { ...n, children: filterNodes(n.children, query) } : n));
}

interface ContextMenuState {
  x: number;
  y: number;
  nodeId: string;
  collectionId: string;
  isCollection: boolean;
  nodeType: "Folder" | "Request";
}

export function CollectionTree({
  collections,
  selectedNodeId,
  selectedCollectionId,
  onSelectNode,
  onAddCollection,
  onAddRequest,
  onAddFolder,
  onDeleteNode,
  onRenameNode,
}: CollectionTreeProps) {
  const [expandedIds, setExpandedIds] = useState<Set<string>>(() =>
    new Set(collections.map((c) => c.id)),
  );
  const [search, setSearch] = useState("");
  const [contextMenu, setContextMenu] = useState<ContextMenuState | null>(null);
  const [renamingId, setRenamingId] = useState<string | null>(null);
  const [renameValue, setRenameValue] = useState("");
  const [renameCollectionId, setRenameCollectionId] = useState<string | null>(null);
  const renameInputRef = useRef<HTMLInputElement | null>(null);

  useEffect(() => {
    setExpandedIds((prev) => {
      const next = new Set(prev);
      collections.forEach((c) => next.add(c.id));
      return next;
    });
  }, [collections]);

  useEffect(() => {
    if (renamingId && renameInputRef.current) {
      renameInputRef.current.focus();
      renameInputRef.current.select();
    }
  }, [renamingId]);

  const toggleExpand = (id: string) => {
    setExpandedIds((prev) => {
      const next = new Set(prev);
      if (next.has(id)) next.delete(id);
      else next.add(id);
      return next;
    });
  };

  const expandAll = () => {
    const all = new Set<string>();
    const collect = (nodes: ApiCollectionNode[]) => {
      for (const n of nodes) {
        all.add(n.id);
        if (n.type === "Folder") collect(n.children);
      }
    };
    collections.forEach((c) => {
      all.add(c.id);
      collect(c.nodes);
    });
    setExpandedIds(all);
  };

  const collapseAll = () => {
    setExpandedIds(new Set(collections.map((c) => c.id)));
  };

  const startRename = (nodeId: string, currentName: string, collectionId: string) => {
    setRenamingId(nodeId);
    setRenameValue(currentName);
    setRenameCollectionId(collectionId);
    setContextMenu(null);
  };

  const confirmRename = () => {
    if (renamingId && renameValue.trim() && renameCollectionId) {
      onRenameNode(renamingId, renameCollectionId, renameValue.trim());
    }
    setRenamingId(null);
    setRenameValue("");
    setRenameCollectionId(null);
  };

  const handleContextMenu = (e: React.MouseEvent, nodeId: string, collectionId: string, isCollection: boolean, nodeType: "Folder" | "Request") => {
    e.preventDefault();
    e.stopPropagation();
    setContextMenu({ x: e.clientX, y: e.clientY, nodeId, collectionId, isCollection, nodeType });
  };

  useEffect(() => {
    const close = () => setContextMenu(null);
    if (contextMenu) {
      document.addEventListener("click", close);
      document.addEventListener("contextmenu", close);
      return () => {
        document.removeEventListener("click", close);
        document.removeEventListener("contextmenu", close);
      };
    }
  }, [contextMenu]);

  const filteredCollections = search
    ? collections
        .map((c) => ({ ...c, nodes: filterNodes(c.nodes, search) }))
        .filter(
          (c) =>
            c.name.toLowerCase().includes(search.toLowerCase()) ||
            c.nodes.length > 0,
        )
    : collections;

  const renderNode = (node: ApiCollectionNode, collectionId: string, depth = 0) => {
    const isExpanded = expandedIds.has(node.id);
    const isSelected = selectedNodeId === node.id;
    const hasChildren = node.type === "Folder" && node.children && node.children.length > 0;
    const isRenaming = renamingId === node.id;
    const method = node.type === "Request" && node.request ? node.request.method : null;

    return (
      <div key={node.id}>
        <div
          data-testid={`collection-node-${node.type}-${node.id}`}
          className={`group flex cursor-pointer items-center gap-1 rounded px-2 py-1 text-sm ${
            isSelected ? "bg-primary text-primary-foreground" : "hover:bg-accent"
          }`}
          style={{ paddingLeft: `${12 + depth * 16}px` }}
          onClick={() => onSelectNode(node, collectionId)}
          onDoubleClick={(e) => {
            e.stopPropagation();
            startRename(node.id, node.name, collectionId);
          }}
          onContextMenu={(e) => handleContextMenu(e, node.id, collectionId, false, node.type)}
        >
          {node.type === "Folder" && (
            <button
              data-testid={`folder-toggle-${node.id}`}
              className="p-0.5"
              onClick={(e) => {
                e.stopPropagation();
                toggleExpand(node.id);
              }}
            >
              {isExpanded ? <ChevronDown className="h-3 w-3" /> : <ChevronRight className="h-3 w-3" />}
            </button>
          )}
          {node.type === "Folder" && <Folder className="h-4 w-4 shrink-0" />}
          {node.type === "Request" && (
            <>
              {method && (
                <span className={`shrink-0 font-mono text-[10px] font-bold ${methodColors[method] ?? ""}`}>
                  {method.toUpperCase().slice(0, 4)}
                </span>
              )}
              <FileText className="h-3.5 w-3.5 shrink-0 opacity-50" />
            </>
          )}
          {isRenaming ? (
            <input
              ref={renameInputRef}
              type="text"
              value={renameValue}
              onChange={(e) => setRenameValue(e.target.value)}
              onBlur={confirmRename}
              onKeyDown={(e) => {
                if (e.key === "Enter") confirmRename();
                if (e.key === "Escape") { setRenamingId(null); setRenameValue(""); }
              }}
              onClick={(e) => e.stopPropagation()}
              className="flex-1 rounded border bg-background px-1 py-0 text-sm"
            />
          ) : (
            <span className="flex-1 truncate">{node.name}</span>
          )}
          {!isRenaming && (
            <button
              data-testid={`node-menu-${node.id}`}
              className="p-0.5 opacity-0 hover:bg-accent-foreground/10 group-hover:opacity-100"
              onClick={(e) => {
                e.stopPropagation();
                handleContextMenu(e, node.id, collectionId, false, node.type);
              }}
            >
              <MoreVertical className="h-3 w-3" />
            </button>
          )}
        </div>
        {node.type === "Folder" && isExpanded && hasChildren && (
          <div>
            {node.children!.map((child) => renderNode(child, collectionId, depth + 1))}
          </div>
        )}
      </div>
    );
  };

  return (
    <>
      <div className="flex h-full w-64 flex-col border-r bg-card" data-testid="collection-tree">
        {/* Header */}
        <div className="flex items-center justify-between border-b p-2">
          <span className="text-sm font-semibold">Collections</span>
          <div className="flex gap-1">
            <button
              data-testid="add-collection-button"
              className="rounded p-1 hover:bg-accent"
              title="Add collection"
              onClick={onAddCollection}
            >
              <Plus className="h-4 w-4" />
            </button>
            <button
              data-testid="add-request-button"
              className="rounded p-1 hover:bg-accent disabled:opacity-40"
              title="Add request"
              disabled={!selectedCollectionId}
              onClick={() => selectedCollectionId && onAddRequest(selectedCollectionId)}
            >
              <FileText className="h-4 w-4" />
            </button>
            <button
              data-testid="add-folder-button"
              className="rounded p-1 hover:bg-accent disabled:opacity-40"
              title="Add folder"
              disabled={!selectedCollectionId}
              onClick={() => selectedCollectionId && onAddFolder(selectedCollectionId)}
            >
              <Folder className="h-4 w-4" />
            </button>
          </div>
        </div>

        {/* Search + expand/collapse */}
        <div className="flex items-center gap-1 border-b px-2 py-1">
          <Search className="h-3 w-3 shrink-0 text-muted-foreground" />
          <input
            type="text"
            data-testid="collection-search"
            value={search}
            onChange={(e) => setSearch(e.target.value)}
            placeholder="Filter..."
            className="flex-1 rounded border bg-background px-2 py-0.5 text-xs"
          />
          <button
            onClick={expandAll}
            title="Expand all"
            className="rounded p-0.5 text-xs text-muted-foreground hover:bg-accent"
          >
            <ChevronDown className="h-3 w-3" />
          </button>
          <button
            onClick={collapseAll}
            title="Collapse all"
            className="rounded p-0.5 text-xs text-muted-foreground hover:bg-accent"
          >
            <ChevronRight className="h-3 w-3" />
          </button>
        </div>

        {/* Tree */}
        <div className="flex-1 overflow-auto p-2">
          {collections.length === 0 && (
            <div className="p-2 text-xs text-muted-foreground">
              No collections. Click + to create one.
            </div>
          )}
          {filteredCollections.map((collection) => {
            const isExpanded = expandedIds.has(collection.id);
            const isSelected = selectedNodeId === collection.id;
            const isRenaming = renamingId === collection.id;
            return (
              <div key={collection.id}>
                <div
                  data-testid={`collection-root-${collection.id}`}
                  className={`group flex cursor-pointer items-center gap-1 rounded px-2 py-1 text-sm font-medium ${
                    isSelected ? "bg-primary text-primary-foreground" : "hover:bg-accent"
                  }`}
                  onClick={() =>
                    onSelectNode(
                      {
                        id: collection.id,
                        type: "Folder",
                        name: collection.name,
                        isExpanded: true,
                        children: collection.nodes,
                        defaultAuth: collection.defaultAuth,
                        request: null,
                      },
                      collection.id,
                    )
                  }
                  onDoubleClick={(e) => {
                    e.stopPropagation();
                    startRename(collection.id, collection.name, collection.id);
                  }}
                  onContextMenu={(e) => handleContextMenu(e, collection.id, collection.id, true, "Folder")}
                >
                  <button
                    className="p-0.5"
                    onClick={(e) => {
                      e.stopPropagation();
                      toggleExpand(collection.id);
                    }}
                  >
                    {isExpanded ? (
                      <ChevronDown className="h-3 w-3" />
                    ) : (
                      <ChevronRight className="h-3 w-3" />
                    )}
                  </button>
                  <Folder className="h-4 w-4 shrink-0" />
                  {isRenaming ? (
                    <input
                      ref={renameInputRef}
                      type="text"
                      value={renameValue}
                      onChange={(e) => setRenameValue(e.target.value)}
                      onBlur={confirmRename}
                      onKeyDown={(e) => {
                        if (e.key === "Enter") confirmRename();
                        if (e.key === "Escape") { setRenamingId(null); setRenameValue(""); }
                      }}
                      onClick={(e) => e.stopPropagation()}
                      className="flex-1 rounded border bg-background px-1 py-0 text-sm"
                    />
                  ) : (
                    <span className="flex-1 truncate">{collection.name}</span>
                  )}
                  {!isRenaming && (
                    <button
                      data-testid={`node-menu-${collection.id}`}
                      className="p-0.5 opacity-0 hover:bg-accent-foreground/10 group-hover:opacity-100"
                      onClick={(e) => {
                        e.stopPropagation();
                        handleContextMenu(e, collection.id, collection.id, true, "Folder");
                      }}
                    >
                      <MoreVertical className="h-3 w-3" />
                    </button>
                  )}
                </div>
                {isExpanded && collection.nodes.map((node) => renderNode(node, collection.id))}
              </div>
            );
          })}
        </div>
      </div>

      {/* Context menu */}
      {contextMenu && (
        <div
          className="fixed z-50 min-w-[160px] rounded-md border bg-popover py-1 shadow-lg"
          style={{
            left: Math.min(contextMenu.x, window.innerWidth - 180),
            top: Math.min(contextMenu.y, window.innerHeight - 200),
          }}
          data-testid="tree-context-menu"
          onClick={(e) => e.stopPropagation()}
        >
          {(contextMenu.isCollection || contextMenu.nodeType === "Folder") && (
            <>
              <button
                className="flex w-full items-center gap-2 px-3 py-1.5 text-sm hover:bg-accent"
                onClick={() => {
                  onAddRequest(
                    contextMenu.collectionId,
                    contextMenu.isCollection ? undefined : contextMenu.nodeId,
                  );
                  setContextMenu(null);
                }}
                data-testid="ctx-add-request"
              >
                <FilePlus className="h-3.5 w-3.5" /> New Request
              </button>
              <button
                className="flex w-full items-center gap-2 px-3 py-1.5 text-sm hover:bg-accent"
                onClick={() => {
                  onAddFolder(
                    contextMenu.collectionId,
                    contextMenu.isCollection ? undefined : contextMenu.nodeId,
                  );
                  setContextMenu(null);
                }}
                data-testid="ctx-add-folder"
              >
                <FolderPlus className="h-3.5 w-3.5" /> New Folder
              </button>
            </>
          )}
          <button
            className="flex w-full items-center gap-2 px-3 py-1.5 text-sm hover:bg-accent"
            onClick={() => {
              const node = contextMenu.isCollection
                ? collections.find((c) => c.id === contextMenu.nodeId)
                : findNodeInCollections(collections, contextMenu.nodeId);
              if (node) startRename(contextMenu.nodeId, node.name, contextMenu.collectionId);
            }}
            data-testid="ctx-rename"
          >
            <Pencil className="h-3.5 w-3.5" /> Rename
          </button>
          <button
            className="flex w-full items-center gap-2 px-3 py-1.5 text-sm text-destructive hover:bg-destructive/10"
            onClick={() => {
              onDeleteNode(contextMenu.nodeId, contextMenu.collectionId);
              setContextMenu(null);
            }}
            data-testid="ctx-delete"
          >
            <Trash2 className="h-3.5 w-3.5" /> Delete
          </button>
        </div>
      )}
    </>
  );
}

function findNodeInCollections(
  collections: ApiCollection[],
  nodeId: string,
): ApiCollectionNode | undefined {
  for (const c of collections) {
    const found = findNode(c.nodes, nodeId);
    if (found) return found;
  }
  return undefined;
}

function findNode(
  nodes: ApiCollectionNode[],
  nodeId: string,
): ApiCollectionNode | undefined {
  for (const n of nodes) {
    if (n.id === nodeId) return n;
    if (n.type === "Folder") {
      const found = findNode(n.children, nodeId);
      if (found) return found;
    }
  }
  return undefined;
}
