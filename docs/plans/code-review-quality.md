# Code Review & Quality Improvement Plan

First full review of the Tauri/React rewrite to identify and fix quality, maintainability, and performance issues across the frontend, sidecar, and test infrastructure.

## Execution Order

Review each area in this order:
1. Frontend architecture & patterns
2. Sidecar architecture & patterns
3. Type safety & API contract alignment
4. Performance & rendering
5. Security & error handling
6. Consistency & naming

---

## 1. Frontend Architecture & Patterns

### 1.1 `hooks.ts` — Single 625-line File
**Issue**: All React Query hooks (30+ functions) live in one file (`web/src/lib/hooks.ts`). This is hard to navigate, hard to tree-shake, and creates import cycles.

**Recommendation**: Split into domain-specific files:
- `web/src/lib/hooks/useProfile.ts`
- `web/src/lib/hooks/useServiceBus.ts`
- `web/src/lib/hooks/useAks.ts`
- `web/src/lib/hooks/useApiClient.ts`
- `web/src/lib/hooks/useRedis.ts`
- `web/src/lib/hooks/useStorage.ts`
- `web/src/lib/hooks/useAgent.ts`
- `web/src/lib/hooks/index.ts` (re-export all)

**Priority**: Medium | **Effort**: Low | **Risk**: Low

### 1.2 `types.ts` — Single 679-line File
**Issue**: All TypeScript types in one file. Same problem as hooks.

**Recommendation**: Split into domain-specific files:
- `web/src/lib/types/profile.ts`
- `web/src/lib/types/service-bus.ts`
- `web/src/lib/types/aks.ts`
- `web/src/lib/types/api-client.ts`
- `web/src/lib/types/redis.ts`
- `web/src/lib/types/storage.ts`
- `web/src/lib/types/agent.ts`
- `web/src/lib/types/index.ts`

**Priority**: Medium | **Effort**: Low | **Risk**: Low

### 1.3 `RedisPage.tsx` — 492-line Monolith Component
**Issue**: Entire Redis page (key browser, detail panel, server info, slow log) is one single component with 15+ hooks and inline rendering. This is hard to maintain and test.

**Recommendation**: Extract sub-components:
- `RedisKeyBrowser.tsx` (key list + search)
- `RedisKeyDetail.tsx` (key detail panel)
- `RedisServerInfo.tsx` (server info tab)
- `RedisSlowLog.tsx` (slow log tab)
- `RedisPage.tsx` becomes a thin orchestrator

**Priority**: High | **Effort**: Medium | **Risk**: Low

### 1.4 `StoragePage.tsx` — 304-line Component with State Management Issues
**Issue**: Manages `allItems`, `continuationToken`, `prefixHistory` as local state. The `useStorageBlobs` hook is called but results are merged manually via `useEffect`, creating potential race conditions.

**Recommendation**: 
- Move pagination logic into the hook or a dedicated `useBlobBrowser` hook
- Use `useInfiniteQuery` instead of manual continuation token management
- Extract `BlobBrowser`, `BlobDetail` as separate components

**Priority**: High | **Effort**: Medium | **Risk**: Medium

### 1.5 No Shared UI Component Library
**Issue**: Every component re-implements common patterns (loading spinner, empty state, error state, confirm dialog, tabs, badge). The MAUI app had `Shared/` with 49 items. The React app has zero shared components.

**Recommendation**: Create shared components:
- `web/src/components/shared/LoadingSpinner.tsx`
- `web/src/components/shared/EmptyState.tsx`
- `web/src/components/shared/ErrorCallout.tsx`
- `web/src/components/shared/ConfirmDialog.tsx`
- `web/src/components/shared/Tabs.tsx`
- `web/src/components/shared/Badge.tsx`
- `web/src/components/shared/KeyValueGrid.tsx`
- `web/src/components/shared/SkeletonRows.tsx`

