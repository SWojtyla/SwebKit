# Test Coverage Expansion Plan

Expand test coverage for the Tauri/React frontend from the current 35 smoke-level E2E tests to comprehensive coverage including unit tests, integration tests, and deep E2E tests.

## Current State

### E2E Tests (35 tests total)
- `navigation.spec.ts` — 8 tests (nav to each page)
- `dashboard.spec.ts` — 3 tests (load, navigate, demo toggle)
- `service-bus.spec.ts` — 2 tests (peek active, peek DLQ)
- `aks.spec.ts` — 2 tests (deployments, tab switching)
- `api-client.spec.ts` — 2 tests (create+send, header add/remove)
- `redis.spec.ts` — 6 tests (key browse, hash, server info, slowlog, search, delete)
- `storage.spec.ts` — 5 tests (containers, blob detail, folder nav, CSV, metadata)
- `agent.spec.ts` — 6 tests (empty state, clear, send, loading, error, keyboard)
- `settings.spec.ts` — 1 test (tab switching)

### Gaps
- **Zero unit tests** — no Vitest setup, no component tests, no hook tests, no utility tests
- **Zero sidecar integration tests** — no tests for sidecar endpoints
- **E2E tests are smoke-level** — only verify basic rendering, not workflows
- **No error path tests** — only happy path tested
- **No demo mode data validation** — tests check visibility but not data correctness
- **No keyboard accessibility tests** — no tab navigation, screen reader, or ARIA tests
- **No responsive/layout tests** — no split panel, resize, or layout tests
- **No theme/appearance tests** — no dark/light mode, theme persistence

---

## Phase 1: Unit Test Infrastructure

### 1.1 Setup Vitest
- [ ] Install vitest, @testing-library/react, @testing-library/jest-dom, @testing-library/user-event
- [ ] Install vitest jsdom environment
- [ ] Create `vitest.config.ts` with jsdom environment, path aliases (@/ → src/)
- [ ] Add `"test": "vitest"` script to `package.json`
- [ ] Create `web/src/test/setup.ts` — global setup (jest-dom matchers, mocks)
- [ ] Create `web/src/test/utils.tsx` — render helper with QueryClientProvider wrapper

### 1.2 Mock Infrastructure
- [ ] Create `web/src/test/mocks/handlers.ts` — MSW request handlers for all sidecar endpoints
- [ ] Create `web/src/test/mocks/server.ts` — MSW server setup
- [ ] Create `web/src/test/mocks/data.ts` — mock data matching demo mode responses
- [ ] Setup MSW in test setup file (start/stop/reset between tests)

### 1.3 Test Helpers
- [ ] Create `web/src/test/helpers.ts` — common test utilities (mock profile, mock collections, etc.)
- [ ] Create custom render function that wraps components with required providers (QueryClient, Router)

---

## Phase 2: Unit Tests — Hooks (`web/src/lib/hooks.ts`)

