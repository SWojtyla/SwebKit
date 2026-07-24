# SwebKit Tauri + React + TypeScript Rewrite

Complete rewrite of the SwebKit desktop app: replace MAUI Blazor Hybrid with Tauri (Rust shell) + React + Vite + Tailwind + shadcn/ui, keeping the existing .NET backend projects as a local sidecar (ASP.NET Minimal API) for all Azure/K8s/Redis/Agent business logic.

---

## Architecture

```
┌──────────────────────────────────────────────────────────┐
│ Tauri App (Rust)                                         │
│                                                          │
│  ┌─────────────────┐     HTTP/JSON     ┌──────────────┐  │
│  │ React + Vite    │ ←──────────────→ │ .NET Sidecar │  │
│  │  Tailwind       │   localhost:port  │ ASP.NET      │  │
│  │  shadcn/ui      │                   │ Minimal API  │  │
│  │  TanStack Query │                   │              │  │
│  │  Zustand        │                   │ SwebKit.Core │  │
│  │  Monaco Editor  │                   │ SwebKit.Azure│  │
│  │  xterm.js       │                   │ SwebKit.K8s  │  │
│  │  @dnd-kit       │                   │ SwebKit.Redis│  │
│  │  react-apexcharts│                  │ SwebKit.Agents│ │
│  └─────────────────┘                   │ SwebKit.Obs  │  │
│         ↑                               │ SwebKit.DevOps│ │
│  Tauri APIs:                            └──────────────┘  │
│  - window/menu/tray                                       │
│  - file system (JSON config)                              │
│  - auto-updater                                           │
│  - sidecar process lifecycle                              │
└──────────────────────────────────────────────────────────┘
```

### Responsibility split

| Layer | Technology | Handles |
|---|---|---|
| **App shell** | Tauri (Rust) | Window, menu, tray, file system, auto-update, sidecar lifecycle |
| **UI** | React + Vite + Tailwind + shadcn/ui | All components, routing, state |
| **Server state** | TanStack Query | Caching, refetching, optimistic updates |
| **Client state** | Zustand | UI state, selection context, connection state, active project/env |
| **Backend** | .NET ASP.NET Minimal API | All business logic: Azure SDK, K8s, Redis, AI Agent, config persistence |

### Communication: React ↔ .NET sidecar

- Sidecar runs as a local ASP.NET Minimal API on a dynamic port
- Tauri starts the sidecar as a managed child process, reads the port from stdout
- React calls the sidecar via `fetch()` with TanStack Query wrappers
- Streaming endpoints (pod logs, xterm) use Server-Sent Events (SSE) or WebSocket from the sidecar
- Auto-generated TypeScript types via `NSwag` or `Kiota` from the .NET OpenAPI spec

---

## Project Structure

```
swebkit/
├── src-tauri/              # Tauri Rust shell
│   ├── src/
│   │   ├── main.rs         # Tauri entry, sidecar spawn, window setup
│   │   ├── sidecar.rs      # .NET process lifecycle management
│   │   ├── config.rs       # JSON config read/write via Tauri fs
│   │   └── menu.rs         # App menu, tray icon
│   ├── Cargo.toml
│   └── tauri.conf.json
│
├── src/                    # React + Vite frontend
│   ├── main.tsx
│   ├── App.tsx
│   ├── router.tsx          # React Router
│   ├── lib/
│   │   ├── api.ts          # Generated API client (from OpenAPI)
│   │   ├── query-client.ts # TanStack Query setup
│   │   └── stores/         # Zustand stores
│   │       ├── connection.ts
│   │       ├── selection.ts
│   │       └── settings.ts
│   ├── components/
│   │   ├── ui/             # shadcn/ui components
│   │   ├── layout/         # Shell, sidebar, header
│   │   ├── service-bus/    # Service Bus feature
│   │   ├── api-client/     # API Client feature
│   │   ├── aks/            # AKS feature
│   │   ├── redis/          # Redis feature
│   │   ├── storage/        # Storage account feature
│   │   ├── agent/          # AI Agent chat
│   │   ├── dashboard/      # Dashboard
│   │   └── settings/       # Settings page
│   ├── hooks/              # Custom React hooks
│   └── styles/
│       └── globals.css     # Tailwind directives
│
├── src-sidecar/            # .NET ASP.NET Minimal API sidecar
│   ├── SwebKit.Sidecar.csproj
│   ├── Program.cs          # Minimal API endpoints
│   ├── Endpoints/
│   │   ├── ServiceBusEndpoints.cs
│   │   ├── AksEndpoints.cs
│   │   ├── RedisEndpoints.cs
│   │   ├── StorageEndpoints.cs
│   │   ├── AgentEndpoints.cs
│   │   └── ConfigEndpoints.cs
│   └── openapi.json        # Generated spec for TypeScript client
│
├── src-core/               # Existing .NET libraries (unchanged)
│   ├── SwebKit.Core/
│   ├── SwebKit.Azure/
│   ├── SwebKit.Kubernetes/
│   ├── SwebKit.Redis/
│   ├── SwebKit.Agents/
│   ├── SwebKit.Observability/
│   └── SwebKit.DevOps/
│
├── tests/
│   ├── src/                # Vitest + React Testing Library
│   └── existing-dotnet/    # Keep all 555 existing .NET tests
│
├── package.json
├── vite.config.ts
├── tailwind.config.ts
├── tsconfig.json
└── README.md
```

