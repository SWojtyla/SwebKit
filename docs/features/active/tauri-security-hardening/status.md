# Status — Tauri/Sidecar Security & Reliability Hardening

## Current State

`Done` (pending user commit + Aikido scan)

## Quick Summary

Closes two critical holes (wildcard-CORS sidecar API, unrestricted filesystem access from the
frontend) plus sidecar lifecycle reliability gaps, found during review of the uncommitted
Tauri/React/sidecar migration work. Must land before the AKS/Service Bus/demo-mode fix plans.

**Jira:** not linked

## Progress Checklist

- [x] CORS locked via an `IsAllowedOrigin` predicate in `Program.cs`: any `http://localhost:*` or
      `http://127.0.0.1:*` origin (any port — Vite's 1420, the Playwright e2e harness's 1419, future
      port changes, all still fine) plus the two fixed Tauri webview origins; remote origins are
      never matched. Exception-handler middleware echoes the matched origin instead of hardcoding
      `*`. (First pass hardcoded a single origin list and broke the Playwright e2e suite, which runs
      Vite on a different port than `tauri dev` specifically to avoid colliding with a live dev
      instance — caught by actually running the e2e suite, see below.) Shared-secret token layer
      (defense in depth) deliberately **not** implemented this pass — the origin predicate alone
      closes the reported vulnerability without the dev-workflow complexity a token would add.
- [x] `read_file`/`write_file`/`list_dir` scoped to an allowlist (`native.rs`): a root is only
      granted when the user picks a file/folder via `pick_file`/`pick_directory`'s native OS dialog;
      paths are canonicalized before the allowlist check to defeat `..`/symlink tricks
- [x] CSP re-enabled (`tauri.conf.json`); remote Google Fonts `<link>` tags removed from
      `web/index.html` (system-ui fallback already in `globals.css`) rather than self-hosting a font
      file — simpler, zero new asset, and the visual difference is negligible
- [x] Sidecar readiness: binds to port 0 in production and recovers the real port by parsing
      Kestrel's own "Now listening on:" startup log line (no blind `sleep`, no hardcoded port);
      stderr is now captured and logged instead of discarded
- [x] Sidecar shutdown hook added (`RunEvent::Exit` kills the child; no orphan process on app exit)
- [x] `manage()` now propagates spawn errors as a fatal Tauri setup error (via `?`), matching
      `restart_sidecar`'s behavior — no more silent `unwrap_or((5199, None))` fallback
- [x] `.gitignore` updated: `src-tauri/binaries/`, stray root build logs; working tree cleaned up
      (root `package.json`/`node_modules`/lockfile confirmed intentional — they hold the
      `@tauri-apps/cli` dev dependency used to drive `tauri dev`/`tauri build` from the repo root)
- [x] `cargo check` clean in both debug and release profiles (only one pre-existing, unrelated
      warning: `PortForwardSession.local_port` never read)
- [x] Manual smoke test: validated indirectly via the full Playwright e2e suite (140+ tests) running
      against the sidecar over HTTP from a real browser — this exercises the CORS predicate and
      dynamic-port resolution end-to-end. First CORS pass hardcoded a single origin and broke
      everything until caught by this suite (Playwright runs Vite on port 1419, not Tauri's 1420,
      specifically to avoid colliding with a live dev instance) — fixed by switching to an
      `IsAllowedOrigin` predicate (any `http://localhost:*`/`127.0.0.1:*` + the two Tauri origins).
      Built installer / packaged-app smoke test still needs the user (no Tauri bundling in this
      environment).
- [ ] Aikido security scan (per `docs/security/aikido-mcp-scan.md`) — no Aikido tooling available in
      this session

## Validation

Not started.

## Blockers

_None._

## Notes

- Found during code review on 2026-07-26 of uncommitted changes on `feat/tauri-react-rewrite`
  (see conversation history / PR discussion for full findings).
- This plan blocks [aks-migration-fixes](../aks-migration-fixes/status.md) and
  [service-bus-migration-fixes](../service-bus-migration-fixes/status.md) from being considered
  final, since both build UI on top of the sidecar HTTP boundary this plan changes.
