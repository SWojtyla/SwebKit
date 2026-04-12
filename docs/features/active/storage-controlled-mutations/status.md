# Status - storage-controlled-mutations

---

title: "Status - storage-controlled-mutations"
owner: "GitHub Copilot"
state: "Planned"
jira: "not linked"
branch: ""
started: "2026-04-12"
last_updated: "2026-04-12"

---

## Quick summary

Planning is complete for a three-wave Storage expansion that keeps read-only inspection as the default and adds guarded mutations only behind explicit per-account enablement. The next useful implementation step is Wave 1: config and client support for opt-in upload and same-account copy.

Jira: not linked

Current focus: establish the mutation safety model first so every later upload, metadata, diff, or recovery action reuses the same explicit production controls.

## Progress checklist

### Planning

- [x] Narrowed the feature to single-blob, operator-initiated mutations with explicit safety controls.
- [x] Chosen read-only-by-default behavior with per-account mutation enablement.
- [x] Captured likely source, UI, and test touchpoints.

### Wave 1 - Mutation safety plus upload and copy

- [ ] Extend storage configuration with an additive mutation opt-in field.
- [ ] Add upload and same-account copy client contracts plus demo support.
- [ ] Add focused mutation dialogs and progress or confirmation flows in the Storage page.

### Wave 2 - Metadata update and version diff

- [ ] Add metadata patch support with before-versus-after preview.
- [ ] Extend version-aware property and content loading for compare views.
- [ ] Add bounded text diff and metadata-only fallback for large or binary blobs.

### Wave 3 - Recovery

- [ ] Add version restore and soft-delete recovery support with capability detection.
- [ ] Surface recovery actions in the versions or detail experience with explicit result summary.
- [ ] Run focused App, Azure, and Core test passes and update Storage functionality docs.

## Completed

- Framed the feature around guarded single-blob mutations rather than general write access.
- Chosen opt-in mutation enablement at the storage-account level so current behavior remains unchanged for existing environments.
- Defined confirmation, overwrite, and recovery safety expectations before implementation starts.

## Remaining

- Implement the mutation policy and Wave 1 upload/copy flow.
- Implement Wave 2 metadata and diff support.
- Implement Wave 3 recovery behavior and capability handling.
- Update related docs when code lands.

## Blockers

- Jira ticket is not linked (informational).
- Recovery value depends on account capabilities such as blob versioning or soft delete; the plan assumes those capabilities will be detected and surfaced rather than required everywhere.

## Validation

- Test Plan: `test-plan.md`
- Validation status: Not started

## Notes

- Bulk or wildcard mutations stay out of scope for this feature.
- Typed confirmation is required anywhere the action can overwrite or recover content in a production environment.
- If `AllowMutations` is false, the page should remain visually and behaviorally read-only.
