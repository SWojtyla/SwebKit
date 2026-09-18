# React / Tauri Frontend Pitfalls

Recurring traps in `web/` (React 19, Tailwind 4, CodeMirror 6, Playwright) and at the
`web` ↔ `src-tauri` boundary. Add an entry whenever a bug here costs more than one debugging session.

## CodeMirror

### `defaultHighlightStyle` is light-theme only

`@codemirror/language`'s `defaultHighlightStyle` ships colours tuned for a white background — `#219`
blue, `#a11` red, `#164` green. Against SwebKit's dark theme background (`oklch(0.16 0.018 260)`)
those are effectively black on black, so syntax highlighting looks _absent_ rather than wrong. This
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

Command _arguments_ are converted from JS camelCase to Rust snake*case automatically, but serialized
\_return values* are not. A Rust field `index_state` arrives in TypeScript as `index_state`, so a TS
interface declaring `indexState` silently reads `undefined`. Put
`#[serde(rename_all = "camelCase")]` on any returned struct with a multi-word field, and keep the TS
interface next to it.

> `PortForwardSessionInfo` in `native.rs` had exactly this mismatch (`local_port`/`remote_port` vs
> `localPort`/`remotePort` in `tauri-bridge.ts`) until the `ux-interaction-consistency` pass added
> `#[serde(rename_all = "camelCase")]` while extending the struct with a `context` field — a good
> reminder to check for this any time a returned struct changes.

### `dragDropEnabled` (on by default) breaks HTML5 drag-and-drop in the window

Tauri installs an OS-level drag/drop handler on the webview unless you turn it off, and on Windows
that handler swallows HTML5 `dragstart`/`dragover`/`drop` inside WebView2 — you can pick an item up
and then find nowhere to put it. Tauri's own config doc says as much: _"Disabling it is required to
use HTML5 drag and drop on the frontend on Windows."_

Set `"dragDropEnabled": false` on the window in `src-tauri/tauri.conf.json` (safe as long as nothing
listens for `onDragDropEvent` / file drops). **Playwright cannot catch this** — the e2e suite runs in
a plain browser where HTML5 DnD works fine, so the API Client reorder tests passed for the whole time
the feature was unusable in the app. Anything drag-related needs a manual check in the Tauri window.

### A `draggable` element is not focused by a click

Chromium suppresses the default mousedown-focus on `draggable` elements, so making a `tabIndex={0}`
row a drag source silently breaks every keyboard interaction that assumed clicking it focuses it
(here: `Alt`+`Arrow` reordering). Call `e.currentTarget.focus()` in the row's `onClick`.

### A panicking `.setup()` is a silent crash — no console exists to show it

`main.rs` builds with `windows_subsystem = "windows"`, so nothing the process prints — including a
panic message — is ever visible to the user. `sidecar::manage()` used to propagate spawn failure out
of `.setup()` with `?`, which failed `build()` and hit `.expect("error while building tauri
application")`: on an overloaded machine, where the self-contained .NET sidecar can exceed a 15s
READY_TIMEOUT just getting extracted and JIT-compiled, the app simply vanished on launch with no
error anywhere.

Treat every error inside `.setup()` as fatal-by-invisible-panic: degrade instead. `manage()` now
returns a `SidecarState` with `port = 0`, the frontend falls into its existing "Disconnected" state
(Reconnect button + health poll), and a background thread keeps retrying the spawn and emits the
usual `sidecar-*` lifecycle events. Also make READY*TIMEOUTs generous — the failure cost is an app
that \_looks* dead, not a slow start.

### `AllowedRoots` is in-memory, so a persisted path is not an authorized path

`AllowedRoots` is populated _only_ by the native `pick_file`/`pick_directory` dialogs — that is what
stops the webview granting itself filesystem access. Persisting a path in `localStorage` and passing
it back after a restart bypasses that entirely: any script in the webview can write to
`localStorage`. Persist the _grant list_ on the Rust side and re-admit from it (see
`restore_allowed_root`); the frontend may only persist which granted root is selected.

### `validate_within_roots` is for files, not directories

It canonicalizes the _parent_ directory because a file may not exist yet on write. A directory
argument needs `validate_dir_within_roots`, which canonicalizes the directory itself.

### A shared-blob Tauri command needs its own lock — Tauri's IPC dispatch does not serialize for you

