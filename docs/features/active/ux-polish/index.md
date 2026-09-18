# UX Polish — Effortless Operator Experience

## Goal

Make SwebKit feel instant and intuitive: eliminate stale-data windows and silent failures,
restore the operator's last workspace (context, namespace, page, selections), and normalize
loading/empty/error patterns across every feature page.

## Scope

Four implementation modules, delivered one at a time with review between each:

| Module | Doc | Focus |
| ------ | --- | ----- |
| 1. AKS context switching & namespaces | `aks-context-switching.md` | 10 confirmed bugfixes + per-cluster query scoping + instant namespace restore + selector UX |
| 2. Startup warm-up & resume | `startup-resume.md` | background prefetch of AKS bootstrap queries + resume-last-route |
| 3. Page restore & deep-link parity | `page-restore-parity.md` | URL params + persisted selection for Storage/Redis/SQL/Monitoring |
| 4. Consistency & polish sweep | `consistency-sweep.md` | shared SearchableSelect, signal audit, mutation notify audit, a11y, QueryState normalization |

Stretch items (opt-in per session): command-palette context switching, namespace MRU/badges,
shared server-side namespace cache, jump-to-resource.

## Non-goals

- AI/agent improvements (agent chat, tools, ACP, visualizations) — separate session.
- MAUI/Blazor work — React/Tauri + sidecar is the primary stack.
- New feature capabilities — this pass fixes and polishes existing surfaces.

## Dependencies

- TanStack Query key scoping (Module 1) precedes warm-up prefetch keys (Module 2).
- Profile-save pool eviction (Module 1) precedes pool keep-alive on context switch.
- `view-pref:` localStorage convention (`web/src/lib/stores/panel-preferences.ts`) is the
  persistence mechanism for all restore state.

## Risks

- Query-key scoping touches ~20 hooks in `useAks.ts` — keep context resolution inside hooks
  so call sites are unchanged.
- Pooled `KubernetesAksClient` keep-alive on context switch requires profile-save eviction
  to cover kubeconfig-path/credential changes.
- Resume-last-route must fire once at cold start and never fight explicit navigation.

## Quick links

- Feature test scope: `test-plan.md`
- AKS deep dive: `../../architecture/functionalities/aks.md`
- Frontend pitfalls: `../../pitfalls/react-frontend.md`
