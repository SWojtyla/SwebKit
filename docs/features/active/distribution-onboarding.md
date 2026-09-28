# Distribution & Onboarding — team packs, demo tour, updater, deep links

State: Done — all four items shipped in commit 350adfff (team pack with
credential-stripped export + dry-run import, DemoAwareKeyVaultResolver +
PRD-day tour stop, notify-only update check, swebkit:// deep links with
single-instance forwarding). Signed auto-update stays deferred per
non-goals (private release assets need auth).

## Goal

Four items that make SwebKit shareable and self-updating: a secrets-free team pack
format, a "PRD day" demo tour stop, an update channel, and `swebkit://` deep links.

## Scope

### 1. Team workspace exports ("team pack")

- `TeamPack` DTO (`format:"swebkit-team-pack"`, `schemaVersion`, sections: `Maps`,
  `SavedSqlQueries`, `AlertRules`, `CollectionsData`, `EnvironmentsData`) +
  `TeamPackService` next to `ConfigurationBundleService`.
- Export: opt-in sections; strip demo ids; SQL queries get `connectionHint`
  (server+database — `ConnectionId` is machine-local); emit a `credentialRefs` manifest
  (key names only) for the "N secrets to re-link" report; flag `Plain`-sourced env
  variables (they can hold real secrets — warn in pack + export UI).
- Import: `POST /api/config/team-pack/import` + `?dryRun=true` preview; merge (default)
  or replace per section; natural keys (`Name`, `Name+Folder`); fresh ids for
  collections/envs (existing `AddImported*` precedent); rebind `SavedSqlQuery.
  ConnectionId` via hint else null; `AlertRuleRepository.UpsertAsync`; returns
  `{added,updated,skipped,conflicts[],requiredCredentialRefs[]}`; reuse
  `StripCredentialSecrets` + demo-id strip guards.
- UI: "Team sharing" in General Settings — export → blob download; import → picker →
  dry-run report dialog → `ConfirmBar` → invalidate profile/environments/collections/
  sql-queries/monitoring keys.

### 2. "PRD day" demo tour stop

- Existing: `demo-tour.ts` `DEMO_TOUR_STEPS` (10 stops) + `DemoTour.tsx` (route-driven,
  auto-enables demo) + `demo-scenarios.ts` canned transcripts.
- New steps: `/sql?connection=demo-sql-prd` targeting `sql-schema-hidden`; restricted
  Key Vault; an `prd-day` scenario transcript ending in an "Access gaps" report.
- **Gap found**: demo vault denials don't exist yet — need a `DemoAwareKeyVaultResolver`
  (selector pattern like `SqlResourceDiscoverySelector`) returning `RequestFailedException`
  403 for `*prod*`/`*restricted*` vaults + demo vault overlay in profile endpoints +
  demo-id strip guards (never persist). `MultiVaultKeyVaultSecretResolver` swallows
  errors to null — the demo resolver must surface denied status.
- `DemoTourStep` gains `scenarioId`/`connection` hints; update the stale skill doc
  (says 8 stops, actually 10).

### 3. Update channel

- **A — notify-only first**: `release.yml` publishes `latest.json`; frontend
  `useUpdateCheck` semver-compares vs `getVersion()` → toast + "Check for updates" in
  Settings. No signing/plugins needed.
- **B — full updater later**: `tauri-plugin-updater` + `createUpdaterArtifacts` +
  signing secrets in CI. ⚠ Private GitHub release assets need auth — updater endpoint
  needs a public manifest host or public releases.
- Version is triplicated (`tauri.conf.json`, `Cargo.toml`, `web/package.json`) — add a
  bump script.

### 4. `swebkit://` deep links

- `tauri-plugin-deep-link` + `tauri-plugin-single-instance` (with `deep-link` feature —
  **required** on Windows, else each link spawns a second process racing the sidecar).
- `tauri.conf.json` `plugins.deep-link.desktop.schemes: ["swebkit"]` — NSIS installer
  registers the protocol; `get_current()` for cold start, single-instance callback
  focuses + forwards via `emit`.
- `web/src/lib/deep-links.ts`: whitelist `swebkit://servicebus/queue?...` →
  `/service-bus?ns=&entity=&view=dlq` etc. onto the existing query-param conventions;
  `DeepLinkHandler` in AppLayout via `onOpenUrl`; resolve aliases lazily via
  `ensureQueryData(["profile"])`; unknown → notification + dashboard.
- Caveats: dev mode needs `register_all()` under `debug_assertions` (pollutes HKCU) or
  an installed build; `/settings` tab selection needs a `?tab=` param to be linkable;
  `profile=` in URLs is a no-op (single-profile app) — drop it.

## Non-goals

- Multi-profile support (implied by `profile=` deep links) — out of scope.
- Signed auto-update (phase B) until signing/release-visibility decided.

## Test plan

- xUnit `TeamPackServiceTests` (merge keys, id regen, hint rebind, credential manifest),
  endpoint dry-run + demo-mode ban tests.
- Playwright: team-pack round-trip in `settings.spec.ts`; tour steps in
  `dashboard.spec.ts`; deep-link mapping via the handler layer (OS dispatch untestable
  in e2e — dev checklist instead).
- vitest: `deep-links.ts` mapping, pack merge logic.

## Sequencing

1. PRD-day tour stop (smallest, highest demo value) → 2. Team pack → 3. notify-only
   update check → 4. deep links → 5. signed updater when decisions land.
