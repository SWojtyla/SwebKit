# Demo Mode Parity (recreated stub)

## Status

`Archived`

## Why this stub exists

Multiple active/archived feature docs (`docs/features/README.md`,
`docs/features/active/monitoring-rebuild/*`, `docs/features/active/post-migration-ux-review/*`,
and others) link to `demo-mode-parity/index.md` as the source of one specific decision. The
original document was lost at some point before this repository state; this stub recreates only
the load-bearing decision so those links resolve and the traceability contract in
`docs/features/README.md` holds, rather than reconstructing the full original feature history
(which isn't recoverable and isn't needed for that purpose).

## The decision this doc is cited for

**Observability (Application Insights logs/traces/metrics) and DevOps/Pipelines/Releases are
permanently dropped from the Tauri + React rewrite, effective 2026-07-26.** Not deferred, not
planned for a later pass. This was corroborated independently across several other docs
(`docs/features/README.md`, `docs/features/active/post-migration-ux-review/index.md`) at the time
of the production-readiness review in `docs/features/active/tauri-react-primary-tool/`, so the
decision itself is treated as solid even though its original primary source document is gone.

Incident Timeline (a related MAUI-only feature) was never ported and was never on the rewrite's
roadmap in the first place — not a "dropped" feature so much as one that was never started for
the new stack.

## See also

- `docs/features/README.md` — restates this decision in the feature catalog
- `docs/features/active/tauri-react-primary-tool/production-readiness-review.md` — documents the
  dangling-reference cleanup this stub is part of, and the fuller MAUI-vs-React parity analysis
