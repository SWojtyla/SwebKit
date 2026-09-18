# Status — Workspace Map Overhaul

**State:** `Review` (implementation complete, validated, awaiting ship)

## Progress

- [x] Explore current implementation (`WorkspaceMapSettings`, endpoints,
      `InvestigateWorkspaceIssueTool`, `AgentSystemPromptBuilder`)
- [x] Confirm gap: topology was not in the system prompt
- [x] Decide direction with user: graph + list, node inspector, stays in Settings
- [x] Frontend: extract shared cytoscape graph component
      (`web/src/components/shared/TopologyGraph.tsx`, reused by
      `AgentVisualizationPanel`)
- [x] Frontend: graph view (nodes by area, labeled edges, dashed suggestions,
      fcose layout, click-to-inspect)
- [x] Frontend: node inspector (`WorkspaceMapInspector.tsx` — DraftInput label
      edit, relationship table + add form, ConfirmBar removals, orphan hint,
      map summary when nothing selected)
- [x] Frontend: list view via `ProfileListLayout` + Graph/List toggle
- [x] Frontend: filter (search + per-area chips) applied to graph and list
- [x] Frontend: add-resource picker (`WorkspaceMapAddPicker.tsx` — searchable,
      per-area groups, "Add all", custom-resource form, auto-opens on empty map)
- [x] Frontend: declare `cytoscape`/`cytoscape-fcose` in `web/package.json`
      (were transitives via mermaid); theme var → hex resolution in
      `web/src/lib/theme-colors.ts` (cytoscape can't parse var()/oklch)
- [x] Backend: `BuildWorkspaceMapSection` in `AgentSystemPromptBuilder`
      (bounded: 30 nodes/area, 40 edges, `+N more`; omitted when empty)
- [x] Backend: `investigate_workspace_issue` description tweak
- [x] Unit tests: 7 new `AgentSystemPromptBuilderTests` cases (present/absent/
      caps/labels/dangling edge)
- [x] Unit tests: `workspace-map-utils.test.ts` (15 tests) + `oklchToHex`
- [x] Playwright: existing Map tab specs updated for graph/list/inspector
      (both pass)
- [x] Validation matrix: `npm run build` ✓, `npm run test:unit` (473) ✓,
      `dotnet build` sidecar+agents ✓, `dotnet test` sidecar (456) ✓,
      `playwright settings.spec.ts` (22) ✓, eslint clean on new files
- [x] Docs: `functionalities/agent.md` (workspace-map prompt section),
      `functionalities/settings-and-configuration.md` (topology bullet)
- [ ] Manual check: graph in the real Tauri WebView2 window
- [ ] pre-ship-review → azure-devops → swebifix → feature-archive

## Validation results

| Check | Result |
|-------|--------|
| `web: npm run build` | ✓ (cytoscape/fcose code-split, not in main bundle) |
| `web: npm run test:unit` | 473 passed |
| `web: eslint` (new files) | clean |
| `src-sidecar: dotnet build` | ✓ |
| `src/SwebKit.Agents: dotnet build` | ✓ |
| `SwebKit.Sidecar.Tests` | 456 passed |
| `playwright settings.spec.ts` | 22 passed |
