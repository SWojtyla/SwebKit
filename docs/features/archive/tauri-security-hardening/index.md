# Tauri Security Hardening (recreated stub)

## Status

`Done` (landed; original doc missing)

## Why this stub exists

Cited from `docs/features/active/monitoring-rebuild/*` and
`docs/features/active/post-migration-ux-review/*` as the source of the sidecar's CORS-hardening
pattern (an `IsAllowedOrigin` predicate scoped to Tauri/localhost origins, not a wildcard). The
original document is missing from this repository state. Recreated as a minimal stub so those links
resolve.

## What this doc covered, and its current state (verified against code)

Confirmed **landed** in `src-sidecar/Program.cs`: CORS is scoped via an `IsAllowedOrigin` check
that only trusts `tauri://localhost`, `http://tauri.localhost`, and `http://localhost|127.0.0.1:*`
origins, with an explanatory comment about the "localhost CORS drive-by" threat model this
mitigates. This was independently confirmed during the
`docs/features/active/tauri-react-primary-tool/production-readiness-review.md` sidecar audit
(§5, Security) as a fix that has already landed since the codebase-review-2026-07-18 docs flagged
`AllowAnyOrigin` as an open issue.

Any new sidecar route added going forward should rely on this existing `IsAllowedOrigin` predicate,
not reintroduce a wildcard or hardcoded origin list — this is the one actionable instruction from
this doc that still matters going forward, and is carried into
`docs/features/active/tauri-react-primary-tool/technical-plan.md` Module 3 as an implicit
convention to preserve.

## See also

- `src-sidecar/Program.cs`
- `docs/features/active/tauri-react-primary-tool/production-readiness-review.md` §5
