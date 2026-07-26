# Tauri/Sidecar Security & Reliability Hardening

## Summary

The Tauri shell and its `.NET` sidecar bridge (from the MAUI → Tauri + React rewrite) expose two
critical security holes and several reliability gaps, found during code review of the uncommitted
native-bridge changes (`src-tauri/src/native.rs`, `src-tauri/src/sidecar.rs`,
`src-sidecar/Program.cs`). **This must land before any further AKS/Service Bus/demo-mode fix work
is merged**, because those fixes build UI on top of the same sidecar HTTP boundary this plan locks
down — fixing them first would mean redoing the wiring once CORS/auth changes.

**Jira:** not linked

## Root causes

1. The sidecar is a plain ASP.NET HTTP server on `127.0.0.1:5199` with `AllowAnyOrigin()` CORS —
   any origin loaded in the user's regular browser (not just the Tauri webview) can call it.
2. New `read_file`/`write_file`/`list_dir` Tauri commands accept an unvalidated path straight from
   the frontend, with no scoping to a project/workspace root.
3. Sidecar process lifecycle (readiness, port, shutdown) is held together with a blind `sleep(2)`
   and a hardcoded port, with stdout/stderr thrown away — failures are invisible.

## Scope

- Lock down the sidecar's HTTP surface so only the Tauri app itself can call it.
- Constrain filesystem access exposed via Tauri commands to an explicit allowlist.
- Make sidecar startup/shutdown deterministic and observable.
- Re-enable CSP.
- Stop the sidecar binary and build artifacts from leaking into git.

## Non-Goals

- Not rewriting the sidecar's business logic (Service Bus/AKS/Redis/Storage clients) — that's
  covered by the feature-specific plans ([aks-migration-fixes](../aks-migration-fixes/index.md),
  [service-bus-migration-fixes](../service-bus-migration-fixes/index.md)).
- Not switching the sidecar transport off HTTP (e.g. to named pipes/UDS) — out of scope for this
  pass; the shared-secret header approach below closes the hole without a transport rewrite. Worth
  a future follow-up if this app's threat model hardens further.

## Tasks

### 1. Close the sidecar CORS hole (Critical)

**File:** `src-sidecar/Program.cs:52` (CORS policy setup), `Program.cs:66-82` (exception handler
that also re-affirms `Access-Control-Allow-Origin: *`).

- Replace `AllowAnyOrigin()` with an explicit allowed-origins list: the Tauri prod origin
  (`tauri://localhost` on Windows is actually `http://tauri.localhost` — verify the exact scheme
  Tauri v2 uses for this app via `src-tauri/tauri.conf.json`) and the Vite dev origin
  (`http://localhost:1420` or whatever `vite.config.ts` binds).
- **Additionally** (defense in depth, since origin headers can be spoofed by non-browser clients
  anyway): generate a random shared-secret token in Rust at sidecar spawn time
  (`src-tauri/src/sidecar.rs`), pass it to the sidecar process via an environment variable or
  command-line arg, and require it as a header (e.g. `X-Sidecar-Token`) on every request via
  ASP.NET middleware in `Program.cs`. `web/src/lib/tauri-bridge.ts` needs a Tauri command to fetch
  this token once at startup (e.g. `get_sidecar_token`) and `web/src/lib/api.ts` must attach it to
  every fetch.
- Fix the exception-handler middleware (`Program.cs:66-82`) to reflect the same locked-down origin
  policy instead of hardcoding `*`.
- **Test:** from a plain browser tab (not the Tauri app) at `http://example.com`, attempt
  `fetch('http://127.0.0.1:5199/api/servicebus/...')` and confirm it's blocked by CORS; confirm the
  Tauri app itself still works end-to-end.

### 2. Sandbox filesystem access from the frontend (Critical)

**File:** `src-tauri/src/native.rs:341-364` (`read_file`, `write_file`, `list_dir` commands),
wired in `src-tauri/src/lib.rs:12,52-55`, called from `web/src/lib/tauri-bridge.ts:176-196`.