---

## npm Dependencies

### Core
- `react`, `react-dom` — UI framework
- `react-router-dom` — routing
- `@tanstack/react-query` — server state (caching, refetching)
- `zustand` — client state (simplest option, minimal boilerplate)
- `vite`, `@vitejs/plugin-react` — build tool
- `typescript` — type safety

### UI
- `tailwindcss`, `postcss`, `autoprefixer` — styling
- `shadcn/ui` (Radix primitives) — component library
- `lucide-react` — icons
- `class-variance-authority`, `clsx`, `tailwind-merge` — shadcn/ui utilities

### Feature-specific
- `@monaco-editor/react` — code editor (replaces BlazorMonaco)
- `@xterm/xterm`, `@xterm/addon-fit` — terminal (replaces JS interop xterm)
- `@dnd-kit/core`, `@dnd-kit/sortable` — drag-and-drop (replaces native HTML5 DnD workarounds)
- `react-apexcharts`, `apexcharts` — charts (replaces Blazor-ApexCharts)
- `react-markdown`, `remark-gfm` — markdown rendering (replaces Markdig)
- `js-yaml` — YAML parsing (replaces YamlDotNet in UI)

### Dev
- `vitest`, `@testing-library/react`, `@testing-library/jest-dom` — testing
- `@types/react`, `@types/react-dom`, `@types/node` — type definitions
- `eslint`, `@typescript-eslint/parser`, `@typescript-eslint/eslint-plugin` — linting

---

## .NET Sidecar API Surface

The sidecar exposes the existing interfaces as HTTP endpoints. Each interface maps to a route prefix:

### Service Bus (`/api/servicebus`)
| Method | Route | Maps to |
|---|---|---|
| GET | `/{nsId}/info` | `IServiceBusClient.GetNamespaceInfoAsync` |
| GET | `/{nsId}/queues` | `ListQueuesAsync` |
| GET | `/{nsId}/topics` | `ListTopicsAsync` |
| GET | `/{nsId}/topics/{topic}/subscriptions` | `ListSubscriptionsAsync` |
| PATCH | `/{nsId}/queues/{queue}/enabled` | `SetQueueEnabledAsync` |
| GET | `/{nsId}/entities/{path}/stats` | `GetEntityStatsAsync` |
| GET | `/{nsId}/entities/{path}/peek?count=&fromSeq=` | `PeekMessagesAsync` |
| GET | `/{nsId}/entities/{path}/dlq?count=&fromSeq=` | `PeekDeadLetterAsync` |
| POST | `/{nsId}/entities/{path}/complete` | `CompleteMessagesAsync` |
| POST | `/{nsId}/entities/{path}/purge` | `PurgeMessagesAsync` |
| POST | `/{nsId}/entities/{path}/send` | `SendMessageAsync` |
| POST | `/{nsId}/entities/{path}/send-batch` | `SendBatchAsync` |
| POST | `/{nsId}/entities/{path}/schedule` | `ScheduleMessageAsync` |
| DELETE | `/{nsId}/entities/{path}/schedule/{seq}` | `CancelScheduledMessageAsync` |
| POST | `/{nsId}/entities/{path}/resubmit` | `ResubmitDeadLetterAsync` |
| POST | `/{nsId}/entities/{path}/dlq/complete` | `CompleteDeadLetterAsync` |
| GET | `/{nsId}/test` | `TestConnectionAsync` |

