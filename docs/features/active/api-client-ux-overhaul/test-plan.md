# Test Plan — API Client UX Overhaul

## Testing infrastructure — a gap this feature has to close

`web/` currently has **no unit test runner**. There is no Vitest or Jest config anywhere in the
repository, and no `*.test.ts` / `*.spec.ts` file under `web/src`. All frontend testing is Playwright
e2e in `web/e2e/` (18 tests in `api-client.spec.ts`, 6 in `api-client-deferred.spec.ts`, 2 git-panel
tests in `git-notifications.spec.ts`). Unit tests exist only for .NET under `tests/`.

This feature introduces several genuinely pure, branch-heavy functions — byte/duration formatting,
content-type → language selection, size-threshold selection, versioned preference load, fractional
width resolution — that are wasteful and slow to cover through a browser.

**Required as part of this feature:** add Vitest to `web/`.

```bash
npm --prefix web install -D vitest @vitest/coverage-v8
```

- `vitest.config.ts` reusing the existing Vite config's `@` alias (see
  [web/vite.config.ts](../../../../web/vite.config.ts)); `environment: "node"` is sufficient — none of
  the units under test touch the DOM.
- `"test:unit": "vitest run"` and `"test:unit:watch": "vitest"` in `web/package.json`.
- Tests colocated as `<module>.test.ts` next to the module.

No component-level React testing library is proposed: component behaviour stays with Playwright,
which already exercises it against the real app. This keeps the new infrastructure to one runner and
zero DOM shims.

---

## Unit tests (Vitest)

### `web/src/lib/api-client-format.test.ts`

| Case | Expectation |
|---|---|
| `formatBytes(-1)` | `"—"` — the sidecar's "unknown length" sentinel, today rendered as `size unknown` |
| `formatBytes(0)` | `"0 B"` |
| `formatBytes(512)` | `"512 B"` |
| `formatBytes(1536)` | `"1.5 kB"` |
| `formatBytes(4 * 1024 * 1024)` | `"4.0 MB"` — the sidecar body cap |
| `formatElapsed(190)` | `"190 ms"` |
| `formatElapsed(999)` | `"999 ms"` |
| `formatElapsed(2400)` | `"2.4 s"` |
| `formatElapsed(0)` | `"0 ms"`, not `""` or `"—"` |

### `web/src/components/api-client/method-badge.test.ts`

| Case | Expectation |
|---|---|
| Every member of `ApiRequestMethod` has a `METHOD_META` entry | Enumerate the `methods` array from `RequestEditor` and assert no `undefined` — guards DEC-5's exhaustiveness at runtime as well as compile time |
| Short labels | `Delete → "DEL"`, `Options → "OPT"`, `GraphQl → "GQL"`, `WebSocket → "WS"`, `Patch → "PATCH"` |
| No label is a truncated word | No label is a strict prefix of its method name unless the method name is that short — regression test for `slice(0, 4)` |
| Tone mapping | `Get → info`, `Post → success`, `Delete → destructive`, `Head`/`Options → neutral` |

### `web/src/components/api-client/response-language.test.ts`

| Case | Expectation |
|---|---|
| `contentType: "application/json"` | JSON language |
| `contentType: "application/problem+json"` | JSON language |
| `contentType: null`, body starts `{` | JSON language (body sniff) |
| `contentType: null`, body starts `[` | JSON language |
| `contentType: "text/html"` | XML language |
| `contentType: null`, body starts `<` | XML language |
| `contentType: "text/plain"`, body `hello` | no language |
| `contentType: "application/octet-stream"` | no language (hex-encoded binary from the sidecar) |
| Empty body, any content type | no language, no throw |

### `web/src/components/api-client/body-render-mode.test.ts`

Asserts the DEC-3 / Module 3.3 thresholds by name, not by magic number:

| Body length | Mode |
|---|---|
| `0` | `"pre"` |
| `PRE_MAX_BYTES - 1` | `"pre"` |
| `PRE_MAX_BYTES` | `"codemirror"` |
| `HIGHLIGHT_MAX_BYTES - 1` | `"codemirror"` |
| `HIGHLIGHT_MAX_BYTES` | `"codemirror-plain"` |
| `4 MB` | `"codemirror-plain"` |
| Forced-on override at 4 MB | `"codemirror"` |

### `web/src/lib/stores/panel-preferences.test.ts`

| Case | Expectation |
|---|---|
| Round-trip save then load | Same widths returned |
| Missing key | `null` (caller applies defaults) |
| Malformed JSON | `null`, no throw |
| Stored `version` older than current | `null` — DEC-6's intentional reset |
| Stored panel count ≠ expected count | `null` |
| `localStorage.setItem` throws (quota/private mode) | Swallowed, matching `sb-preferences.ts` |

### `web/src/components/ui/resizable-widths.test.ts`

The `fr` resolution logic must be extracted from the component into a pure function to be testable:

| Case | Expectation |
|---|---|
| `[300, "1fr", "1fr"]`, container 1000, resizer 6px | `[300, ~347, ~347]` — leftover split evenly, resizer width deducted |
| `[300, "2fr", "1fr"]`, container 1200 | Request pane gets twice the response pane's leftover share |
| Leftover smaller than the sum of `minWidths` | Every panel at least its `minWidth`; overflow scrolls rather than collapsing a pane to zero |
| `null` entry | Treated as `"1fr"` |
| Legacy numeric-only input `[260, 540, null]` | Still resolves — backward compatibility for other callers of `ResizablePanels` |

---

## E2E tests (Playwright)

