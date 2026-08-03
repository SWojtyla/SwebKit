# Workspace Intelligence — UX Plan

Companion to `technical-plan.md`. See `index.md` for the Application Insights/Observability decision
still needed and overall scope.

## The Map view (Module 1/2)

A new page, reachable from Settings (not a top-level nav item for v1 — this is configuration, used
occasionally, not a daily-driver page like AKS/Service Bus). Two panes:

- **Left: known resources**, grouped by area (AKS/Service Bus/Redis/Storage), pulled from what's
  already configured/browsed — nothing to type, just what the app already knows about.
- **Right: relationships**, a simple table (From → label → To → remove), plus an "Add relationship"
  row (pick two resources from the left pane, optional free-text label).

Once Module 2's heuristic suggestions exist, suggested-but-unconfirmed relationships appear in a
visually distinct "Suggested" section above the confirmed table — same row shape, but with
Confirm/Dismiss instead of just Remove, so it never reads as already-true information.

No graph-rendering/force-directed visualization for v1 (per the non-goal) — the table is more
honest about what's actually known (a short, curated list) than a graph layout would be for what's
likely to start as a handful of relationships.

## Cross-area investigation (Module 3)

Inside any contextual assistant panel (from `ai-augmented-app`), an additional toggle/button —
"Search across my whole workspace" — next to the existing Ask/Ask & do mode toggle. Turning it on
for a turn doesn't change Ask vs. Ask & do; it changes which *read* tools are available, orthogonal
to the mutate-gating mode. Visually this should read as "look wider," not as a third mode alongside
Ask/Ask & do — it's a scope toggle, not a safety toggle.

When the assistant uses this to pull in another area's data, say so in the reply's reasoning trace
(Module 6) at minimum, and ideally inline too ("checked `orders-queue` since it's linked to this
deployment") — the point of the topology model is exactly to make correlated answers feel earned,
not to silently widen scope with no indication anything beyond the current page was touched.

## Proactive insights (Module 4)

A dismissible card, visually distinct from a regular chat message and from a regular alert-history
row — it's neither. Short, scannable: what fired, a one-line generated hypothesis, an "Investigate"
button. Placement: near the existing alert surfaces (dashboard, Monitoring page) rather than
interrupting whatever page the user happens to be on when it fires — this is meant to be there when
relevant, not to pop over active work.

Explicitly avoid over-alerting: if the global rate limit (technical-plan.md Module 4) means some
firings don't get a proactive insight, that's fine and expected — the alert itself still fires and
appears in alert history as it does today; the AI insight is a bonus layer on top, not a replacement
for the existing alert surface, and its absence for a given firing should never look like a bug.

## Context-usage indicator and reasoning trace (Module 6)

- A small, unobtrusive usage indicator (e.g. a thin bar or a "62%" label) near the conversation
  input, always visible but never demanding attention — until it crosses into a "getting full"
  state (which should coincide with Module 5's summarization threshold), at which point it can use a
  slightly more noticeable color, still not an alarming one — summarization is the system handling
  it gracefully, not a failure state.
- Each assistant reply gets a small "Show reasoning" disclosure, collapsed by default. Expanded, it
  shows: which tools were called (human labels, not raw tool names — reuse the label mapping pattern
  already used for Service Bus entity actions), a short preview of each result, and timing. This is
  aimed at two audiences: a user trying to understand why an answer looks the way it does, and a
  developer debugging a local model's flaky tool-calling behavior — both need the same information.
- When summarization fires, a clearly-labeled system-style message appears inline in the
  conversation ("Earlier parts of this conversation were summarized to stay within the model's
  context window") — placed exactly where the summarization happened chronologically, not just as a
  banner at the top, so it's clear what got compressed and what's still verbatim.

## Local-model guardrails (Module 7)

Extends `ai-augmented-app` `ux-plan.md`'s existing local-model guardrail language: the same
"disabled with a one-line reason" pattern used there for the Ask & do toggle applies to the
"search across my workspace" escalation when tool calling isn't available, and the context-usage
indicator's warning threshold should visibly reflect a small declared/detected context window (i.e.
a local model with a 4k window should show "getting full" sooner, in absolute conversation length,
than a cloud model with a 128k window — the percentage is what's shown, but it should feel
proportionate to what a user actually experiences turn to turn, not identical across wildly
different window sizes).

## Implementation notes (2026-08-03) — read alongside this plan, not instead of it

Modules 1-6 are done (see `status.md`'s "Handoff" note and per-module entries, `technical-plan.md`
for exact detail). What actually shipped matches this UX plan closely, with a few real, documented
deviations worth knowing before assuming the design below is exactly what's in the app:

- The Map view is a **Settings tab**, not a standalone page — same two-pane layout as planned
  (known resources by area on the left, relationships table on the right), plus a manual "add a
  custom resource" form the plan didn't explicitly call out (needed for resources finer-grained than
  the auto-populated candidates, e.g. a specific queue name).
- Module 2's suggestions render as planned (a dashed-border "Suggested — confirm?" section, separate
  from the confirmed table), but **dismissal is session-only client state**, not persisted — a real
  scope decision, since nothing in the plan required durable dismissal for suggestions specifically
  (unlike Module 4's proactive insights, which do persist dismissal, via `sessionStorage`).
- Module 3's "search across my whole workspace" toggle is a **checkbox**, not a separate button,
  placed inline with the existing mode radio group in `ContextualAssistant.tsx` rather than as its
  own distinct control — same "look wider, not a third mode" intent, different exact widget.
- Module 4's "Investigate" button does **not** open the specific backend-seeded session the plan
  implies — it injects the generated summary into the existing global agent conversation and
  navigates to `/agent` instead, since neither `AgentPage.tsx` nor `GlobalAgentPanel.tsx` support
  viewing an arbitrary session today, and building that viewer would have been a separably-sized
  feature. The seeded session still exists and is reachable via the API. Also only wired into the
  Monitoring page, not also the Dashboard, as a first pass.
- Module 6's usage indicator and reasoning trace match this plan closely: a small "· NN% of context
  window" label (warning-colored at ≥75%, matching Module 5's own summarization threshold) and a
  collapsed-by-default "Show reasoning (N steps)" disclosure, both shared across all three chat
  surfaces. The "lower priority" raw request/response inspector was deliberately not built — flagged
  as a real follow-up in `technical-plan.md`, not silently dropped.

## Non-goals for this UX pass

- No dedicated "workspace intelligence" top-level nav item — the Map view lives in Settings, and
  everything else extends existing `ai-augmented-app` surfaces rather than introducing new ones.
- No graph visualization (per `technical-plan.md`'s non-goal).
- No persistent, browsable history of past proactive insights beyond in-session dedup — see
  `ai-augmented-app`'s candidate enhancement on a durable action audit log, which this could
  piggyback on later but isn't scoped here.