### AKS (`/api/aks`)
| Method | Route | Maps to |
|---|---|---|
| GET | `/contexts` | `GetContextsAsync` |
| GET | `/namespaces` | `GetNamespacesAsync` |
| GET | `/{ns}/pods` | `GetPodsAsync` |
| GET | `/{ns}/deployments` | `GetDeploymentsAsync` |
| GET | `/{ns}/services` | `GetServicesAsync` |
| GET | `/{ns}/ingresses` | `GetIngressesAsync` |
| GET | `/{ns}/ingresses/{name}/analysis` | `AnalyzeIngressAsync` |
| GET | `/{ns}/network-policies?workloadKind=&workloadName=` | `AnalyzeNetworkPoliciesAsync` |
| GET | `/{ns}/gateways` | `GetGatewaysAsync` |
| GET | `/{ns}/httproutes` | `GetHttpRoutesAsync` |
| GET | `/{ns}/helm/releases` | `GetHelmReleasesAsync` |
| GET | `/{ns}/helm/releases/{name}/history` | `GetHelmReleaseHistoryAsync` |
| GET | `/{ns}/helm/releases/{name}/values` | `GetHelmReleaseValuesAsync` |
| POST | `/{ns}/helm/releases/{name}/rollback` | `RollbackHelmReleaseAsync` |
| GET | `/{ns}/statefulsets` | `GetStatefulSetsAsync` |
| GET | `/{ns}/configmaps` | `GetConfigMapsAsync` |
| GET | `/{ns}/secrets` | `GetSecretsAsync` |
| GET | `/{ns}/secrets/{name}/values` | `GetSecretValuesAsync` |
| GET | `/{ns}/events?limit=&involvedObject=` | `GetEventsAsync` |
| GET | `/{ns}/pods/{pod}/logs?container=&follow=` (SSE) | `StreamPodLogsAsync` |
| GET | `/{ns}/deployments/{dep}/logs?follow=` (SSE) | `StreamDeploymentLogsAsync` |
| GET | `/{ns}/resources/{kind}/{name}/yaml` | `GetResourceYamlAsync` |
| POST | `/{ns}/resources/{kind}/{name}/yaml` | `ApplyResourceYamlAsync` |
| POST | `/{ns}/deployments/{name}/restart` | `RestartDeploymentAsync` |
| DELETE | `/{ns}/pods/{pod}` | `DeletePodAsync` |
| POST | `/{ns}/deployments/{name}/scale` | `ScaleDeploymentAsync` |
| POST | `/port-forward` | `StartPortForwardAsync` |
| DELETE | `/port-forward/{id}` | `StopPortForwardAsync` |
| GET | `/test` | `TestConnectionAsync` |

### Redis (`/api/redis`)
| Method | Route | Maps to |
|---|---|---|
| GET | `/{cacheId}/test` | `TestConnectionAsync` |
| GET | `/{cacheId}/scan?pattern=&cursor=&pageSize=` | `ScanKeysAsync` |
| GET | `/{cacheId}/keys/{key}/type` | `GetKeyTypeAsync` |
| GET | `/{cacheId}/keys/{key}/info` | `GetKeyInfoAsync` |
| GET | `/{cacheId}/keys/{key}/value` | `GetKeyValueAsync` |
| GET | `/{cacheId}/keys/{key}/hash` | `GetHashFieldsAsync` |
| GET | `/{cacheId}/keys/{key}/list?start=&stop=` | `GetListItemsAsync` |
| GET | `/{cacheId}/keys/{key}/set` | `GetSetMembersAsync` |
| GET | `/{cacheId}/keys/{key}/zset?start=&stop=` | `GetSortedSetMembersAsync` |
| PUT | `/{cacheId}/keys/{key}/value` | `SetKeyValueAsync` |
| PUT | `/{cacheId}/keys/{key}/hash/{field}` | `SetHashFieldAsync` |
| DELETE | `/{cacheId}/keys` | `DeleteKeysAsync` |
| POST | `/{cacheId}/import` | `ImportAsync` |
| GET | `/{cacheId}/keys/{key}/ttl` | `GetTtlAsync` |
| PUT | `/{cacheId}/keys/{key}/ttl` | `SetTtlAsync` |
| DELETE | `/{cacheId}/keys/{key}/ttl` | `RemoveTtlAsync` |
| POST | `/{cacheId}/flush` | `FlushDatabaseAsync` |
| GET | `/{cacheId}/server-info` | `GetServerInfoAsync` |
| GET | `/{cacheId}/slowlog?top=` | `GetSlowLogAsync` |
| GET | `/{cacheId}/pubsub?pattern=` | `GetPubSubSnapshotAsync` |

