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

### `dragDropEnabled` (on by default) breaks HTML5 drag-and-drop in the window

Tauri installs an OS-level drag/drop handler on the webview unless you turn it off, and on Windows
that handler swallows HTML5 `dragstart`/`dragover`/`drop` inside WebView2 — you can pick an item up
and then find nowhere to put it. Tauri's own config doc says as much: *"Disabling it is required to
use HTML5 drag and drop on the frontend on Windows."*

Set `"dragDropEnabled": false` on the window in `src-tauri/tauri.conf.json` (safe as long as nothing
listens for `onDragDropEvent` / file drops). **Playwright cannot catch this** — the e2e suite runs in
a plain browser where HTML5 DnD works fine, so the API Client reorder tests passed for the whole time
the feature was unusable in the app. Anything drag-related needs a manual check in the Tauri window.

### A `draggable` element is not focused by a click

Chromium suppresses the default mousedown-focus on `draggable` elements, so making a `tabIndex={0}`
row a drag source silently breaks every keyboard interaction that assumed clicking it focuses it
(here: `Alt`+`Arrow` reordering). Call `e.currentTarget.focus()` in the row's `onClick`.

### `AllowedRoots` is in-memory, so a persisted path is not an authorized path

`AllowedRoots` is populated *only* by the native `pick_file`/`pick_directory` dialogs — that is what
stops the webview granting itself filesystem access. Persisting a path in `localStorage` and passing
it back after a restart bypasses that entirely: any script in the webview can write to
`localStorage`. Persist the *grant list* on the Rust side and re-admit from it (see
`restore_allowed_root`); the frontend may only persist which granted root is selected.

### `validate_within_roots` is for files, not directories

It canonicalizes the *parent* directory because a file may not exist yet on write. A directory
argument needs `validate_dir_within_roots`, which canonicalizes the directory itself.

## Sidecar contract

### A TypeScript union standing in for a C# enum must use the member names exactly

`ServiceBusNamespace.authMode` was typed `"ConnectionString" | "Entra"`, but the C# enum is
`SbAuthMode { DefaultAzureCredential, ConnectionString, ServicePrincipal }`. There is no `Entra`
member, so choosing Entra ID sent a value the sidecar could not deserialize, the **whole profile
save** was rejected, and the radio silently snapped back — the failure surfaced as "the button does
nothing", nowhere near the type that caused it.

Enums cross the wire as their member names (`profiles.json` stores `"authMode": "ConnectionString"`).
When mirroring one in `types.ts`, copy the member names verbatim, and remember that one bad field
fails the entire document, not just that property.

### Settings fields write the whole profile, so commit on blur, not per keystroke

The profile is a single document: every field's save is a full `PUT` plus an atomic rewrite of
`profiles.json`. Wiring an input's `onChange` straight to the mutation therefore cost a disk write
and a round trip **per character**, and because the input was controlled off server state, each
character had to complete that loop before it appeared. Use `DraftInput`, which holds the text
locally and commits on blur, Enter, or unmount — the unmount case matters because switching settings
tabs removes the field without firing a blur.

Discrete controls (radio, checkbox, select) commit immediately; there is nothing to debounce.

## Server-sent events

### Close every `EventSource` in the effect cleanup

An SSE stream opened with `follow=true` never ends on its own. Without a cleanup that calls
`close()`, navigating away leaves it delivering into an unmounted component, and each remount opens
another one on top. The multi-pod log view holds a `Map<pod, EventSource>` precisely so it can close
them individually when a pod is deselected and all of them on unmount — see `MultiPodLogView.tsx`.
This is the React form of BL-7 in `blazor-maui.md`.

It matters more than it looks: browsers cap concurrent HTTP/1.1 connections per origin at six, so a
handful of leaked streams will silently stall every later request to the sidecar rather than failing
loudly.

### Never render per received message

A busy pod emits far faster than the browser can paint, and calling `setState` per message saturates
the render queue until the UI stops responding. Buffer into a ref and flush on a timer — `useLogBuffer`
does it at 10 fps, and `LogLineText` is memoised so the flush does not re-tokenize every visible line.
This is BL-8 in `blazor-maui.md`; the React log views hit it just as hard, and `MultiPodLogView`
shipped violating it (one `setLogs` per line, per pod).

### `EventSource` is GET-only, so every option is a query parameter

There is no way to send a body, which is why the log-stream endpoint takes `container`, `tail`,
`follow`, `sinceSeconds`, `previousContainer`, `filter` and `timestamps` in the URL. See the note at
`web/src/lib/api.ts:108`.

### A server-side text filter must not match the timestamp prefix

With `timestamps=true` Kubernetes prefixes every line with an RFC3339 stamp. A filter applied to the
whole line then matches the prefix, so filtering for a year returns everything. Split the line first —
`LogLineTimestamp` on the backend, `parseLogLine` on the frontend — and match only the message.

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

