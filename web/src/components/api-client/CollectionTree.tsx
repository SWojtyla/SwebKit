import { useState, useEffect, useRef, useMemo } from "react";
import { useVirtualizer } from "@tanstack/react-virtual";
import {
  Plus, Folder, FileText, Trash2, ChevronRight, ChevronDown,
  Search, MoreVertical, Pencil, FolderPlus, FilePlus, Download,
  GripVertical,
} from "lucide-react";
import type { ApiCollection, ApiCollectionNode } from "@/lib/types";
import {
  DEMO_COLLECTION_ID,
  resolveDropTarget,
  type FlatRow,
  type MoveNodeTarget,
  type MoveCollectionTarget,
  type DragData,
} from "@/lib/collection-tree-utils";
import { MethodBadge } from "./method-badge";
import { CollectionImportButton, CollectionImportDialog } from "./CollectionImportDialog";

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
  onMoveNode: (nodeId: string, sourceCollectionId: string, target: MoveNodeTarget) => void;
  onMoveCollection: (collectionId: string, target: MoveCollectionTarget) => void;
  onExportCollection: (collectionId: string) => void;
}

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

function collectionRootNode(collection: ApiCollection): ApiCollectionNode {
  return {
    id: collection.id,
    type: "Folder",
    name: collection.name,
    isExpanded: true,
    children: collection.nodes,
    defaultAuth: collection.defaultAuth,
    request: null,
  };
}

