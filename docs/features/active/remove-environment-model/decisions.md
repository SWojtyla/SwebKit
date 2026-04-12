# Decisions - remove-environment-model

---

title: "Decisions - remove-environment-model"
owner: ""
status: "Accepted"

---

## Decision 001 - Preserve demo mode and remote platform environment concepts

**Status:** Accepted

**Date:** 2026-04-12

### Context

The abandoned model is the local SwebKit environment/profile system (`Environments`, `ActiveEnvironmentName`, shell environment labels). The codebase also uses the word “environment” for Azure DevOps stages and pipeline environment status, which are still valid external concepts.

### Decision

Remove only the local environment/profile model. Keep demo mode and keep remote Azure DevOps environment metadata untouched.

### Consequences

- Shell and settings code simplify materially.
- Release and pipeline experiences keep their current terminology and behavior.
- The implementation must be careful not to delete DTO fields or UI that represent Azure DevOps data.

### Alternatives considered

- Remove every `Environment*` symbol in the repo - rejected because it would break legitimate Azure DevOps features.

---

## Decision 002 - Migrate legacy profile files in place

**Status:** Accepted

**Date:** 2026-04-12

### Context

Existing local installs may still have `profiles.json` files containing `Environments` and `ActiveEnvironmentName`. Hard-failing or forcing manual repair would add support cost and create unnecessary user friction.

### Decision

`ProfileRepository.LoadAsync()` will accept the legacy shape, normalize it to a single `Config`, and save only the simplified schema on the next successful save.

### Consequences

- Existing users keep a non-fatal upgrade path.
- Unit tests need explicit migration coverage.
- The repository will carry a small amount of legacy-read logic until the migration is considered complete.

### Alternatives considered

- Breaking schema change with fail-fast loading - rejected because it would make the upgrade path brittle for little product value.

---

## Decision 003 - Remove environment from incident scope keys

**Status:** Accepted

**Date:** 2026-04-12

### Context

`IncidentWorkloadScope` currently includes `EnvironmentName`, and `ToScopeKey()` bakes it into request fingerprints. Once local environment selection is gone, that field becomes dead data that complicates query creation and test fixtures.

### Decision

Drop `EnvironmentName` from `IncidentWorkloadScope` and use `{ClusterContext}|{Namespace}|{WorkloadKind}|{WorkloadName}` as the scope key.

### Consequences

- Timeline request-key behavior becomes easier to reason about.
- All constructors, mappings, and tests that instantiate `IncidentWorkloadScope` must be updated together.

### Alternatives considered

- Keep the field but always pass `null` - rejected because it preserves dead contract surface and ongoing confusion.

---

## Decision 004 - Treat the shell cleanup as part of the feature, not a separate refactor

**Status:** Accepted

**Date:** 2026-04-12

### Context

Visible environment labels were already a source of confusion, and removing them is a safe first slice that reduces user-facing drift before the deeper model refactor.

### Decision

Count the `TopBar` and `IncidentTimelinePage` label removal as Wave 0 within this feature and track it in `status.md`.

### Consequences

- The feature has partial implementation progress before the backend refactor begins.
- Planning and status docs must reflect that the feature is already in progress rather than purely proposed.

### Alternatives considered

- Leave the preview cleanup undocumented until the full refactor lands - rejected because it would make the active feature state inaccurate.
