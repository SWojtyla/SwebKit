# Summary — Workspace Map Overhaul (archived 2026-09-18)

## Goal

Fix three problems with Settings → Map: the curated topology was never rendered
as a graph, the flat lists didn't scale, and the AI agent never saw the declared
relationships (they were only reachable through a scope-fenced tool).

## Delivered

- **Graph-first Map tab** — extracted shared `TopologyGraph.tsx` cytoscape
  component (reused by `AgentVisualizationPanel`); nodes grouped/colored by
  area, labeled edges, dashed suggestion edges, fcose layout, click-to-inspect.
- **List view toggle** — `ProfileListLayout`-based, carries the full edit
  surface (keyboard/screen-reader/Playwright-friendly path the canvas lacks).
- **Node inspector** — `WorkspaceMapInspector.tsx`: label editing, relationship
  table + add form, `ConfirmBar` removals, orphan hint, map summary when nothing
  selected.
- **Filtering** — text search + per-area chips applied to both graph and list.
- **Add-resource picker** — searchable, per-area groups, "Add all",
  custom-resource form, auto-opens on empty map.
- **Workspace map in the per-turn system prompt** — `BuildWorkspaceMapSection`
  in `AgentSystemPromptBuilder`, bounded (30 nodes/area, 40 edges, `+N more`
  overflow), omitted when empty. Every provider sees declared relationships on
  every turn; `investigate_workspace_issue` remains for *running* investigations.
- `cytoscape`/`cytoscape-fcose` declared as direct deps (were transitive via
  mermaid); theme-var → hex resolution in `web/src/lib/theme-colors.ts`.

## Key decisions

See `decisions.md` (archived alongside this summary) — notably D3: "always as
context" means the system prompt, not a tool the model must think to call; and
D6: the persisted topology model was deliberately untouched.

## Validation

- `npm run build` clean (cytoscape code-split, not in main bundle);
  `vitest run` 473 pass; eslint clean on new files.
- `dotnet build` sidecar + agents clean; `SwebKit.Sidecar.Tests` 456 pass
  (incl. 7 new `AgentSystemPromptBuilder` cases).
- Playwright `settings.spec.ts` 22 pass; 15 `workspace-map-utils` unit tests.

## Lessons learned

- Canvas visualizations need a full-fidelity accessible fallback, not a degraded
  one — the list view carries every edit action.
- Never import a dependency that's only hoisted transitively through a sibling
  package — declare it explicitly or it vanishes on the sibling's next update.

## Follow-up

- Live health/status badges on map nodes (needs per-area live data — noted as a
  future idea, connects to proactive-monitoring ambitions).
- Auto-inferred relationships beyond the existing heuristic suggestion scan.
- Persisted suggestion dismissal (currently session-only by design).
