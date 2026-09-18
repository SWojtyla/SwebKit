# Decisions — Workspace Map Overhaul

## D1 — Interactive graph as primary view, list as accessible fallback

**Chosen:** cytoscape canvas (extracted shared component) + `ProfileListLayout`
list view toggle.

- Cytoscape is already used by `AgentVisualizationPanel`'s `TopologyGraph` for
  agent-emitted topology blocks — same library, same idioms, extracted once.
- Rejected: mermaid (read-only, layout degrades past ~20 nodes, no click handling);
  list-only (fails the "see the shape" goal — the map's value is the edges).
- Canvas is not keyboard/screen-reader accessible → the list view carries the full
  edit surface, not a degraded subset. Both views share one selection state and
  one inspector.

## D2 — Inspector-driven editing over persistent forms

**Chosen:** click a node (graph or list) → right-side inspector with all actions.

- The current two-column form puts add-node, add-relationship, suggestions and the
  relationships table on screen simultaneously — it doesn't scale and buries the
  per-node actions. Selection-driven editing means controls only exist for the
  node being acted on.
- Remove actions keep `ConfirmBar` (existing convention, Batch 8.7 parity).

## D3 — Map injected into the per-turn system prompt, bounded

**Chosen:** `## Workspace map` section in `AgentSystemPromptBuilder.Build`,
`MaxMapNodes = 30` / `MaxMapEdges = 40` with `+N more` overflow.

- "Always as context" = the prompt, not a tool the model must think to call —
  and `investigate_workspace_issue` is additionally fenced behind workspace scope
  on contextual panels, so tool-only delivery misses most turns.
- Both providers covered: OpenAI-compatible sends it as the system message; ACP
  prompt-stuffs it (`AcpAgentModelClient.ComposePrompt`), refreshing whenever the
  prompt text changes.
- Caps exist because the prompt is rebuilt every turn and feeds the context budget
  (`AgentContextBudgetPlanner` counts system-prompt chars). 30/40 comfortably
  covers realistic curated maps; a user with more gets a "+N more" marker, not a
  blown window.
- Rejected: per-turn topology *tool call* (the model would still need to know to
  call it — same discovery problem); including the map only on workspace scope
  (still misses feature-scoped turns where cross-area hints matter most — the
  fenced-areas section already tells the model other areas exist; now it also
  knows how they connect).

## D4 — Declare `cytoscape` (and `cytoscape-fcose`) as direct deps

**Chosen:** pin `cytoscape ^3.33.3` / `cytoscape-fcose ^2.2.0` in
`web/package.json`.

- Both are currently transitive deps of mermaid — hoisted into node_modules, so
  `import("cytoscape")` resolves today but disappears the day mermaid drops them.
  A core settings surface can't ride on a sibling's dependency list.

## D5 — Suggestion dismissal stays session-only

**Unchanged:** `dismissedKeys` lives in component state; reload re-suggests.

- Matches the original workspace-intelligence scoping (no server-side
  accepted/dismissed bookkeeping). The graph just renders suggestions as dashed
  edges; confirm/dismiss semantics identical. If session-only dismissal proves
  annoying in practice, that's a separate feature (needs a persisted field).

## D6 — Persisted model untouched

`WorkspaceTopology`/`WorkspaceResourceNode`/`WorkspaceResourceRelationship` and
the `profile.config.topology` round-trip stay exactly as they are — the overhaul
is presentation + prompt injection only. Profile export/import, normalization
(`WorkspaceTopologyNormalizationTests`), and the investigation tool's
`ResourceKey` contract (`namespace/deployment`, `server/db`, `account/container`)
all keep working.
