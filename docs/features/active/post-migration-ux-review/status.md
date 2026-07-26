# Status — Post-Migration UX & Feature-Parity Review

## Current State

`Planned`

## Quick Summary

Fresh MAUI-vs-React comparison across API Client, Redis, Storage, Dashboard, Shell/Layout, Settings,
and Monitoring, plus a UX-quality (not bug-parity) pass over AKS/Service Bus — scoped to avoid
duplicating the existing `aks-migration-fixes`, `service-bus-migration-fixes`, `demo-mode-parity`,
and `dashboard-redesign` plans. Found 5 "critical" findings where UI presents as functional but is
wired to nothing or silently wrong (Monitoring, Redis Pub/Sub, Redis export, Storage
upload/copy/metadata/versions, plaintext auth secrets), plus 25 smaller feature/UX gaps. See
[technical-plan.md](technical-plan.md) for the phased Verify → Fix sequence and
[test-plan.md](test-plan.md) for how each phase gets checked.

**Every item below must be re-verified against the running app before being fixed** — the findings
came from a static code-reading pass, not from exercising the app, and the codebase is actively
changing under another in-progress session.

**Jira:** not linked

## Progress Checklist

### Phase 1 — Critical (do first)
- [ ] 1.1 Auth secrets: verified in plaintext, then moved to a real OS-backed secret store
- [ ] 1.2 Monitoring: scope decision made (rebuild vs. drop) and recorded here
- [ ] 1.3 Redis Pub/Sub: verified broken, then hidden or wired to real endpoints
- [ ] 1.4 Redis export: verified wrong, then fixed to fetch real per-key data server-side
- [ ] 1.5 Storage mutations: verified all dead (upload/copy/metadata/versions/undelete), then
      routes added or buttons disabled (not silently no-op); Metadata Save fixed first

### Phase 2 — Quick wins
- [ ] 2.1 `DevOpsSettings` dead tab removed
- [ ] 2.2 Appearance Font Size/Density wired or removed
- [ ] 2.3 API Client Capture Rules tab wired to `request.captureRules`
- [ ] 2.4 Destructive-action mutations get `onError` toasts (AKS first, then SB/Redis/Storage)

### Phase 3 — Feature-level gaps
- [ ] API Client: body editor (CodeMirror6), variable generator, command-palette integration,
      Git multi-repo picker, conflict-resolution toast
- [ ] Redis: hash/list/set/zset mutation routes, keyspace-health analyzer port, prefix-memory
      bytes, namespace tree recursion/virtualization
- [ ] Storage: `allowMutations` enforcement, real file upload, version-diff pane, copy-dialog
      container picker
- [ ] Dashboard: "Pin to dashboard" affordance, Settings getting-started checklist (check
      `dashboard-redesign` status first)
- [ ] Shell: live per-area health strip, command-palette fuzzy search + MRU
- [ ] AKS/SB: shared `<ResizablePanel>`, Pods grid resource-usage column, URL-driven drill-down
      state, Service Bus bulk DLQ actions

## Validation

Not started. See [test-plan.md](test-plan.md) — manual verification per phase, automated coverage
added alongside any new sidecar endpoint.

## Blockers

None to start Phase 1. Any AKS/SB item in Phase 3 should check current state of
`aks-migration-fixes`/`service-bus-migration-fixes` first in case the other in-progress session has
since expanded their scope. Dashboard items should check `dashboard-redesign`'s status first.

## Notes

- Produced 2026-07-26 via direct comparison against the deleted MAUI source at git commit `85d24ed`
  (`src/SwebKit.App/Components/*`, last commit before the Tauri+React scaffold in `ec175a7`).
- Deliberately excludes Observability, DevOps/Pipelines/Releases (permanently dropped,
  2026-07-26) and IncidentTimeline (never ported, not requested).
