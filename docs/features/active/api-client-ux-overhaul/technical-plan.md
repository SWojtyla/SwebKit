# Technical Plan — API Client UX Overhaul

All paths are relative to the repository root. This feature is **frontend-only**: no sidecar
endpoints, no Tauri commands, no C# changes.

## Current state (verified 2026-07-31)

| Observation | Location |
|---|---|
| Tree and request panes are fixed pixels; response is `flex: 1` and absorbs all extra width | [web/src/components/api-client/ApiClientPage.tsx:739](../../../../web/src/components/api-client/ApiClientPage.tsx) |
| Panel widths never persisted; resizers are mouse-only, no ARIA, no reset, no `user-select` guard | [web/src/components/ui/ResizablePanels.tsx](../../../../web/src/components/ui/ResizablePanels.tsx) |
| `methodColors` duplicated in three files using raw Tailwind palette | `CollectionTree.tsx:21`, `RequestEditor.tsx:41`, `RequestTabStrip.tsx:19` |
| Method label truncated to 4 chars → `DELE`, `PATC` | [web/src/components/api-client/CollectionTree.tsx:211](../../../../web/src/components/api-client/CollectionTree.tsx) |
| Response body is a plain `<pre>`, no highlighting, no line numbers, no search | [web/src/components/api-client/ResponseViewer.tsx:310](../../../../web/src/components/api-client/ResponseViewer.tsx) |
| Request body editor uses light-only `defaultHighlightStyle` — invisible in dark/fancy | [web/src/components/api-client/RequestEditor.tsx:83](../../../../web/src/components/api-client/RequestEditor.tsx) |
| Request name is a bare full-width input on its own row | [web/src/components/api-client/RequestEditor.tsx:430](../../../../web/src/components/api-client/RequestEditor.tsx) |
| `Save Example` writes to component-local state; saved-example buttons have no `onClick` | [web/src/components/api-client/ResponseViewer.tsx:288](../../../../web/src/components/api-client/ResponseViewer.tsx) |
| Response history is `ResponseViewer` local state, lost on remount | [web/src/components/api-client/ResponseViewer.tsx:72](../../../../web/src/components/api-client/ResponseViewer.tsx) |
| Response size rendered as raw bytes / `size unknown` | [web/src/components/api-client/ResponseViewer.tsx:150](../../../../web/src/components/api-client/ResponseViewer.tsx) |
| Middle pane has `border-r` on both the wrapper and `RequestEditor` → doubled border | `ApiClientPage.tsx:753` + `RequestEditor.tsx:357` |

Existing assets to reuse rather than reinvent:

- **Aurora tokens** — `--info` / `--success` / `--warning` / `--destructive` / `--muted-foreground`,
  defined per theme class in [web/src/styles/globals.css](../../../../web/src/styles/globals.css).
- **Theme store** — `useSettingsStore` with `light` / `dark` / `fancy`, applied as a class on
  `document.documentElement` ([web/src/lib/stores/settings.ts](../../../../web/src/lib/stores/settings.ts)).
- **Highlight-token precedent** — the AKS YAML viewer's `.yml-viewer .yml-*` token set in
  `globals.css` with `.dark` overrides, driven by
  [web/src/lib/yamlHighlight.ts](../../../../web/src/lib/yamlHighlight.ts).
- **Persisted-preference precedent** — plain `localStorage` load/save helpers with a defaults merge,
  as in [web/src/lib/stores/sb-preferences.ts](../../../../web/src/lib/stores/sb-preferences.ts).
- **CodeMirror packages** — already dependencies: `@codemirror/state`, `view`, `commands`,
  `language`, `lang-json`, `lang-xml`.

---

## Module 1 — Fractional, persisted, accessible panel layout

**Files:** `web/src/components/ui/ResizablePanels.tsx`,
`web/src/lib/stores/panel-preferences.ts` (new), `web/src/components/api-client/ApiClientPage.tsx`

### 1.1 Support fractional widths

Extend the props so a panel can be declared as a fraction of the *remaining* space after fixed
panels are laid out:

