# React / Tauri Frontend Pitfalls

Recurring traps in `web/` (React 19, Tailwind 4, CodeMirror 6, Playwright) and at the
`web` ↔ `src-tauri` boundary. Add an entry whenever a bug here costs more than one debugging session.

## CodeMirror

### `defaultHighlightStyle` is light-theme only

`@codemirror/language`'s `defaultHighlightStyle` ships colours tuned for a white background — `#219`
blue, `#a11` red, `#164` green. Against SwebKit's dark theme background (`oklch(0.16 0.018 260)`)
those are effectively black on black, so syntax highlighting looks *absent* rather than wrong. This
went unnoticed in the API Client request body editor for the whole React migration.

**Use `swebkitHighlighting()` from `web/src/lib/codemirror-theme.ts`** in every editor instead.

### Theme a CodeMirror editor with CSS custom properties, not JS colour values

A `HighlightStyle` is baked into the `EditorState` at creation. With three themes applied as a class
on `<html>`, JS colour values would require a `Compartment` reconfigure or a full view rebuild on
every theme switch — losing cursor, scroll and undo state. Reference `var(--cm-*)` custom properties
(defined per theme class in `globals.css`) and the browser re-resolves them for free.

### CodeMirror only renders the visible viewport

Off-screen lines are not in the DOM, so a Playwright `toContainText` against a long document fails on
content that is genuinely there. Keep a hidden, `aria-hidden` mirror element holding the full text
(see `ResponseBodyViewer` and `BodyCodeEditor`) so assertions and assistive tech can read it.

## Tauri boundary

### Tauri does not camelCase struct fields on the way out

Command *arguments* are converted from JS camelCase to Rust snake_case automatically, but serialized
*return values* are not. A Rust field `index_state` arrives in TypeScript as `index_state`, so a TS
interface declaring `indexState` silently reads `undefined`. Put
`#[serde(rename_all = "camelCase")]` on any returned struct with a multi-word field, and keep the TS
interface next to it.

> `PortForwardSessionInfo` in `native.rs` still has this mismatch (`local_port` vs `localPort` in
> `tauri-bridge.ts`) — unrelated to the API Client, but the same trap.

### `AllowedRoots` is in-memory, so a persisted path is not an authorized path

`AllowedRoots` is populated *only* by the native `pick_file`/`pick_directory` dialogs — that is what
stops the webview granting itself filesystem access. Persisting a path in `localStorage` and passing
it back after a restart bypasses that entirely: any script in the webview can write to
`localStorage`. Persist the *grant list* on the Rust side and re-admit from it (see
`restore_allowed_root`); the frontend may only persist which granted root is selected.

### `validate_within_roots` is for files, not directories

It canonicalizes the *parent* directory because a file may not exist yet on write. A directory
argument needs `validate_dir_within_roots`, which canonicalizes the directory itself.

## Layout

### Fixed-pixel panels make one pane absorb all extra width

A `ResizablePanels` list of `[260, 540, null]` gives the final panel `flex: 1`, so on a wide monitor
every spare pixel lands there. Declare panels that should share space as `"1fr"` and only genuinely
fixed panels in pixels.

### Panel minimums must fit a 1280px window

If the sum of `minWidths` plus the fixed panels exceeds the container, every pane pins to its minimum,
dragging silently does nothing, and the container overflows. Check the arithmetic against a 1280px
viewport, not just the developer's monitor.

### Incremental keyboard resize must use a functional state update

Reading `widthsRef.current` (refreshed only on render) means rapid arrow-key repeats all compute from
the same stale base and collapse into a single step. Use `setWidths(prev => …)` for anything relative
to the previous value. A drag is different — its delta is cumulative from the pointerdown snapshot, so
a captured base is correct there.

### Set `user-select: none` while dragging

Without it, dragging a divider selects the text underneath it.

## Playwright

### `addInitScript` re-runs on every navigation

It is not "run once at test start". It fires on every `goto` **and** every `reload`, so clearing
`localStorage` in `addInitScript` destroys exactly the state a persistence test is trying to verify.
Clear storage with `page.evaluate` after the first navigation, then `reload` once.

### The e2e sidecar uses a throwaway appdata shared by every test in a file

`SWEBKIT_APPDATA_ROOT` points at `web/e2e/.e2e-appdata`, which is reset per *run*, not per test.
Collections, templates and rules accumulate across tests in a file, so `.first()` will eventually
select another test's data. Always filter by name:
`getByTestId(/collection-node-Request-/).filter({ hasText: name })`.

There are also no demo collections in a fresh e2e appdata — the demo data a developer sees comes from
their real `%APPDATA%\SwebKit`. A test that needs a request must create it.

### Waiting on a value that is already correct does not wait

`await expect(status).toContainText("200")` passes instantly on the second send, because the first
response is still on screen. Meanwhile the Send button is disabled mid-flight, so the next click is
swallowed and the test silently exercises fewer sends than it looks like. Wait on something that
advances — a history count, a new row — not on a value that is already there.

### `webServer.command` runs through cmd.exe on Windows

POSIX-isms in `playwright.config.ts` fail or misbehave: `rm -rf` does not exist, and
`mkdir -p <dir>` creates a literal `-p` directory next to the target. The stray `-p` then makes every
later run fail with "A subdirectory or file -p already exists", so the suite runs exactly once per
checkout. Do filesystem setup in the config module with `node:fs` instead — it is cross-platform and
runs before the servers start.

## Rust tests

`src-tauri` has a lib target, so `cargo test --lib` works with no extra tooling. Extract parsing
logic into pure functions (`parse_porcelain_v2`) so it can be tested against captured real command
output instead of requiring a repository fixture.
