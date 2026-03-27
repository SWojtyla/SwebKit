# Feature Overview — Performance v2: Blazing Fast UI

---

title: "Feature Overview — Performance v2: Blazing Fast UI"
owner: ""
status: "Planned"
created: "2026-03-27"
updated: "2026-03-27"

---

## Goal

Make the SwebKit UI blazing fast and non-blocking. Fix AKS log freezes, eliminate render flooding, add virtualization for large lists, fix cancellation token races, and harden async patterns across all pages.

## Value

Users report that the UI still feels laggy despite v1 improvements. AKS pod logs can freeze the UI completely, cancellation doesn't always work when navigating away, and StateHasChanged flooding causes excessive re-renders. This feature directly addresses the top user-facing pain points.

## Scope

**In scope — 15 items across 3 waves:**

- **Wave 0 — AKS Log Freeze Fix (CRITICAL):** PERF2-1 through PERF2-5
- **Wave 1 — Async Correctness & Safety:** PERF2-6, PERF2-7, PERF2-10, PERF2-12
- **Wave 2 — Render Optimization:** PERF2-8, PERF2-9, PERF2-11, PERF2-13, PERF2-14, PERF2-15

**Out of scope:**

- No new features or UI additions
- No backend API optimization beyond channel/stream fixes in `KubernetesAksClient`
- No infrastructure or deployment changes
- No skeleton screens (deferred from v1 — separate concern)

## Dependencies

- Blazor `Virtualize<T>` component (built-in, no new deps)
- Existing `PageDataCache` from v1
- Pitfalls: BL-2 (InvokeAsync), BL-3 (guard before await), CS-2 (OperationCanceledException)

## Risks & mitigations

- **Risk:** StateHasChanged → InvokeAsync migration (PERF2-7) across all pages could introduce subtle UI bugs where renders are missed or duplicated — **Mitigation:** Methodical file-by-file migration with component tests validating each page's loading/error/data states
- **Risk:** CTS race fix (PERF2-4, PERF2-10) with Interlocked pattern could miss edge cases — **Mitigation:** Dedicated unit tests for rapid-replacement scenarios
- **Risk:** Virtualization changes to log views could break scroll-to-bottom behavior — **Mitigation:** Manual testing with 1000+ line log streams

## Related documents

- Archive: `docs/features/archive/performance-improvements/summary.md`
- Pitfalls: `docs/pitfalls/blazor-maui.md` (BL-2, BL-3), `docs/pitfalls/dotnet-csharp.md` (CS-2)
- Architecture: `docs/architecture/architecture.md`

## Quick links

- Status: `status.md`
- Frontend plan: `frontend.md`
- Backend plan: `backend.md`
- Test plan: `test-plan.md`
- Decisions: `decisions.md`