```ts
interface ResizablePanelsProps {
  children: ReactNode[];
  /** Number = px (fixed). String "2fr" = fraction of leftover space. null = 1fr. */
  initialWidths?: (number | string | null)[];
  minWidths?: number[];
  /** When set, widths are persisted under this key. */
  storageKey?: string;
  className?: string;
}
```

Internally keep the existing px-based `widths` state for drag maths, but derive the initial values
from the measured container: fixed panels keep their px value, and the leftover is divided among the
`fr` panels in proportion. The existing container-measure `useEffect` is already the right place —
it currently only resolves `null`/percent entries, so extend `toNumber` to parse `fr` and split the
remainder.

`ApiClientPage` then becomes:

```tsx
<ResizablePanels
  initialWidths={[300, "1fr", "1fr"]}
  minWidths={[220, 420, 380]}
  storageKey="api-client-panels"
  className="w-full min-w-0"
>
```

Tree grows from 260 → 300 px (the current 260 truncates collection names in the demo data at the
default font size); request and response then split the remainder evenly instead of the response
taking everything.

### 1.2 Persist widths

New `web/src/lib/stores/panel-preferences.ts`, following the `sb-preferences.ts` shape exactly —
plain functions, `try/catch`, defaults merge, no zustand:

```ts
export interface PanelWidths { version: number; widths: (number | null)[] }
export function loadPanelWidths(key: string, expectedCount: number): (number | null)[] | null
export function savePanelWidths(key: string, widths: (number | null)[]): void
```

`version` guards against the pixel→fraction change: a stored record whose `version` or panel count
does not match is discarded and the new defaults apply. Save on drag end (`mouseup`), not on every
`mousemove`, to avoid hammering `localStorage`.

### 1.3 Resizer accessibility and drag polish

On the resizer element in `ResizablePanels`:

- `role="separator"`, `aria-orientation="vertical"`, `aria-valuenow` (left panel width as a
  percentage), `tabIndex={0}`, and an `aria-label` naming the two panes.
- `onKeyDown`: `ArrowLeft`/`ArrowRight` move by 16 px, with `Shift` by 64 px; `Home`/`End` drive the
  left pane to its min/max.
- `onDoubleClick`: reset that pair to their initial proportions.
- During drag set `document.body.style.userSelect = "none"` and `cursor = "col-resize"`, cleared in
  the existing `up` handler — today dragging selects response text.
- Switch `onMouseDown`/`mousemove`/`mouseup` to pointer events (`onPointerDown` +
  `setPointerCapture`) so the drag survives the cursor leaving the window.

### 1.4 Remove the doubled border

Drop `border-r` from the middle-pane wrapper in `ApiClientPage.tsx:753` — `RequestEditor` already
carries it. The resizer itself provides the visual divider.

---

## Module 2 — Shared method badge and status colour vocabulary

**Files:** `web/src/components/api-client/method-badge.tsx` (new),
`web/src/lib/api-client-format.ts` (new), `CollectionTree.tsx`, `RequestEditor.tsx`,
`RequestTabStrip.tsx`, `ResponseViewer.tsx`, `web/src/styles/globals.css`

### 2.1 One method vocabulary

Delete all three `methodColors` maps and replace with a single module exporting both the short label
and the token-based colour:

```ts
export const METHOD_META: Record<ApiRequestMethod, { short: string; tone: Tone }> = {
  Get:       { short: "GET",   tone: "info" },
  Post:      { short: "POST",  tone: "success" },
  Put:       { short: "PUT",   tone: "warning" },
  Patch:     { short: "PATCH", tone: "warning" },
  Delete:    { short: "DEL",   tone: "destructive" },
  Head:      { short: "HEAD",  tone: "neutral" },
  Options:   { short: "OPT",   tone: "neutral" },
  GraphQl:   { short: "GQL",   tone: "accent" },
  WebSocket: { short: "WS",    tone: "accent" },
};
```