### Storage (`/api/storage`)
| Method | Route | Maps to |
|---|---|---|
| GET | `/{accountId}/test` | `TestConnectionAsync` |
| GET | `/{accountId}/containers` | `ListContainersAsync` |
| GET | `/{accountId}/containers/{container}/blobs?prefix=&continuation=&pageSize=` | `ListBlobsAsync` |
| GET | `/{accountId}/containers/{container}/blobs/{blob}/properties` | `GetBlobPropertiesAsync` |
| GET | `/{accountId}/containers/{container}/blobs/{blob}/content?maxBytes=` | `GetBlobContentAsync` |
| GET | `/{accountId}/containers/{container}/blobs/{blob}/sas?expiry=` | `GetBlobSasUrlAsync` |
| POST | `/{accountId}/containers/{container}/blobs/{blob}/upload` | `UploadBlobAsync` |
| DELETE | `/{accountId}/containers/{container}/blobs/{blob}` | `DeleteBlobAsync` |

### AI Agent (`/api/agent`)
| Method | Route | Maps to |
|---|---|---|
| POST | `/chat` (SSE streaming) | `IApiClientAgentService.ChatStreamAsync` |
| GET | `/config` | `AgentConfig` |
| PUT | `/config` | Update `AgentConfig` |

### Config (`/api/config`)
| Method | Route | Maps to |
|---|---|---|
| GET | `/profiles` | `ProfileRepository` load |
| PUT | `/profiles` | `ProfileRepository` save |
| GET | `/environments` | `EnvironmentRepository` load |
| PUT | `/environments` | `EnvironmentRepository` save |
| GET | `/collections` | `CollectionsStore` load |
| PUT | `/collections` | `CollectionsStore` save |
| GET | `/agent-config` | Agent config load |
| PUT | `/agent-config` | Agent config save |

---

## Config Storage (JSON files, fresh format)

New config files in Tauri's app data directory:

| File | Contents |
|---|---|
| `settings.json` | Global app settings (theme, active project, window state) |
| `projects.json` | List of projects with environments (Dev/Test/Acc/Prod) |
| `service-bus.json` | Service Bus namespace connections per project/env |
| `api-collections.json` | API client collections, requests, folders |
| `api-environments.json` | API client environments and variables |
| `aks-config.json` | AKS cluster connections per project/env |
| `redis-config.json` | Redis cache connections per project/env |
| `storage-config.json` | Storage account connections per project/env |
| `agent-config.json` | AI agent provider profiles |

Config is read/written by the .NET sidecar (not Tauri/Rust) to keep all business logic in one place. The sidecar uses the same `ProfileRepository` / `EnvironmentRepository` patterns but with new JSON schemas.

---

## Feature Migration Scope

### Initial release (must-have)

| Feature | Blazor files | React components | Complexity | Key libraries |
|---|---|---|---|---|
| **Dashboard** | 1 page | 1 page | Low | react-apexcharts |
| **Settings** | 3 files | 3-4 components | Low | shadcn/ui forms |
| **Service Bus** | 24 files | ~15 components | High | TanStack Virtual (message list), Monaco (composer) |
| **AKS** | 71 files | ~30 components | High | xterm.js (logs), Monaco (YAML), TanStack Table (grids) |
| **API Client** | 68 files | ~25 components | High | @dnd-kit (tree), Monaco (body editor), TanStack Table |
| **Redis** | 14 files | ~8 components | Medium | Monaco (value viewer) |
| **Storage** | 13 files | ~6 components | Medium | TanStack Table |
| **AI Agent** | 2 files | ~3 components | Low | SSE streaming, react-markdown |

### Deferred (post-launch)

| Feature | Reason |
|---|---|
| **Observability** | App Insights queries — can use Azure REST API later |
| **Pipelines** | Azure DevOps pipeline views |
| **Releases** | Release management and approvals |
| **Incident Timeline** | Complex investigation feature |

