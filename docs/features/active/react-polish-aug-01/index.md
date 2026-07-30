# React/TAuri UX parity and performance follow-up

## Scope

This follow-up addresses the gaps found after the previous MAUI → Tauri/React parity batch landed in `main`:

1. **Redis** – the namespace tree was removed and the separator stopped doing anything useful. Restore a single tree view as the key list.
2. **Storage** – selecting a file that lives in a virtual folder (blob name contains `/`) returns 404 because the sidecar route treats the slash as a path separator.
3. **Feedback** – actions such as Settings export/import and AKS scale/restart give no visible confirmation or error toast.
4. **AKS actions** – scale/restart mutations do not always refresh the table the user is looking at because they invalidate the wrong `react-query` key.
5. **AKS performance** – every tab/refresh triggers a fresh namespace list and, for `*`, a per-namespace fan-out that can be reduced.

## Status

`Planned`

## Requirements

- Redis key browser is a single, separator-driven namespace tree (expand/collapse) with load-more / load-all and a key count.
- Storage blob-scoped endpoints accept `blobName` via query/body so slashes are preserved.
- Settings export/import and all AKS/Storage mutations emit a success or error in-app notification.
- AKS actions refresh the currently visible table reliably.
- AKS sidecar caches namespace list results and uses cluster-scoped list APIs for `*` when available.

## Related docs

- `technical-plan.md` – implementation plan with symbols and control/data flow.
- `docs/features/active/ux-followup-july-27/index.md` – previous parity batch.