The explicit `short` map replaces `method.toUpperCase().slice(0, 4)`. `Patch` and `Put` share the
`warning` tone but are distinguished by label — acceptable, and better than inventing a token.

`<MethodBadge method tone size>` renders a chip (`rounded px-1.5 font-mono text-[10px] font-bold`)
with `color` and a low-alpha `background` derived from the same token, matching how
`statusColor` in `ResponseViewer` already treats status codes. Used in the tree, the tab strip, and
as the trigger styling for the method `<select>` in the request URL bar.

### 2.2 Token-backed tones

Add to `globals.css` under the existing token block:

```css
@theme inline {
  --color-accent-strong: var(--aurora-2);
}
```

Tone → token mapping (`info`, `success`, `warning`, `destructive`, `accent`, `neutral` →
`--info`, `--success`, `--warning`, `--destructive`, `--aurora-2`, `--muted-foreground`). Because all
six already have `light` / `dark` / `fancy` values, method colours become theme-correct for free —
today `text-gray-500` for `Options` is fixed regardless of theme.

Rework `statusColor` in `ResponseViewer.tsx:19` onto the same tones: `2xx → success`,
`3xx → info`, `4xx → warning`, `5xx / 0 → destructive`. Currently it mixes `--destructive` with raw
`green-500` / `blue-500` / `yellow-500` / `red-500`.

### 2.3 Formatting helpers

`web/src/lib/api-client-format.ts`:

```ts
export function formatBytes(bytes: number): string   // -1 → "—", 0 → "0 B", 1536 → "1.5 kB"
export function formatElapsed(ms: number): string    // 190 → "190 ms", 2400 → "2.4 s"
```

Replaces `${response.contentLength} bytes` / `"size unknown"` and
`response.elapsedMs.toFixed(0)` in the status bar and history rows.

### 2.4 Consistent tab count badges

`RequestEditor`'s tab strip shows counts for Params only; `ResponseViewer` shows one for History
only. Add counts to Headers (request and response) and Capture, rendered as a uniform
`rounded-full bg-muted px-1.5 text-[10px]` pill rather than the current inline parenthesised text.

---

## Module 3 — Theme-aware syntax highlighting

**Files:** `web/src/lib/codemirror-theme.ts` (new), `web/src/styles/globals.css`,
`RequestEditor.tsx`, `ResponseViewer.tsx`

### 3.1 CSS-variable-driven highlight style

Because the theme is a class on `document.documentElement` and there are three themes, the
CodeMirror theme must not hardcode colours. Define `--cm-*` tokens per theme in `globals.css`,
alongside (and reusing the hues of) the existing `.yml-*` tokens so the two viewers look like one
system:

```css
:root {
  --cm-key: oklch(0.55 0.18 265);
  --cm-string: oklch(0.55 0.15 155);
  --cm-number: oklch(0.55 0.18 295);
  --cm-bool: oklch(0.55 0.18 265);
  --cm-null: var(--muted-foreground);
  --cm-punct: var(--muted-foreground);
  --cm-comment: oklch(0.55 0.08 140);
  --cm-tag: oklch(0.55 0.15 340);
  --cm-attr: oklch(0.60 0.12 75);
  --cm-gutter: var(--muted-foreground);
  --cm-selection: var(--primary-glow);
}
.dark { /* lifted-lightness variants, mirroring the existing .dark .yml-* overrides */ }
.fancy { /* aurora-hued variants */ }
```

Then a single exported extension pair consumed by both editors:

```ts
export const swebkitHighlightStyle = HighlightStyle.define([
  { tag: t.propertyName, color: "var(--cm-key)", fontWeight: "600" },
  { tag: t.string,       color: "var(--cm-string)" },
  { tag: t.number,       color: "var(--cm-number)" },
  { tag: [t.bool, t.keyword], color: "var(--cm-bool)", fontWeight: "600" },
  { tag: t.null,         color: "var(--cm-null)", fontStyle: "italic" },
  { tag: t.punctuation,  color: "var(--cm-punct)" },
  { tag: t.comment,      color: "var(--cm-comment)", fontStyle: "italic" },
  { tag: [t.tagName],    color: "var(--cm-tag)", fontWeight: "600" },
  { tag: [t.attributeName], color: "var(--cm-attr)" },
]);

export const swebkitEditorTheme = EditorView.theme({ /* transparent bg, token'd gutters, selection */ });
export function swebkitHighlighting() {
  return [syntaxHighlighting(swebkitHighlightStyle), swebkitEditorTheme];
}
```

