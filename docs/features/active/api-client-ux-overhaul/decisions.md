# Decisions — API Client UX Overhaul

## DEC-1 — CodeMirror highlighting is driven by CSS custom properties, not JS colour values

**Decision.** The shared `HighlightStyle` references `var(--cm-*)` custom properties defined per
theme class in `globals.css`, rather than literal colour strings.

**Why.** SwebKit has three themes (`light`, `dark`, `fancy`) applied as a class on
`document.documentElement` by `useSettingsStore`. A JS-valued `HighlightStyle` is baked into the
`EditorState` at creation, so a theme switch would require either subscribing to the theme store and
reconfiguring every mounted editor via a `Compartment`, or destroying and recreating the view. Both
are more code and both risk losing editor state (cursor, scroll, undo history) on a theme toggle.
CSS custom properties are re-resolved by the browser when the root class changes, so a single static
extension serves all three themes and any future theme, at zero runtime cost.

**Consequence.** Adding a theme means adding a `--cm-*` block, never touching TypeScript. The hues
are deliberately shared with the existing `.yml-*` tokens so the API Client and the AKS YAML viewer
read as one system.

**Rejected.** A `Compartment` reconfigured from a `useSettingsStore` subscription — correct but
strictly more moving parts for no gain. Pre-built themes such as `@codemirror/theme-one-dark` — would
put a fourth, unrelated palette next to Aurora and would not follow the `fancy` theme at all.

---

## DEC-2 — Response bodies use a read-only CodeMirror view, not a hand-rolled tokenizer

**Decision.** Render response bodies through a read-only `EditorView` rather than extending the
`yamlHighlight.ts` string-tokenizer approach to JSON/XML.

**Why.** The tokenizer approach is lighter and would match the AKS viewer exactly, but it produces a
single flat HTML string with one `<span>` per token and no virtualization. The sidecar caps response
bodies at 4 MB (`HttpRequestResult.ResponseBodyMaxBytes`), and today's `<pre>` already puts the whole
body in the DOM with `whitespace-pre-wrap`. Scaling that to per-token spans makes the worst case
worse. CodeMirror is already a dependency, virtualizes by viewport, and brings folding, in-body
search, and line numbers — all of which a response viewer genuinely needs and none of which the
tokenizer can offer.

**Consequence.** `@codemirror/search` becomes a new direct dependency. E2E assertions cannot rely on
the full body being present in the DOM, because CodeMirror only renders the visible viewport — see
[test-plan.md](test-plan.md) for how the existing `response-body` assertions are kept working.

**Rejected.** Extending `yamlHighlight.ts` — good consistency, wrong scaling behaviour. Monaco —
far heavier, and the repo already chose CodeMirror.

---

## DEC-3 — Small bodies keep the plain `<pre>` path

**Decision.** Bodies under 2 KB render as a plain `<pre>`; CodeMirror mounts only above that.

**Why.** Most API responses during interactive debugging are small, and paying editor construction
for a 40-line JSON object is a regression in perceived responsiveness on every Send. The threshold
also preserves the simplest possible DOM for the majority of the existing e2e assertions.

**Consequence.** Two rendering paths must both apply highlighting consistently. The `<pre>` path gets
highlighting via the same `--cm-*` tokens through a small span-based formatter, so the two paths look
identical — this is the one place the tokenizer approach from DEC-2 is still the right tool.

---

## DEC-4 — Panel widths are fractions of leftover space, not percentages of the container

**Decision.** `ResizablePanels` accepts `"1fr"`-style entries meaning "a share of the space left
after fixed-width panels", not a percentage of total container width.

**Why.** The collections tree wants to stay roughly fixed — its content width does not scale with the
window. Only the request/response split should absorb extra width. Percentages of the total would
grow the tree on wide monitors, which is the opposite of what is wanted, and would fight the
`minWidths` clamping already in the drag maths.

**Consequence.** `toNumber` gains an `fr` branch and the container-measure effect divides the
remainder. Drag maths is unchanged: it continues to operate on resolved pixel widths.

---

## DEC-5 — Method short labels are an explicit map, not string truncation

**Decision.** A `METHOD_META` table supplies each method's display label.

**Why.** `method.toUpperCase().slice(0, 4)` produces `DELE`, `PATC`, `OPTI`, `GRAP`, `WEBS` — every
one of which reads as a rendering bug rather than an abbreviation. An explicit map gives conventional
short forms (`DEL`, `OPT`, `GQL`, `WS`) and keeps `PATCH` unabbreviated, since it fits.

**Consequence.** Adding a method to `ApiRequestMethod` requires adding a `METHOD_META` entry.
The record is typed as `Record<ApiRequestMethod, …>` so a missing entry is a compile error rather
than a silent fallback — this is deliberate, and is why the type is not `Partial`.

---

## DEC-6 — Panel width persistence is versioned and falls back to new defaults

**Decision.** The stored record carries a `version`; a mismatched version or panel count is discarded
rather than migrated.

**Why.** This feature changes the defaults from `[260, 540, flex]` pixels to `[300, 1fr, 1fr]`. A
user who has previously dragged their panels would otherwise be restored into the old cramped
proportions and never see the fix.

**Consequence.** The one-time reset is intentional and acceptable for a UI preference. Follows the
defaults-merge pattern already in `sb-preferences.ts`, which tolerates unknown/missing keys the same
way.
