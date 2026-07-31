# AKS Internal Refactor — shared table, workspace context, and mutation conventions

## Summary

The AKS page (`AksPage.tsx`) and its 15 tab components have become a 1000-line monolith with duplicated table markup, duplicated loading/empty/namespace-column handling, and ad-hoc mutation wiring. The recent round of small bugs (silent mutations, stale tables, missing toasts, pod age formatting, namespace selector UX) all traced back to the same root cause: the same code was written by hand in many places. This refactor extracts the shared parts while **keeping the current visuals exactly as they are**.

## Goals

1. Reduce `AksPage.tsx` to an orchestrator: header, tab bar, URL state, detail panels, confirm bar, and a single context menu portal.
2. Replace duplicated `<table>` markup in every tab with one generic `ResourceTable<T>` component.
3. Introduce `AksWorkspaceContext` so tabs and row actions can open detail panels/YAML/logs without prop drilling through `AksPage`.
4. Introduce a `useNotifyMutation` helper so every mutating action automatically shows success/error toasts and invalidates the right query keys.
5. Move per-resource action logic (context menu items, restart/scale/suspend/delete) into the tab that owns the resource.

## Non-goals

- No visual change: colors, spacing, fonts, table headers, badges, and `data-testid` values stay the same.
- No new AKS features (no new tabs, no new sidecar endpoints).
- No query-key renaming unless required for invalidation correctness.
- No refactor of Storage, Redis, Service Bus, API Client, Monitoring, or Dashboard as part of this work.

## Dependencies

- `useNotification()` from `NotificationSystem.tsx` must remain available at the root.
- `ContextMenu.tsx` already exposes `ContextMenuItem`.
- `ResizablePanel.tsx` already exists and is reused for detail panels.

## Risks

| Risk | Mitigation |
|---|---|
| `AksWorkspaceContext` value changes on every render and causes excessive re-renders | Memoize the provider value with `useMemo` |
| Moving context-menu item generation into tabs repeats the same boilerplate | Provide a small `buildContextMenu` helper for common actions (Copy name, View YAML, View Logs) |
| Tests rely on exact `data-testid` attributes | `ResourceTable` preserves the current `*-table-body` and `*-row-${name}` testids |

## Relationship to other docs

- Builds on `../react-polish-aug-01/` (the last bug-fix pass).
- Does not replace `../aks/` (the feature-level AKS docs); it is an internal implementation plan.
- Should be archived under `../archive/aks-internal-refactor/` once merged.
