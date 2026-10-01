# Feature Catalog

This folder tracks in-flight implementation work — **one plan file per feature**, nothing more.

Plan files are the shared intent record: any tool or later session reads them to know what's being built and why. Session-native planning (plan mode, conversation) produces the content; this file persists it.

## Structure

- `docs/features/active/<feature>.md` — a single plan file per active feature.
- No `archive/`. When a feature ships: fold durable learnings into
  `docs/pitfalls/` or `docs/architecture/`, delete the plan file, and remove its
  line from this catalog. Git history preserves the full record.

- `codebase-quality-program.md` — phased deep-scan megaplan: hygiene, architecture, per-feature deep dives, Dashboard AI cockpit.
- `agent-mcp-evolution.md` — external MCP passthrough for ACP profiles, harness tool-quality pass (composite tools, memoization, projection caps), standalone MCP exposure of the SwebKit tools bridge (read-only default), and an MCP client adapter proxying external tools into non-ACP profiles with a confirm-before-execute pipeline.
- `access-denial-awareness.md` — phase 1: structured `access_denied` agent tool results with least-privilege remedies, SQL hidden-metadata detection via `sys.fn_my_permissions`, restricted demo connection.
- `access-awareness-pipeline.md` — phases 2–4: per-env access report, request artifacts (principal/scope/`az` line), declared-object SQL browsing, generic request webhook (Teams Power App hook point).
- `agent-colleague.md` — deep-linked evidence, structured access gaps, "watch this"→rule draft, entity-indexed screen state, feedback capture, change timeline, cross-env compare.
- `monitoring-closed-loop.md` — alert→autofix proposals (BackgroundProposalEligible gate), rule suggestions from surfaces, silence windows, persisted alert history + ops dashboard.
- `service-bus-power-ops.md` — reach-message DLQ-park/restore op, DLQ triage + resend-with-edit fix, session awareness, cross-env replay.
- `ux-power-pack.md` — palette deepening, pinned rail, env badgeing, keyboard grids, toast dedupe.
- `distribution-onboarding.md` — team workspace packs, "PRD day" demo tour stop, update channel, `swebkit://` deep links.
- `aks-multi-context.md` — phased multi-context AKS workspace: attached secondary contexts (URL/view-pref state), grouped namespace picker, merged resource tables with a Context column, context-routed mutations; primary context keeps profile semantics.
- `api-client-agent-fixes.md` — agent create-request resolves collection by id-or-name and auto-creates missing collection/folders, `list_api_collections` tool, non-AI cURL import dialog, chat markdown styling + drop stale context-window stats.
- `ai-reports-kanban.md` — AI report board (Queued/Ready/Done) plus per-rule investigation modes (Off/Auto/Manual): Manual prepares deterministic context without a model call, explicit Investigate spends tokens.

## Plan file contract

An active feature file is a single Markdown document containing:

- `State:` — exactly one of `Proposed`, `Planned`, `In Progress`, `Review`, `Done`
- Goal, scope, non-goals
- Implementation tasks (checklist)
- Test plan
- Validation results
- Decisions — only when non-obvious tradeoffs were made; omit the section otherwise

Keep it to one file and keep it honest — update `State` and the checklist as work
proceeds. If a section isn't needed, omit it rather than leaving a stub.
