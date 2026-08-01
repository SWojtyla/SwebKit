# React/Tauri Frontend Architecture & Performance Review

**Date:** 2026-07-31  
**Scope:** `web/src` after the MAUI → Tauri/React migration.

## TL;DR

The architecture is **good enough to ship today** but will hit a wall with large collections (1000+ requests/messages). The three biggest risks are:

1. **No virtualization** in `CollectionTree`, `MessageList`, and `ResourceTable` — O(n) DOM for large lists.
2. **`ApiClientPage` is a 916-line state monolith** with 15+ `useState` hooks and deep JSON clone churn.
3. **No `React.memo` anywhere** — list rows re-render on every parent state twitch.

The recommended first step is a shared virtualized list primitive plus an `ApiClientContext`. These two changes unlock the rest of the roadmap.

---

## Architecture Shape

### State ownership

- **Good:** `AksWorkspaceContext.tsx` (lines 108-162) centralizes AKS workspace state and uses URL search params for drill-down persistence.
- **Bad:** `ApiClientPage.tsx` (lines 234-328) owns tab state, collections, environments, git, conflict banners, and five dialog states in one component. This makes props drilling unavoidable and re-render scope huge.
- **Good:** `lib/hooks.ts` provides consistent react-query data hooks with stable query keys (`["profile"]`, `["environments"]`, etc.).

### Component size

| Component | Lines | Concerns |
| --- | --- | --- |
| `ApiClientPage.tsx` | 916 | collections, tabs, requests, response, env manager, git, dialogs |
| `RequestEditor.tsx` | 937 | URL, auth, headers, query, body, GraphQL, WebSocket |
| `MessageList.tsx` | 829 | filter, pagination, bulk actions, columns, composer integration |
| `ServiceBusPage.tsx` | 445 | entity tree, message list, detail view, composer |

### Shared UI vocabulary

- **Extracted well:** `ResourceTable<T>`, `ResizablePanels`, `ConfirmBar`, `method-badge`.
- **Still duplicated:** context menu logic in AKS tabs, filter/sort in `EntityTree`, bulk actions in `MessageList`.

---

## Performance Risks

### 1. Large collections will not be smooth

`CollectionTree.tsx:161-234` renders every node recursively. With 1000 requests it creates 1000+ DOM elements and its search `filter` (lines 36-41) runs `filter` + `map` + recursive clone on every keystroke. `ResourceTable.tsx:64-96` and `MessageList.tsx:709-829` do the same.

**Answer to the user's question:** importing tons of requests will **not** stay smooth today. The app will lag during tree render, search, and tab switching.

### 2. `JSON.parse(JSON.stringify(obj))` clone churn

`ApiClientPage.tsx:45-47` defines a `deepClone` that is called 10+ times for every request open/save/duplicate. It is O(n), loses `Date`/`Map`/`undefined`, and allocates large intermediate strings. This was replaced with `structuredClone` as a quick win in this session.

### 3. No memoization

A grep of `web/src` found 18 uses of `useMemo` but **zero** `React.memo`. Every list row re-renders when its parent updates.

---

## Cross-Cutting Patterns to Apply

### Must-fix (land first)

1. **Virtualized list primitive**
   - Add `@tanstack/react-virtual` to `package.json`.
   - Create `components/shared/VirtualList.tsx` and `VirtualTree.tsx`.
   - Apply to `CollectionTree`, `MessageList`, and `EntityTree`.
   - `ResourceTable` should accept an `virtualizeThreshold` prop and use the same primitive.

2. **`ApiClientPage` decomposition**
   - Create `components/api-client/ApiClientContext.tsx` + `lib/hooks/useApiClientState.ts`.
   - Move tabs, selection, dialogs, and git state out of `ApiClientPage`.
   - Reduce `ApiClientPage` to layout composition (~400 lines).

### Should-fix (next)

3. **Component memoization**
   - Wrap row components in `React.memo` (pass stable callbacks from `useCallback`).
   - Start with `ResourceTable` rows and `CollectionTree` node items.

4. **Shared mutation wrapper**
   - Extend existing `useNotifyMutation` and use it for all feature mutations.
   - Add a generic `useAsyncAction` for non-mutation async work (e.g. preview fetch).

5. **Virtualize `MessageList`**
   - High impact for Service Bus queues with deep backlogs.

### Nice-to-have

6. **Error boundaries** — wrap lazy routes in `App.tsx` and panel children.
7. **Command palette registry** — register AKS/Service Bus/API Client commands in a single store.
8. **Bulk action bar** — extract from `MessageList` to `components/shared/BulkActionBar.tsx`.

---

## Smallest High-Impact Refactor

**Extract `ApiClientPage` state into `ApiClientContext` / `useApiClientState`.**

This touches three files, removes ~500 lines from the page, and enables the following:
- `React.memo` on `RequestEditor` and `ResponseViewer` because props stop changing.
- Virtualization of `CollectionTree` because it can consume stable filtered data from context.
- A shared command-palette registry because tab/collection state is reachable from anywhere.

---

## Roadmap

| # | Refactor | Effort | Impact | Files |
| --- | --- | --- | --- | --- |
| 1 | Virtualize `CollectionTree` | Medium | Critical | `CollectionTree.tsx`, `package.json` |
| 2 | Create `ApiClientContext` / `useApiClientState` | Large | High | `ApiClientPage.tsx`, new context/hook |
| 3 | Replace `JSON.parse(JSON.stringify)` with `structuredClone` | Small | Medium | `ApiClientPage.tsx` — **done in this session** |
| 4 | Memoize list rows (`ResourceTable`, `CollectionTree`) | Small | Medium | `ResourceTable.tsx`, `CollectionTree.tsx` |
| 5 | Virtualize `MessageList` | Medium | High | `MessageList.tsx` |
| 6 | Extract `BulkActionBar` | Medium | Medium | `MessageList.tsx`, `components/shared/BulkActionBar.tsx` |
| 7 | Error boundaries for lazy routes | Small | Medium | `App.tsx`, `components/shared/ErrorBoundary.tsx` |
| 8 | Command palette registry | Medium | Medium | `CommandPalette.tsx`, feature pages |

---

## One thing already done in this session

`ApiClientPage.tsx:45-47` `deepClone` now uses `structuredClone` with a JSON fallback, cutting clone time and preserving non-JSON types. All builds and tests remain green.
