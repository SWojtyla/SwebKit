# Test Plan — Workspace Map Overhaul

## Unit — sidecar (`tests/SwebKit.Sidecar.Tests/AgentSystemPromptBuilderTests.cs`)

| # | Scenario | Expected |
|---|----------|----------|
| 1 | Topology with nodes + relationships | `## Workspace map` section present; nodes grouped by area label; edge renders `from → to (label)` |
| 2 | Null/empty topology | Section omitted entirely |
| 3 | > MaxMapNodes nodes | Overflow marker `+N more`, section stays bounded |
| 4 | > MaxMapEdges relationships | Overflow marker on the relationships line |
| 5 | Unlabeled relationship | `from → to` with no trailing parens/`(null)` |
| 6 | Demo-mode profile | Same code path (topology is profile config — no demo special-casing) |

## Unit — web (`vitest`, new `workspace-map-utils.test.ts`)

| # | Scenario | Expected |
|---|----------|----------|
| 1 | `buildGraphElements(topology, suggestions)` | Confirmed edges solid; suggestions dashed; orphan edges (missing endpoint) dropped |
| 2 | `filterTopology(topology, search, areas)` | Matches label AND resourceKey; area chips exclude whole areas and their edges |
| 3 | Orphan detection | Node with zero relationships flagged |
| 4 | Candidate grouping | `groupCandidates(candidates)` → per-area groups, sorted |

## Playwright (`web/e2e/` — extend `settings.spec.ts` or new `workspace-map.spec.ts`)

| # | Scenario | Steps |
|---|----------|-------|
| 1 | Add node from picker | Map tab → Add → pick candidate → appears in list view + graph canvas non-empty |
| 2 | Select + inspect | Click node (list view) → inspector shows area/key/label |
| 3 | Add relationship | Inspector → pick target + label → edge appears in list/graph |
| 4 | Remove relationship | ConfirmBar flow — remove, verify gone |
| 5 | Remove node | ConfirmBar warns about N relationships → node + edges gone |
| 6 | Suggestion confirm | Demo-mode suggestion → Confirm → becomes solid edge; Dismiss → gone for session |
| 7 | Filter | Search text narrows list; area chip hides that area's nodes/edges |
| 8 | View toggle | Graph ⇄ List; selection persists across the toggle |
| 9 | Failure path | Remove target of an existing relationship → both gone, no dangling edge |

## Manual / not-automatable checks

- Graph renders in the actual Tauri WebView2 window (canvas, fcose layout) —
  Playwright runs in Chromium, which usually matches, but verify once.
- `import("cytoscape")` code-splits: main bundle doesn't grow; Map chunk loads on
  tab open.
- Very large map (~60 nodes): filter + layout stay interactive; prompt-section cap
  keeps a chat turn under the context budget (check the `ContextUsagePercent`
  reply field doesn't jump).
- Dark/light/custom themes: per-area node colors readable (CSS vars only).
- Demo mode toggle on → demo candidates appear in the picker.

## Coverage results

| Plan item | Result |
|-----------|--------|
| Sidecar unit 1–6 | ✓ Implemented + passing (`AgentSystemPromptBuilderTests`, 7 cases incl. dangling-edge guard) — 456 suite total |
| Web unit 1–4 | ✓ Implemented + passing (`workspace-map-utils.test.ts`, 15 tests) |
| Playwright 3 (add relationship), 4 (remove relationship), 5 (remove node + edges), 6 (suggestion confirm/dismiss), 9 (failure path) | ✓ Covered by the two updated `settings.spec.ts` Map tests — 22/22 spec pass |
| Playwright 1 (add from picker) | ⚠ Deferred — picker flow is exercised indirectly (manual-add path covers `addNode`; picker adds call the same callback). Owner: follow-up e2e pass. |
| Playwright 2 (select + inspect) | ⚠ Partially — list-view selection drives the inspector in the relationship test; no dedicated assert on area/key fields. Same follow-up. |
| Playwright 7 (filter), 8 (view toggle + selection persistence) | ⚠ Deferred — filtering covered by web unit tests (`filterTopology`); toggle is a UI-only path. Same follow-up. |
| Manual checks | ⚠ Pending — Tauri WebView2 render, ~60-node layout, themes, demo candidates, context-budget check. Owner: user (needs the real desktop window). |

## Full validation matrix (before PR)

```
(cd web && npm run build)          # ✓ run
(cd web && npm run test:unit)      # ✓ 473 passed
(cd src-sidecar && dotnet build)   # ✓ run
(cd tests/SwebKit.Sidecar.Tests && dotnet test)  # ✓ 456 passed
(cd web && npx playwright test)    # ⚠ settings.spec.ts only (22 passed); full suite not yet run
```
