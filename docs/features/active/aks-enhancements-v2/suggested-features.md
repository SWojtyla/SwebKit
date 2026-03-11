# Suggested Features - AKS Enhancements v2

---

title: "Suggested Features - AKS Enhancements v2"
owner: ""
status: "For Review"
created: "2026-03-11"

---

These are additional features that could add value to the AKS experience. They are listed here for review and consideration — not committed to this phase.

## Deployment favorites / pinned resources

**What:** Star/pin specific deployments or pods to always show them at the top of the list, or in a separate "Favorites" section.
**Why:** Most developers care about 2-5 deployments in a namespace with 50+. Pinning reduces noise.
**Effort:** Medium — requires persistence in `AksConfig.WatchedDeployments` (already exists but unused) and UI for pin/unpin.

## Events timeline view

**What:** Replace or augment the events list with a timeline/swimlane visualization grouped by involved object.
**Why:** The current event list is chronological but hard to correlate across objects. A timeline view groups events per object.
**Effort:** High — requires custom timeline component with time axis rendering.

## Copy resource as kubectl command

**What:** Context menu action "Copy as kubectl" that generates the equivalent kubectl command for the current view/action.
**Why:** Useful for sharing commands with team members or scripting. Bridges the gap between GUI and CLI.
**Effort:** Low — string template generation per resource type/action.

## Diff between Helm revisions

**What:** Side-by-side diff view between two Helm revisions showing what changed in values or manifests.
**Why:** Before rollback, developers want to see what changed between the current and target revision.
**Effort:** High — requires decoding two revisions and a diff rendering component.
