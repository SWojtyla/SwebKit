# Test Plan - Redis Manager

---

title: "Test Plan - Redis Manager"
owner: ""
status: "Not started"
created: "2026-03-12"
updated: "2026-03-12"

---

## Goal

Validate Redis connection management, key browsing, inspection, mutation, TTL management, and server info across both the real client and demo client.

## Scope

- In scope: `IRedisClient` contract, `DemoRedisClient`, `RedisClient` unit-testable helpers, UI states
- Out of scope: Integration tests against live Redis (deferred), e2e tests, performance/load testing

## Main scenarios (priority)

1. **Connection lifecycle** — Connect with valid connection string, verify `TestConnectionAsync` returns true. Connect with invalid string, verify graceful error.
2. **Key scanning** — Scan with `*` pattern returns keys. Scan with specific pattern (e.g., `user:*`) filters correctly. Cursor-based pagination returns all keys across multiple batches. Empty keyspace returns empty result.
3. **Key inspection** — Get info for string, hash, list, set, sorted set keys. Verify type, TTL, encoding, memory fields populated correctly.
4. **Value retrieval** — Get string value. Get hash fields. Get list items with range. Get set members. Get sorted set members with scores.
5. **Key mutation** — Set string value. Set hash field. Delete single key. Delete multiple keys. Verify keys are gone after deletion.
6. **TTL management** — Get TTL on key with expiry. Get TTL on key without expiry (returns null). Set TTL. Remove TTL. Verify TTL changes.
7. **Flush database** — Flush DB, verify keyspace is empty afterward.
8. **Server info** — Get server info, verify all fields populated (version, memory, clients, hit ratio, databases).
9. **Production guard** — Destructive operations (delete, flush) require confirmation. Production environments require typed name confirmation.

## Automated coverage

### Unit tests — `DemoRedisClient`

Target: test all `IRedisClient` methods against the demo implementation.

- [x] `TestConnectionAsync` returns true
- [x] `ScanKeysAsync` with `*` returns demo keys
- [x] `ScanKeysAsync` with pattern filters correctly
- [x] `ScanKeysAsync` cursor pagination exhausts all keys
- [x] `GetKeyInfoAsync` returns correct type per key
- [x] `GetKeyInfoAsync` returns TTL for keys with expiry
- [x] `GetKeyValueAsync` returns string value
- [x] `GetHashFieldsAsync` returns field/value pairs
- [x] `GetListItemsAsync` returns items in range
- [x] `GetSetMembersAsync` returns all members
- [x] `GetSortedSetMembersAsync` returns members with scores
- [x] `SetKeyValueAsync` creates/updates key
- [x] `SetHashFieldAsync` adds/updates hash field
- [x] `DeleteKeysAsync` removes keys from keyspace
- [x] `GetTtlAsync` returns correct TTL
- [x] `SetTtlAsync` updates TTL
- [x] `RemoveTtlAsync` clears TTL
- [x] `FlushDatabaseAsync` empties keyspace
- [x] `GetServerInfoAsync` returns populated info

### Unit tests — `RedisClient` helpers

- [ ] Connection string password masking helper
- [ ] Value truncation helper
- [ ] JSON detection and formatting helper
- [ ] Type icon/color mapping helper

## Test data and setup

### DemoRedisClient demo keyspace

Provide a realistic set of demo keys:

| Key                       | Type   | TTL   | Value                                                      |
| ------------------------- | ------ | ----- | ---------------------------------------------------------- |
| `user:1001`               | string | 3600s | `{"id":1001,"name":"Alice","email":"alice@example.com"}`   |
| `user:1002`               | string | 3600s | `{"id":1002,"name":"Bob","email":"bob@example.com"}`       |
| `session:abc123`          | hash   | 1800s | `{user_id: "1001", ip: "10.0.0.1", created: "..."}`        |
| `session:def456`          | hash   | 1800s | `{user_id: "1002", ip: "10.0.0.2", created: "..."}`        |
| `cache:products`          | list   | 300s  | `["product-1", "product-2", ..., "product-10"]`            |
| `cache:categories`        | set    | none  | `{"electronics", "clothing", "food", "books"}`             |
| `leaderboard:daily`       | zset   | none  | `{("alice", 1500), ("bob", 1200), ("charlie", 900)}`       |
| `config:feature-flags`    | hash   | none  | `{dark_mode: "true", beta_api: "false", max_retries: "3"}` |
| `rate-limit:api:10.0.0.1` | string | 60s   | `"42"`                                                     |
| `lock:inventory-sync`     | string | 30s   | `"worker-1"`                                               |

### Mocking strategy

- `DemoRedisClient` maintains an in-memory dictionary simulating the Redis keyspace.
- Mutations (set, delete, flush) modify the in-memory state for realistic behavior.
- SCAN pagination simulated by returning keys in batches from the dictionary.

## Manual checks

- Check: Connect to Redis, verify green status indicator — Enter valid connection string in settings, save, navigate to Redis page, verify connection dot is green and keys are listed.
- Check: Key browsing with pattern filter — Enter `user:*` in pattern input, click Scan, verify only user keys shown.
- Check: Key inspection — Click a hash key, verify detail panel shows fields/values, TTL, type, memory usage.
- Check: Edit string value — Click a string key, click Edit, change value, save, verify new value persists.
- Check: Delete key with production guard — On production environment, right-click key, select Delete, verify typed-name confirmation required.
- Check: Flush database — Click flush, verify confirmation dialog, confirm, verify empty keyspace.
- Check: Server info — Click Server Info, verify dashboard shows version, memory, clients, hit ratio.
- Check: TTL management — Select key, click Set TTL, enter 600, save, verify TTL updated. Click Remove TTL, verify key has no expiry.

## Regression risks & mitigations

- Risk: `ProjectEnvironment` serialization breaks with new `RedisConfig` field — Mitigation: field is nullable, existing JSON deserializes correctly without it.
- Risk: Large keyspaces freeze UI — Mitigation: cursor-based SCAN with pagination, virtual scroll, value truncation.
- Risk: Connection string secrets logged/displayed — Mitigation: password masking helper, no raw connection string in logs.

## Acceptance criteria

- All DemoRedisClient unit tests pass
- All helper unit tests pass
- Build succeeds for all projects
- No regressions in existing tests
- Manual checks pass on demo data

## Validation status

- Automated: DemoRedisClient suite passing (14 tests)
- Manual: Not started
