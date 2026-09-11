import type { ApiCollection, ApiCollectionNode } from "./types";

/** Synthetic demo collection ID used by DemoApiCollectionFactory. */
export const DEMO_COLLECTION_ID = "__demo__samples";

export interface FlatRow {
  id: string;
  node: ApiCollectionNode;
  collectionId: string;
  depth: number;
  isCollection: boolean;
}

export interface MoveNodeTarget {
  targetCollectionId: string;
  /** undefined means the collection root itself. */
  targetNodeId?: string;
  placement: "before" | "after" | "inside";
}

export interface MoveCollectionTarget {
  targetCollectionId: string;
  placement: "before" | "after";
}

export interface DragData {
  id: string;
  collectionId: string;
  kind: "collection" | "node";
}

interface RemovalResult {
  node: ApiCollectionNode;
  collectionsAfterRemoval: ApiCollection[];
}

/** Returns true if `candidateId` is the same as `ancestorId` or inside one of its descendant subtrees. */
export function isDescendant(
  nodes: ApiCollectionNode[],
  ancestorId: string,
  candidateId: string,
): boolean {
  if (ancestorId === candidateId) return true;
  for (const n of nodes) {
    if (n.id === ancestorId) {
      return nodeContains(n, candidateId);
    }
    if (n.type === "Folder" && isDescendant(n.children, ancestorId, candidateId)) {
      return true;
    }
  }
  return false;
}

function nodeContains(ancestor: ApiCollectionNode, id: string): boolean {
  if (ancestor.id === id) return true;
  if (ancestor.type !== "Folder") return false;
  return ancestor.children.some((c) => c.id === id || nodeContains(c, id));
}

function removeNodeFromNodes(
  nodes: ApiCollectionNode[],
  id: string,
): { node: ApiCollectionNode; newNodes: ApiCollectionNode[] } | null {
  for (let i = 0; i < nodes.length; i++) {
    const n = nodes[i];
    if (n.id === id) {
      return {
        node: n,
        newNodes: [...nodes.slice(0, i), ...nodes.slice(i + 1)],
      };
    }
    if (n.type === "Folder") {
      const found = removeNodeFromNodes(n.children, id);
      if (found) {
        return {
          node: found.node,
          newNodes: [
            ...nodes.slice(0, i),
            { ...n, children: found.newNodes },
            ...nodes.slice(i + 1),
          ],
        };
      }
    }
  }
  return null;
}

function removeNode(
  collections: ApiCollection[],
  id: string,
): RemovalResult | null {
  for (let i = 0; i < collections.length; i++) {
    const c = collections[i];
    const found = removeNodeFromNodes(c.nodes, id);
    if (found) {
      return {
        node: found.node,
        collectionsAfterRemoval: [
          ...collections.slice(0, i),
          { ...c, nodes: found.newNodes },
          ...collections.slice(i + 1),
        ],
      };
    }
  }
  return null;
}

function withCollection(
  collections: ApiCollection[],
  index: number,
  collection: ApiCollection,
): ApiCollection[] {
  return [...collections.slice(0, index), collection, ...collections.slice(index + 1)];
}

/**
 * Rebuilds `nodes` with `node` inserted relative to `targetNodeId`, recursing into
 * folders so a nested target is rewritten in place. Returns `null` when the target
 * is not in this subtree, which is how the caller distinguishes "nothing to do"
 * from "inserted at the root".
 *
 * This recursion replaced a `findParentList` helper that returned the array
 * *directly containing* the target and let the caller splice it — then assigned
 * the result to `collection.nodes`. For a nested target that array is a folder's
 * `children`, so dropping a request into a nested folder overwrote the whole
 * collection with the contents of one inner list: everything else in it
 * disappeared. Only a top-level target happened to be correct, which is exactly
 * what the drag-and-drop tests covered.
 */
function insertRelativeToNode(
  nodes: ApiCollectionNode[],
  targetNodeId: string,
  node: ApiCollectionNode,
  placement: "before" | "after" | "inside",
): ApiCollectionNode[] | null {
  for (let i = 0; i < nodes.length; i++) {
    const n = nodes[i];
    if (n.id === targetNodeId) {
      if (placement === "inside") {
        if (n.type === "Folder") {
          return [...nodes.slice(0, i), { ...n, children: [...n.children, node] }, ...nodes.slice(i + 1)];
        }
        // A request cannot hold children, so treat "inside a request" as "after it".
        return [...nodes.slice(0, i + 1), node, ...nodes.slice(i + 1)];
      }
      const index = placement === "before" ? i : i + 1;
      return [...nodes.slice(0, index), node, ...nodes.slice(index)];
    }
    if (n.type === "Folder") {
      const children = insertRelativeToNode(n.children, targetNodeId, node, placement);
      if (children) {
        return [...nodes.slice(0, i), { ...n, children }, ...nodes.slice(i + 1)];
      }
    }
  }
  return null;
}