**Priority**: High | **Effort**: Medium | **Risk**: Low

### 1.6 No Error Boundary
**Issue**: No React error boundary. A single component crash takes down the entire app.

**Recommendation**: Add `ErrorBoundary` component wrapping each route in `App.tsx`.

**Priority**: High | **Effort**: Low | **Risk**: Low

### 1.7 `apiFetch` / `apiSend` — No Timeout, No Retry, No Abort
**Issue**: `web/src/lib/api.ts` — fetch calls have no timeout, no retry, no AbortController support. A hanging sidecar request will hang the UI indefinitely.

**Recommendation**:
- Add `AbortController` support to `apiFetch` and `apiSend`
- Add default timeout (30s) using `AbortSignal.timeout()`
- Add retry for transient failures (network errors, 503)
- Add request deduplication for GET requests

**Priority**: High | **Effort**: Low | **Risk**: Low

### 1.8 `window.prompt` / `window.confirm` Usage
**Issue**: `ApiClientPage.tsx` uses `window.prompt` for collection/request/folder naming and `window.confirm` for deletion. These are blocking, ugly, and not testable without dialog handlers.

**Recommendation**: Replace with proper modal dialogs:
- `PromptDialog.tsx` — modal with text input
- `ConfirmDialog.tsx` — modal with confirm/cancel
- Use these throughout the app

**Priority**: High | **Effort**: Low | **Risk**: Low

---

## 2. Sidecar Architecture & Patterns

