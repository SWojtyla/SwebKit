# Status — Post-Migration UX & Feature-Parity Review

## Current State

`In Review (triaged)` — review produced 30 findings on 2026-07-26. Each finding must be
re-verified against the running app before being fixed (per the standing rule below). This pass
**triages** the findings: records what has since been resolved by other active plans and groups
the remainder into follow-up work.

**Jira:** not linked

## Standing rule

> Every item must be re-verified against the running app before being fixed. The findings came
> from a static code-reading pass, not from exercising the app, and the codebase is actively
> changing.

## Triage outcome (2026-07-27)

### Resolved / closed by another plan
- **#1 Monitoring** — split out and **rebuilt** as `../monitoring-rebuild/` (persisted rules,
  sidecar evaluation engine, live React UI + SSE). No longer a mockup. Finding closed.
- **#1.2 Monitoring scope decision** — rebuild chosen (user decision 2026-07-27). Closed.

### Remaining critical findings (Phase 1) — still open, need app verification + fix
- **#1.1 Auth secrets persisted in plaintext** — highest priority; needs a real OS-backed secret
  store (see `agent-multimodel-api-client` for the credential-store direction). **Not yet done.**
- **#1.3 Redis Pub/Sub** — broken endpoints; hide panel or wire real SSE. Open.
- **#1.4 Redis export** — exports wrong data; needs server-side per-key fetch. Open.
- **#1.5 Storage mutations** — upload/copy/metadata/versions/undelete routes missing; Metadata
  Save is a no-op. Open (large — candidate for a dedicated `storage-mutation-endpoints` plan).

### Quick wins (Phase 2) — open
- **#2.1 `DevOpsSettings` dead tab** — delete per `demo-mode-parity` cleanup decision.
- **#2.2 Appearance Font Size/Density** — wire or remove.
- **#2.3 API Client Capture Rules** — wire to `request.captureRules`.
- **#2.4 Destructive-action `onError` toasts** — AKS first, then SB/Redis/Storage.

### Feature-level gaps (Phase 3) — open, tracked as future work
- API Client: body editor (CodeMirror6), variable generator, command-palette integration,
  Git multi-repo picker, conflict-resolution toast.
- Redis: hash/list/set/zset mutation routes, keyspace-health analyzer, prefix-memory bytes,
  namespace tree recursion/virtualization.
- Storage: `allowMutations` enforcement, real file upload, version-diff pane, copy-dialog
  container picker.
- Dashboard: "Pin to dashboard", Settings getting-started checklist.
- Shell: live per-area health strip, command-palette fuzzy + MRU.
- AKS/SB: shared `<ResizablePanel>`, Pods grid resource-usage column, URL-driven drill-down,
  Service Bus bulk DLQ actions.

## Blocker
- This is a **review/triage**, not an implementation plan. The open items should be promoted into
  their own `docs/features/active/` folders (matching the established convention) before work
  starts — do not fold them into this doc.