### 2.1 Query Hooks
- [ ] `useProfile` — returns profile data, loading state, error state
- [ ] `useUpdateProfile` — mutation succeeds, invalidates profile query
- [ ] `useSbEntities` — returns entities, enabled when nsId+entityPath provided
- [ ] `useSbPeekMessages` — returns messages, enabled when nsId+entityPath
- [ ] `useSbPeekDlq` — returns DLQ messages, enabled when nsId+entityPath
- [ ] `useSbComplete` — mutation calls correct endpoint
- [ ] `useSbCompleteDlq` — mutation calls correct endpoint
- [ ] `useSbResubmit` — mutation calls correct endpoint
- [ ] `useSbPurge` — mutation calls correct endpoint with deadLetter flag
- [ ] `useAksResources` — returns resource list for each type (pods, deployments, etc.)
- [ ] `useAksEvents` — returns events
- [ ] `useAksHelmReleases` — returns helm releases
- [ ] `useCollections` — returns collections list
- [ ] `useUpdateCollections` — mutation persists collections
- [ ] `useExecuteRequest` — mutation calls execute endpoint, returns response
- [ ] `useRedisScanKeys` — returns keys matching pattern
- [ ] `useRedisKeyValue` — returns value for key
- [ ] `useRedisHashFields` — returns hash fields
- [ ] `useRedisListItems` — returns list items
- [ ] `useRedisSetMembers` — returns set members
- [ ] `useRedisSortedSetMembers` — returns sorted set members
- [ ] `useRedisServerInfo` — returns server info
- [ ] `useRedisSlowLog` — returns slow log entries
- [ ] `useRedisDeleteKey` — mutation deletes key
- [ ] `useStorageContainers` — returns container list
- [ ] `useStorageBlobs` — returns blob list for container+prefix
- [ ] `useStorageBlobDetail` — returns blob properties + content
- [ ] `useAgentChat` — mutation sends message, returns reply
- [ ] `useAgentClear` — mutation clears history
- [ ] `useAgentStatus` — returns agent status, polls every 5s

### 2.2 Utility Tests
- [ ] `apiFetch` — success parsing, error handling, 401 redirect, network error
- [ ] `apiUrl` — correct base URL construction
- [ ] Any utility functions in `web/src/lib/`

---

## Phase 3: Unit Tests — Components

### 3.1 Service Bus Components
- [ ] `ServiceBusPage` — renders namespace selector, entity tree, message list, detail
- [ ] `EntityTree` — renders queues/topics, handles selection, shows stats
- [ ] `MessageList` — renders messages, handles selection, shows loading/empty states
- [ ] `MessageList` — text filter filters messages by messageId, correlationId, subject, body
- [ ] `MessageList` — advanced filter rules (application property, enqueued time, delivery count, sequence number)
- [ ] `MessageDetail` — renders message properties, body, application properties
- [ ] `MessageDetail` — complete button calls mutation
- [ ] `MessageDetail` — resubmit button calls mutation
- [ ] `MessageDetail` — purge button shows confirmation

### 3.2 API Client Components
- [ ] `ApiClientPage` — renders collection tree, request editor, response viewer
- [ ] `ApiClientPage` — add collection creates new collection
- [ ] `ApiClientPage` — add request creates new request in collection
- [ ] `ApiClientPage` — add folder creates new folder
- [ ] `ApiClientPage` — delete node removes from tree
- [ ] `ApiClientPage` — save persists request changes
- [ ] `ApiClientPage` — send executes request and shows response
- [ ] `CollectionTree` — renders collections, folders, requests
- [ ] `CollectionTree` — handles node selection
- [ ] `CollectionTree` — shows empty state
- [ ] `RequestEditor` — method change updates request
- [ ] `RequestEditor` — URL input updates request
- [ ] `RequestEditor` — add/remove headers
- [ ] `RequestEditor` — add/remove query params
- [ ] `RequestEditor` — body mode switch (None/Json/Xml/Text/FormData)
- [ ] `RequestEditor` — auth type switch (None/Bearer/Basic/ApiKey)
- [ ] `RequestEditor` — send button calls onSend
- [ ] `RequestEditor` — save button calls onSave
- [ ] `ResponseViewer` — shows placeholder when no response
- [ ] `ResponseViewer` — shows sending state
- [ ] `ResponseViewer` — shows status badge with correct color
- [ ] `ResponseViewer` — shows body tab with response content
- [ ] `ResponseViewer` — shows headers tab with header table
- [ ] `ResponseViewer` — shows error message for failed requests

### 3.3 AKS Components
- [ ] `AksPage` — renders namespace selector + tab bar
- [ ] `AksPage` — namespace selection loads resources
- [ ] `PodsTab` — renders pod grid with correct columns
- [ ] `PodsTab` — pod status badges (Running, Pending, Failed)
- [ ] `DeploymentsTab` — renders deployment grid with replicas
- [ ] `ServicesTab` — renders service grid with type/ports
- [ ] `SecretsTab` — renders secret grid
- [ ] `EventsTab` — renders event list with severity
- [ ] `HelmTab` — renders helm release list

