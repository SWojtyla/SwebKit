# Redis Settings Clarity & Entra ID Auth Mode — Test Plan

## Unit (`tests/SwebKit.Core.Tests/RedisClientTests.cs`)

| Scenario | Expected |
| --- | --- |
| `BuildAadConnectionOptionsAsync` with null/empty/whitespace `CacheName` | Throws before any token acquisition or network attempt |
| `CreateAsync` with `UseAad = true` and empty `CacheName` | Throws, same as the existing empty-connection-string case |
| `RedisConfig.Validate()`, `UseAad = true`, empty `CacheName` | Throws, message names `CacheName` |
| `RedisConfig.Validate()`, `UseAad = true`, valid `CacheName`, no `ConnectionString` | Does not throw — AAD mode does not require a connection string |
| Existing connection-string-mode tests | Unaffected (behavior unchanged when `UseAad` is false, the default) |

## Regression check

No existing frontend test coverage exists for `RedisSettings.tsx` or its sibling
`StorageSettings.tsx` (confirmed: neither has a dedicated unit or e2e spec today), so no new
frontend test was added, consistent with the sibling component's existing convention.

## Results

- `dotnet test tests/SwebKit.Core.Tests` (Redis-related tests) — 42 passed (7 new).
- `dotnet test tests/SwebKit.Sidecar.Tests` (Redis/config-related tests) — 38 passed, no
  regressions.
- `dotnet build src/SwebKit.Redis`, `src-sidecar` — clean.
- `dotnet build src/SwebKit.App` (Blazor) — compiles; only pre-existing, unrelated
  `SigningCertificateThumbprintNotInStore` MSIX packaging failure (local machine cert store, not
  a compile error, not introduced by this change).
- `npx tsc --noEmit` in `web/` — clean.

## Manual verification

Not yet performed — owner: Sebastien. Open Settings → Redis in the running Tauri app:

1. Add a cache, confirm it defaults to Connection String mode.
2. Switch to Entra ID (AAD), confirm the connection-string field is replaced by the cache-name
   field and helper text reads clearly.
3. Enter a real Azure Cache for Redis resource name and use Test Connection to confirm Entra ID
   auth actually succeeds against a live cache with the signed-in identity granted the
   appropriate Data Access Configuration role.
4. Confirm an existing connection-string cache still connects after the change (no regression on
   the unchanged path).
