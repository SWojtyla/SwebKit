# Status — API Client UX Overhaul

## Current State

`Review` — implemented on branch `feat/api-client-ux-and-git` on 2026-07-31, alongside
[api-client-git-completion](../api-client-git-completion/index.md). Not committed.

**Jira:** not linked

## Origin

User-reported on 2026-07-31: the API Client "feels off when I open it. Left panel too small,
response is xxl. no syntax highlighting for the response or bodies. Colors would help."

A code-level deep dive confirmed all three complaints and traced each to a specific cause. See
[technical-plan.md](technical-plan.md) "Current state" for the verified inventory with line
references.

## Progress

### Module 1 — Fractional, persisted, accessible panel layout
- [x] `ResizablePanels` accepts `"Nfr"` widths; leftover-space resolution extracted as a pure function
- [x] `panel-preferences.ts` with versioned load/save
- [x] `ApiClientPage` switched to `[300, "1fr", "1fr"]` with `storageKey`
- [x] Panel minimums lowered to `[200, 340, 320]` — the planned `[220, 420, 380]` pinned every pane
      to its minimum and overflowed the container at a 1280px window
- [x] Resizer `role="separator"` + ARIA + keyboard (arrows, Shift, Home/End)
- [x] Double-click resets a pair
- [x] Pointer events + `user-select` guard during drag
- [x] Doubled `border-r` on the middle pane removed

### Module 2 — Shared method badge and status colour vocabulary
- [x] `method-badge.tsx` with `METHOD_META` and `<MethodBadge>`
- [x] Three duplicate `methodColors` maps deleted
- [x] `statusColor` reworked onto Aurora tones
- [x] `api-client-format.ts` with `formatBytes` / `formatElapsed`, wired into the status bar and history
- [x] Tab count badges made uniform across request and response tab strips

### Module 3 — Theme-aware syntax highlighting
- [x] `--cm-*` tokens for `:root`, `.dark`, `.fancy` in `globals.css`
- [x] `codemirror-theme.ts` exporting `swebkitHighlighting()`
- [x] Request body editor switched off `defaultHighlightStyle`; inline theme block removed
- [x] Request body editor gains bracket matching, fold gutter, active line, and pane-filling height
- [x] `@codemirror/search` added as a direct dependency
- [x] Read-only response body viewer with line numbers, folding, search, wrap toggle, download
- [x] Content-type → language selection with body-sniff fallback
- [x] Size thresholds (`pre` / `codemirror` / `codemirror-plain`) with a visible large-body notice
- [x] Pretty/Raw relabelled as a state-showing two-segment control

### Module 4 — Request pane hierarchy
- [x] Request name becomes an inline-editable heading on the tab-strip row
- [x] Variable-preview toggle regrouped with the URL field
- [x] Dirty state shown as a dot rather than a `Save*` label

### Module 5 — Response pane persistence
- [x] `Save Example` persists into `HttpRequestEntry.responseExamples` via `saveActiveTab`
- [x] Saved examples clickable, with a viewing banner and return-to-live
- [x] Secret scrubbing before an example is persisted
- [x] Response history moved into per-tab `TabState`

### Module 6 — Documentation
- [x] `docs/architecture/functionalities/api-client.md` Core Runtime Flow updated to the React graph
- [x] Response-rendering and state-persistence rows updated
- [x] `docs/architecture/index.md` API Client routing row still accurate after the rewrite

### Testing
- [x] Vitest added to `web/` with config, scripts, and the `@` alias
- [x] Unit suites: format, method badge, response language, render mode, panel prefs, `fr` resolution
- [x] Existing 18 + 6 API Client e2e tests pass unchanged
- [x] New e2e groups: layout, colour/badges, highlighting, response persistence, accessibility
- [x] Manual desktop verification on the widest available monitor, all three themes

## Definition of Done

1. Opening the API Client maximized on a wide monitor gives a response pane within 15% of the request
   pane's width, and both are comfortably wider than before.
2. Panel widths persist across an app restart and are adjustable by keyboard.
3. JSON and XML are syntax-highlighted in **both** the request body and the response body, readable
   in `light`, `dark`, and `fancy`.