`src-tauri/src/secrets.rs` stores every API Client auth secret as one JSON blob (a
`HashMap<key, secret>`) under a single OS keychain entry, and `save_secret`/`delete_secret` each did
an unsynchronized read-modify-write: load the whole vault, mutate one key, write the whole vault
back. Tauri dispatches plain (non-`async`) commands onto a thread pool, so two `save_secret` calls
for _different_ requests' secrets — realistic whenever a user sets auth on two requests within the
same couple of seconds their own save debounces already create — can interleave: both read the same
starting snapshot, both write back, and whichever finishes last silently drops the other's key. The
symptom on the frontend looked nothing like a race: a token was visible right after typing it
(served from the in-memory, never-persisted `AuthConfig.credentialSecret`) and then appeared to
"vanish" only later — on reopening the tab or restarting the app, once `getSecret()` was the only
source left and it could no longer find the clobbered key. Fixed with a single
`static VAULT_LOCK: Mutex<()>` held across each command's full load-mutate-save sequence.

**This class of bug is invisible to Playwright.** `web/src/lib/tauri-bridge.ts`'s `isTauri()` check
is false under Chromium, so every e2e run takes the `localStorage` fallback branch — single-threaded,
no possible interleaving — never the real keychain path. A Playwright repro of a Tauri-secrets race
will not reproduce it no matter how the test is written; only the real desktop app can.

### `plugin:event|listen not allowed by ACL` — `core:` plugins need a capability grant

App-defined commands work with no capability file at all, but every `core:` plugin API the
frontend touches (`@tauri-apps/api/event`'s `listen`/`unlisten`, window controls, …) is
ACL-gated: with no `src-tauri/capabilities/*.json` granting it, the packaged app throws
`Command plugin:event|listen not allowed by ACL` while dev mode and every Playwright run
(browser, no Tauri) stay green. The pod shell's output stream hit exactly this — it subscribes
with `listen()`, which needs `core:event`'s `allow-listen`/`allow-unlisten`, covered by the
`core:default` permission set granted in `src-tauri/capabilities/default.json`. Any new
`@tauri-apps/api/*` import in the frontend needs its matching permission added there.

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

### A required query parameter with no default fails silently — as a 400, not as no data

`MultiPodLogView.tsx` never sent `previousContainer` in its `EventSource` URL; only `PodLogView.tsx`
did. The sidecar's `/logs/stream` route bound it as `bool previousContainer` — no `?`, no default —
which ASP.NET's minimal-API model binding treats as **required**: omitting it from the query string
fails the request with a 400 _before the handler body runs at all_, real client or demo alike. On the
wire, and to `EventSource.onerror`, that is indistinguishable from a stream that opened fine and just
never delivered anything — which is exactly what every multi-pod correlation looked like: an
indefinite "Connecting...".

This is easy to miss in review because every existing test bypassed it: the sidecar unit tests call
the extracted `AksEndpoints.StreamPodLogsAsync` handler directly with typed C# arguments, never going
through actual route/query binding, and the e2e assertion only checked that the empty-state string was
gone — true the instant a pod was selected, regardless of whether any log line ever arrived. Prefer
giving every primitive query parameter on an SSE route a C# default (`bool previousContainer = false`,
`int tail = 0`, …) so a caller that reasonably omits a flag degrades instead of 400ing invisibly, and
write at least one e2e assertion against actual content (a line count, not just an absent placeholder
string) for any stream-shaped view.

### A custom SSE event name must not be `error`

`EventSource` reserves the event type `"error"` for its own native connection-failure signal, fired to
both `.onerror` and any `addEventListener("error", ...)` listener as a plain `Event` (no `.data`). If
the server also frames a named event as `event: error`, it dispatches to the _same_ listener as a
`MessageEvent` (with `.data`) — the two are register-compatible but shape-incompatible, and nothing
stops a handler written for one from receiving the other. Name an application-level error frame
something else entirely (`stream-error`, here) so it can never collide with the browser's own error
delivery.

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
shortcut and the post-apply-YAML refresh were all silent no-ops. It fails _quietly_: the UI shows no
error, and between ticks the tables usually look identical anyway.

Group-invalidate through a `predicate` instead — see `web/src/lib/aks-query-keys.ts`, which also
keeps the slow cluster-scoped queries (`aks-namespaces`, ~18s cold) off the periodic timer.

Corollary: **if auto-refresh is invisible, users assume it is broken.** Show when the data was last
updated (AKS puts a fixed-width "updated 12s ago" in the toolbar) — otherwise a working refresh and
a broken one look the same.

### A whole-store `PUT` derived from a render snapshot loses concurrent writes

`useUpdateCollections` replaces the entire collections file. Computing the new array from a
component's `collections` variable means computing it from a _render snapshot_, so two saves close
together each send a full store built before the other landed and the loser's changes disappear —
creating two requests quickly left only the second, a collection variable saved and then reopened
empty, and one of two quick drag-reorders was dropped.

