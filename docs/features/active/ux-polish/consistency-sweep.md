# Module 4 — Consistency & Polish Sweep

Cross-cutting polish: shared primitives, query hygiene, mutation feedback, accessibility,
and normalized loading/empty/error states. Small diffs, wide surface.

## Shared `SearchableSelect`

- Extract the `ContextSelector` pattern (search box, keyboard nav, Escape, outside-click,
  `aria-haspopup`/`aria-expanded`) into `web/src/components/shared/SearchableSelect.tsx`.
- Reuse for: Redis cache select, Storage account select, SQL connection select, Service Bus
  namespace select — where list size justifies search. Keep the `sr-only` native `<select>`
  pattern for Playwright compatibility (see `NamespaceSelector.tsx` comment).
- AKS selectors converge onto it in Module 1.

## `queryFn` `signal` pass-through audit

- Every hook in `web/src/lib/hooks/*`: `queryFn: ({ signal }) => apiFetch(url, { signal })`.
  Many AKS hooks omit it today (`useAksDeployments`, `useAksPods`, `useAksNamespaces`, …) —
  pitfall: a superseded request keeps running server-side holding its pooled client.
- Grep audit `queryFn: () =>` across `web/src` and fix all hits.

## Mutation feedback audit

Every `useMutation` must notify success/error and invalidate the right keys. Known gaps:

- `useAksSetContext` — handled in Module 1.
- `useAksHelmRollback` — invalidates but never notifies (`useAks.ts`).
- `useAksApplyYaml`, `useAksValidateYaml` — verify notification path.
- Port-forward / pod-shell actions (`tauri-bridge` calls) — verify error toasts.
- Storage upload/copy/metadata/restore — verify.
- `useTogglePinnedResource` — has onError; verify success path stays silent-by-design or add.

## Accessibility pass

- Dropdowns/dialogs: `aria-haspopup`, `aria-expanded`, `aria-activedescendant`, Escape close,
  focus return on close — `ContextSelector`, `NamespaceSelector`, `SearchableSelect`,
  `EntityCommandPalette`, `CommandPalette` (verify), `ScaleDialog`.
- `data-testid` on every state-changing control (guardrails).

## Loading/empty/error normalization

- `QueryState` / `EmptyState` / `SkeletonRows` replace hand-rolled states:
  - `StoragePage.tsx` container list: `"Loading..."`, `"Error: ..."`, `"No containers"` strings.
  - Redis tabs and Monitoring panels — audit for hand-rolled equivalents.
- `LastRefreshed` indicator where auto/background refresh exists (Service Bus page if missing).

## Disabled-control affordances

- Every disabled button gets a `title` reason — "never leave a click with no response"
  (guardrails). Known spots: `aks-multi-pod-logs`, auto-refresh controls when `!namespaceToken`,
  Apply in `NamespaceSelector` (has `!hasChanges` — verify it communicates why).

## Acceptance criteria

- `rg "queryFn: \(\) =>" web/src` returns no live-query hits (or each justified).
- Mutation audit list fully green — each entry notifies + invalidates.
- Playwright: dropdown Escape/arrow navigation specs pass; storage states render shared
  components (testids `query-error`, `skeleton-*`).
