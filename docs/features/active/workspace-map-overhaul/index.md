# Workspace Map Overhaul

## Status

`Review` — see `status.md`.

## Goal

Three user-facing problems, one feature:

1. **The Map tab doesn't explain itself.** Settings → Map is a two-column form
   (`WorkspaceMapSettings.tsx`): flat per-area lists of nodes/candidates on the left,
   a relationships table on the right. The user curates a graph but never *sees* one —
   the actual shape of their workspace (what connects to what, what's orphaned) is
   invisible. The Map becomes a graph-first view: an interactive canvas where nodes are
   colored/grouped by area and declared relationships render as labeled edges.

2. **It doesn't scale.** With lots of resources the flat lists and the relationships
   table become unusable — no search, no filtering, no way to focus on one area or one
   neighborhood of the graph. The redesign adds text filtering, per-area filtering,
   and selection-driven inspection instead of everything-on-screen-at-once editing.

3. **The AI never sees the map.** `AgentSystemPromptBuilder.Build` injects only a
   coarse workspace summary (cluster context, namespace aliases, counts). The topology
   is reachable solely through the `investigate_workspace_issue` tool, which is fenced
   behind `scope: "workspace"` on contextual panels — so in practice the agent works
   without the declared relationships on almost every turn. The map gets injected into
   the per-turn system prompt as a bounded `## Workspace map` section, so every
   provider (OpenAI-compatible and ACP alike — the latter receives the prompt via
   prompt-stuffing in `AcpAgentModelClient`) always has it.

## Scope

### Frontend (`web/src/components/settings/`)

- `WorkspaceMapSettings.tsx` — full restructure:
  - **Graph view** (default): cytoscape canvas, nodes colored per `WorkspaceResourceArea`,
    edges labeled with the relationship label, unconfirmed suggestions drawn as dashed
    edges. Click a node → inspector.
  - **List view** (toggle): reuse `ProfileListLayout` — nodes grouped by area,
    filterable, selection opens the same inspector. Keeps a fully
    keyboard/screen-reader/Playwright-friendly path (canvas alone isn't accessible).
  - **Node inspector** (right panel): area, resource key, editable display label,
    relationship list with remove (ConfirmBar), add-relationship form
    (target picker + label), remove node, orphan hint when unconnected.
  - **Add-resource picker**: candidates grouped by area, searchable, replaces the
    inline per-area candidate lists. Bulk "add all" per area.
  - **Filter controls**: text search (label + resource key) and per-area chips; filter
    applies to both graph and list.
  - Suggestions keep confirm/dismiss semantics (session-only dismissal — unchanged).
- Shared graph component extracted so `AgentVisualizationPanel`'s `TopologyGraph`
  and the Map don't fork two cytoscape wrappers.
- `web/package.json` — declare `cytoscape` (+ `cytoscape-fcose` if used) explicitly;
  today it's only hoisted transitively via mermaid.

### Sidecar (`src-sidecar/`)

- `Services/AgentSystemPromptBuilder.cs` — new `BuildWorkspaceMapSection(config)`:
  nodes grouped by area, edges as `From → To (label)`. Bounded: caps on node/edge
  counts with "+N more" overflow so a large map can't blow the context window.
  Section omitted entirely when the topology is empty.
- `src/SwebKit.Agents/Tools/InvestigateWorkspaceIssueTool.cs` — description tweak so
  the model knows the map is already in context and the tool is for *running*
  investigations, not discovering the map.

## Non-goals

- Changing the persisted model (`WorkspaceTopology`/`WorkspaceResourceNode`/
  `WorkspaceResourceRelationship`) — it round-trips through `profile.config.topology`
  unchanged.
- Persisting suggestion dismissal server-side (deliberately session-only, unchanged).
- Live health/status badges on nodes (needs per-area live data — future idea).
- Auto-inferred relationships beyond the existing heuristic suggestion scan.
- Moving the Map out of Settings (decided: stays a Settings tab — it's configuration).
- Multiple profiles sharing one map, or map-level import/export (profile export
  already carries it).

## Dependencies

- `ProfileListLayout` / `profile-list-utils` (from active feature
  `settings-profiles-aks-shell`) — reuse for the list view rather than a third
  list pattern.
- `TopologyGraph` in `web/src/components/agent/AgentVisualizationPanel.tsx` —
  cytoscape usage to extract/generalize, not duplicate.
- `useWorkspaceTopologyCandidates` / `useWorkspaceTopologySuggestions` —
  unchanged data sources.
- `useProfile` / `useUpdateProfile` — updater-form saves only (pitfall:
  whole-store PUT from a render snapshot loses concurrent writes).

## Risks

- **Prompt size**: an unbounded map in the system prompt eats the context window —
  mitigated by the cap + overflow count (see `decisions.md`).
- **Canvas accessibility**: cytoscape renders to `<canvas>` — mitigated by the list
  view toggle carrying the full edit surface.
- **Vite bundle**: cytoscape is ~300KB; keep the dynamic `import()` so it only loads
  when a graph actually renders (Map tab or agent visualization).
- **Coordination** with `settings-profiles-aks-shell` (In Progress): both touch
  settings components; the map reuses its `ProfileListLayout`, no file conflicts
  expected since it only adds shared layout files.

## Links

- `docs/architecture/functionalities/agent.md` (current pipeline / ACP section)
- `docs/architecture/functionalities/settings-and-configuration.md`
- `docs/pitfalls/react-frontend.md` (TanStack Query updater + testid rules)
- `docs/features/active/settings-profiles-aks-shell/` (ProfileListLayout source)