Two things fix it together: take an **updater function** and evaluate it inside `mutationFn` against
`queryClient.getQueryData`, and give the mutation a **`scope`** so saves to the same store are
serialized rather than overlapping. Also give such a mutation an `onError` — a silently swallowed
save is indistinguishable from the user never having typed anything.

Corollary for tests: once saves are serialized, an assertion fired immediately after the action can
read the pre-save state. Use `expect.poll`, not a single `allTextContents()`.

### An updater function must not read `e.target` — it runs after React restored the DOM

Updater functions (`mutate((prev) => …)`) are evaluated inside `mutationFn`, which the mutation
`scope` defers until the previous save settles — _after_ the event handler returns and after React
has already restored the controlled input's DOM value back to the prop. So
`mutate((prev) => ({ ...prev, x: e.target.value }))` reads the _reverted_ value, not the one the
user picked: `selectOption("large")` in Playwright produced a PUT body with `"medium"`. Capture DOM
reads eagerly before the mutate call:

```ts
onChange={(e) => {
  const fontSize = e.target.value as UserSettings["fontSize"];
  updateSettings.mutate((prev) => ({ ...prev, fontSize }));
}}
```

`useUpdateUserSettings` takes `UserSettings | ((prev) => UserSettings)` — same convention as
`useUpdateProfile`; spread `prev`, never the render-time `settings`, or a fast second edit reverts
the first (the appearance font-size/density regression was exactly that, layered under this one).

### A `queryFn` that ignores `signal` keeps the server working for an answer nobody wants

TanStack hands every `queryFn` an `AbortSignal`. Every hook here ignored it, because `apiFetch`
took a `RequestInit` nobody ever passed. So retyping a Redis filter, clicking through entities, or
navigating away left the previous request running to completion **server-side** — holding its
pooled connection and its backend round trips. With the global `retry: 1`, a request that timed
out was then silently run a second time, so the user waited roughly twice as long for a result
that had already been superseded.

Write `queryFn: ({ signal }) => apiFetch(url, { signal })`. The server half already worked: ASP.NET
binds a handler's `CancellationToken` to `HttpContext.RequestAborted`, so aborting really does stop
the work — it just has to be triggered.

### `refetchOnWindowFocus` defaults to `true`, which is wrong for a desktop app

People alt-tab constantly. The default re-fires every active query on every focus, and in this app
one query is often a fan-out — a cluster-wide namespace list, or one request per Service Bus topic.
It is off globally in `main.tsx`; every page has an explicit Refresh, and the volatile queries carry
their own short `staleTime`.

Related: give _structural_ queries (entity trees, namespace lists — things deployments change, not
users) a `staleTime` in minutes, and keep the seconds-scale one for counts and status.

### `keepPreviousData` makes `data` briefly belong to the _previous_ query key

`placeholderData: keepPreviousData` is the right fix for a list that blanks between pages — but any
effect that advances pagination off `data` must now also check `isFetching`, or it will re-run
against the previous page and re-append it. Redis's "Load all" needed both that and a guard against
re-advancing on the same cursor. The symptom before `keepPreviousData` was the mirror image: the
effect keyed off `data` identity, which went `undefined` when the key changed, so the loop switched
itself off after exactly one page.

### Don't guard a mutation with a no-op check against a stale snapshot

`if (moveNode(collections, id, target) === collections) return;` looks like a harmless optimization.
`moveNode` returns its input unchanged when it cannot find the source node — and a node created a
moment ago is not in this render's snapshot yet — so the guard cancelled exactly the moves that
needed the fresh data. Detect the no-op inside the updater, where the data is current, and let a
genuinely redundant write be a redundant write.

### Query keys that omit the server-side identity serve one backend's data under another's name

Every AKS resource key was `["aks-pods", ns]`, with no context element — the key did not
identify which cluster the data came from. Two consequences: during a context switch the tables
kept showing the _previous_ cluster's rows labelled as the new one, and switching _back_ never
hit cache, so every round trip re-paid the full fetch. Worse, the workspace picked the next
namespace from the old cluster's list, which could be a name the new cluster doesn't even have.

