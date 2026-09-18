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

## Legacy projects — do not touch by default

The primary stack is **Tauri (Rust) + React (`web/`) + .NET sidecar (`src-sidecar/`)**. These projects are legacy, kept in-repo as reference/backup only:

- `src/SwebKit.App/` — .NET MAUI Blazor Hybrid shell (previous primary app)
- `src/SwebKit.WinUI/` — abandoned WinUI experiment
- `src/SwebKit.Agent.PocConsole/` — early agent proof-of-concept console
- `tests/SwebKit.App.Tests/` — bUnit tests for the legacy shell

Do not search, read, or modify them unless the task explicitly concerns the legacy stack. Shared libraries (`SwebKit.Core`, `.Azure`, `.Kubernetes`, `.Redis`, `.Sql`, `.Agents`, etc.) are still live — the sidecar consumes them.

## Delivery paths

- **Jira-driven (autonomous):** `swebify` — ticket key → full feature end-to-end
- **General (manual control):** `swebiplan` → implement via orchestrator → `pre-ship-review` → `azure-devops` → `swebifix` → `feature-archive`

## Status values

Use exactly one of: `Proposed`, `Planned`, `In Progress`, `Review`, `Done`, `Archived`
