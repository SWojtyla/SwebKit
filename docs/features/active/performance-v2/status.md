# Status — Performance v2: Blazing Fast UI

---

title: "Status — Performance v2: Blazing Fast UI"
owner: ""
state: "Planned"
branch: ""
started: ""
last_updated: "2026-03-27"

---

## Quick summary

Plan complete. All 15 items documented across 3 waves. Ready for implementation starting with Wave 0 (AKS log freeze — the most painful user-facing issue).

**Current focus:** Begin Wave 0 — fix AKS log streaming render flooding and channel completion hangs.

## Progress checklist

### Wave 0 — AKS Log Freeze Fix (CRITICAL)

- [ ] PERF2-1: Batch StateHasChanged in MultiPodLogView (render-batching timer)
- [ ] PERF2-2: Batch StateHasChanged in PodLogView (render-batching timer)
- [ ] PERF2-3: Virtualize log display (replace @foreach with Virtualize or capped window)
- [ ] PERF2-4: Fix CTS null-reference race in PodLogView (Interlocked swap pattern)
- [ ] PERF2-5: Fix channel completion hang in KubernetesAksClient (try/finally on writer)

### Wave 1 — Async Correctness & Safety

- [ ] PERF2-6: Fix async void in RedisPage (convert to async Task)
- [ ] PERF2-7: Migrate bare StateHasChanged to InvokeAsync across all pages
- [ ] PERF2-10: Fix rapid CTS replacement race in AksPage (Interlocked swap)
- [ ] PERF2-12: Fix silent pod stream failures in KubernetesAksClient

### Wave 2 — Render Optimization

- [ ] PERF2-8: Batch AksPage incremental loading StateHasChanged calls
- [ ] PERF2-9: Cache FilteredLines computation (invalidate on add, not on render)
- [ ] PERF2-11: Add @key directives to repeated elements across components
- [ ] PERF2-13: Call StateHasChanged immediately after setting loading state
- [ ] PERF2-14: Add virtualization to EntityTree for large namespaces
- [ ] PERF2-15: Bound and cleanup \_podColorIndex dictionary in MultiPodLogView

## Completed

_(none yet)_

## Remaining

- All 15 items across 3 waves

## Blockers

- None

## Validation

- Test Plan: `test-plan.md`
- Validation status: Not started

## Notes

- Wave 0 is the top priority — AKS log freezing is the most-reported user pain point
- Wave 1 fixes correctness issues that cause crashes or silent failures
- Wave 2 is polish — measurable but lower-severity
- Agent assignment: `[blazor-expert]` for Waves 0 and 2, `[dotnet-expert]` for PERF2-5/PERF2-12 backend work
