# SwebKit Documentation Entry Point

This is the canonical starting point for SwebKit documentation.

## Recommended Reading Order

1. `docs/README.md` (this file)
2. `docs/context.md` — global orientation: stack, directory map, conventions, commands
3. `docs/features/README.md` — active feature catalog
4. `docs/architecture/index.md` — routes you to the deep dives relevant to the task
5. `docs/pitfalls/` — read the file for the stack you're touching before non-trivial changes

## Documentation Model

Feature docs are **working memory**: one `docs/features/active/<feature>.md` per
in-flight feature, deleted at close-out after durable learnings are folded into
`docs/pitfalls/` or `docs/architecture/`. Long-lived knowledge lives in
`docs/context.md`, `docs/architecture/`, and `docs/pitfalls/` — not in
per-feature folders or archives.

## Canonical Sources

- Global context: `docs/context.md`
- Feature catalog: `docs/features/README.md`
- Architecture router: `docs/architecture/index.md`
- Pitfalls index: `docs/pitfalls/index.md`
- History: `docs/MIGRATION-NOTES.md`
