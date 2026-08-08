# API Client Collection / Request Drag-and-Drop Reorder — Technical Plan

## Overall approach

Use native HTML5 drag-and-drop instead of adding a new dependency. The tree is already virtualized (`@tanstack/react-virtual`) and each rendered row knows its flat index, depth, and parent, so DnD state stays small: only a `draggingId` and `dropTarget` are tracked in `CollectionTree`. The actual move logic lives in pure helpers, and `ApiClientPageContext` persists the reordered store through the existing `useUpdateCollections` mutation.

Order is stored implicitly by list index: `CollectionsStore.Collections`, `ApiCollection.Nodes`, and `ApiCollectionNode.Children` are ordered arrays, so no schema changes are required.

## 1. Pure move helpers

### New file: `web/src/lib/collection-tree-utils.ts`

```ts
export const DEMO_COLLECTION_ID = "__demo__samples";

export interface MoveNodeTarget {
  targetCollectionId: string;
  targetParentId?: string;
  targetIndex: number;
}

/** Removes a node from its current parent (any depth) and inserts it at the requested place. */
export function moveNode(
  collections: ApiCollection[],
  sourceId: string,
  sourceCollectionId: string,
  target: MoveNodeTarget,
): ApiCollection[];

/** Reorders top-level collections. */
export function moveCollection(
  collections: ApiCollection[],
  sourceId: string,
  targetIndex: number,
): ApiCollection[];

/** Returns true if `ancestorId` is the source node itself or one of its descendants. */
export function isDescendant(
  nodes: ApiCollectionNode[],
  ancestorId: string,
  candidateId: string,
): boolean;

/** Finds the list that should receive a drop and the insertion index within it. */
export function resolveDropTarget(
  collections: ApiCollection[],
  flatRows: FlatRow[],
  targetRowId: string,
  clientY: number,
  targetRect: DOMRect,
): MoveNodeTarget | null;
```

Notes:
- `moveNode` must copy immutable collections and preserve tab / selection state by keeping node IDs stable.
- `moveCollection` clamps the target index so the demo collection (if present) remains at index `0`.
- `resolveDropTarget` uses the pointer position relative to the row to decide whether the drop lands *before*, *after*, or *inside* a folder. For a folder row, the top/bottom 25% are treated as "before/after" and the middle 50% as "inside".

## 2. State and persistence

### `web/src/components/api-client/ApiClientPageContext.tsx`

Add to context value:

```ts
handleMoveNode: (
  nodeId: string,
  sourceCollectionId: string,
  target: MoveNodeTarget,
) => void;
handleMoveCollection: (collectionId: string, targetIndex: number) => void;
```

Implementation:
- Compute the next `ApiCollection[]` with `moveNode` / `moveCollection`.
- If a request is moved to another collection, update open tabs whose `nodeId` matches by setting the new `collectionId`.
- Call `updateCollections.mutate(next, { onSuccess: () => notify("success", "Moved", name), onError: (e) => notify("error", "Move failed", e.message) })`.
- Keep `selectedNodeId` and `selectedCollectionId` unchanged unless the selected collection itself was moved out of the filtered view.

## 3. Tree UI changes

### `web/src/components/api-client/CollectionTree.tsx`

Props added:

```ts
onMoveNode: (nodeId: string, sourceCollectionId: string, target: MoveNodeTarget) => void;
onMoveCollection: (collectionId: string, targetIndex: number) => void;
```

Per-row changes:
- Add a `GripVertical` drag handle at the left of every non-demo row. Use `data-testid="tree-drag-handle-{rowId}"`.
- Make the row `draggable` only when `search` is empty and the row is not the demo collection.
- `onDragStart`: set `dataTransfer` with `{ id, collectionId, kind: "collection" | "node" }` and set a local `draggingId` state for styling.
- `onDragOver`: for each virtual row, call `resolveDropTarget` using the current pointer Y and row bounding rect; highlight the row with a top/bottom/inside indicator.
- `onDrop`: clear drag state and call `onMoveNode` / `onMoveCollection`.
- `onDragEnd`: always clear drag state.

Keyboard support:
- In `handleRowKeyDown`, add `Alt+ArrowUp` / `Alt+ArrowDown` to move the focused row by one position (or by one visible row when nested).
- For a collection root, call `onMoveCollection` with `targetIndex ± 1`.
- For a node, resolve the next visible flat row in the same parent list and call `onMoveNode` with that target.

Search state:
- When `search` is non-empty, hide drag handles and set `draggable={false}` on rows.

Demo collection:
- `DemoCollectionId` is a shared constant; the demo collection row is never draggable and cannot be a drop target (or only a drop target *after* it, not before it).

## 4. Styling

### `web/src/styles/globals.css` (or inline Tailwind classes)

Add small CSS classes for drag states:

```css
.tree-drag-over-before { border-top: 2px solid var(--primary); }
.tree-drag-over-after  { border-bottom: 2px solid var(--primary); }
.tree-drag-over-inside { background-color: color-mix(in oklch, var(--primary) 15%, transparent); }
.tree-dragging         { opacity: 0.5; }
```

## 5. Backend

No new endpoint is required. `PUT /api/config/collections` already accepts the full `CollectionsStore`, writes it atomically, and strips the demo collection before saving.

If we later want a dedicated "reorder" endpoint for smaller payloads, it would be an addition in `ConfigEndpoints.cs`, but the full-store PUT is sufficient for the first pass and matches how renames and deletes already work.

## 6. Testing strategy

- Unit tests for `moveNode`, `moveCollection`, and `resolveDropTarget` in `web/src/lib/collection-tree-utils.test.ts`.
- Playwright e2e specs for:
  - dragging a request above/below another request in the same collection,
  - dragging a request into a folder,
  - dragging a collection root to reorder,
  - Alt+Arrow keys moving a focused request,
  - attempt to drag the demo collection (should be disabled).