No theme-change subscription or editor re-creation is needed: the browser re-resolves the custom
properties when the root class changes. This is the core reason for the CSS-variable approach — see
[decisions.md](decisions.md) DEC-1.

### 3.2 Fix the request body editor

In `RequestEditor.tsx`, replace `syntaxHighlighting(defaultHighlightStyle)` (line 83) with
`swebkitHighlighting()` and drop the inline `EditorView.theme({...})` block at lines 88–92, whose
job the shared theme now does. `defaultHighlightStyle` ships light-only colours (`#219`, `#a11`,
`#164`) which are effectively black-on-black against the dark theme's
`oklch(0.16 0.018 260)` background — this is the whole reason request-body highlighting reads as
absent today.

Also add to the body editor while it is being touched:
- `bracketMatching()` and `closeBrackets()` from `@codemirror/language` / `@codemirror/autocomplete`
  (autocomplete is a transitive dependency of `lang-json`; verify before importing, and add it
  explicitly to `package.json` if it is not already a direct dependency).
- `highlightActiveLine()` and `foldGutter()`.
- Grow the fixed `height: "12rem"` to `flex-1` with a `min-height`, so the body editor uses the pane
  instead of a 192 px letterbox.

### 3.3 Response body viewer

New `ResponseBodyViewer` component inside `ResponseViewer.tsx` (or extracted to
`web/src/components/api-client/ResponseBodyViewer.tsx` if it exceeds ~120 lines):

- Read-only `EditorView` with `EditorState.readOnly.of(true)` and `EditorView.editable.of(false)`.
- Extensions: `lineNumbers()`, `foldGutter()`, `highlightActiveLine()`, `search` keymap from
  `@codemirror/search` (**new direct dependency** — add to `package.json`), `swebkitHighlighting()`,
  and a `Compartment` for the language plus one for line wrapping.
- Language chosen from `response.contentType` with a body-sniff fallback, reusing the existing
  heuristic already in `tryPrettyPrint` (`ResponseViewer.tsx:27`): `json` for `*json*` or a body
  starting `{`/`[`, `xml` for `*xml*`/`*html*` or a body starting `<`, otherwise no language.
- New toolbar controls beside the existing Pretty/Copy/Save Example row: **Wrap** toggle
  (`EditorView.lineWrapping` via compartment, persisted with the panel prefs) and **Download**
  (`.json`/`.xml`/`.txt` by content type via a Blob object URL).
- Keep the current `Pretty`/`Raw` toggle but label it by state rather than by action — today the
  button reads `Pretty` while showing raw, which is ambiguous. Render as a two-segment control.

**Size guard.** The sidecar caps bodies at 4 MB (`HttpRequestResult.ResponseBodyMaxBytes`), and
today the whole thing goes into one `whitespace-pre-wrap` `<pre>`. Introduce two thresholds:

| Body size | Rendering |
|---|---|
| < 2 KB | plain `<pre>` — avoids paying CodeMirror setup for a tiny payload |
| 2 KB – 512 KB | CodeMirror with language parsing and folding |
| > 512 KB | CodeMirror with **no** language extension (still virtualized), plus a visible notice — "Highlighting disabled for large responses" — and a button to force it on |

Thresholds live as named constants so the test plan can assert against them rather than magic
numbers.

**Preserve `data-testid="response-body"`** on whichever element holds the text, and keep the
existing hidden-textarea mirror trick that `BodyCodeEditor` uses (`RequestEditor.tsx:119`) if e2e
assertions need to read the content — CodeMirror virtualizes, so off-screen lines are not in the
DOM and a naive `toContainText` on a long body would fail.