### 3.4 Redis Components
- [ ] `RedisPage` — renders key browser, detail panel, tabs
- [ ] `RedisPage` — key selection shows detail
- [ ] `RedisPage` — pattern search filters keys
- [ ] `RedisPage` — delete key removes from list
- [ ] `RedisPage` — server info tab shows info
- [ ] `RedisPage` — slow log tab shows entries

### 3.5 Storage Components
- [ ] `StoragePage` — renders container list
- [ ] `StoragePage` — container selection loads blobs
- [ ] `StoragePage` — blob selection shows detail
- [ ] `StoragePage` — folder navigation works
- [ ] `StoragePage` — breadcrumb navigation works

### 3.6 Agent Components
- [ ] `AgentPage` — renders empty state
- [ ] `AgentPage` — send message adds user bubble
- [ ] `AgentPage` — clear button shows confirmation
- [ ] `AgentPage` — loading indicator while pending
- [ ] `AgentPage` — error message display
- [ ] `AgentPage` — Enter sends, Shift+Enter adds newline

### 3.7 Settings Components
- [ ] `SettingsPage` — renders all tabs
- [ ] `SettingsPage` — tab switching shows correct content
- [ ] `ServiceBusSettings` — add/remove namespace
- [ ] `AksSettings` — kubeconfig path input
- [ ] `RedisSettings` — add/remove cache
- [ ] `StorageSettings` — add/remove storage account
- [ ] `AgentSettings` — profile management
- [ ] `GeneralSettings` — demo mode toggle

### 3.8 Layout Components
- [ ] `AppLayout` — renders sidebar with all nav items
- [ ] `AppLayout` — active nav item highlighted
- [ ] `DashboardPage` — renders service cards
- [ ] `DashboardPage` — service card click navigates
- [ ] `DashboardPage` — demo mode toggle works

---

## Phase 4: Deep E2E Tests

### 4.1 Service Bus Deep Tests
- [ ] Text filter: type in search, verify filtered results
- [ ] Advanced filter: add rule (application property = "orderId"), verify filtered results
- [ ] Advanced filter: add rule (delivery count >= 2), verify filtered results
- [ ] Advanced filter: add rule (enqueued time after date), verify filtered results
- [ ] Advanced filter: disable rule, verify all messages shown
- [ ] Advanced filter: remove rule, verify all messages shown
- [ ] Column toggle: hide/show columns
- [ ] Session pinning: filter by session ID
- [ ] Message detail: copy body to clipboard
- [ ] Message detail: complete message, verify removed from list
- [ ] Message detail: resubmit DLQ message, verify removed from DLQ
- [ ] Message detail: purge all messages with confirmation
- [ ] Entity tree: expand/collapse topics to show subscriptions
- [ ] Entity tree: select subscription, verify messages load
- [ ] Namespace selector: switch namespace, verify entities reload
- [ ] Empty namespace: shows "no entities" message
- [ ] Error state: connection error shows error message

### 4.2 API Client Deep Tests
- [ ] Create collection with name
- [ ] Create folder inside collection
- [ ] Create request inside folder
- [ ] Rename collection (inline edit)
- [ ] Rename request (inline edit)
- [ ] Delete request with confirmation
- [ ] Delete folder with confirmation
- [ ] Delete collection with confirmation
- [ ] Send GET request, verify response status + body
- [ ] Send POST request with JSON body
- [ ] Send request with custom headers
- [ ] Send request with query parameters
- [ ] Send request with bearer token auth
- [ ] Send request with basic auth
- [ ] Send request with API key auth
- [ ] Response: verify status badge color (2xx green, 4xx yellow, 5xx red)
- [ ] Response: verify headers tab shows all headers
- [ ] Response: verify elapsed time displayed
- [ ] Response: verify content length displayed
- [ ] Response: verify error message for failed request
- [ ] Save request: verify changes persisted after reload
- [ ] Collection tree: filter by name
- [ ] Collection tree: expand/collapse folders
- [ ] Empty state: no collections shows empty message

