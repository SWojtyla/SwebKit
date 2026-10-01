# API Client settings tab + collections location

State: Done

## Goal

API Client settings were mixed into the General settings tab. They get their
own tab, and the tab answers "where are my collections stored?" — the path is
resolved server-side (`AppDataPaths.CollectionsJson`) so it reflects the real
`SWEBKIT_APPDATA_ROOT` override rather than a guessed `%APPDATA%` path.

## Scope

- `ApiClientSettings.tsx` — new settings component holding the API Client
  toggles (SSL verification, request tabs, auto-save), the Azure Key Vaults
  list, and a Storage section showing the collections file path with a
  copy-to-clipboard button.
- `GeneralSettings.tsx` — API Client section removed; general sections
  (profile, startup, import/export, updates) unchanged.
- `SettingsPage.tsx` — new `api-client` tab between Storage and Access;
  deep links (`/settings?tab=api-client`, `swebkit://settings/api-client`)
  and the command palette entry work through the existing tab plumbing.
- Sidecar: `GET /api/config/collections/location` returns
  `{ path, directory }` from `AppDataPaths.CollectionsJson` — no
  client-supplied input, nothing else exposed.

## Non-goals

- No "open in Explorer/Finder" reveal action — the path is copyable, which is
  enough.
- Only the collections path is surfaced; other app-data files aren't listed.

## Decisions

- Path comes from a dedicated `location` endpoint rather than extending
  `CollectionsStoreResponse`: the store response flows through
  `useCollections`'s `select` (which strips it down to the collection array),
  and the location is static metadata that doesn't belong in a
  concurrency-tokened payload.
- Key Vaults moved with the API Client tab: they're only consumed by
  environment variables of type "Key Vault", which is API Client territory.

## Test plan

- `dotnet test tests/SwebKit.Sidecar.Tests --filter ConfigEndpointsTests` —
  new `GetCollectionsLocation` test asserts the returned path lands inside
  the sandboxed app-data root. (22 passed)
- `npx playwright test e2e/settings.spec.ts` — new "api client tab shows
  where collections are stored" case asserts `collections.json` under the
  e2e appdata root; the stale `sql` tab was added to the all-tabs list.
  (26/26 passed)
- `npm run build` + eslint on changed files — clean.
