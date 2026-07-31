---
status: In Progress
---

# Storage detail panel overflow fix

## Objective
Prevent the Storage blob detail panel from showing a horizontal scrollbar when blob properties, metadata, versions, or content contain long lines. All detail tabs should display nicely within the available width.

## Scope
- `web/src/components/storage/StoragePage.tsx`

## Acceptance criteria
- The blob name header wraps instead of overflowing.
- Metadata and versions tables stay within the panel width; long keys/values wrap or truncate with `break-all`.
- Content `<pre>` wraps long lines instead of overflowing horizontally.
- Version diff `<pre>` wraps and stays within bounds.
- Existing Playwright storage tests continue to pass.
