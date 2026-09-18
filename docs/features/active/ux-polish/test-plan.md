# Test Plan — UX Polish

Umbrella test scope for all modules. Per guardrails: every change covers empty/loading/error/
single-item states, special characters in resource names, demo mode, `data-testid`s on
state-changing controls, and `useNotification` feedback on mutations.

## Module 1 — AKS context switching & namespaces

### Unit (sidecar, `tests/SwebKit.Sidecar.Tests`)

- `GetAksClient("ctx-other")` builds a client for `ctx-other`, not the profile context —
  cached under the explicit key, not cross-wired.
- Profile save with changed `aksConfig`/`serviceBusNamespaces`/`redisConfig` evicts pooled
  clients; unchanged profile save does not.
- `POST /api/aks/context` does not drain other-context pooled clients.
- `/api/aks/contexts` returns demo contexts when `DemoModeService.IsDemoMode`.

### Unit (web, vitest)

- `aks-query-keys` predicates still match ctx-scoped keys (`["aks-pods", ctx, ns]`).

### E2E (Playwright, demo mode)

- Context switch A→B: `ns` param becomes B's persisted/default ns; selector shows a loading
  label (not A's namespaces); tables show skeletons then B's data.
- Switch back within stale window: data renders from cache without full reload flash.
- Failed connect (`{connected:false, error}`): error toast, no fake success.
- Failed POST (route abort): error toast.
- First visit with persisted `aks-selected-ns:<ctx>`: `ns` applied before namespace list resolves.
- Not-configured profile: friendly empty state with Settings CTA.
- Demo mode: context selector lists demo contexts.

## Module 2 — Startup warm-up & resume

- Cold start with AKS configured: `aks-namespaces` prefetch fires before visiting `/aks`.
- Resume-last-route: landing restores last `pathname+search` once; toggle off → Dashboard.
- Explicit navigation to `/` is never hijacked after the initial restore.

## Module 3 — Page restore & deep-link parity

- `/storage?account=&container=` deep link selects both; reload restores last account/container.
- `/redis?tab=slowlog` lands on the tab; last pattern restored per cache.
- `/sql?connection=` honored (palette deep link).
- Palette `state` items still drive selection alongside URL params.

## Module 4 — Consistency sweep

- All `queryFn`s pass `signal` (lint/grep audit).
- Every mutation surfaces success/error notification (audit list in module doc).
- Dropdowns: Escape closes, arrows navigate, `aria-expanded` reflects state (Playwright).
- Storage/Redis loading-empty-error states render via shared components.

## Regression matrix (per module)

- `cd web && npm run build`, `npx tsc -b`, `npm run test:unit`
- `cd src-sidecar && dotnet build`; `cd tests/SwebKit.Sidecar.Tests && dotnet test`
- `cd web && npx playwright test` (module-relevant specs minimum)