The fix has three parts that all have to be true at once: put the resolved context in every
namespaced/cluster key (`["aks-pods", ctx, ns, …]`, derived from the cached profile, not from a
prop threaded down), hold the queries in a gated token so nothing fires against cluster A with
cluster B's namespace mid-switch (`namespaceToken` in `AksWorkspaceContext`), and restore the
target context's _persisted_ namespace (`view-pref:aks-selected-ns:<ctx>`) instead of inferring
it from whichever list happens to be in memory. Any consumer that bypasses the hook — the
command palette reading cached namespaces — must prefix-scan (`getQueriesData`) since it can no
longer know the context element.

## React Router

### `searchParams` in a callback is a snapshot, so two writes in one tick clobber each other

A helper shaped like `updateParams` that does `new URLSearchParams(searchParams)` builds on the
params from the render that created it. Two writes before the next commit — selecting an AKS
namespace and immediately clicking a tab — both build on the same empty base, and the second drops
the first's parameter, leaving the page on "Select a namespace to view resources".

The functional setter form (`setSearchParams(prev => …)`) does **not** fix this: React Router's
implementation calls the updater with the same captured `searchParams`. With `<BrowserRouter>`
(which pushes to history synchronously) read `window.location.search` at call time instead.

### A "restore on launch" read must happen before the save effect's first write

Persisting `last-route` on every navigation and restoring it on launch look independent, but the
save effect fires on the very first commit — writing `last-route="/"` over the stored value
_before_ the async settings query that gates the restore has even resolved. The restore then
reads its own overwrite and does nothing.

Capture the stored value at mount with a lazy initializer (`useState(() => loadViewPreference(…))`)
— initializers run before any effect — and restore from the capture. Then the save effect can
truthfully record every navigation, `/` included: if the user's real last page was the dashboard,
`"/"` is the correct thing to restore (i.e. restore nothing).

### An effect that defaults a URL param can overwrite the user's choice

"Initialize the selection once the list loads" effects race with the interaction the loaded list
makes possible: the list arriving is what populates the `<select>`, so the change event can land
after that commit but before React flushes the passive effect it scheduled. The effect still sees
the pre-selection `searchParams`, decides nothing is selected, and `replace`s the URL back to the
default. Guard with a ref set by the user-facing setter (`namespacePickedRef` in
`AksWorkspaceContext`), not with the `searchParams` the effect closed over.

## Inputs

### A commit-normalizing field must re-sync on every render, not only when the prop changes

`DraftInput` reconciles its local draft against the stored value when the save echoes back —
guarded on "value ≠ last committed text" so our own echo doesn't fight the cursor. Guarding
the _effect_ on `[value]` breaks the case where the parent normalizes the commit back to the
value already stored: type `-5`, the clamp writes `5`, `5` was already saved, the prop is
byte-identical, the effect never fires, and the raw `-5` sits in the box permanently while a
different value is on disk. Run the reconciliation unconditionally (no dep array) — the
`committedRef` guard alone is what protects in-progress typing.

### A native input cannot colour its own content

To highlight inside a single-line field (`{{variable}}` tokens, say), render an `aria-hidden` overlay
holding the same string with per-token spans and make the input's own text transparent
(`text-transparent caret-foreground`) — see `components/api-client/VariableInput.tsx`, the same
technique as the AKS YAML editor's overlay. Two rules keep it from drifting: the two layers must
share _exact_ text metrics (pass one class string to both; put border/background on a wrapper, never
on the input, where it would paint over the overlay), and the overlay's `scrollLeft` must be mirrored
from the input in a layout effect — `onScroll` alone misses caret-driven scrolling.

## Tables

### An inline row editor re-lays out the whole table

Swapping a row's `Scale` button for an input plus two more buttons widens the Actions column, so
every row shifts the moment you click — you lose your place in the list you were acting on. Use a
modal (`components/aks/ScaleDialog.tsx`), which also has room to say _which_ resource and namespace
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

`SWEBKIT_APPDATA_ROOT` points at `web/e2e/.e2e-appdata`, which is reset per _run_, not per test.
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

### A `.last()` locator resolves at action time, not at assertion time

`page.locator('[data-testid^="redis-database-"]').last()` binds to whichever row is last
_when the next action runs_. Click "Add Cache", then `fill` immediately: while the add's
`PUT` is still in flight the new row isn't mounted, so `.last()` is the _previous_ last row
and the fill lands on the wrong input — after the PUT lands the same locator silently points
at the new row instead. Wait for the add's `PUT /api/config/profiles` response (the
`saveProfile` pattern used elsewhere in `settings.spec.ts`) before resolving the locator.

The virtualized Redis tree has the sibling trap: a namespace row starts collapsed, so a
key under it has no DOM row at all — scrolling can never reach it. Always go through
`scrollToRedisKey` in `helpers.ts`, which expands all namespaces first.

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
