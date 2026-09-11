---
status: Review
---

# Redis Settings Clarity & Entra ID Auth Mode — Status

- **Current phase:** Review — implemented and unit/type-checked; manual verification pending.
- **Reported by:** Sebastien — Redis settings panel unclear (Database/Active/Namespace Separator
  unexplained), requested Entra ID auth by cache name plus keeping full connection-string support.
- **Implementation PR:** not raised yet.

## Definition of Done

- [x] `RedisCacheEntry`/`RedisConfig.Validate()` support an Entra ID (AAD) mode alongside the
      existing connection-string mode.
- [x] `RedisClient` connects via Entra ID using the shared `AzureCredentialFactory`, matching the
      Storage/Service Bus pattern.
- [x] Redis settings UI exposes a connection-string vs Entra ID toggle mirroring
      `StorageSettings.tsx`.
- [x] Namespace Separator, connection-string/cache-name field, Database and Active all carry
      explanatory helper text.
- [x] Unit tests for both new guard paths and `Validate()` branches.
- [x] Architecture doc (`redis.md`) updated with a Credential Modes table.
- [ ] Manual verification in the running Tauri app, including a live Entra ID connection test —
      **owner: Sebastien**.
- [ ] Aikido security scan.

## Follow-ups

None identified yet — flag if the manual verification surfaces anything (e.g. token-refresh
behavior on long-lived key-browser sessions, which was not exercised by unit tests).