---

## Implementation Phases

### Phase 1: Foundation (Week 1-2)

**1.1 Tauri project scaffold**
- `npm create tauri-app` with React + TypeScript + Vite
- Configure `tauri.conf.json`: window size, title, permissions
- Set up Tailwind CSS + shadcn/ui
- Verify dev server + Tauri dev mode works

**1.2 .NET sidecar project**
- Create `SwebKit.Sidecar` ASP.NET Minimal API project
- Reference existing `SwebKit.Core`, `SwebKit.Azure`, `SwebKit.Kubernetes`, `SwebKit.Redis`, `SwebKit.Agents`, `SwebKit.Observability`, `SwebKit.DevOps`
- Set up DI container with all existing service registrations
- Implement health check endpoint (`/health`)
- Implement config endpoints (`/api/config/*`)
- Generate OpenAPI spec

**1.3 Sidecar lifecycle (Rust)**
- `sidecar.rs`: spawn .NET process as child, pass port via `--urls` arg
- Read the actual port from sidecar stdout
- Expose port to React via Tauri command
- Handle sidecar crash + restart
- Graceful shutdown on app close

**1.4 React app shell**
- Layout: sidebar nav + main content area
- React Router with routes for each feature
- TanStack Query client setup with sidecar base URL
- Zustand stores: `connectionStore`, `selectionStore`, `settingsStore`
- shadcn/ui base components: Button, Input, Dialog, Dropdown, Toast, Tooltip, Tabs, Table
- Dark/light theme toggle

**1.5 TypeScript API client**
- Generate TypeScript types from OpenAPI spec (NSwag or Kiota)
- Create typed `apiClient` wrapper with fetch + error handling
- TanStack Query hooks per endpoint

### Phase 2: Config + Dashboard (Week 2)

**2.1 Config management**
- Settings page: project list, environment selector, connection management
- Service Bus namespace config form
- AKS cluster config form
- Redis cache config form
- Storage account config form
- Agent config form
- All saved via sidecar `/api/config` endpoints

**2.2 Dashboard**
- Overview cards: connected services status
- Quick links to each feature
- Recent activity (favorites/pinned resources)

### Phase 3: Service Bus (Week 3-4)

**3.1 Entity tree**
- Queue/topic/subscription tree with expand/collapse
- Entity stats badges (active count, DLQ count)
- Search/filter

**3.2 Message list view**
- Virtualized list (TanStack Virtual) for large message counts
- Peek vs DLQ mode toggle
- "Load more" with sequence number pagination
- Message selection → detail pane

**3.3 Message detail pane**
- Headers grid (key-value)
- Body viewer (Monaco editor, JSON/XML/text formatting)
- Message metadata (EnqueuedTime, SequenceNumber, etc.)

**3.4 Message composer**
- Monaco editor for message body
- Header key-value grid
- Send / Send batch / Schedule
- Template picker (saved templates)

**3.5 DLQ operations**
- Resubmit dialog with remap rules
- Complete DLQ
- Purge

### Phase 4: AKS (Week 4-6)

**4.1 Connection bar**
- Context selector (kubectl contexts)
- Namespace selector
- Connection test

**4.2 Resource grids**
- Pods, Deployments, StatefulSets, Services, Ingresses, ConfigMaps, Secrets
- TanStack Table with sorting, filtering, column visibility
- Resource-specific actions (restart, scale, delete, edit YAML)

**4.3 Pod log viewer**
- xterm.js terminal component
- SSE streaming from sidecar
- Multi-pod log aggregation (deployment logs)
- Log filter options (container, tail, follow, since, previous)

**4.4 YAML viewer/editor**
- Monaco editor with YAML language
- Apply/dry-run validation
- Helm release values viewer

**4.5 Port forwarding**
- Active sessions panel
- Start dialog (resource, local port, remote port)
- Stop session

**4.6 Helm panel**
- Release list
- Release history with rollback
- Values viewer
- Diff preview

### Phase 5: API Client (Week 6-8)

**5.1 Collection tree**
- @dnd-kit sortable tree for collections/folders/requests
- Context menu (rename, delete, duplicate, move)
- Inline rename input
- Search/filter
- Method badges (GET=green, POST=blue, etc.)

