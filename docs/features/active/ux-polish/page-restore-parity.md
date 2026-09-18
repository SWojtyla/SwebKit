# Module 3 — Page Restore & Deep-Link Parity

Every feature page restores where the operator left it and is deep-linkable — the URL-driven
pattern AKS and Service Bus already use, applied consistently.

Reference pattern: `window.location.search`-based `updateParams` (see `AksWorkspaceContext.tsx`),
per-context persisted prefs via `view-pref:*` (`lib/stores/panel-preferences.ts`), palette
`state` items as the existing deep-link convention (`useCommandPalette.ts`).

## Storage — `StoragePageContext.tsx` (biggest gap)

Currently **everything resets on re-entry**: `activeAccountId` initializes before the profile
loads, container/prefix/blob/filters are plain `useState` — no URL params, no persistence.

- URL params: `?account=`, `?container=`, `?prefix=`, `?blob=`, `?view=` (browser|recovery).
- Persist: `view-pref:storage-last-account`, `view-pref:storage-last-container:<accountId>`.
- Restore on mount: persisted account (validated against `accounts`), then its last container.
- Keep `location.state.accountId` (palette) working — translate to URL param on arrival.
- `handleSelectAccount`/`handleSelectContainer`/`handleNavigatePrefix`/`handleBreadcrumb`/
  `handleSelectBlob` write params instead of only local state; clear downstream params
  (container change clears prefix/blob, etc.).

## Redis — `RedisPageContext.tsx`

- `?tab=` URL param (7 tabs in `mainTabs`).
- Persist last applied pattern per cache: `view-pref:redis-last-pattern:<cacheId>`; restore on
  cache switch (default `"*"`).
- Cache already persists via `profile.config.redisConfig.activeCacheId` — verify the select
  writes it (check `handleCacheChange` → `updateProfile`).

## SQL — `SqlPage.tsx`

- Verify `?connection=` is honored on load (palette items emit it); persist
  `view-pref:sql-last-connection` if a "last used" concept is missing.

## Monitoring — `MonitoringPage.tsx`

- `?tab=` param for rules|history (small).

## Not-configured CTAs — all pages

- Audit each page's "No X configured" empty state → add a link/button to the matching
  `/settings` tab using the `state: { tab }` deep-link convention (palette already uses it;
  `SettingsPage` must honor `state.tab`).

## Acceptance criteria

- Navigate away and back / reload on Storage: same account, container, prefix, blob, view.
- Deep links: `/storage?account=x&container=y`, `/redis?tab=slowlog`, `/sql?connection=z`.
- Back/forward navigates selections (params are history entries).
- Command palette resource items still drive selection.
