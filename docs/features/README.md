# Feature Catalog

This folder tracks in-flight implementation work — **one plan file per feature**, nothing more.

Plan files are the shared intent record: any tool or later session reads them to know what's being built and why. Session-native planning (plan mode, conversation) produces the content; this file persists it.

## Structure

- `docs/features/active/<feature>.md` — a single plan file per active feature.
- No `archive/`. When a feature ships: fold durable learnings into
  `docs/pitfalls/` or `docs/architecture/`, delete the plan file, and remove its
  line from this catalog. Git history preserves the full record.

- `codebase-quality-program.md` — phased deep-scan megaplan: hygiene, architecture, per-feature deep dives, Dashboard AI cockpit.
- `agent-mcp-evolution.md` — external MCP passthrough for ACP profiles, harness tool-quality pass, standalone MCP exposure of the SwebKit tools bridge.

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