**5.2 Request builder**
- Method selector + URL bar
- Headers grid (KeyValueGrid)
- Query params grid
- Body editor (Monaco: JSON, XML, form-data, raw)
- Auth panel (None, Basic, Bearer, API Key, OAuth2)
- Variable substitution preview

**5.3 Response viewer**
- Status code + time + size badges
- Response headers grid
- Response body (Monaco with syntax highlighting)
- Response format detection (JSON, XML, HTML, text)

**5.4 Tabs**
- Multiple open requests in tabs
- Tab strip with close + dirty indicator

**5.5 Environments**
- Environment manager panel
- Variable editor with secret source picker
- Active environment selector per collection

**5.6 Export/import**
- Collection export dialog (JSON, cURL, Postman format)
- Collection import

**5.7 WebSocket panel**
- Connection URL + headers
- Message log (sent/received)
- Send message input
- Connection status indicator

**5.8 GraphQL panel**
- Schema explorer
- Query editor (Monaco GraphQL)
- Variables input
- Response viewer

### Phase 6: Redis (Week 8-9)

**6.1 Connection bar**
- Cache selector
- Connection test

**6.2 Key browser**
- SCAN-based key list with pattern filter
- Key type badges
- TTL display

**6.3 Key detail**
- String: Monaco value viewer
- Hash: key-value grid
- List: item list
- Set: member list
- Sorted set: score + member table
- TTL management (set, remove)
- Rename key, delete key, delete hash field

**6.4 Server info**
- INFO command output
- Slowlog viewer
- Pub/sub channel snapshot

### Phase 7: Storage (Week 9)

**7.1 Container tree**
- Container list
- Virtual folder navigation (prefix-based)

**7.2 Blob list**
- TanStack Table with name, size, content type, last modified
- Pagination via continuation token

**7.3 Blob detail**
- Properties panel (metadata, tags, tier, lease)
- Content viewer (text/JSON, binary detection)
- SAS URL generation
- Upload, download, delete

### Phase 8: AI Agent (Week 9-10)

**8.1 Chat panel**
- Message thread (markdown rendering)
- Streaming responses (SSE from sidecar)
- Provider profile selector
- History management (clear, max messages)

**8.2 Tool integration**
- Agent tool invocations display
- Tool result rendering

### Phase 9: Polish + Testing (Week 10-11)

**9.1 Testing**
- Vitest + React Testing Library for component tests
- Integration tests for sidecar endpoints
- Keep all 555 existing .NET tests passing

**9.2 Keyboard shortcuts**
- Global command palette (cmd+k)
- Feature-specific shortcuts

**9.3 Notifications**
- Toast notifications for success/error
- Windows notification integration (Tauri plugin)

**9.4 Auto-update**
- Tauri updater plugin
- GitHub releases as update source

**9.5 Cross-platform testing**
- Windows: primary target
- macOS: test WebView (WKWebView)
- Linux: test WebView (WebKitGTK)

**9.6 Packaging**
- Tauri bundler: MSI/NSIS for Windows, DMG for macOS, AppImage for Linux
- CI/CD: GitHub Actions for multi-platform builds

---

## What Gets Eliminated

| Current code | Why it's gone |
|---|---|
| `SwebKitComponentBase.cs` (render coalescing) | React handles rendering natively |
| `SwebKitLayoutBase.cs` | Same |
| 125 JS interop calls across 41 files | Monaco, xterm, dnd-kit are native JS |
| `wwwroot/js/uiState.js` (enableDragDrop, clampMenu) | Native in React |
| `wwwroot/js/keyboardShortcuts.js` | React keyboard hooks |
| `wwwroot/js/splitter.js` | react-resizable-panels |
| `wwwroot/js/monacoLoader.js` | @monaco-editor/react handles loading |
| `wwwroot/js/yamlHighlight.js` | Monaco YAML extension |
| `@onkeydown:preventDefault` / `@onkeydown:stopPropagation` conflicts | Native DOM events in React |
| `@ondragover:preventDefault` async timing issues | @dnd-kit handles DnD natively |
| `Virtualize` component workarounds | TanStack Virtual |
| `ElementReference` + `FocusAsync()` | React refs + useEffect |
| MSIX certificate management | Tauri bundler handles signing |
| `DISABLE_XAML_GENERATED_BREAK_ON_UNHANDLED_EXCEPTION` | No WinUI XAML |
| MAUI bootstrap COMExceptions | No MAUI |
| `install.ps1` certificate trust | Tauri handles installation |
| CSS isolation scoping (`b-xyz123` selectors) | Tailwind + CSS Modules |
| Duplicate CSS in `02-api-components.css` | Single source of truth |
| FluentUI Blazor 633 usages | shadcn/ui replaces all |

