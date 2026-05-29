# Redis UX Rework Test Plan

## Focused Automated Checks

- Redis toolbar component tests cover scan, selection summary, select-all, clear, and delete-selected visibility.
- Redis namespace tree node tests cover explicit checkbox selection, namespace subtree selection, row expansion, and key opening.
- Redis key detail tests cover full value rendering and copy actions when feasible.

## Manual Checks

- Open Redis in demo mode and confirm the key tree renders with visible selection controls.
- Click a key label and confirm detail opens without changing selection.
- Select individual keys and a namespace subtree, then clear selection.
- Open a long string value and confirm it can be scrolled and copied.
- Confirm health, memory, and ops insights do not consume default vertical space until expanded.