---

## Module 4 — Request pane hierarchy

**Files:** `RequestEditor.tsx`

- Replace the standalone request-name row (line 430) with an inline-editable heading: a
  `text-sm font-semibold` span that becomes an input on click or `F2`, sitting on the same row as
  the tab strip, right-aligned against the tab buttons. This removes a full-width form field from
  the visual centre of the pane and stops the name from being the third echo of the same string
  (tree → tab strip → this input).
- Keep `data-testid="request-name-input"` on the input that appears in edit mode.
- Group the URL-bar buttons: `Send` (primary) and `Save` stay together on the right; move the
  variable-preview eye toggle to sit immediately after the URL input as an affordance *of* the URL
  field rather than a peer of Send/Save.
- Show the dirty state as a dot on the Save button rather than mutating the label to `Save*`.

---

## Module 5 — Response pane persistence

**Files:** `ResponseViewer.tsx`, `ApiClientPage.tsx`

### 5.1 Saved examples actually save

`HttpRequestEntry.responseExamples` already exists in the type and in `emptyRequest()`
(`ApiClientPage.tsx:75`), and is already persisted through `updateCollections`. Wire it up:

- `ResponseViewer` gains `onSaveExample(name: string, body: string)` and reads the list from
  `request.responseExamples` instead of local `savedExamples` state.
- `ApiClientPage` implements the callback by patching the active tab draft and calling the existing
  `saveActiveTab()`.
- Clicking a saved example shows it in the body viewer (a `viewing: "live" | exampleId` state in
  `ResponseViewer`) with a visible "Viewing saved example" banner and a return-to-live action. Today
  the buttons render with no `onClick` at all.
- Scrub before persisting, mirroring the documented Blazor behaviour: never store `Authorization`,
  `Set-Cookie`, or values matching `isLikelySecret` from
  [web/src/lib/variable-utils.ts](../../../../web/src/lib/variable-utils.ts).

### 5.2 History survives remount

Move `history` out of `ResponseViewer` local state into the per-tab `TabState` in `ApiClientPage`
(`interface TabState`, line 207) as `history: ResponseHistoryEntry[]`, appended where the response is
already stored in `handleSend`. Keeps the existing 20-entry cap and makes history per-tab rather than
per-mount. Still session-only, matching the documented behaviour.

---

## Module 6 — Documentation

**Files:** `docs/architecture/functionalities/api-client.md`, `docs/architecture/index.md`

`docs/architecture/functionalities/api-client.md` still documents the **Blazor** implementation —
`ApiClientPage.razor`, `ApiClientWorkspace`, `ApiClientTreePanel`, the `ApiClientRequestTabs` user
setting, linked `.swebkit-api` roots — none of which exist in the React app. That mismatch is why the
git gaps went unnoticed.

Within this feature's scope, update:
- The **Core Runtime Flow** tree to the React component graph (`ApiClientPage` → `ResizablePanels` →
  `CollectionTree` / `RequestEditor` / `ResponseViewer`, with `RequestTabStrip` always on rather than
  behind a setting).
- The response-rendering and state-persistence rows for highlighting, saved examples, per-tab
  history, and persisted panel widths.

Leave the git and linked-root sections to [api-client-git-completion](../api-client-git-completion/index.md),
which owns correcting them. Note the split in both plans so neither doc half-rewrites the same file.

---

## Implementation order

1. **Module 2** (badges, tones, formatters) — small, self-contained, immediately visible; unblocks
   consistent colour use in later modules.
2. **Module 3.1 + 3.2** (shared highlight theme, fix request body) — the highest value-per-line
   change in the feature.
3. **Module 1** (layout, persistence, accessibility).
4. **Module 3.3** (response body viewer) — the largest single piece; benefits from 3.1 landing first.
5. **Module 4** (request hierarchy) and **Module 5** (persistence).
6. **Module 6** (docs) — last, so it describes what actually shipped.

Modules 1–2 and 3.1–3.2 are independently shippable; do not batch the whole feature into one commit.