### 2.1 Static Mutable AKS Client
**Issue**: `AksEndpoints.cs:10-30` — `static IAksClient? _client` with `static Lock`. This is a process-global singleton that:
- Never gets disposed
- Can't handle kubeconfig changes without app restart
- Creates thread contention on every AKS request
- Is not testable (can't inject mock client)

**Recommendation**: 
- Register `IAksClientFactory` in DI (as done for Service Bus and Redis)
- Use `IAksClientFactory` per-request (or scoped) instead of static singleton
- Dispose client on config change

**Priority**: High | **Effort**: Low | **Risk**: Low

### 2.2 No Error Handling Middleware
**Issue**: Most endpoints have inline try/catch or no error handling at all. Error responses are inconsistent (some return `Results.Ok(new { error = ... })`, some throw, some return `Results.NotFound`).

**Recommendation**:
- Add global exception handler middleware
- Standardize error response format: `{ error: string, detail?: string }`
- Return proper HTTP status codes (400, 404, 500)
- Add logging for all exceptions

**Priority**: High | **Effort**: Low | **Risk**: Low

### 2.3 Demo Mode Interception is Inconsistent
**Issue**: Demo mode is handled differently per endpoint:
- Profile endpoint: modifies response inline
- Service Bus: `CreateClient` checks demo mode
- AKS: `GetClient` checks demo mode
- Redis: endpoint checks demo mode
- Storage: endpoint checks demo mode
- API Client: no demo mode support

**Recommendation**: 
- Create `IDemoDataService` interface with methods for each domain
- Use a middleware or filter to intercept requests when demo mode is on
- Centralize all demo data in one service

**Priority**: Medium | **Effort**: Medium | **Risk**: Medium

### 2.4 `Program.cs` — Inline Endpoint Definitions
**Issue**: `Program.cs` (178 lines) has several inline endpoint definitions (health, demo-mode, config/profiles, config/environments, config/collections, config/user-settings) instead of using extension methods like the feature endpoints.

**Recommendation**: Move inline endpoints to `ConfigEndpoints.cs` and `SystemEndpoints.cs`.

**Priority**: Low | **Effort**: Low | **Risk**: Low

### 2.5 No Request Validation
**Issue**: No input validation on any endpoint. Missing route parameters, invalid JSON, or null values cause unhandled exceptions.

**Recommendation**:
- Add minimal validation (null checks, required field checks)
- Use `[FromBody]` with proper DTOs instead of raw `JsonElement`
- Return 400 Bad Request for invalid input

**Priority**: Medium | **Effort**: Medium | **Risk**: Low

### 2.6 No Cancellation Token Propagation
**Issue**: No endpoint accepts `CancellationToken` from the HTTP context. Long-running operations (Service Bus peek, AKS log streaming) can't be cancelled.

**Recommendation**: Pass `HttpContext.RequestAborted` to all async operations.

**Priority**: Medium | **Effort**: Low | **Risk**: Low

---

## 3. Type Safety & API Contract Alignment

### 3.1 No Shared Type Generation
**Issue**: TypeScript types in `types.ts` are manually maintained to match C# domain models. Any C# change requires manual TS update — easy to drift.

**Recommendation**: 
- Option A: Generate TypeScript types from C# using tool like `Kiota` or `Refitter` (OpenAPI)
- Option B: Add a test that serializes C# types and compares to TS type definitions
- Option C: Create OpenAPI spec from sidecar and generate TS client

**Priority**: Medium | **Effort**: High | **Risk**: Low

### 3.2 `any` Type Usage
**Issue**: `(import.meta as any).env?.VITE_SIDECAR_URL` in `api.ts` — uses `any` cast.

**Recommendation**: Add proper Vite env type declarations in `vite-env.d.ts`.

**Priority**: Low | **Effort**: Low | **Risk**: Low

### 3.3 Inconsistent Nullable Handling
**Issue**: Some types use `T | null`, others use `T | undefined`, some use `T | null | undefined`. This creates confusion in conditional checks.

**Recommendation**: Standardize on `T | null` for all nullable fields (matching C# null semantics). Use `undefined` only for optional function parameters.

**Priority**: Low | **Effort**: Low | **Risk**: Low

### 3.4 Missing TypeScript Strictness
**Issue**: No `noUncheckedIndexedAccess` in tsconfig. Array access returns `T` instead of `T | undefined`.

**Recommendation**: Enable `noUncheckedIndexedAccess` in `tsconfig.json` for safer array access.

**Priority**: Low | **Effort**: Low | **Risk**: Low

---

## 4. Performance & Rendering

### 4.1 No Virtualization for Large Lists
**Issue**: `MessageList.tsx`, `CollectionTree.tsx`, and all AKS grid components render all items without virtualization. The MAUI app uses `<Virtualize>` for message lists and collection trees.

**Recommendation**: 
- Use `@tanstack/react-virtual` for message lists, key lists, collection trees
- Virtualize any list that could exceed 100 items

**Priority**: High | **Effort**: Medium | **Risk**: Low

### 4.2 No Memoization in List Components
**Issue**: List components re-render on every parent state change. No `useMemo`, `useCallback`, or `React.memo` usage anywhere in the codebase.

**Recommendation**:
- Wrap list item components in `React.memo`
- Memoize filter/sort computations with `useMemo`
- Memoize event handlers with `useCallback` in page components

**Priority**: Medium | **Effort**: Low | **Risk**: Low

### 4.3 React Query Cache Key Inconsistencies
**Issue**: Some hooks use different cache keys for the same logical data. For example, `useSbPeekMessages` includes `count` in the key but `useSbPeekDlq` also includes `count` — changing count creates a new cache entry instead of invalidating.

**Recommendation**: Review all query keys for consistency. Use hierarchical keys: `["sb", "peek", nsId, entityPath, { count, viewMode }]`.

**Priority**: Low | **Effort**: Low | **Risk**: Low

### 4.4 No Stale-While-Revalidate
**Issue**: All queries use default `staleTime: 0` (immediately stale). This causes refetch on every mount/visibility change.

**Recommendation**: Set sensible `staleTime` per domain:
- Profile/config: 60s
- Resource lists: 10s
- Key details: 30s
- Agent status: 5s (already set)

**Priority**: Medium | **Effort**: Low | **Risk**: Low

### 4.5 No Prefetching or Optimistic Updates
**Issue**: All mutations wait for server response before updating UI. No optimistic updates for delete operations.

**Recommendation**: 
- Add optimistic update for key deletion (Redis), message complete (Service Bus)
- Prefetch adjacent pages on hover (e.g., prefetch blob detail on blob hover)

**Priority**: Low | **Effort**: Medium | **Risk**: Medium

---

## 5. Security & Error Handling

### 5.1 CORS Allows Any Origin
**Issue**: `Program.cs:51` — `AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod()`. This is fine for local dev but unsafe if sidecar is ever exposed.

**Recommendation**: Restrict to `tauri://localhost` and `http://localhost:1420` in production.

**Priority**: Low | **Effort**: Low | **Risk**: Low

### 5.2 No Authentication on Sidecar
**Issue**: Sidecar has no authentication. Any process on the machine can call the API.

**Recommendation**: Add shared token authentication (Tauri generates a token, passes to sidecar, sidecar validates on every request). At minimum, bind to `127.0.0.1` only (already done).

**Priority**: Low | **Effort**: Medium | **Risk**: Low

### 5.3 Error Messages Leak Internal Details
**Issue**: Exception messages are returned directly to the frontend (e.g., `ex.Message` in AKS endpoints). These can contain file paths, connection strings, or stack trace details.

**Recommendation**: Sanitize error messages before returning to client. Log full exception server-side.

**Priority**: Medium | **Effort**: Low | **Risk**: Low

### 5.4 No Frontend Error Logging
**Issue**: No error logging on the frontend. Errors are swallowed in catch blocks or shown as inline messages with no logging.

**Recommendation**: Add error logging service (console in dev, remote logging in production). Wrap all error catch blocks with logging.

**Priority**: Low | **Effort**: Low | **Risk**: Low

---

## 6. Consistency & Naming

### 6.1 Inconsistent `data-testid` Patterns
**Issue**: Test IDs follow different patterns:
- `sb-namespace-select` (prefix-kebab)
- `message-list-empty` (kebab)
- `entity-tree-queue-order-created` (hierarchical)
- `redis-key-user:1001` (includes data values)
- `response-header-row-0` (index-based)

**Recommendation**: Standardize on `feature-area-element-name` pattern. Document the convention.

**Priority**: Low | **Effort**: Low | **Risk**: Low

### 6.2 Inconsistent Loading States
**Issue**: Some components show "Loading..." text, others show spinners, others show nothing. No consistent loading pattern.

**Recommendation**: Create `LoadingSpinner` and `SkeletonRows` shared components. Use skeleton for lists, spinner for panels.

**Priority**: Low | **Effort**: Low | **Risk**: Low

### 6.3 Inconsistent Empty States
**Issue**: Some components show "No items" text, others show styled empty states, others show nothing.

**Recommendation**: Create `EmptyState` shared component with icon, title, subtitle, and action slots.

**Priority**: Low | **Effort**: Low | **Risk**: Low

### 6.4 Inconsistent Error Display
**Issue**: Some errors show as inline text, some as red-tinted boxes, some as toast notifications (planned but not implemented).

**Recommendation**: Create `ErrorCallout` shared component. Use toast notifications for transient errors, inline error callouts for persistent errors.

**Priority**: Low | **Effort**: Low | **Risk**: Low

---

## 7. Code Organization

### 7.1 No Feature-Based Folder Structure
**Issue**: Components are organized by feature (`components/service-bus/`, `components/aks/`) which is good, but there's no `components/shared/` for cross-cutting components, and no `lib/utils/` for utility functions.

**Recommendation**: 
- Create `web/src/components/shared/` for shared components
- Create `web/src/lib/utils/` for utility functions (formatBytes, formatDate, etc.)
- Move domain-specific utilities to their feature folders

**Priority**: Medium | **Effort**: Low | **Risk**: Low

### 7.2 Utility Functions Duplicated
**Issue**: `formatBytes` is defined in both `RedisPage.tsx` and `StoragePage.tsx`. `formatDate` is defined in `StoragePage.tsx`. These should be shared.

**Recommendation**: Extract to `web/src/lib/utils/format.ts`.

**Priority**: Low | **Effort**: Low | **Risk**: Low

### 7.3 No Constants File
**Issue**: Magic numbers scattered (e.g., `count = 50` for peek, `5199` for sidecar port, polling intervals).

**Recommendation**: Create `web/src/lib/constants.ts` with all magic numbers and configuration values.

**Priority**: Low | **Effort**: Low | **Risk**: Low

---

## Summary Priority Matrix

| Priority | Item | Effort | Risk |
|----------|------|--------|------|
| **High** | 1.5 — Shared UI component library | Medium | Low |
| **High** | 1.6 — Error boundary | Low | Low |
| **High** | 1.7 — API timeout/retry/abort | Low | Low |
| **High** | 1.8 — Replace window.prompt/confirm | Low | Low |
| **High** | 2.1 — Fix static mutable AKS client | Low | Low |
| **High** | 2.2 — Error handling middleware | Low | Low |
| **High** | 4.1 — List virtualization | Medium | Low |
| **High** | 1.3 — Split RedisPage monolith | Medium | Low |
| **High** | 1.4 — Fix StoragePage state management | Medium | Medium |
| **Medium** | 1.1 — Split hooks.ts | Low | Low |
| **Medium** | 1.2 — Split types.ts | Low | Low |
| **Medium** | 2.3 — Centralize demo mode | Medium | Medium |
| **Medium** | 2.5 — Request validation | Medium | Low |
| **Medium** | 2.6 — Cancellation token | Low | Low |
| **Medium** | 3.1 — Type generation | High | Low |
| **Medium** | 4.2 — Memoization | Low | Low |
| **Medium** | 4.4 — Stale-while-revalidate | Low | Low |
| **Medium** | 5.3 — Sanitize error messages | Low | Low |
| **Medium** | 7.1 — Feature-based folder structure | Low | Low |
| **Low** | 2.4 — Move inline endpoints | Low | Low |
| **Low** | 3.2-3.4 — Type strictness | Low | Low |
| **Low** | 4.3 — Cache key consistency | Low | Low |
| **Low** | 4.5 — Optimistic updates | Medium | Medium |
| **Low** | 5.1-5.2 — Security hardening | Low/Medium | Low |
| **Low** | 5.4 — Frontend error logging | Low | Low |
| **Low** | 6.1-6.4 — Consistency | Low | Low |
| **Low** | 7.2-7.3 — Utilities & constants | Low | Low |

## Acceptance Criteria

- [ ] All **High** priority items addressed
- [ ] No `window.prompt` or `window.confirm` in codebase
- [ ] No `any` type casts in production code
- [ ] Error boundary wraps all routes
- [ ] API calls have timeout and abort support
- [ ] Sidecar has consistent error handling
- [ ] No static mutable state in sidecar endpoints
- [ ] Lists with > 100 items are virtualized
- [ ] Shared components extracted and used consistently
- [ ] All changes have corresponding tests

## Scope

### In Scope
- Frontend code quality improvements
- Sidecar code quality improvements
- Type safety improvements
- Performance optimizations
- Security hardening
- Consistency standardization

### Out of Scope
- New feature implementation (covered by feature parity plan)
- Test coverage expansion (covered by test coverage plan)
- Backend library changes (SwebKit.Core, SwebKit.Azure, etc.)
- CI/CD pipeline changes

## Constraints

- No breaking changes to existing API endpoints
- No breaking changes to existing E2E tests
- All changes must pass TypeScript strict mode
- All changes must pass existing E2E tests
- Refactors must be incremental (one component/file at a time)