4. No method label renders as a truncated word anywhere in the UI.
5. No raw Tailwind palette colour remains in `web/src/components/api-client/` — all method and status
   colour comes from Aurora tokens.
6. `Save Example` and response history survive a tab switch and an app restart respectively as
   specified in [technical-plan.md](technical-plan.md) Module 5.
7. Vitest runs green in `web/`; all unit suites in [test-plan.md](test-plan.md) implemented.
8. The full Playwright suite passes, including the 24 pre-existing API Client tests, unmodified except
   where [test-plan.md](test-plan.md) explicitly documents an interaction change.
9. `docs/architecture/functionalities/api-client.md` describes the React implementation for
   everything this feature touched.
10. `ship-readiness` run clean before merge.

## Verification status

| Check | Result |
| --- | --- |
| `npx tsc -b` in `web` | clean |
| `npm --prefix web run build` | succeeds |
| `npm --prefix web run test:unit` | 116 passed across 9 files |
| API Client Playwright specs | 19 new (`api-client-layout.spec.ts`) + 24 pre-existing, all passing except one noted below |
| Manual desktop verification | **Not performed** — see Notes |

## Deviations from the plan

| Planned | Shipped | Why |
| --- | --- | --- |
| `minWidths` `[220, 420, 380]` | `[200, 340, 320]` | The planned values summed above the available width at 1280px, so all three panes pinned to their minimum, dragging did nothing, and the container overflowed. Caught by the narrow-window e2e test. |
| Request name heading "right-aligned against the tab buttons" | Same position, but the tab strip is now `overflow-x-auto` with `shrink-0` buttons | Without it, a long request name overflowed the flexible tab container and rendered on top of the tab buttons, making them unclickable. Caught by two pre-existing e2e tests. |
| `<pre>` path highlighting described loosely | Added `lib/bodyHighlight.ts`, a token-returning tokenizer | Returning tokens rather than an HTML string (as `yamlHighlight.ts` does) avoids `dangerouslySetInnerHTML` on third-party API response bodies entirely. |
| Module 2 scoped to method/status colour | Also swept `WebSocketPanel`, `EnvironmentManager`, the legacy-secret notice and capture warnings | Definition of Done item 5 promised no raw Tailwind palette colour anywhere under `components/api-client/`; the narrower scope would have left 11. |
| — | Added `docs/pitfalls/react-frontend.md` | Four traps hit during implementation had no home: CodeMirror light-only default style, the Tauri camelCase boundary, `addInitScript` re-running on reload, and the `mkdir -p` cmd.exe bug. |
| — | Fixed `web/playwright.config.ts` | Pre-existing: `mkdir -p` under cmd.exe created a literal `-p` directory, so the e2e suite could only be run once per checkout. This blocked verification. |

## Notes and open questions

- **Vitest is new infrastructure for this repo.** It is justified by the pure functions this feature
  introduces, but it is a decision with reach beyond the API Client — it becomes the frontend unit
  test runner for everything that follows. Flag it explicitly in review rather than letting it land
  quietly inside a UX change. 116 unit tests across 9 files now run via `npm --prefix web run test:unit`.
- **`ApiClientPage` chunk grew to ~476 kB** (from ~390 kB) because the response viewer pulls in
  CodeMirror's search and fold extensions. Still under Vite's 500 kB warning threshold, but the next
  addition to this page will cross it — worth a `manualChunks` entry for `@codemirror/*` at that point.
- **One pre-existing e2e failure is unrelated to this work:** `api-client.spec.ts` "collection
  variables editor works" fails identically on a clean `main` (verified by stashing). Not fixed here.
- `@codemirror/autocomplete`, `@codemirror/search` and `@lezer/highlight` were all transitive; each is
  now an explicit direct dependency in `web/package.json`.
- Module 6 rewrites part of a file that
  [api-client-git-completion](../api-client-git-completion/index.md) Module 5 also rewrites. Land this
  feature's doc changes first and note the remaining git-section staleness so the second pass has a
  clean base.
