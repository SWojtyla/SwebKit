# AI-Augmented App — UX Plan

Companion to `technical-plan.md`, which covers the backend/frontend wiring. This document covers
user-facing flows and interaction design. See `index.md` for scope and non-goals.

## Core interaction model

Every feature area gets an "Ask AI" entry point that opens a **contextual assistant panel** —
docked to the side or a flyover, never a full-page navigation away from what the user was doing.
The existing global `/agent` page stays for open-ended questions not tied to something on screen
right now ("what does a Service Bus dead-letter queue actually do"); the contextual panels are for
questions/actions tied to *this pod*, *this queue*, *this key*, *this request*.

## Ask vs. Ask & do

A two-option toggle (segmented control, not a dropdown — both options should be visible and
reachable in one click) at the top of every assistant panel, contextual or global:

- **Ask** (default) — read-only. The assistant can look things up, explain, and reason, but every
  tool call it can make is non-mutating. Nothing about the user's cluster/queue/cache/collection can
  change from an Ask conversation, regardless of what's asked. This is the safe default and should
  stay the default for a fresh conversation even if the user chose Ask & do in a previous one (see
  technical-plan.md Module 5 — no silent sticky "dangerous by default").
- **Ask & do** — the assistant can also *propose* actions. Proposing is never the same as doing:
  every proposal renders as a confirm card (below) that the user must explicitly accept before
  anything happens. Switching to this mode should visually read as "more capable", not "riskier" —
  a distinct accent color/icon, not a warning-red treatmen that would make users avoid a mode that's
  actually safe by design (confirm-gated).

Mode is per-conversation, chosen every time a panel opens (pre-selected from the user's last choice
as a convenience default, per technical-plan.md Module 5, but always visible and one click to
change) — not a global setting buried in Settings.

## The confirm card (the trust-critical surface)

This is the single most important piece of UI in this feature, because it's the only thing standing
between "the model suggested something" and "something actually happened." One shared component
(`PendingActionCard`, technical-plan.md Module 3) used identically everywhere an action can be
proposed:

- **What**: a one-line summary in plain language ("Delete key `session:abc123` from cache
  `prod-cache`", not "ProposeDeleteKeyTool executed with args {...}").
- **Preview**: whatever the action-specific diff/preview is — a request method+URL+headers+body
  diff for API Client, a before/after for a TTL change, a "this cannot be undone" note for
  deletions.
- **Risk**: a visible badge (Low / High, from the existing `ToolRisk` enum) — not a raw enum value,
  a short human label plus a one-line reason where it's not obvious (e.g. "High — this permanently
  deletes data").
- **Actions**: Confirm / Reject, plus a visible expiry (the existing 5-minute
  `PendingAgentAction.ExpiresAt`) so a card the user comes back to after stepping away clearly shows
  whether it's still actionable rather than failing with a confusing error on click.
- **After confirm**: show the apply result inline in the same card (success message, or a clear
  failure reason) rather than just dismissing it — the user needs to know whether their confirmed
  action actually happened.

A **High** risk action (e.g. delete) should require the same one-click Confirm as a **Low** risk
one — this plan deliberately does not add a second "type to confirm" friction step. The
confirmation step itself, plus the visible risk badge and preview, is the safety mechanism; adding
extra friction on top narrows the gap between "Ask & do" and "just do it in the UI yourself" without
adding real safety, and the existing `ToolRisk` design doesn't call for it. Revisit only if real
usage shows accidental confirms are a problem.

## Feature-area entry points

Consistent placement across areas: an "Ask AI" affordance next to whatever the primary detail view
already is for that area (not buried in a menu), so it reads as always-available rather than a
hidden power feature.

| Area | Entry point | Example question |
|---|---|---|
| AKS | Pod/deployment detail panel, next to "Open shell in pod" | "Why is this pod crash-looping?" |
| Service Bus | Entity/message detail | "What's causing messages to land in the DLQ?" |
| Redis | Key detail panel | "Is this key using an unusual amount of memory?" |
| Storage | Blob detail panel | "When was this blob last modified and by what?" |
| API Client | Request editor (see below — a distinct flow, not just chat) | "Generate a request for this endpoint" |
| Monitoring | Alert rule / alert history row | "Explain this alert and suggest a fix" |

### API Client: "generate a request" is not just a chat box

The user's own example — generating a request — deserves a more specific affordance than "open the
generic chat and type a sentence": a small "Ask AI" action directly in the request editor toolbar
that opens a focused, single-purpose prompt ("Describe the request you want") rather than a full
open-ended conversation. The result is still routed through the same Ask & do confirm-card flow as
any other proposed mutation (a generated/edited request is a change to the collection, same as any
other), but the entry UX is purpose-built rather than generic, since this is likely to be the
highest-frequency "do" action in the whole feature.

## Local-model UX guardrails

Since a local LM Studio model may have a small context window or unreliable tool-calling:

- If a profile's capability test (technical-plan.md Module 1) reports `ChatOnly` (no tool calling),
  don't silently hide the Ask & do toggle — show it disabled with a one-line reason ("This model
  doesn't support tool calling — switch models or use a cloud profile to enable Ask & do") so the
  user understands why, rather than wondering where the feature went.
- If a capability test has never been run for the active profile (`Unknown`), show a small inline
  prompt to run it before relying on tool-calling-dependent behavior, rather than failing silently
  mid-conversation.

## Non-goals for this UX pass

- No streaming/typing-indicator token-by-token rendering yet (technical-plan.md Module 8, stretch).
- No persistent per-action audit log UI yet (see `index.md`'s "Candidate future enhancements" —
  candidate future work, not scoped here).
- No redesign of the existing global `/agent` page's layout beyond adding the mode toggle and
  markdown rendering — it keeps its current place in navigation and the command palette.