### 4.3 AKS Deep Tests
- [ ] Namespace selector: switch namespace, verify resources reload
- [ ] Pods tab: verify pod name, status, node, age columns
- [ ] Pods tab: filter pods by name
- [ ] Deployments tab: verify name, replicas, image, age columns
- [ ] Deployments tab: verify ready/available ratio
- [ ] Services tab: verify name, type, cluster IP, ports
- [ ] Secrets tab: verify name, type, data count
- [ ] Events tab: verify event list with timestamp, type, message
- [ ] Events tab: filter events by type (Warning, Normal)
- [ ] Helm tab: verify release name, namespace, revision, status
- [ ] Resource count: verify correct counts per tab
- [ ] Auto-refresh: toggle auto-refresh, verify data updates
- [ ] Empty namespace: shows "no resources" message

### 4.4 Redis Deep Tests
- [ ] Key browser: verify all demo keys visible
- [ ] Key detail: string type shows value
- [ ] Key detail: hash type shows field-value table
- [ ] Key detail: list type shows items
- [ ] Key detail: set type shows members
- [ ] Key detail: sorted set type shows members with scores
- [ ] Key detail: TTL display (no expiry vs active TTL)
- [ ] Pattern search: wildcard patterns (user:*, session:*, *:pending)
- [ ] Pattern search: exact key name
- [ ] Delete key: confirmation dialog, verify removed
- [ ] Delete key: cancel confirmation, verify still present
- [ ] Server info: version, clients, memory, uptime
- [ ] Slow log: entries with command, duration, client
- [ ] Tab switching: keys / server info / slow log

### 4.5 Storage Deep Tests
- [ ] Container list: verify all demo containers visible
- [ ] Container selection: loads blob list
- [ ] Blob list: verify blob name, size, type columns
- [ ] Blob detail: content tab shows content
- [ ] Blob detail: properties tab shows properties
- [ ] Blob detail: metadata section shows metadata
- [ ] Folder navigation: click folder, verify breadcrumb updates
- [ ] Breadcrumb navigation: click crumb, verify navigation
- [ ] CSV content: verify CSV content displayed
- [ ] JSON content: verify JSON content displayed
- [ ] Empty container: shows "no blobs" message

### 4.6 Agent Deep Tests
- [ ] Send message: user bubble appears with correct text
- [ ] Send message: input cleared after send
- [ ] Loading indicator: visible while waiting
- [ ] Error message: shown when no LLM configured
- [ ] Clear conversation: confirmation dialog appears
- [ ] Clear conversation: confirm clears all messages
- [ ] Clear conversation: cancel preserves messages
- [ ] Clear button: disabled when no messages
- [ ] Enter key: sends message
- [ ] Shift+Enter: adds newline (input not cleared)
- [ ] Empty state: shows helpful prompt
- [ ] Multiple messages: scroll to bottom on new message

### 4.7 Settings Deep Tests
- [ ] General tab: demo mode toggle
- [ ] Service Bus tab: add namespace form
- [ ] Service Bus tab: remove namespace
- [ ] AKS tab: kubeconfig path input
- [ ] Redis tab: add cache form
- [ ] Redis tab: remove cache
- [ ] Storage tab: add storage account
- [ ] Storage tab: remove storage account
- [ ] Agent tab: add profile
- [ ] Agent tab: remove profile
- [ ] Settings persistence: changes saved and persist after reload