All run in demo mode via the existing `setDemoMode` helper in
[web/e2e/helpers.ts](../../../../web/e2e/helpers.ts). New tests extend
`web/e2e/api-client.spec.ts`; a new `api-client-layout.spec.ts` holds the layout group.

### Regression guard — must run first

**The existing 18 + 6 API Client tests must pass unchanged.** No `data-testid` may be renamed or
removed. Highest-risk assertions:

- `response-body` — moves from a `<pre>` to a CodeMirror view above 2 KB. CodeMirror renders only the
  visible viewport, so any `toContainText` against a long body will fail on off-screen content.
  **Mitigation:** keep the `data-testid="response-body"` on the container and mirror the full text
  into a hidden, `aria-hidden` element exactly as `BodyCodeEditor` already does at
  `RequestEditor.tsx:119`. Audit each existing assertion against the demo `/posts` response, which is
  well over 2 KB and therefore lands on the CodeMirror path.
- `request-body-editor` / `request-body-codemirror` — the hidden-textarea mirror must survive the
  extension changes in Module 3.2.
- `request-name-input` — must still resolve once the field becomes click-to-edit; the test may need
  a preceding click on the new heading.
- `response-pretty-toggle` — becomes a two-segment control; keep the testid on the element that
  toggles to pretty.

### Layout

| Scenario | Assertion |
|---|---|
| Open the API Client at 1920×1080 | Response pane width is within 15% of the request pane width — the core "response is XXL" regression guard |
| Open at 1280×800 | All three panes at or above their `minWidths`; no horizontal page scrollbar |
| Drag `resizer-1` left by 200 px | Request narrows, response widens by the same amount; total unchanged |
| Reload after dragging | Widths restored from `localStorage` |
| Clear `localStorage`, reload | Widths back to defaults, response ≈ request |
| Double-click `resizer-1` | Pair resets to the default proportion |
| Focus `resizer-0`, press `ArrowRight` ×4 | Tree widens by 64 px; `aria-valuenow` updates |
| Drag across the response body | No text selection artifacts (`window.getSelection()` empty after drag) |
| Drag `resizer-0` far left | Tree clamps at 220 px, never collapses |

### Colour and badges

| Scenario | Assertion |
|---|---|
| Tree with GET/POST/PUT/PATCH/DELETE requests | Badges read `GET` `POST` `PUT` `PATCH` `DEL` — never `DELE` or `PATC` |
| Same in the tab strip and method select | Identical labels in all three surfaces |
| Toggle to `light`, then `fancy` | Method badge colour changes with the theme (computed `color` differs per theme) — proves tokens, not hardcoded Tailwind |
| Send a request returning 200 | Status pill uses the `success` tone |
| Send a request returning 404 | `warning` tone |
| Send a request to an unreachable host | `ERROR` pill, `destructive` tone |
| Response status bar | Size reads `1.2 kB`-style, not `1234 bytes`; unknown length reads `—` |

### Syntax highlighting

| Scenario | Assertion |
|---|---|
| Send `GET /posts` in dark theme | At least three distinct computed text colours inside the response body — the direct test for "no syntax highlighting" |
| Same in `light` and `fancy` | Still ≥3 distinct colours, and each has ≥4.5:1 contrast against the pane background |
| Request body set to JSON in dark theme | ≥3 distinct colours — regression test for the invisible `defaultHighlightStyle` |
| Response with `Content-Type: application/xml` | XML tokens highlighted; JSON-only tokens absent |
| Response with `Content-Type: text/plain` | Renders, single colour, no crash |
| Toggle Wrap | Long lines wrap / stop wrapping; setting persists across reload |
| Click Download on a JSON response | Download event fires with a `.json` filename |
| Fold a JSON object in the response | Collapsed range hides its children |
| `Ctrl+F` inside the response body | CodeMirror search panel opens, scoped to the body, not the browser find |

### Response persistence

| Scenario | Assertion |
|---|---|
| Send, Save Example as `"happy path"`, navigate away and back | Example still listed |
| Click a saved example | Body switches to the saved content; a "viewing saved example" banner and a return-to-live action appear |
| Save an example from a response with an `Authorization` request header | Persisted example contains no secret value |
| Send three requests in one tab, switch tabs and back | History shows 3 entries — regression test for history dying on remount |
| Two tabs, send in each | Each tab's history is independent |
| Send 25 times | History caps at 20 |

### Accessibility

| Scenario | Assertion |
|---|---|
| Tab through the request pane | Focus order: method → URL → variable preview → Send → Save → tabs; every stop has a visible focus ring |
| Resizers | Reachable by keyboard, exposed as `separator` with an accessible name |
| Read-only response editor | Not announced as an editable textbox |

---

## Manual verification

Run the desktop app (`npm --prefix web run tauri dev`) — not just the browser dev server, since the
Tauri window has different chrome and the panel maths must hold there too:

1. Maximize on the widest available monitor. The response pane must not dominate. This is the
   original complaint and the primary acceptance check.
2. Cycle all three themes with a JSON response and a JSON request body on screen. Confirm both are
   readable and consistent in each.
3. Send a deliberately large response (an AKS log dump or a large public JSON API) and confirm the
   large-body notice appears, scrolling stays smooth, and forcing highlighting on works.
4. Restart the app and confirm panel widths and the wrap setting survived.

## Out of scope for this plan's tests

- Git panel behaviour — covered by
  [api-client-git-completion/test-plan.md](../api-client-git-completion/test-plan.md).
- Capture rules, variable generators, command-palette integration — still open findings in
  [post-migration-ux-review](../post-migration-ux-review/status.md), untouched here.
