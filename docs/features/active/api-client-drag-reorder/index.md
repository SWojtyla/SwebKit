---
status: Proposed
---

# API Client Collection / Request Drag-and-Drop Reorder

## Scope

Let users reorder API collections and the requests/folders inside them by dragging and dropping in the existing `CollectionTree`. The persistence model already stores collections and child nodes in ordered lists, so the feature is mainly a UI/UX layer on top of the current `ApiCollection` / `ApiCollectionNode` domain.

1. **Reorder top-level collections** by dragging a collection root up or down.
2. **Reorder nodes within a collection** by dragging a request or folder up, down, or into/out of folders.
3. **Keyboard fallback** for accessibility (Alt + Arrow Up/Down to move the focused item).
4. **Demo-mode awareness** — the synthetic demo collection stays pinned and cannot be dragged.
5. **Search-mode awareness** — drag is disabled while the tree is filtered so reordering a partial list cannot corrupt the real order.

## Outcomes

- Users can reorder collections and requests visually.
- Dropped order is persisted to `collections.json` on the next save.
- Dragging respects the tree boundaries (folders can contain folders, requests cannot contain children, no cycles).
- Keyboard and screen-reader users can also reorder.
- Demo and search states do not break the feature.

## Dependencies

- `web/src/components/api-client/CollectionTree.tsx` (the virtualized tree).
- `web/src/components/api-client/ApiClientPageContext.tsx` (state mutations and persistence).
- `web/src/lib/hooks/useApiClient.ts` (`useUpdateCollections` already persists the full store).
- `src-sidecar/Endpoints/ConfigEndpoints.cs` `SaveCollectionsAsync` (already preserves list order and strips demo collection).
- `src/SwebKit.Core/Services/DemoApiCollectionFactory.DemoCollectionId`.

## Traceability

- Technical plan: `technical-plan.md`
- Test plan: `test-plan.md`
- Architecture context: `docs/architecture/architecture.md`, `docs/architecture/design.md`