### 4.8 Dashboard Deep Tests
- [ ] Service cards: correct counts per service
- [ ] Service cards: demo mode updates counts
- [ ] Navigation: service card click navigates to correct page
- [ ] Sidecar status: shows connected when sidecar running
- [ ] Demo mode: toggle on shows banner
- [ ] Demo mode: toggle off hides banner

### 4.9 Cross-Cutting E2E Tests
- [ ] Demo mode toggle persists across page navigation
- [ ] Demo mode toggle persists across reload
- [ ] Dark/light theme toggle
- [ ] Theme persists across reload
- [ ] Sidebar collapse/expand
- [ ] Sidebar active item highlighted per route
- [ ] Error boundary: component error shows fallback
- [ ] Network error: sidecar down shows error state
- [ ] Responsive: split panels resize
- [ ] Keyboard: tab navigation through main UI

---

## Phase 5: Sidecar Integration Tests

### 5.1 Sidecar Endpoint Tests
- [ ] Create test project `tests/SwebKit.Sidecar.Tests` (xUnit)
- [ ] Test profile endpoint: `GET /api/profile` returns profile
- [ ] Test profile update: `PUT /api/profile` persists changes
- [ ] Test Service Bus endpoints: peek, dlq, complete, resubmit, purge
- [ ] Test AKS endpoints: list resources, events, helm releases
- [ ] Test API Client endpoints: collections CRUD, execute request
- [ ] Test Redis endpoints: scan, key info, value, hash, list, set, zset, delete, server info, slow log
- [ ] Test Storage endpoints: containers, blobs, blob detail
- [ ] Test Agent endpoints: chat, clear, status
- [ ] Test error handling: 404 for unknown resources, 400 for bad input, 500 for server errors
- [ ] Test demo mode: endpoints return demo data when demo mode enabled

### 5.2 Sidecar Unit Tests
- [ ] `SidecarAgentChatService` — conversation history, context building, error handling
- [ ] Demo data providers — verify demo data structure matches expected schema
- [ ] Credential store — verify credential retrieval
- [ ] Profile repository — verify profile loading/saving

---

## Phase 6: Accessibility & Performance Tests

### 6.1 Accessibility Tests
- [ ] Setup `@axe-core/playwright` for automated a11y checks
- [ ] Run axe scan on each page, assert no critical violations
- [ ] Test keyboard navigation: tab through all interactive elements
- [ ] Test ARIA labels: all buttons have accessible names
- [ ] Test ARIA roles: lists, navigation, main, complementary where appropriate
- [ ] Test focus management: focus moves correctly on route change
- [ ] Test screen reader: all content readable with NVDA/VoiceOver (manual)

### 6.2 Performance Tests
- [ ] Setup Playwright trace + performance metrics
- [ ] Page load time: each page loads < 2s
- [ ] Large list rendering: 1000+ messages/keys/blobs render < 1s
- [ ] Memory: no memory leaks on repeated navigation
- [ ] Bundle size: check for oversized chunks

---

## Acceptance Criteria

- [ ] Vitest configured and running
- [ ] All hooks have unit tests (30+ tests)
- [ ] All major components have unit tests (50+ tests)
- [ ] E2E tests expanded to 100+ tests covering all workflows
- [ ] Sidecar integration tests created and passing
- [ ] Accessibility tests running with no critical violations
- [ ] All tests pass in CI
- [ ] Test coverage report generated (aim for 60%+ on hooks, 40%+ on components)

## Scope

### In Scope
- Vitest setup and configuration
- Unit tests for all hooks and components
- Deep E2E tests for all features
- Sidecar integration tests
- Accessibility tests
- Performance tests

### Out of Scope
- Visual regression testing (Percy/Chromatic) — future enhancement
- Cross-browser testing (Chrome only for now)
- Load testing
- Security testing

## Constraints

- All tests must pass with sidecar running in demo mode
- Tests must be isolated (no cross-test state leakage)
- E2E tests must run in < 5 minutes total
- Unit tests must run in < 30 seconds total
- No flaky tests — all tests must be deterministic
