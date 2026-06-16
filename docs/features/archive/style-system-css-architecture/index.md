# Feature Overview - style-system-css-architecture

---

title: "Feature Overview - style-system-css-architecture"
owner: ""
status: "Review"
jira: ""
created: "2026-06-14"
updated: "2026-06-14"

---

## Goal

Make the global stylesheet architecture healthy by turning `app.css` into a small ordered entry point and moving the existing global rules into named layer files with clear ownership.

## Value

The style-system and polish work made shared controls more consistent, but `src/SwebKit.App/wwwroot/app.css` was still a 5,600+ line mixed-responsibility file. That made review, ownership, and future cleanup too risky. This feature keeps runtime loading stable while making the CSS architecture navigable and enforceable.

## Scope

### In scope

- Keep `src/SwebKit.App/wwwroot/app.css` as the stylesheet linked by `wwwroot/index.html`.
- Split the current global CSS into ordered files under `src/SwebKit.App/wwwroot/styles/`.
- Preserve selector order and current visual behavior.
- Update `scripts/style-inventory.ps1` to report entry-point and layer-file metrics.
- Update architecture guidance with CSS layer ownership.

### Out of scope

- No visual redesign.
- No selector migration from global to `.razor.css` in this split.
- No compatibility alias removal.
- No theme removal.

## Layer Ownership

| File | Responsibility |
| --- | --- |
| `app.css` | Entry point only. Imports layers in order. |
| `styles/00-tokens-themes.css` | Tokens, themes, compatibility aliases. |
| `styles/01-base.css` | Document and Blazor host base styles. |
| `styles/02-shell-navigation.css` | App shell, top bar, status bar, nav, tabs. |
| `styles/03-workspaces.css` | Command/dialog/workspace globals, Service Bus globals, AKS shared globals. |
| `styles/04-page-surfaces.css` | Page headers, settings, storage, page-level shared surfaces. |
| `styles/05-primitives-utilities.css` | Shared primitives, context menus, form helpers, text utilities, empty states, micro-interactions. |
| `styles/06-observability.css` | Observability global styles pending later local migration. |
| `styles/07-pipelines-legacy.css` | Pipelines/Releases helpers, skeletons, validation, legacy shared helpers. |

## Dependencies

- `docs/features/active/style-system-harmonization/`
- `docs/features/active/style-system-polish-9/`
- `docs/architecture/codebase-guide.md`
- `scripts/style-inventory.ps1`

## Risks & Mitigations

- Risk: CSS import order changes visuals. Mitigation: split by original line-order ranges and keep imports ordered.
- Risk: imported files are not included in MAUI static assets. Mitigation: keep files under `wwwroot/styles/` and validate app build.
- Risk: future contributors add feature CSS to the wrong layer. Mitigation: layer ownership documented in `codebase-guide.md`.

## Quick Links

- Status: `status.md`
- Test plan: `test-plan.md`
- Decisions: `decisions.md`