function insertNode(
  collections: ApiCollection[],
  node: ApiCollectionNode,
  target: MoveNodeTarget,
): ApiCollection[] {
  const collectionIndex = collections.findIndex((c) => c.id === target.targetCollectionId);
  if (collectionIndex === -1) return collections;

  const collection = collections[collectionIndex];

  if (!target.targetNodeId) {
    // Dropping onto the collection root: prepend for "before", append otherwise.
    const nodes =
      target.placement === "before" ? [node, ...collection.nodes] : [...collection.nodes, node];
    return withCollection(collections, collectionIndex, { ...collection, nodes });
  }

  const nodes = insertRelativeToNode(collection.nodes, target.targetNodeId, node, target.placement);
  if (!nodes) return collections;
  return withCollection(collections, collectionIndex, { ...collection, nodes });
}

/** Moves a node (request or folder) to a new position. Returns a new collections array. */
export function moveNode(
  collections: ApiCollection[],
  sourceId: string,
  target: MoveNodeTarget,
): ApiCollection[] {
  if (sourceId === target.targetNodeId) return collections;

  const removal = removeNode(collections, sourceId);
  if (!removal) return collections;

  const { node, collectionsAfterRemoval } = removal;

  if (target.targetNodeId && node.type === "Folder" && nodeContains(node, target.targetNodeId)) {
    return collections;
  }

  return insertNode(collectionsAfterRemoval, node, target);
}

/** Moves a top-level collection to a new position. Demo collection stays pinned at index 0. */
export function moveCollection(
  collections: ApiCollection[],
  sourceId: string,
  target: MoveCollectionTarget,
): ApiCollection[] {
  if (sourceId === DEMO_COLLECTION_ID) return collections;
  if (sourceId === target.targetCollectionId) return collections;

  const sourceIndex = collections.findIndex((c) => c.id === sourceId);
  const targetIndex = collections.findIndex((c) => c.id === target.targetCollectionId);
  if (sourceIndex === -1 || targetIndex === -1) return collections;

  const next = collections.slice();
  const [removed] = next.splice(sourceIndex, 1);

  const newTargetIndex = next.findIndex((c) => c.id === target.targetCollectionId);
  let insertIndex = target.placement === "before" ? newTargetIndex : newTargetIndex + 1;

  // Keep the demo collection pinned at the top.
  const demoIndex = next.findIndex((c) => c.id === DEMO_COLLECTION_ID);
  if (demoIndex !== -1 && insertIndex <= demoIndex) {
    insertIndex = demoIndex + 1;
  }

  next.splice(insertIndex, 0, removed);
  return next;
}

/** Resolves a pointer drop over a target row into a concrete move instruction. */
export function resolveDropTarget(
  draggingRow: FlatRow,
  targetRow: FlatRow,
  clientY: number,
  targetRect: DOMRect,
): { kind: "collection"; target: MoveCollectionTarget } | { kind: "node"; target: MoveNodeTarget } | null {
  const threshold = targetRect.height * 0.25;
  const relative = clientY - targetRect.top;

  if (draggingRow.isCollection) {
    if (!targetRow.isCollection) return null;
    const placement: "before" | "after" = relative < targetRect.height / 2 ? "before" : "after";
    return {
      kind: "collection",
      target: { targetCollectionId: targetRow.collectionId, placement },
    };
  }

  let placement: "before" | "after" | "inside";
  if (relative < threshold) {
    placement = "before";
  } else if (relative > targetRect.height - threshold) {
    placement = "after";
  } else {
    placement = "inside";
  }

  if (targetRow.isCollection) {
    // Dropping a node around a collection root: place it at the start/end of the root node list.
    const target: MoveNodeTarget = {
      targetCollectionId: targetRow.collectionId,
      targetNodeId: undefined,
      placement: placement === "before" ? "before" : "after",
    };
    return { kind: "node", target };
  }

  if (placement === "inside" && targetRow.node.type !== "Folder") {
    placement = "after";
  }

  return {
    kind: "node",
    target: {
      targetCollectionId: targetRow.collectionId,
      targetNodeId: targetRow.node.id,
      placement,
    },
  };
}
