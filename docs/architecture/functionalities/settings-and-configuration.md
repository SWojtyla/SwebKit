# Settings and Configuration

## Scope

The React Settings page at `/settings` edits the active SwebKit profile, local user preferences, workspace maps, and diagnostics behavior through the ASP.NET sidecar. It is split into these tabs:

- General
- Service Bus
- AKS
- Redis
- SQL
- Storage
- AI Agent
- Map
- Diagnostics
- Appearance

Command-palette navigation can open a specific tab through one-shot React Router `location.state`. When demo mode is active, connection tabs show that their real connection fields are inert until demo mode is disabled.

## Configuration Domains

### Profile data

`ProfileRepository` persists environment/workspace configuration exposed by `GET/PUT /api/config/profiles`, including:

- Service Bus namespaces;
- AKS kubeconfig/default context and namespace;
- Redis caches and active cache;
- SQL connections and active connection;
- Storage accounts;
- agent profiles and active profile;
- Azure Key Vault references;
- favorite resources; and
- named workspace maps and declared relationships.

Feature settings use `useProfile` plus updater-style `useUpdateProfile` mutations. `DraftInput` keeps typing local and commits on blur/unmount so ordinary field editing does not PUT the entire profile on every keystroke.

### User settings

`GET/PUT /api/config/user-settings` stores machine-local behavior such as:

- theme/appearance;
- API Client TLS verification, request tabs, and auto-save;
- startup connection warm-up;
- last-workspace restoration; and
- diagnostics/logging preferences.

These settings are separate from the profile because they describe this installation rather than a shared environment.

### API Client stores

The same config endpoint module also exposes dedicated environment and collection stores. They are managed by the API Client rather than the Settings tabs, but full export/import includes them.

### Workspace maps

The Map tab edits multiple named `AppConfig.Maps`. A map contains resources from AKS, Service Bus, Redis, SQL, and Storage plus user-declared relationships. Operators can:

- add discovered or manual resources;
- pin AKS context/namespace/deployment metadata;
- inspect and relabel nodes;
- add/remove relationships;
- accept or dismiss suggested relationships; and
- switch between list and shared `TopologyGraph` views.

Maps feed both the dashboard topology and the agent's bounded workspace-map prompt/tool context. Legacy single-topology profile data migrates during profile loading.

## Readiness and Connection Tests

The current React implementation distinguishes two concepts:

- **Configured** — required local fields/entities exist. `useSettingsReadiness` drives the status dots beside Service Bus, AKS, Redis, SQL, and Storage tabs and the General getting-started checklist.
- **Connected** — an explicit test endpoint succeeds. Each connection form owns a `Test connection` action and displays its sanitized result.

There is no live `ConfigurationProbeService` or global readiness dashboard in the current stack. Connection tests are user-triggered and route through feature endpoints/pools. This avoids background probes during settings rendering and keeps failures scoped to the relevant form.

SQL test/discovery actions use the currently typed ad-hoc values rather than a potentially stale saved client. Other connection edits invalidate the affected pooled clients when the profile is saved.

## Save and Pool Invalidation Flow

```text
Settings component
  → useUpdateProfile(updater)
  → PUT /api/config/profiles
  → ConfigEndpoints.SaveProfileAsync
      → strip demo overlay entities
      → ProfileRepository.ReplaceProfileData + SaveAsync
      → invalidate affected connection pools
      → React Query profile refresh
```

`SaveProfileAsync` invalidates all Storage and Service Bus clients, selectively evicts changed Redis/SQL entries, and evicts affected monitoring clients for AKS/Service Bus/Redis connection changes. Selection-only changes such as Redis `ActiveCacheId` do not drain healthy pools.

The GET endpoint clones profile data before applying demo overlays. The PUT endpoint removes known demo IDs before persistence so toggling demo mode cannot poison real configuration.

## Export and Import

General Settings can export or import the versioned configuration bundle through:

- `GET /api/config/export`
- `POST /api/config/import`

Import requires confirmation because it replaces profiles, API collections/environments, and user settings. The UI advises a restart after import so all consumers reload the replaced stores.

The bundle is intentionally broader than `AppConfig`; it represents persisted operator state managed by `ConfigurationBundleService`.

## Diagnostics and Appearance

- Diagnostics settings control structured file logging and expose Tauri-backed open-folder/export-log actions.
- Appearance settings apply the selected theme to the document and persist it through user settings.
- The Tauri shell owns window/tray lifecycle; the deleted MAUI shell is not involved.

## Main Code Locations

- `web/src/components/settings/SettingsPage.tsx` — tab shell, readiness dots, demo banner
- `web/src/components/settings/GeneralSettings.tsx` — checklist, API Client/startup preferences, Key Vaults, bundle transfer
- `web/src/components/settings/ServiceBusSettings.tsx`
- `web/src/components/settings/AksSettings.tsx`
- `web/src/components/settings/RedisSettings.tsx`
- `web/src/components/settings/SqlSettings.tsx`
- `web/src/components/settings/StorageSettings.tsx`
- `web/src/components/settings/AgentSettings.tsx`
- `web/src/components/settings/WorkspaceMapSettings.tsx`
- `web/src/components/settings/WorkspaceMapAddPicker.tsx`
- `web/src/components/settings/WorkspaceMapInspector.tsx`
- `web/src/components/settings/DiagnosticsSettings.tsx`
- `web/src/components/settings/AppearanceSettings.tsx`
- `web/src/components/settings/DraftInput.tsx`
- `web/src/lib/hooks/useProfile.ts` — profile/user settings/readiness/export-import hooks
- `src-sidecar/Endpoints/ConfigEndpoints.cs` — profile, user settings, collection, environment, bundle endpoints
- `src/SwebKit.Core/Configuration/ProfileRepository.cs`
- `src/SwebKit.Core/Configuration/UserSettingsRepository.cs`
- `src/SwebKit.Core/Services/ConfigurationBundleService.cs`
- `src/SwebKit.Core/Domain/WorkspaceModels.cs`
- `src-tauri/src/` — desktop shell, tray, and native diagnostics commands

## Security and Reliability Constraints

- Store secret values in Windows Credential Store or Azure Key Vault; profile JSON should contain opaque keys/references, not secrets.
- Never persist demo entities from GET overlays.
- Connection-test responses must be sanitized; detailed exceptions belong in sidecar logs.
- Profile updates must evict clients whose connection-affecting fields changed.
- Keep machine-local user preferences separate from environment/profile configuration.
- Bundle import is destructive replacement and must remain confirmation-gated.
- Workspace maps are bounded before insertion into agent prompts so large topologies cannot consume the context window.

## Validation Pointers

- `web/e2e/settings.spec.ts` — tabs, connection forms, maps, tests, appearance, confirmations
- `web/e2e/page-restore.spec.ts` — not-configured CTAs into Settings
- `web/e2e/workspace-resume.spec.ts` — startup preferences
- `web/src/components/settings/profile-list-utils.test.ts`
- `web/src/components/settings/workspace-map-utils.test.ts`
- `web/src/components/settings/agent-settings.test.ts`
- `tests/SwebKit.Sidecar.Tests/ConfigEndpointsTests.cs`
- `tests/SwebKit.Sidecar.Tests/ConfigCollectionsCredentialSecretTests.cs`
- `tests/SwebKit.Core.Tests/AppStateServiceProfileLoadTests.cs`
- `tests/SwebKit.Core.Tests/ConfigurationBundleServiceTests.cs`
- `tests/SwebKit.Core.Tests/UserSettingsRepositoryTests.cs`
- `tests/SwebKit.Core.Tests/WorkspaceProfileMigrationTests.cs`
