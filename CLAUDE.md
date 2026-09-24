# Claude Code Instructions

The canonical documentation entry point is `docs/README.md`. Read it first for the repository structure, feature model, and reading order — everything below is Claude-specific context.

## Read first

1. `docs/README.md` — canonical docs entry point (structure, canonical sources)
2. `docs/context.md` — global orientation (stack, directory map, conventions, commands)
3. Relevant files in `docs/pitfalls/` — check before making non-trivial changes
4. `docs/security/aikido-mcp-scan.md` — run Aikido security scans on new/modified code

## Where things live

| What                 | Where                                   |
| -------------------- | --------------------------------------- |
| Global context       | `docs/context.md`                       |
| Docs entry point     | `docs/README.md`                        |
| Feature catalog      | `docs/features/README.md`               |
| Active feature plans | `docs/features/active/<feature>.md`     |
| Architecture         | `docs/architecture/`                    |
| Pitfalls             | `docs/pitfalls/`                        |
| Security scanning    | `docs/security/aikido-mcp-scan.md`      |

**Never write plans, feature docs, or decisions outside the repository.** Everything belongs under `docs/`.

## Stack

The primary stack is **Tauri (Rust) + React (`web/`) + .NET sidecar (`src-sidecar/`)**. The legacy .NET MAUI/Blazor app was removed (see `docs/MIGRATION-NOTES.md`); shared libraries (`SwebKit.Core`, `.Azure`, `.Kubernetes`, `.Redis`, `.Sql`, `.Agents`, etc.) are live — the sidecar consumes them.

## Delivery paths

- **Autonomous:** `swebify` — freeform description → full feature end-to-end (plan, implement, validate, ship)
- **Manual control:** `swebiplan` → implement → `pre-ship-review` → `azure-devops` → `swebifix` → `feature-archive` (close-out)

## Status values

Use exactly one of: `Proposed`, `Planned`, `In Progress`, `Review`, `Done`
