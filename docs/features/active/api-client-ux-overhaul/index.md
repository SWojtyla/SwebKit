# API Client UX Overhaul — layout, colour system, syntax highlighting

## Summary

The React API Client works but reads as unfinished: opening it on a wide window gives a cramped
collections tree, a fixed-width request pane, and a response pane that absorbs every spare pixel;
response bodies have no syntax highlighting at all; request bodies have highlighting that is
invisible in the dark theme; and the whole page uses raw Tailwind palette colours instead of the
Aurora design tokens every other page uses.

This feature makes the API Client feel like the rest of SwebKit: a proportional, persisted,
keyboard-accessible 3-pane layout; one shared method-badge and status colour vocabulary built on
design tokens; and real, theme-aware syntax highlighting for both request and response bodies.

**Jira:** not linked

## Goal

Opening the API Client on any window size should feel deliberate: panes sized in proportion to what
they hold, colour used to convey method and status rather than decorate, and payloads readable at a
glance in all three themes.

## Scope

### Layout
- Fractional widths for the request/response split so extra window width is shared, not dumped on
  the response pane.
- Panel widths persisted per-user and restored on mount.
- Keyboard-accessible resizers (`role="separator"`, arrow keys, double-click reset) and no text
  selection during drag.
- Request-header hierarchy fix: the request name becomes a title-styled inline-editable heading
  instead of a bare full-width input on its own row.

### Colour & visual hierarchy
- One shared `methodBadge` module replacing the three duplicated `methodColors` maps.
- HTTP method and response-status colours sourced from Aurora tokens (`--info`, `--success`,
  `--warning`, `--destructive`, `--muted-foreground`) so they adapt across `light` / `dark` /
  `fancy`.
- Method labels rendered as proper badges with correct short forms, replacing
  `method.toUpperCase().slice(0, 4)` (today's `DELE`, `PATC`, `OPTI`, `GRAP`, `WEBS`).
- Tab strips gain count badges consistently (Headers currently has none while History does).

### Syntax highlighting
- Response body rendered through a read-only CodeMirror view: JSON/XML highlighting, line numbers,
  code folding, in-body search, wrap toggle, and virtualized rendering for large payloads.
- A single shared, theme-aware highlight style used by **both** the response viewer and the existing
  request body editor, replacing CodeMirror's light-only `defaultHighlightStyle`.
- Content-type-driven language selection with a graceful plain-text fallback.

### Response pane completeness
- `Save Example` persists into the already-existing `HttpRequestEntry.responseExamples` field and
  saved examples become clickable.
- Response history survives remount by moving out of `ResponseViewer` local state.
- Human-readable response sizes instead of raw byte counts and a bare `size unknown`.

## Non-Goals

- **Git panel work** — tracked separately in [api-client-git-completion](../api-client-git-completion/index.md).
- **Linked `.swebkit-api` repositories** — the concept exists only in the legacy Blazor app and is
  out of scope here (see the git feature's non-goals for the same statement).
- **Capture Rules wiring** — a distinct correctness bug, still open as finding #7 in
  [post-migration-ux-review](../post-migration-ux-review/status.md).
- **Variable generator editor** — finding #8, unchanged by this work.
- **Command-palette integration for requests** — finding #9, depends on a shared palette registry.
- No new sidecar or Tauri endpoints. This feature is frontend-only.
- Not a rewrite: `ApiClientPage` keeps its current state ownership and tab model.

## Relationship to existing plans

This feature **promotes** three findings out of
[post-migration-ux-review](../post-migration-ux-review/status.md) into a real implementation plan, as
that document's own blocker section requires:

| Finding | Status after this feature |
|---|---|
| #6 — body editor regressed, no highlighting | Closed. CodeMirror already landed for the request body; this fixes the dark-theme invisibility and extends highlighting to the response. |
| #11 — no conflict-resolution UI for externally edited collections | Already closed independently — the conflict banner exists at [ApiClientPage.tsx:706](../../../../web/src/components/api-client/ApiClientPage.tsx). Recorded here so it stops being re-raised. |
| #26 — no resizable detail panels, fixed widths everywhere | Partially addressed. `ResizablePanels` gains persistence, keyboard support and double-click reset; other pages adopting it is follow-up. |

Also related: [react-polish-aug-01](../react-polish-aug-01/index.md) covers the same class of
post-migration polish for other feature areas and shares the notification/feedback conventions.

## Dependencies

- No blocking dependencies. Frontend-only, no sidecar or Rust changes.
- `@codemirror/*` packages are already dependencies ([web/package.json](../../../../web/package.json)) —
  no new runtime dependency is required for the response viewer.
- Coordinate with [api-client-git-completion](../api-client-git-completion/index.md) only on
  `ApiClientPage.tsx`, which both features touch (this one for the panel layout, that one for the
  git drawer). Land this one first to avoid a merge conflict in the same render tree.

## Risks

| Risk | Mitigation |
|---|---|
| A read-only CodeMirror view for the response is heavier than a `<pre>` and could slow first paint on small responses | Mount CodeMirror lazily only for the Body tab, and keep a plain `<pre>` path for bodies under a size threshold; measure before committing to a single path |
| Very large bodies (the sidecar caps at 4 MB — `HttpRequestResult.ResponseBodyMaxBytes`) still cost a full parse for highlighting | Disable language parsing above a threshold and fall back to plain-text CodeMirror, which still virtualizes; surface the degradation in the UI rather than silently jank |
| Switching panel widths from pixels to fractions changes the default look for existing users | Persisted widths are keyed and versioned; a missing/incompatible key falls back to the new defaults rather than the old pixel values |
| Three themes (`light`, `dark`, `fancy`) must all produce readable highlighting | Drive the CodeMirror theme from CSS custom properties defined per theme class, so a single theme extension serves all three and any future theme (see [decisions.md](decisions.md) DEC-1) |
| `data-testid` churn breaks the 18 existing API Client e2e tests | Preserve every current `data-testid`, including `response-body` and `request-body-editor`, and add new ones rather than renaming |

## Related docs

- [technical-plan.md](technical-plan.md) — implementation modules, symbols, file paths
- [decisions.md](decisions.md) — design decisions taken up front
- [test-plan.md](test-plan.md) — scenarios and coverage approach
- [status.md](status.md) — progress checklist and Definition of Done
- [docs/architecture/functionalities/api-client.md](../../../architecture/functionalities/api-client.md) —
  canonical feature doc (currently describes the Blazor implementation; see technical plan Module 6)