function flattenTree(filteredCollections: ApiCollection[], expandedIds: Set<string>): FlatRow[] {
  const rows: FlatRow[] = [];

  function walk(nodes: ApiCollectionNode[], collectionId: string, depth: number) {
    for (const n of nodes) {
      rows.push({ id: n.id, node: n, collectionId, depth, isCollection: false });
      if (n.type === "Folder" && expandedIds.has(n.id)) {
        walk(n.children, collectionId, depth + 1);
      }
    }
  }

  for (const c of filteredCollections) {
    const root = collectionRootNode(c);
    rows.push({ id: c.id, node: root, collectionId: c.id, depth: 0, isCollection: true });
    if (expandedIds.has(c.id)) {
      walk(c.nodes, c.id, 1);
    }
  }

  return rows;
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
  onMoveNode,
  onMoveCollection,
  onExportCollection,
}: CollectionTreeProps) {
  const [expandedIds, setExpandedIds] = useState<Set<string>>(() =>
    new Set(collections.map((c) => c.id)),
  );
  const [search, setSearch] = useState("");
  const [contextMenu, setContextMenu] = useState<ContextMenuState | null>(null);
  const [renamingId, setRenamingId] = useState<string | null>(null);
  const [renameValue, setRenameValue] = useState("");
  const [renameCollectionId, setRenameCollectionId] = useState<string | null>(null);
  const [showImportDialog, setShowImportDialog] = useState(false);
  const [draggingRow, setDraggingRow] = useState<FlatRow | null>(null);
  const [dragOver, setDragOver] = useState<{ index: number; placement: "before" | "after" | "inside" } | null>(null);
  const renameInputRef = useRef<HTMLInputElement | null>(null);
  const listRef = useRef<HTMLDivElement | null>(null);

  useEffect(() => {
    setExpandedIds((prev) => {
      const next = new Set(prev);
      let changed = false;
      collections.forEach((c) => {
        if (!next.has(c.id)) {
          next.add(c.id);
          changed = true;
        }
      });
      return changed ? next : prev;
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

  const canDragRow = (row: FlatRow) => {
    if (search) return false;
    if (row.isCollection && row.id === DEMO_COLLECTION_ID) return false;
    return true;
  };

  const handleDragStart = (e: React.DragEvent<HTMLDivElement>, row: FlatRow) => {
    if (!canDragRow(row)) {
      e.preventDefault();
      return;
    }
    setDraggingRow(row);
    e.dataTransfer.effectAllowed = "move";
    const data: DragData = { id: row.id, collectionId: row.collectionId, kind: row.isCollection ? "collection" : "node" };
    e.dataTransfer.setData("application/json+swebkit-tree-drag", JSON.stringify(data));
  };

  const handleDragOver = (e: React.DragEvent<HTMLDivElement>, targetRow: FlatRow, targetIndex: number) => {
    e.preventDefault();
    if (!draggingRow || draggingRow.id === targetRow.id) {
      setDragOver(null);
      return;
    }
    const rect = e.currentTarget.getBoundingClientRect();
    const resolved = resolveDropTarget(draggingRow, targetRow, e.clientY, rect);
    if (!resolved) {
      setDragOver(null);
      return;
    }
    const placement = resolved.kind === "collection" ? resolved.target.placement : resolved.target.placement;
    setDragOver({ index: targetIndex, placement });
  };

  const handleDrop = (e: React.DragEvent<HTMLDivElement>, targetRow: FlatRow) => {
    e.preventDefault();
    if (!draggingRow || draggingRow.id === targetRow.id) return;
    const rect = e.currentTarget.getBoundingClientRect();
    const resolved = resolveDropTarget(draggingRow, targetRow, e.clientY, rect);
    if (!resolved) return;
    if (resolved.kind === "collection") {
      onMoveCollection(draggingRow.id, resolved.target);
    } else {
      onMoveNode(draggingRow.id, draggingRow.collectionId, resolved.target);
      if (resolved.target.placement === "inside") {
        setExpandedIds((prev) => {
          const next = new Set(prev);
          next.add(targetRow.id);
          return next;
        });
      }
    }
    setDraggingRow(null);
    setDragOver(null);
  };

  const handleDragEnd = () => {
    setDraggingRow(null);
    setDragOver(null);
  };

  const buildNodeTargetFromRow = (targetRow: FlatRow, placement: "before" | "after"): MoveNodeTarget => ({
    targetCollectionId: targetRow.collectionId,
    targetNodeId: targetRow.isCollection ? undefined : targetRow.id,
    placement,
  });

  const handleKeyboardMove = (sourceIndex: number, direction: "up" | "down") => {
    if (search) return;
    const sourceRow = flatRows[sourceIndex];
    if (!sourceRow) return;
    if (sourceRow.isCollection && sourceRow.id === DEMO_COLLECTION_ID) return;
    const targetIndex = direction === "up" ? sourceIndex - 1 : sourceIndex + 1;
    if (targetIndex < 0 || targetIndex >= flatRows.length) return;
    const targetRow = flatRows[targetIndex];
    if (sourceRow.isCollection) {
      if (!targetRow.isCollection) return;
      const placement = direction === "up" ? "before" : "after";
      onMoveCollection(sourceRow.id, { targetCollectionId: targetRow.collectionId, placement });
      return;
    }
    const placement = direction === "up" ? "before" : "after";
    onMoveNode(sourceRow.id, sourceRow.collectionId, buildNodeTargetFromRow(targetRow, placement));
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

  const filteredCollections = useMemo(
    () =>
      search
        ? collections
            .map((c) => ({ ...c, nodes: filterNodes(c.nodes, search) }))
            .filter(
              (c) =>
                c.name.toLowerCase().includes(search.toLowerCase()) ||
                c.nodes.length > 0,
            )
        : collections,
    [collections, search],
  );

  const flatRows = useMemo(
    () => flattenTree(filteredCollections, expandedIds),
    [filteredCollections, expandedIds],
  );

  const virtualizer = useVirtualizer({
    count: flatRows.length,
    getScrollElement: () => listRef.current,
    estimateSize: () => 32,
    getItemKey: (index) => flatRows[index].id,
    measureElement: (el) => el?.getBoundingClientRect().height ?? 32,
  });

  // Roving keyboard navigation across the (possibly virtualized) tree rows.
  // scrollToIndex forces the target row to render before we try to focus it,
  // which matters because react-virtual only mounts rows near the viewport.
  const focusRowByFlatIndex = (index: number) => {
    if (index < 0 || index >= flatRows.length) return;
    virtualizer.scrollToIndex(index, { align: "auto" });
    requestAnimationFrame(() => {
      const el = listRef.current?.querySelector(
        `[data-tree-index="${index}"]`,
      ) as HTMLElement | null;
      el?.focus();
    });
  };

  const handleRowKeyDown = (
    e: React.KeyboardEvent<HTMLDivElement>,
    row: FlatRow,
    rowIndex: number,
  ) => {
    const { node, collectionId } = row;
    switch (e.key) {
      case "Enter":
      case " ":
        e.preventDefault();
        onSelectNode(node, collectionId);
        break;
      case "ArrowRight":
        if (node.type === "Folder") {
          e.preventDefault();
          if (!expandedIds.has(node.id)) {
            toggleExpand(node.id);
          } else {
            focusRowByFlatIndex(rowIndex + 1);
          }
        }
        break;
      case "ArrowLeft":
        if (node.type === "Folder" && expandedIds.has(node.id)) {
          e.preventDefault();
          toggleExpand(node.id);
        }
        break;
      case "ArrowDown":
        e.preventDefault();
        if (e.altKey) {
          handleKeyboardMove(rowIndex, "down");
        } else {
          focusRowByFlatIndex(rowIndex + 1);
        }
        break;
      case "ArrowUp":
        e.preventDefault();
        if (e.altKey) {
          handleKeyboardMove(rowIndex, "up");
        } else {
          focusRowByFlatIndex(rowIndex - 1);
        }
        break;
      default:
        break;
    }
  };

  const renderRow = (row: FlatRow, rowIndex: number) => {
    const { node, collectionId, depth, isCollection } = row;
    const isExpanded = expandedIds.has(node.id);
    const isSelected = selectedNodeId === node.id;
    const isRenaming = renamingId === node.id;
    const method = node.type === "Request" && node.request ? node.request.method : null;

    const isDragOver = dragOver?.index === rowIndex;
    const dragPlacement = isDragOver ? dragOver.placement : null;
    const isDragging = draggingRow?.id === row.id;
    const draggable = canDragRow(row);

    return (
      <div
        data-testid={
          isCollection
            ? `collection-root-${node.id}`
            : `collection-node-${node.type}-${node.id}`
        }
        data-tree-index={rowIndex}
        role="treeitem"
        aria-level={depth + 1}
        aria-selected={isSelected}
        aria-expanded={node.type === "Folder" ? isExpanded : undefined}
        tabIndex={0}
        className={`group flex cursor-pointer items-center gap-1 rounded px-2 py-1 text-sm ${
          isSelected ? "bg-primary text-primary-foreground" : "hover:bg-accent"
        } ${isCollection ? "font-medium" : ""} ${
          isDragging ? "tree-dragging" : ""
        } ${
          dragPlacement === "before"
            ? "tree-drag-over-before"
            : dragPlacement === "after"
            ? "tree-drag-over-after"
            : dragPlacement === "inside"
            ? "tree-drag-over-inside"
            : ""
        }`}
        style={{ paddingLeft: `${12 + depth * 16}px` }}
        onClick={() => onSelectNode(node, collectionId)}
        onDoubleClick={(e) => {
          e.stopPropagation();
          startRename(node.id, node.name, collectionId);
        }}
        onContextMenu={(e) => handleContextMenu(e, node.id, collectionId, isCollection, node.type)}
        onKeyDown={(e) => handleRowKeyDown(e, row, rowIndex)}
        onDragOver={(e) => handleDragOver(e, row, rowIndex)}
        onDrop={(e) => handleDrop(e, row)}
      >
        <div
          data-testid={`drag-handle-${row.id}`}
          aria-label="Drag to reorder"
          draggable={draggable}
          className={`shrink-0 p-0.5 ${draggable ? "cursor-grab hover:text-primary" : "cursor-not-allowed opacity-30"}`}
          onClick={(e) => e.stopPropagation()}
          onDragStart={(e) => handleDragStart(e, row)}
          onDragEnd={handleDragEnd}
        >
          <GripVertical className="h-3 w-3" />
        </div>
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
            {method && <MethodBadge method={method} variant="text" className="w-10 text-right" />}
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
              handleContextMenu(e, node.id, collectionId, isCollection, node.type);
            }}
          >
            <MoreVertical className="h-3 w-3" />
          </button>
        )}
      </div>
    );
  };

  return (
    <>
      <div className="flex h-full w-full flex-col border-r bg-card" data-testid="collection-tree">
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
            <CollectionImportButton onOpen={() => setShowImportDialog(true)} />
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
        <div
          ref={listRef}
          className="flex-1 overflow-auto p-2"
          data-testid="collection-tree-list"
          role="tree"
          aria-label="Collections"
        >
          {collections.length === 0 && (
            <div className="p-2 text-xs text-muted-foreground">
              No collections. Click + to create one.
            </div>
          )}
          {flatRows.length === 0 && collections.length > 0 && (
            <div className="p-2 text-xs text-muted-foreground">
              No matching collections.
            </div>
          )}
          {flatRows.length > 0 && (
            <div
              style={{ height: `${virtualizer.getTotalSize()}px`, position: "relative", width: "100%" }}
              data-testid="collection-tree-virtualizer"
              role="presentation"
            >
              {virtualizer.getVirtualItems().map((item) => {
                const row = flatRows[item.index];
                return (
                  <div
                    key={item.key}
                    data-index={item.index}
                    ref={virtualizer.measureElement}
                    role="presentation"
                    style={{
                      position: "absolute",
                      top: 0,
                      left: 0,
                      width: "100%",
                      transform: `translateY(${item.start}px)`,
                    }}
                  >
                    {renderRow(row, item.index)}
                  </div>
                );
              })}
            </div>
          )}
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
          {contextMenu.isCollection && (
            <button
              className="flex w-full items-center gap-2 px-3 py-1.5 text-sm hover:bg-accent"
              onClick={() => {
                onExportCollection(contextMenu.collectionId);
                setContextMenu(null);
              }}
              data-testid="ctx-export"
            >
              <Download className="h-3.5 w-3.5" /> Export
            </button>
          )}
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

      {showImportDialog && (
        <CollectionImportDialog onClose={() => setShowImportDialog(false)} />
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
