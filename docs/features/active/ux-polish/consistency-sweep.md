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

## Implementation notes (as built)

- `SearchableSelect` (`web/src/components/shared/SearchableSelect.tsx`) centralizes
  trigger + filter + keyboard nav + Escape + outside-click + focus return, with
  `aria-haspopup`/`aria-expanded`/`aria-activedescendant`. Optional `sr-only` native
  `<select>` (`nativeSelectTestId`) preserves `selectOption()` Playwright paths and
  screen-reader compat; `nativeExtraOptions` covers clearable selects (Service Bus).
  Adopted by Redis cache, Storage account, SQL connection (server subtitle via
  `subtitle`), Service Bus namespace; `ContextSelector` is now a thin wrapper that adds
  MRU ordering + pending label.
- Signal audit: all live `queryFn`s destructure `{ signal }`; helper wrappers
  (`getHelmReleaseNotes`, `getHelmReleaseManifest`, monitoring/redis/sb/storage/sql
  readers in `api.ts`) take an optional `AbortSignal` threaded into `apiFetch`/`apiSend`.
  `useAksResourceYaml` uses raw `fetch` — now passes `{ signal }` too.
- Mutation audit: `useAksHelmRollback` converted to `useNotifyMutation` (was silent on
  success AND error — `HelmDetailPanel` never even received an `onError`). Apply/validate
  YAML notify via their `YamlViewer` callers; port-forward/shell surface errors inline in
  their panels; storage mutations + `useTogglePinnedResource` already notify.
- Loading/empty/error: Storage container list + Redis key-browser state branches now use
  `QueryState`; Monitoring already used `SkeletonRows` + dedicated error testids (left as
  is). Service Bus already renders `LastRefreshed` (`message-list-last-refreshed`).
- Disabled controls: ~70 buttons audited; every `disabled` button/input now carries a
  conditional `title` reason ("Type a message first", "Saving…", "Pick a pod first"…).
  Exceptions where the reason is already in the visible label are intentional
  (e.g. locked Fathom theme tiles show "N / M sessions").
- `aria-activedescendant` on the SearchableSelect filter input tracks the highlighted
  option id; Escape/outside-click return focus to the trigger.

## Validation

- `tsc --noEmit` clean; vitest 473/473; sidecar tests 466/466.
- New `web/e2e/searchable-select.spec.ts` (3 specs: filter+keyboard select, Escape +
  focus return, SQL server subtitles) — all pass.
- Selector regression sweep (AKS/Redis/SB/SQL/Storage, 73 specs) — all pass.
- Full Playwright suite 345/345 — including a de-flaked monitoring snooze spec
  (URL-driven tab switch raced a non-retrying `isVisible()`; now waits on
  `history.or(empty)`).
- Aikido MCP scan: server not installed in this environment — flagged to user; run
  `aikido_full_scan` on the changed files once configured.
