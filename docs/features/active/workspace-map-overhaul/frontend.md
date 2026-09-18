# Frontend — Workspace Map Overhaul

## Target layout (Settings → Map tab)

```
┌────────────────────────────────────────────────────────────────────┐
│ Workspace Map                                   [Graph|List]       │
│ One-line explainer: what the map is, that the AI always sees it.   │
├────────────────────────────────────────────────────────────────────┤
│ [Search…]  [AKS✓][ServiceBus✓][Redis✓][Sql✓][Storage✓]  [+ Add]   │
├───────────────────────────────────────────┬────────────────────────┤
│                                           │  Inspector             │
│   Cytoscape canvas                        │  (selected node, or    │
│   - nodes colored by area                 │   "select a node")     │
│   - edges labeled with rel label          │                        │
│   - suggested edges dashed, confirmable   │  Node: label (edit),   │
│   - fcose/cose layout, fit-to-view        │  area, resource key    │
│   - click node → select + inspect         │  Relationships list    │
│                                           │  + add relationship    │
│   (List view: ProfileListLayout           │  + remove node         │
│    grouped by area, same inspector)       │                        │
└───────────────────────────────────────────┴────────────────────────┘
```

## Components

### `shared/TopologyGraph.tsx` (extracted)

- Generalize `AgentVisualizationPanel.tsx`'s `TopologyGraph` into
  `web/src/components/shared/TopologyGraph.tsx`: props = nodes `{id,label,area}`,
  edges `{from,to,label,dashed?}`, `onNodeClick?`, `layout?`.
- Keep the dynamic `import("cytoscape")` (bundle-splitting — don't pull ~300KB into
  the main chunk). Reuse from both the Map and the agent visualization panel.
- Prefer `fcose` layout (already in node_modules via mermaid's `cytoscape-fcose`)
  for cleaner area clustering; fall back to `cose` if registration is awkward.
- Destroy the cy instance in the effect cleanup (existing code already does —
  keep that contract).
- Style nodes per `WorkspaceResourceArea` with theme CSS vars (`hsl(var(--primary))`
  family already used; add a per-area color map). Keep edge labels readable at
  default zoom.

### `WorkspaceMapSettings.tsx` (restructured)

- Single `selection` state (`nodeId | null`) shared by graph click and list rows —
  the inspector is one component fed by either view.
- Filter state: `search` (matches `displayLabel` + `resourceKey`), `visibleAreas`
  (`Set<WorkspaceResourceArea>`) — applied to what renders in graph AND list.
- Suggestions render as dashed edges; clicking one (or its entry in the inspector
  "Suggested" group) exposes Confirm/Dismiss — same session-only dismissal.
- Orphan nodes (no relationships) get a visual cue (e.g. muted badge in inspector
  "Not connected to anything yet") — nudges the user to wire the map.

### `WorkspaceMapInspector.tsx` (new, right panel)

- Shows: area badge, `displayLabel` (editable via `DraftInput`-style commit-on-blur —
  pitfall: profile saves are whole-doc PUTs, never per keystroke), `resourceKey`
  (read-only display + "edit" only if cheap; key edits change tool matching).
- Relationships of the node: `From → To (label)` rows with Remove + ConfirmBar
  (existing confirm semantics preserved).
- Add relationship: target `<select>` of other visible nodes + label input.
- Remove node: ConfirmBar warning that N relationships go with it (existing copy).

### `WorkspaceMapAddPicker.tsx` (new)

- Popover/inline panel listing `useWorkspaceTopologyCandidates()` grouped by area,
  searchable; "Add" per candidate, "Add all" per area group, plus the existing
  manual custom-resource form (area select + key + label) folded in at the bottom.

### `web/package.json`

- Add `cytoscape` (pin `^3.33.3`, the version mermaid already resolves) and, if
  used, `cytoscape-fcose` (`^2.2.0`). Both are hoisted transitives today — declare
  them so a mermaid upgrade can't silently remove the map's renderer.

## Constraints (from pitfalls/guardrails)

- All saves via `useUpdateProfile().mutate((prev) => …)` updater form — the scope-
  serialized mutation prevents lost writes; never PUT a render snapshot.
- `data-testid` on every control that changes state (view toggle, filter input,
  area chips, picker rows, inspector buttons) — canvas nodes get `data-testid` on
  their list-view counterparts; graph assertions go through container/canvas
  presence + list view for content checks.
- `useNotification()` on every mutating action via the existing mutation `onError`.
- Keep old testids where they still map (`workspace-map-settings`,
  `workspace-node-*`, `workspace-relationship-*`) so `settings.spec.ts` doesn't
  silently lose coverage — rename deliberately, not accidentally.
- Node ids are 8-char `crypto.randomUUID()` slices — reused as DOM/cytoscape ids;
  they're already collision-safe for this scale.