- Determine what these commands are actually used for (grep `web/src` for their call sites —
  likely the API Client's git-linked collections and/or blob recovery export). Scope each call site
  first before locking down the API.
- Add a Rust-side validation function: canonicalize the requested path (`std::fs::canonicalize` or
  equivalent) and verify it is a descendant of one of a small set of allowed roots (e.g. the app's
  data directory, and — only if actually needed — a user-picked project directory that was itself
  selected via the existing native file/folder picker dialog, never typed freely by JS).
- Reject (return an `Err` to the frontend) any path that fails the check, with a clear error the UI
  can surface.
- **Test:** from the browser devtools console inside the running app, call
  `window.__TAURI__.invoke('read_file', { path: 'C:\\Windows\\System32\\drivers\\etc\\hosts' })` (or
  the actual exposed invoke wrapper) and confirm it's rejected. Confirm legitimate in-app file
  operations (whatever they are) still work.

### 3. Re-enable CSP (Major)

**File:** `src-tauri/tauri.conf.json:25` (currently `"csp": null`).

- Set a real CSP: `default-src 'self'`, `connect-src 'self' http://127.0.0.1:5199` (or the sidecar's
  actual dynamic port — see Task 4), `img-src 'self' data:`, and drop the remote Google Fonts fetch
  in `web/index.html:7-9` in favor of a self-hosted font file so `font-src` doesn't need an external
  host at all.
- **Test:** app loads and renders correctly with CSP enabled; check the browser console for CSP
  violation reports and fix any legitimate ones (e.g. inline styles needing a nonce, if any).

### 4. Deterministic sidecar readiness and lifecycle (Major)

**File:** `src-tauri/src/sidecar.rs:17,30-52` (port hardcoded to 5199, `Stdio::null()` on
stdout/stderr, blind `sleep(2)`).

- Bind the sidecar to port 0 (OS-assigned) and have it print the actual bound port to stdout on
  startup (e.g. `SIDECAR_PORT=<port>`); capture stdout in Rust (replace `Stdio::null()` with
  `Stdio::piped()`) and parse that line instead of assuming 5199.
- Replace the blind `sleep(2)` with an actual readiness check: poll a lightweight `/health` endpoint
  (add one to `Program.cs` if it doesn't exist) until it responds or a timeout elapses; surface a
  clear error to the UI if the sidecar never becomes healthy instead of silently reporting port 5199
  as if it were live.
- Fix `manage()`'s silent `unwrap_or((5199, None))` fallback (swallows spawn errors) to propagate
  the real error the same way `restart_sidecar` already does.
- Register a Tauri `RunEvent::Exit` (or `on_window_event` close) handler that kills the sidecar
  child process, so it never survives as an orphan holding the port.
- **Test:** kill the sidecar process externally while the app is running, then trigger
  `restart_sidecar` — the app should recover cleanly. Force a spawn failure (e.g. temporarily rename
  the bundled sidecar executable) and confirm the UI shows a real error instead of a fake "5199 is
  ready" state. Quit the app and confirm no orphaned `dotnet`/sidecar process remains in Task
  Manager.

### 5. Repo hygiene (Minor)

- Add `src-tauri/binaries/` to `.gitignore` (currently untracked but not ignored — it's the full
  self-contained .NET publish output, ~130+ files, referenced from `tauri.conf.json`'s
  `bundle.resources`).
- Add `build_log.txt`, `build_stderr.txt`, `build_stdout.txt`, `tauri_build_err.txt`,
  `tauri_build_out.txt` (currently untracked stray build logs at repo root) to `.gitignore` and
  delete them from the working tree.
- Confirm `node_modules/`, `package-lock.json` at repo root are intentional (a root-level
  `package.json` appeared as untracked — check whether this is meant to exist alongside `web/`'s own
  `package.json`/lockfile, or is a stray `npm install` run from the wrong directory).

## Dependencies

None — this is the foundation the other three plans build on.

## Risks

| Risk | Mitigation |
|---|---|
| Locking CORS/adding a token header breaks the dev workflow (Vite HMR origin differs from prod) | Test both `npm run tauri dev` and a built installer before merging; keep dev origin in the allowlist explicitly |
| Path-sandboxing breaks a legitimate feature that currently relies on free-form paths | Grep all call sites of `read_file`/`write_file`/`list_dir` first (Task 2) before writing the allowlist, so it covers real usage |
| Health-check polling adds startup latency | Use a short poll interval (50-100ms) with a generous timeout (5-10s) — should be faster than the current fixed 2s sleep in the common case |
