# Claude Code Instructions

The canonical documentation entry point is `docs/README.md`. Read it first for the repository structure, feature model, and reading order — everything below is Claude-specific context.

## Read first

1. `docs/README.md` — canonical docs entry point (structure, canonical sources, traceability rules)
2. `docs/features/README.md` — feature catalog and order
3. Relevant files in `docs/pitfalls/` — check before making non-trivial changes
4. `docs/security/aikido-mcp-scan.md` — run Aikido security scans on new/modified code

## Where things live

| What                 | Where                                   |
| -------------------- | --------------------------------------- |
| Docs entry point     | `docs/README.md`                        |
| Feature catalog      | `docs/features/README.md`               |
| Active feature plans | `docs/features/active/<feature-name>/`  |
| Archived features    | `docs/features/archive/<feature-name>/` |
| Architecture         | `docs/architecture/`                    |
| Pitfalls             | `docs/pitfalls/`                        |
| Security scanning    | `docs/security/aikido-mcp-scan.md`      |

**Never write plans, feature docs, or decisions outside the repository.** Everything belongs under `docs/`.

## Delivery paths

- **Jira-driven (autonomous):** `swebify` — ticket key → full feature end-to-end
- **General (manual control):** `swebiplan` → implement via orchestrator → `pre-ship-review` → `azure-devops` → `swebifix` → `feature-archive`

## Status values

Use exactly one of: `Proposed`, `Planned`, `In Progress`, `Review`, `Done`, `Archived`
