# Feature Overview - Redis Manager

---

title: "Feature Overview - Redis Manager"
owner: ""
status: "Proposed"
created: "2026-03-12"
updated: "2026-03-12"

---

## Goal

Add a Redis management module to SwebKit so developers can browse, inspect, and manage Redis caches directly from the app — without switching to CLI tools or third-party GUIs.

## Value

Redis is a core piece of most service architectures but lacks a lightweight, integrated management UI for day-to-day developer use. This feature lets developers quickly check cache state, flush stale keys, and debug data issues without context-switching away from their environment dashboard.

## Scope

### In scope

- Redis connection string management per environment (save, edit, delete, test connection)
- Connection aliasing — friendly names (e.g., "Dev Cache") alongside raw connection strings
- Key browser with cursor-based SCAN and pattern search (e.g., `user:*`, `session:*`)
- Key inspection: view value, TTL, type, encoding, memory usage
- Key value editing — inline edit for string values and hash fields
- TTL management — set, update, and remove TTL on individual keys
- Key deletion (single and bulk) with production guard
- Flush database with confirmation
- DB selector (DB0-DB15)
- Server info dashboard — `INFO` command output: version, memory, clients, hit/miss ratio, keyspace stats
- Support for common data types: String, Hash, List, Set, Sorted Set, Stream
- Auto-refresh with configurable interval (reuse `AutoRefreshToggle`)

**Post-v1 enhancements (backlog):**
- Pub/Sub monitor — live message viewer on selected channels
- Slow log viewer — `SLOWLOG GET` with timestamp, duration, command
- Key namespace grouping — tree view by prefix with counts
- Import/Export — JSON export/import of selected keys
- Keyspace notifications — live key change feed
- Lua script runner — `EVAL` with editor and result viewer
- Cluster topology view — node/slot/replication map
- Memory analysis — per-prefix memory distribution
- Favorites / pinned keys

### Out of scope

- Redis Sentinel management
- Redis configuration editing (`CONFIG SET`)
- Backup/restore (RDB/AOF management)
- Multi-database management beyond `SELECT` (DB0-DB15 selector is in scope)

## Dependencies

- `StackExchange.Redis` NuGet package (de facto .NET Redis client)
- Existing app shell: `ProjectEnvironment` config model, `LeftNav`, `SettingsPage`

## Risks & mitigations

- Risk: Large keyspaces (millions of keys) could freeze the UI — Mitigation: cursor-based `SCAN` with pagination, never use `KEYS *`
- Risk: Accidental flush/delete on production — Mitigation: production guard (reuse `AksConfirmBar` pattern), typed-name confirmation for destructive ops
- Risk: Connection string contains secrets — Mitigation: mask password in UI, store securely via existing app state persistence
- Risk: Redis Cluster vs Standalone differences — Mitigation: start with standalone/single-endpoint; add cluster support as enhancement

## Related documents

- Architecture: `docs/architecture/architecture.md`
- Pitfalls: `docs/pitfalls/blazor-maui.md`
- Pattern reference: AKS feature (`docs/features/archive/aks/`, `docs/features/archive/aks-enhancements/`, `docs/features/archive/aks-enhancements-v2/`)

## Quick links

- Status: `status.md`
- Backend plan: `backend.md`
- Frontend plan: `frontend.md`
- Decisions: `decisions.md`
- Test plan: `test-plan.md`
