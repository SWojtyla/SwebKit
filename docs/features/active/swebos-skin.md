# SwebOS skin — shared design language with SwebFlow/SwebOS

State: Review

## Goal

Put SwebKit on the SwebOS design system (`SwebFlow/docs/design/spec.md`) so the
two apps read as one product family: shared semantic tokens, shared chrome
metrics, and a theme set reduced from nine bespoke palettes to the SwebOS
families — **Aurora** (cool indigo) and **Clay** (warm sand/terracotta), each in
light and dark.

Approved prototypes live in `docs/prototypes/swebos-skin/` (index pitch +
aks / api-client / service-bus mocks, all four themes).

## Decisions

- **Token port, not a fork.** `globals.css` carries the four SwebOS token sets
  verbatim plus the legacy-name bridge (SwebKit components keep compiling
  against `--card`, `--muted-foreground`, `bg-accent`, …). DOM contract is
  SwebOS's: aurora-light → `:root`, aurora-dark → `.dark`, clay-light → `.clay`,
  clay-dark → `.clay.dark`.
- **Theme ids** `light | dark | clay | clay-dark`. `light`/`dark` keep their
  persisted meaning (now Aurora), so most users migrate silently. Stale ids
  (`fancy`, `fathom-*`, `letterpress-*`, `cascade-*`) map to the nearest Aurora
  mode at hydration via `migrateTheme`.
- **Status bar folds into the top bar** (spec §8 — no footer). Connection
  state, per-area health dots, version, env, timezone and theme name move into
  a top-bar status popover; the reconnect action survives inside it. All
  `status-bar-*` testids are preserved on the moved elements.
- **AKS log viewer is untouched** — user sign-off ("our current log viewer is
  perfect"). Same for `logHighlight.ts` tokens; the `--cm-*` palette just gains
  Clay variants.
- **API Client keeps its layout** — collection tree left, request/response
  stacked vertically on the right ("no horizontal layout pls"). Restyle is
  chrome-level only.
- **Ribbon stays a ribbon** on Service Bus — the prototype's tab treatment is
  applied to the existing `SbRibbon` chrome, not a control swap.

## Scope

- `globals.css` — four theme blocks, `@custom-variant dark`, `@theme inline`
  (spec type scale + font stacks + radius/shadow mapping), shell classes
  (`.shell-pill`, `.shell-search`, nav tokens), all existing feature classes
  (`log-tok-*`, `var-tok-*`, `yml-*`, `body-viewer`, `tree-drag-*`) retargeted
  to spec tokens.
- `settings.ts` — 4-theme union, `migrateTheme` for stale ids.
- `AppearanceSettings.tsx` — 4 theme cards.
- `AppLayout.tsx` — 232px/56px sidebar, 48px topbar, status popover, brand mark.
- `AgentMarkdown.tsx` — mermaid dark detection for `dark`/`clay-dark`.
- Pages: AKS rail/table/badge chrome, API Client tree/tabs/editor chrome,
  Service Bus ribbon/tree/message chrome — visuals only, no behaviour or
  testid changes beyond what the status-bar fold forces.
- e2e specs updated where they reference removed themes or the status bar
  element itself (`layout`, `layout-deferred`, `env-badge`, `settings`,
  `aks-ux`).

## Non-goals

- No SwebOS product concepts (actors/stages/work items) — this is the design
  language, not the domain model.
- No layout changes to API Client panes or the AKS log viewer.
- Density/font-size user settings stay as-is.

## Test plan

- `npm run build` + `npm run lint` + `npx vitest run` in `web/`.
- Theme unit tests: `migrateTheme` mapping + `isTheme` guard.
- e2e: `layout`, `layout-deferred`, `env-badge`, `settings`, `aks-ux` suites.
- Visual: Playwright screenshots of Dashboard/AKS/API Client/Service Bus in
  Aurora dark and Clay light.
- Aikido scan on touched files.

## Validation

- [x] `npm run build` — clean (tsc + vite, CSS bundle 93.8 kB vs ~115 kB before)
- [x] `npm run lint` — clean
- [x] `npx tsc --noEmit` — clean
- [x] `npx vitest run` — 916/916 (incl. new `settings.test.ts`: `isTheme`,
      `migrateTheme`, `applyThemeClass` — 6 tests)
- [x] `dotnet build src-sidecar` — 0 warnings; `dotnet test
    tests/SwebKit.Sidecar.Tests` — 925/925
- [x] Playwright, 14 specs touching the reskin (layout, layout-deferred,
      env-badge, settings, navigation, dashboard, aks, aks-ux, aks-url-state,
      api-client, api-client-layout, api-client-variables, service-bus,
      service-bus-url-state) — 195 tests; 14 timed out under single-worker
      load on the dev box, all 14 re-ran green in isolation
- [x] Fresh-eyes diff review (read-only agent): one blocker found —
      `service-bus.spec.ts` asserted `bg-primary` on the active view tab;
      updated to `aria-selected="true"` (the semantic contract). No behavior,
      handler, testid, query-key or notification regressions found elsewhere;
      log-viewer files confirmed absent from the diff
- [x] Visual: Playwright screenshots of Dashboard/AKS/API Client/Service Bus
      in all four themes (aurora light/dark, clay light/dark)
- [x] Aikido `aikido_scan_paths` on all touched files: 2 false positives
      (`col.render(msg)` flagged as `express.render`; mermaid
      `dangerouslySetInnerHTML` — pre-existing, `securityLevel: "strict"`).
      `window.open(\`https://${host}\`)`in HttpRoutesTab/IngressesTab was
    hardened: host is now validated via`new URL`+`target.host === host`    before opening. The rule still reports the`window.open` call itself —
      residual accepted: scheme is hardcoded and the host allowlist check is
      the remediation's own advice.

## Notes

- Service Bus + API Client files were re-indented to the repo's 4-space style
  during restyle — review with `git diff -w`.
- Pre-existing dirty files excluded from the commit: `src-tauri/Cargo.toml`,
  `src-tauri/gen/schemas/*.json`.
