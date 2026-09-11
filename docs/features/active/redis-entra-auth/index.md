---
status: Review
---

# Redis Settings Clarity & Entra ID Auth Mode

## Scope

Reported against Redis settings: the panel exposed a bare connection-string textbox plus
unlabeled "Database" and "Active" controls with no explanation of what any of them do, and
there was no way to connect to Azure Cache for Redis with Entra ID — even though Storage and
Service Bus already support Entra ID auth via a shared credential factory.

1. **No auth-mode choice.** `RedisCacheEntry` only had `ConnectionString`; a user who wanted to
   avoid embedding a password had no alternative.
2. **Unclear controls.** "Database", "Active" and "Namespace Separator" had no explanatory text,
   and the connection-string field's only hint was a `localhost:6379` placeholder.

## Outcomes

- A Redis cache can be configured either by connection string (unchanged) or by Entra ID (AAD),
  authenticating with the same shared credential (`AzureCredentialFactory.CreateDefault()`)
  already used by Storage and Service Bus — no new login flow.
- Entra ID mode only asks for the Azure Cache for Redis resource name; the app builds
  `<name>.redis.cache.windows.net:6380` and connects via
  `Microsoft.Azure.StackExchangeRedis`'s `ConfigureForAzureWithTokenCredentialAsync`.
- Namespace Separator, Database and Active each carry one-line helper text explaining what they
  do.

## Non-goals

- No `ConnectionStringRef`/credential-store indirection for Redis (unlike Storage) — the raw
  connection string keeps being stored directly on the cache entry, matching today's behavior.
  Introducing that indirection is unrelated hardening, not what was asked.
- No changes to the legacy Blazor `RedisConfigForm.razor` — Tauri/React is the active surface;
  the new domain fields are additive/defaulted so the Blazor build is unaffected.
- No support for Azure Managed Redis's `*.region.redis.azure.net` naming — only classic Azure
  Cache for Redis.

## Traceability

- Technical plan: `technical-plan.md`
- Test plan: `test-plan.md`
- Status: `status.md`
- Related: `docs/architecture/functionalities/redis.md` (Credential Modes),
  `docs/architecture/functionalities/storage.md` (the pattern this mirrors)
