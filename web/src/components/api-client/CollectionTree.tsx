import { useState, useEffect } from "react";
import { Plus, Folder, FileText, Trash2, ChevronRight, ChevronDown } from "lucide-react";
import type { ApiCollection, ApiCollectionNode } from "@/lib/types";

interface CollectionTreeProps {
  collections: ApiCollection[];
  selectedNodeId: string | null;
  selectedCollectionId: string | null;
  onSelectNode: (node: ApiCollectionNode, collectionId: string) => void;
  onAddCollection: () => void;
  onAddRequest: () => void;
  onAddFolder: () => void;
  onDeleteNode: (nodeId: string, collectionId: string) => void;
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
}: CollectionTreeProps) {
  const [expandedIds, setExpandedIds] = useState<Set<string>>(() =>
    new Set(collections.map((c) => c.id)),
  );

  useEffect(() => {
    setExpandedIds((prev) => {
      const next = new Set(prev);
      collections.forEach((c) => next.add(c.id));
      return next;
    });
  }, [collections]);

  const toggleExpand = (id: string) => {
    setExpandedIds((prev) => {
      const next = new Set(prev);
      if (next.has(id)) next.delete(id);
      else next.add(id);
      return next;
    });
  };

  const renderNode = (node: ApiCollectionNode, collectionId: string, depth = 0) => {
    const isExpanded = expandedIds.has(node.id);
    const isSelected = selectedNodeId === node.id;
    const hasChildren = node.type === "Folder" && node.children && node.children.length > 0;

    return (
      <div key={node.id}>
        <div
          data-testid={`collection-node-${node.type}-${node.id}`}
          className={`flex cursor-pointer items-center gap-1 rounded px-2 py-1 text-sm ${
            isSelected ? "bg-primary text-primary-foreground" : "hover:bg-accent"
          }`}
          style={{ paddingLeft: `${12 + depth * 16}px` }}
          onClick={() => onSelectNode(node, collectionId)}
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
          {node.type === "Folder" && <Folder className="h-4 w-4" />}
          {node.type === "Request" && <FileText className="h-4 w-4" />}
          <span className="flex-1 truncate">{node.name}</span>
          <button
            data-testid={`delete-node-${node.id}`}
            className="p-1 opacity-0 hover:text-destructive group-hover:opacity-100"
            onClick={(e) => {
              e.stopPropagation();
              onDeleteNode(node.id, collectionId);
            }}
          >
            <Trash2 className="h-3 w-3" />
          </button>
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
    <div className="flex h-full w-64 flex-col border-r bg-card" data-testid="collection-tree">
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
            onClick={onAddRequest}
          >
            <FileText className="h-4 w-4" />
          </button>
          <button
            data-testid="add-folder-button"
            className="rounded p-1 hover:bg-accent disabled:opacity-40"
            title="Add folder"
            disabled={!selectedCollectionId}
            onClick={onAddFolder}
          >
            <Folder className="h-4 w-4" />
          </button>
        </div>
      </div>

      <div className="flex-1 overflow-auto p-2">
        {collections.length === 0 && (
          <div className="p-2 text-xs text-muted-foreground">
            No collections. Click + to create one.
          </div>
        )}
        {collections.map((collection) => {
          const isExpanded = expandedIds.has(collection.id);
          const isSelected = selectedNodeId === collection.id;
          return (
            <div key={collection.id}>
              <div
                data-testid={`collection-root-${collection.id}`}
                className={`flex cursor-pointer items-center gap-1 rounded px-2 py-1 text-sm font-medium ${
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
                <Folder className="h-4 w-4" />
                <span className="flex-1 truncate">{collection.name}</span>
                <button
                  data-testid={`delete-collection-${collection.id}`}
                  className="p-1 opacity-0 hover:text-destructive group-hover:opacity-100"
                  onClick={(e) => {
                    e.stopPropagation();
                    onDeleteNode(collection.id, collection.id);
                  }}
                >
                  <Trash2 className="h-3 w-3" />
                </button>
              </div>
              {isExpanded && collection.nodes.map((node) => renderNode(node, collection.id))}
            </div>
          );
        })}
      </div>
    </div>
  );
}
