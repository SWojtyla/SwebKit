# Decisions - Redis Manager

---

title: "Decisions - Redis Manager"
owner: ""
status: "Proposed"
created: "2026-03-12"

---

## Decision 001 — Use StackExchange.Redis

**Status:** Proposed

### Context

.NET has several Redis client libraries. We need a mature, well-maintained library that supports standalone, cluster, and Azure Cache for Redis.

### Decision

Use `StackExchange.Redis` — the de facto standard .NET Redis client. It supports multiplexed connections, async operations, Lua scripting, pub/sub, cluster, and Sentinel.

### Consequences

- Battle-tested library used by most .NET projects.
- `ConnectionMultiplexer` is long-lived and thread-safe — one instance per connection string.
- Connection string format is widely understood (`host:port,password=...,ssl=true,...`).

### Alternatives considered

- `ServiceStack.Redis` — requires commercial license.
- `FreeRedis` — less mature, smaller community.

---

## Decision 005 — Include editing, TTL, server info, and aliasing in v1

**Status:** Accepted

**Date:** 2026-03-12

### Context

Several post-MVP enhancements were evaluated for inclusion in the first release. Need to balance feature completeness against delivery speed.

### Decision

Include four high-value, low-effort enhancements in v1:
- **Key value editing** — inline edit for strings and hash fields.
- **TTL management** — set/update/remove per key.
- **Server info dashboard** — `INFO` command output (version, memory, clients, hit/miss, keyspace).
- **Connection aliasing** — friendly names alongside connection strings.

Defer the remaining enhancements (pub/sub, slow log, namespace grouping, import/export, keyspace notifications, Lua scripts, cluster topology, memory analysis, favorites) to a v2.

### Consequences

- v1 covers the most common daily workflows without significant extra effort.
- Deferred features are well-scoped for a future iteration.
- Editing is restricted to strings and hash fields — sufficient for config tweaks and cache debugging.

---

## Decision 002 — Cursor-based SCAN for key browsing (never KEYS)

**Status:** Proposed

### Context

`KEYS *` blocks the Redis server and can cause outages on large keyspaces. We need a safe way to browse keys.

### Decision

Always use `SCAN` with cursor-based pagination. Load keys in batches (e.g., 100 at a time) with a "Load more" button or virtual scroll. Support glob patterns (e.g., `user:*`).

### Consequences

- Safe for production use — non-blocking, O(1) per call.
- Results may include duplicates across pages — deduplicate client-side.
- Slightly more complex UI (progressive loading vs. full list).

---

## Decision 003 — Production guard for destructive operations

**Status:** Proposed

### Context

`FLUSHDB`, bulk delete, and key deletion can cause data loss. Same risk pattern as AKS mutative operations.

### Decision

Reuse the `AksConfirmBar` pattern (rename to a shared component). Production environments require typed confirmation. Non-production gets simple confirm/cancel.

### Consequences

- Consistent UX across AKS and Redis features.
- Shared confirmation component reduces duplication.

---

## Decision 004 — Connection string storage

**Status:** Proposed

### Context

Redis connection strings contain passwords. We need to store them securely and mask them in the UI.

### Decision

Store connection strings in `RedisConfig` within `ProjectEnvironment` (same pattern as `AksConfig`). Mask the password portion in the UI. Rely on existing app state file persistence (same security boundary as Service Bus connection strings).

### Consequences

- Consistent with how other connection configs are stored.
- Password masking in UI prevents shoulder-surfing.
- No additional encryption beyond what the app state file already provides.
