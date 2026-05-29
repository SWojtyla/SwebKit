# Redis UX Rework

## Goal

Rework the Redis key browser so operators can browse keys, select multiple keys, inspect long values, and access secondary insights without fighting the layout.

## Scope

- Make multi-key selection visible and direct in the key tree.
- Keep key opening separate from key selection.
- Let long values be fully inspected and copied.
- Move health, memory, and ops insights out of the default detail path.

## Quick Links

- Jira: not linked for this ad-hoc feature request.
- Architecture: `docs/architecture/functionalities/redis.md`
- Entry point: `src/SwebKit.App/Components/Pages/RedisPage.razor`