## TanStack Query

### `invalidateQueries({ queryKey: ["aks-"] })` matches nothing

Query keys are compared **element by element**, not as string prefixes. `["aks-"]` matches only a
query keyed exactly `["aks-"]`, and every AKS query is keyed `["aks-pods", ns]`,
`["aks-deployments", ns]`, and so on — so the AKS Refresh button, the auto-refresh timer, the `r`
shortcut and the post-apply-YAML refresh were all silent no-ops. It fails *quietly*: the UI shows no
error, and between ticks the tables usually look identical anyway.

Group-invalidate through a `predicate` instead — see `web/src/lib/aks-query-keys.ts`, which also
keeps the slow cluster-scoped queries (`aks-namespaces`, ~18s cold) off the periodic timer.

Corollary: **if auto-refresh is invisible, users assume it is broken.** Show when the data was last
updated (AKS puts a fixed-width "updated 12s ago" in the toolbar) — otherwise a working refresh and
a broken one look the same.

### A whole-store `PUT` derived from a render snapshot loses concurrent writes

`useUpdateCollections` replaces the entire collections file. Computing the new array from a
component's `collections` variable means computing it from a *render snapshot*, so two saves close
together each send a full store built before the other landed and the loser's changes disappear —
creating two requests quickly left only the second, a collection variable saved and then reopened
empty, and one of two quick drag-reorders was dropped.

Two things fix it together: take an **updater function** and evaluate it inside `mutationFn` against
`queryClient.getQueryData`, and give the mutation a **`scope`** so saves to the same store are
serialized rather than overlapping. Also give such a mutation an `onError` — a silently swallowed
save is indistinguishable from the user never having typed anything.

Corollary for tests: once saves are serialized, an assertion fired immediately after the action can
read the pre-save state. Use `expect.poll`, not a single `allTextContents()`.

### Don't guard a mutation with a no-op check against a stale snapshot

`if (moveNode(collections, id, target) === collections) return;` looks like a harmless optimization.
`moveNode` returns its input unchanged when it cannot find the source node — and a node created a
moment ago is not in this render's snapshot yet — so the guard cancelled exactly the moves that
needed the fresh data. Detect the no-op inside the updater, where the data is current, and let a
genuinely redundant write be a redundant write.

## React Router

### `searchParams` in a callback is a snapshot, so two writes in one tick clobber each other

A helper shaped like `updateParams` that does `new URLSearchParams(searchParams)` builds on the
params from the render that created it. Two writes before the next commit — selecting an AKS
namespace and immediately clicking a tab — both build on the same empty base, and the second drops
the first's parameter, leaving the page on "Select a namespace to view resources".

The functional setter form (`setSearchParams(prev => …)`) does **not** fix this: React Router's
implementation calls the updater with the same captured `searchParams`. With `<BrowserRouter>`
(which pushes to history synchronously) read `window.location.search` at call time instead.

### An effect that defaults a URL param can overwrite the user's choice

"Initialize the selection once the list loads" effects race with the interaction the loaded list
makes possible: the list arriving is what populates the `<select>`, so the change event can land
after that commit but before React flushes the passive effect it scheduled. The effect still sees
the pre-selection `searchParams`, decides nothing is selected, and `replace`s the URL back to the
default. Guard with a ref set by the user-facing setter (`namespacePickedRef` in
`AksWorkspaceContext`), not with the `searchParams` the effect closed over.

## Inputs

### A native input cannot colour its own content

To highlight inside a single-line field (`{{variable}}` tokens, say), render an `aria-hidden` overlay
holding the same string with per-token spans and make the input's own text transparent
(`text-transparent caret-foreground`) — see `components/api-client/VariableInput.tsx`, the same
technique as the AKS YAML editor's overlay. Two rules keep it from drifting: the two layers must
share *exact* text metrics (pass one class string to both; put border/background on a wrapper, never
on the input, where it would paint over the overlay), and the overlay's `scrollLeft` must be mirrored
from the input in a layout effect — `onScroll` alone misses caret-driven scrolling.

## Tables

### An inline row editor re-lays out the whole table

Swapping a row's `Scale` button for an input plus two more buttons widens the Actions column, so
every row shifts the moment you click — you lose your place in the list you were acting on. Use a
modal (`components/aks/ScaleDialog.tsx`), which also has room to say *which* resource and namespace
is about to change.

With a `table-auto` layout, refreshing data jitters columns too, as an age ticks `9m` → `10m` or a
metric gains a digit. Put `tabular-nums` on the table and `w-full` on the column that should absorb
the slack (the name), so every other column is sized to its content and stops re-measuring.

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
