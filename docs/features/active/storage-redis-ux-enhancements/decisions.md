# Decisions - storage-redis-ux-enhancements

---

title: "Decisions - storage-redis-ux-enhancements"
owner: "GitHub Copilot"
status: "Review"

---

## Decision 001 - Use real byte progress for blob downloads

**Status:** Accepted

**Date:** 2026-04-10

### Context

Current storage downloads only report success or failure after the transfer finishes. Large blobs can take long enough that the page looks stalled.

### Decision

Expose additive byte-progress reporting from the storage client and render real in-flight progress in the UI instead of a timer-based or spinner-only approximation.

### Consequences

- Progress reflects actual transfer work instead of guessed elapsed time.
- The storage contract grows slightly, but only in the download path.
- UI code must avoid rendering on every byte update.

### Alternatives considered

- Alternative A - keep the existing success-only messaging: rejected because it does not address the user-visible stall.
- Alternative B - show an indeterminate spinner only: rejected because it still hides how far a large transfer has progressed.

---

## Decision 002 - Keep storage download improvements local to existing action surfaces

**Status:** Accepted

**Date:** 2026-04-10

### Context

The request is for better download feedback, not a new background transfer manager.

### Decision

Show download progress inline where the download was started, keep the destination as the Downloads folder, and do not introduce a cross-page download queue in this feature.

### Consequences

- Scope stays small and aligned with the current architecture.
- Users keep the same workflow and only gain better visibility.
- Background continuation across navigation stays out of scope.

### Alternatives considered

- Alternative A - add a global download center: rejected because it is larger than the requested UX change.
- Alternative B - push progress into toast notifications only: rejected because inline state is easier to trust and relate to the initiating action.

---

## Decision 003 - Replace page-level Redis purge with selection-first bulk delete

**Status:** Accepted

**Date:** 2026-04-10

### Context

The Redis page currently exposes a direct `Purge All` action even though the page already has multi-select and chunked batch delete support.

### Decision

Remove the primary purge-all action from the Redis page UX and replace it with helper actions that speed up selection, then reuse the existing selected-keys delete confirmation flow.

### Consequences

- Destructive scope becomes visible and reviewable before delete.
- Existing delete plumbing stays useful and becomes the canonical cleanup path.
- Full database flush remains available only outside this page flow, if still needed internally.

### Alternatives considered

- Alternative A - keep purge-all and add a stronger confirmation: rejected because the requested direction is replacement, not a slightly harder flush.
- Alternative B - add a second destructive prefix-delete action: rejected because it preserves hidden delete scope and duplicates existing batch delete behavior.

---

## Decision 004 - Tree helpers act on loaded descendants only

**Status:** Accepted

**Date:** 2026-04-10

### Context

Redis scanning is paged and may not represent the full database at all times. A subtree helper that silently expands beyond loaded keys would be misleading and risky.

### Decision

All full-select and subtree-select helpers operate only on keys currently loaded into the page tree, and the UI must show explicit counts before delete.

### Consequences

- Selection behavior matches what the user can inspect in the UI.
- No hidden SCAN plus delete pass is introduced behind a convenience action.
- The page should remain explicit when more keys are available but not yet loaded.

### Alternatives considered

- Alternative A - server-side prefix delete based on the selected subtree: rejected because it recreates flush-like risk and hides scope.
- Alternative B - silently include unloaded keys under the same prefix: rejected because the user cannot verify what will be deleted.
