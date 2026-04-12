# Backend Plan - operator-navigation-and-workspaces

---

title: "Backend Plan - operator-navigation-and-workspaces"
owner: "GitHub Copilot"
status: "Planned"

---

## Goal

Define the non-visual contracts and persistence model that make shell-level search, favorites, recents, and saved workspaces durable, versionable, and safe to restore across the major operator pages.

## Impacted areas

- Current persistence and domain models:
- `src/SwebKit.Core/Configuration/UiStateRepository.cs`
- `src/SwebKit.Core/Configuration/ProfileRepository.cs`
- `src/SwebKit.Core/Domain/AppConfig.cs`
- `src/SwebKit.Core/Domain/FavoriteEntity.cs`
- Current app-layer orchestration seams to extend rather than replace:
- `src/SwebKit.App/Services/CommandRegistry.cs`
- `src/SwebKit.App/Services/SelectionContext.cs`
- `src/SwebKit.App/Services/TabService.cs`
- `src/SwebKit.App/Services/PageDataCache.cs`
- Likely new or expanded model/service areas:
- `src/SwebKit.Core/Domain/` for resource references, favorite resources, and workspace models
- `src/SwebKit.Core/Services/` for snapshot normalization or restore helpers that are framework-agnostic
- `src/SwebKit.App/Services/` for shell-level provider registries and restore orchestration
- Tests:
- `tests/SwebKit.Core.Tests/`
- `tests/SwebKit.App.Tests/CommandRegistryTests.cs`
- `tests/SwebKit.App.Tests/PageDataCacheTests.cs`

## Design

The feature should use a split architecture:

1. Persistent, semantic models live in `SwebKit.Core` so favorites and saved workspaces can be serialized, versioned, and tested without UI dependencies.
2. Shell-specific provider registries live in `SwebKit.App/Services` because page contributors and search providers are UI-shell extension points, not external platform contracts.
3. Pages contribute resource identity and workspace snapshot data through a narrow provider/contributor interface rather than by exposing raw component objects.
4. Restore is route-first: navigate to the target page, then hand the semantic snapshot to the page contributor so the page can rehydrate safely.

This design keeps Core responsible for durable data shape while letting the app layer own shell orchestration.

## API / Contracts

- Persistent models to introduce or evolve:
- Canonical resource reference model with stable area, resource kind, display label, and route token.
- Expanded favorite resource model, potentially evolving the current `FavoriteEntity` into a richer route-aware favorite contract.
- Workspace snapshot model with name, environment scope, landing route, resource references, page-contributor payloads, and schema version.
- App-layer service contracts likely required:
- Resource-search provider registry for command-palette resource results.
- Workspace-contributor contract for save/restore participation per page.
- Shell-level workspace service for create/list/update/delete/restore flows.
- Backward compatibility notes:
- Existing `RecentCommandIds` should remain valid.
- Existing `FavoriteEntities` data should migrate forward or adapt without manual user cleanup.
- Existing `OpenTabs` persistence remains useful and should complement named workspaces rather than be discarded.

## Tasks

### Wave 1 - Canonical models and storage split [dotnet-expert] (sequential root)

- [ ] Define the canonical resource reference and workspace snapshot models in `SwebKit.Core`.
- [ ] Decide and implement the storage split between `profiles.json` and `ui-state.json`.
- [ ] Keep existing favorites and recent-command state backward compatible.
- [ ] Introduce any required migration helpers or versioning metadata.

### Wave 2 - Shell provider registries [dotnet-expert] (depends on Wave 1)

- [ ] Add a provider model for resource search results so the command palette is no longer hard-coded per resource type.
- [ ] Add a contributor model for pages that can publish favorite/resource/workspace context.
- [ ] Ensure provider registration is additive and page participation is optional.
- [ ] Keep the design small enough that pages can adopt it incrementally.

### Wave 3 - Workspace persistence and restore orchestration [dotnet-expert] (depends on Waves 1-2)

- [ ] Implement create/list/update/delete storage for named workspaces.
- [ ] Implement route-first restore semantics with cancellation-aware contributor callbacks.
- [ ] Define behavior for stale or partially restorable snapshots.
- [ ] Keep `TabService` as transient open-tab persistence and layer named workspaces above it rather than replacing it.

### Wave 4 - Tests and hardening [dotnet-expert] (depends on Waves 1-3)

- [ ] Add unit coverage for model normalization, migration, and restore payload versioning.
- [ ] Add app-layer tests for provider registration, recent/favorite updates, and workspace restore orchestration.
- [ ] Add regression coverage that proves stale or unsupported workspace data degrades safely.
- [ ] Record the final persistence and restore tradeoffs in `decisions.md`.

## Migration and runtime changes

- Existing `FavoriteEntity` persistence may need a compatibility layer or migration into a richer route-aware favorite model.
- Recent resources should remain lightweight and local; named workspaces should remain durable and environment-scoped.
- No infrastructure changes are required.

## Validation

- Unit tests: Not started.
- Integration tests: n/a - most of the behavior is app-shell orchestration and persistence rather than external-service integration.
- Manual checks:
- Verify old favorites remain visible after migration.
- Verify named workspaces survive restart and restore predictably.
- Verify partial restore is explicit rather than silent.

## Notes

- Relevant pitfalls from `docs/pitfalls/dotnet-csharp.md`:
- CS-1 - do not trust `required` properties alone at the serialization boundary; workspace payloads need explicit normalization/defaulting.
- CS-2 - cancellation must propagate through restore workflows.
- The feature should not serialize raw component state or `object` payloads from the current `TabService` as the durable workspace format.