---

## What Stays Unchanged

| Asset | Status |
|---|---|
| `SwebKit.Core` (50 interfaces, models, config) | **100% kept** — sidecar references it |
| `SwebKit.Azure` (Service Bus, KeyVault, Storage) | **100% kept** |
| `SwebKit.Kubernetes` (K8s client, Helm) | **100% kept** |
| `SwebKit.Redis` | **100% kept** |
| `SwebKit.Agents` (Mistral AI) | **100% kept** |
| `SwebKit.Observability` | **100% kept** (deferred feature, but code stays) |
| `SwebKit.DevOps` | **100% kept** (deferred feature, but code stays) |
| All 555 .NET tests | **100% kept** — they test backend logic |

---

## Acceptance Criteria

1. **App launches** on Windows via Tauri (double-click installer, no MSIX)
2. **Sidecar starts** automatically and reconnects on crash
3. **Service Bus**: can connect to a namespace, browse entities, peek messages, send messages, manage DLQ
4. **AKS**: can connect to a cluster, browse pods/deployments/services, tail logs, view/edit YAML, port-forward
5. **API Client**: can create collections, build requests, send them, view responses, drag-and-drop reorder, use environments
6. **Redis**: can connect to a cache, browse keys, view/edit values, manage TTL
7. **Storage**: can connect to an account, browse containers/blobs, view content, download
8. **AI Agent**: can chat with streaming responses, switch provider profiles
9. **Dashboard**: shows connected services status and quick navigation
10. **Settings**: can configure all connections, saved to JSON files
11. **All existing .NET tests pass** unchanged
12. **Bundle size** < 50MB total (Tauri + sidecar)
13. **Cold start** < 3 seconds
14. **No JS interop workarounds** — all UI interactions are native React/DOM

---

## Risks + Mitigations

| Risk | Impact | Mitigation |
|---|---|---|
| Sidecar port conflicts | App won't start | Bind to port 0 (OS-assigned), read actual port from stdout |
| Azure interactive auth in sidecar | Auth fails | Use `InteractiveBrowserCredential` — works in sidecar process, opens system browser |
| Streaming (pod logs) via HTTP | Latency | Use SSE (Server-Sent Events) — simpler than WebSocket, works through fetch |
| Monaco Editor bundle size | Slow startup | Lazy-load Monaco via dynamic import |
| .NET sidecar binary size | ~40MB | Publish as self-contained + trimmed single-file |
| Cross-platform WebView differences | CSS/JS quirks | Test on all 3 platforms; Tauri's WebView abstraction handles most cases |
| Big bang migration takes too long | Feature freeze | Phased implementation — each phase is independently testable |

---

## Timeline Summary

| Phase | Weeks | Deliverable |
|---|---|---|
| 1. Foundation | 1-2 | Tauri shell + sidecar + React scaffold + API client |
| 2. Config + Dashboard | 2 | Settings page + dashboard |
| 3. Service Bus | 3-4 | Full Service Bus feature |
| 4. AKS | 4-6 | Full AKS feature |
| 5. API Client | 6-8 | Full API Client feature |
| 6. Redis | 8-9 | Full Redis feature |
| 7. Storage | 9 | Full Storage feature |
| 8. AI Agent | 9-10 | Full Agent feature |
| 9. Polish + Testing | 10-11 | Cross-platform, packaging, auto-update |
| **Total** | **~11 weeks** | **Complete rewrite** |

---

## Branch Strategy

- **Branch**: `feat/tauri-react-rewrite` (already created)
- **No changes to `main`** until the rewrite is complete
- **Existing .NET projects** (`SwebKit.Core`, `SwebKit.Azure`, etc.) are referenced from the new sidecar project — they stay in place
- **Old `SwebKit.App`** (MAUI Blazor) stays on `main` for reference, deleted from the rewrite branch once the new app is functional
- **Merge to `main`** only when all acceptance criteria are met
