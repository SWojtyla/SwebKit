# Decisions — AKS Enhancements (Batch 2)

---

title: "Decisions — AKS Enhancements Batch 2"
owner: ""
status: "Done"

---

## Decision 001 — Unified flex column inside one ResizablePanel

**Status:** Accepted (revised 2026-03-17)

**Date:** 2026-03-17

### Context

The AKS page originally used `ResizablePanel` components as individual CSS grid children.
When a user opened more than two panels simultaneously (e.g. YAML viewer + HPA detail +
events), the third grid child overflowed onto a new row below the main content, breaking
the layout. The initial batch 2 fix replaced all `ResizablePanel` usages with a plain
`<div class="aks-panels-col">`, which fixed overflow but removed user-resizable width —
regressed UX for long YAML content.

### Decision

Wrap the `aks-panels-col` div inside a single `<ResizablePanel>` component. All
individual panels remain `<div class="aks-panel-pane">` flex children inside the column.
The `ResizablePanel` drag handle sets the width of the outer container; the inner panels
fill it. Width defaults to 420px, min 280px, max 900px.

### Consequences

- Layout remains two-column maximum (`1fr auto`), overflow bug stays fixed
- Drag-resize is restored for the entire right column in a single drag handle
- `ResizablePanel` is back in use by `AksPage`

### Alternatives considered

- **Per-panel ResizablePanel (original)** — individual drag handle per panel; overflow bug returns
- **CSS resize handle only** — would require writing new drag logic; `ResizablePanel` already works

---

## Decision 002 — Events as a peer panel pane, collapsed tab when alone

**Status:** Accepted (revised 2026-03-17)

**Date:** 2026-03-17

### Context

Events were previously a fixed 320px column always visible unless explicitly collapsed.
The batch 2 initial implementation moved events to a collapsible inset at the bottom of
the panel column. This was rejected in review: events at the bottom only takes half the
available space, has a constrained scrollable area, and feels disconnected from the other panels.

### Decision

Events is treated as a full peer `aks-panel-pane` inside the column, visible when
`ShowEvents = true`, just like YAML or logs. It takes `flex: 1` and fills the full column
height. When no other panel AND events are closed, the thin `aks-events-collapsed-tab`
vertical strip appears on the right edge. Default state remains `ShowEvents = false`.

### Consequences

- Events is consistent in UX with all other panels
- Users get the full column height for events, not half
- The `aks-events-inset` CSS classes are removed; the inset toggle header is removed
- `HasAnyPanel` still correctly covers `ShowEvents = true`

### Alternatives considered

- **Events pinned at bottom, always visible** — wastes space when empty; rejected
- **Bottom inset (batch 2 initial)** — half-height, cramped scroll area; reported as regression by user and rejected
  investigate, without expanding.

When no panel is open at all, a thin vertical `aks-events-collapsed-tab` strip appears
on the right edge as a shortcut to open events.

### Consequences

- Events are still accessible but no longer occupy permanent space
- `HasAnyPanel` replaces `HasOpenPanel` as the condition for the side column and the
  auto-refresh pause, so auto-refresh pauses while events are expanded too
- Warning badge in the inset header provides passive awareness without needing expansion

### Alternatives considered

- **Move events to a bottom drawer below the main grid** — would require a horizontal split layout; more disruptive to the shell
- **Keep events as a persistent column** — does not fix the layout overflow and wastes space when events are empty

---

## Decision 003 — YAML search implemented in JS, not in Blazor

**Status:** Accepted

**Date:** 2026-03-17

### Context

YAML content is rendered as highlighted HTML inside a `<pre>` element via JSInterop
(the existing `yamlHighlight.js`). Adding search in Blazor would require re-running
the highlight pass with search annotations on every keystroke, triggering a full
Blazor render cycle and a DOM replacement each time.

### Decision

Implement search in `yamlHighlight.js` as `searchInPre(preEl, query)`. It walks
existing DOM text nodes inside the `<pre>`, wraps matches with `<mark>` elements, and
scrolls the first match into view — all without touching Blazor state. Blazor receives
only the integer match count back to display in the search bar.

### Consequences

- Keystroke response is near-instant (pure DOM manipulation, no Blazor round-trip)
- Match highlighting survives without requiring a re-highlight of the YAML
- The `_yamlViewPre` ElementReference must be passed to JS; it is only valid after the
  YAML `<pre>` is rendered, so search is disabled while `YamlLoading` is true

### Alternatives considered

- **Rebuild highlighted YAML string with `<mark>` injected in C#** — would couple search logic to the YAML tokeniser and force a full re-render per keystroke
- **Browser `window.find()`** — not available in MAUI WebView; non-standard and unreliable

---

## Decision 004 — Ingress URL inferred from host rule, not from TLS annotations

**Status:** Accepted

**Date:** 2026-03-17

### Context

Kubernetes Ingress objects carry host rules but no explicit `http`/`https` flag per rule.
The scheme must be inferred.

### Decision

`BuildIngressUrl(IngressInfo, string host)` (static helper) applies a simple heuristic:

- If `host` looks like a bare IP address → `http://`
- Otherwise → `https://`

The full URL is built as `scheme + host`. The method is `static` so it is testable
without instantiating the page.

### Consequences

- Fast and simple; no TLS annotation parsing required
- Will produce `https://` for named hosts even if they don't actually serve TLS —
  acceptable because the OS browser will handle the redirect or the user can copy
  the URL and edit it
- Does not respect `ingress.kubernetes.io/ssl-redirect: "false"` annotation

### Alternatives considered

- **Parse `nginx.ingress.kubernetes.io/ssl-redirect` annotation** — annotation is nginx-specific; not portable across ingress controllers
- **Always use `http://`** — wrong for the majority of production ingresses

---

## Decision 005 — Pod metric bars scaled to column maximum, no limits API call

**Status:** Accepted

**Date:** 2026-03-17

### Context

The user wants a visual indicator showing "usage vs limit/max" in the Pods overview grid.
CPU and memory limits are available via `ContainerDetail` (a separate on-demand API call)
but are not part of `PodMetrics` (the lightweight metrics API). Adding a per-pod limits
call in the overview grid would be N additional API calls on every refresh.

### Decision

Render a thin horizontal mini-bar alongside each metric value. The bar is scaled relative
to a soft cap: CPU bar is 0–500m; memory bar is 0–512Mi. These values correspond to
typical workload limits and match the existing colour thresholds (green/amber/red).
The bar fills proportionally, capped at 100%.
Label shows the numeric value. No limit data is fetched; the bar conveys relative load, not absolute usage vs limit.

### Consequences

- No extra API calls; renders with data already in hand
- Bar gives instant relative-load signal without precise limit accuracy
- When exact limit comparison is needed, the Container Details panel (right-click) still shows requests and limits per container

### Alternatives considered

- **Fetch limits for all pods on load** — N extra API calls; slows page load and refresh
- **Show only colour, no bar** — existing behaviour; user explicitly asked for visual diff
- **Show bar only when ContainerDetails cached** — inconsistent; most pods won't have cached detail
