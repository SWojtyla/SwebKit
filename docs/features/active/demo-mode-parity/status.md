# Status — Demo Mode Parity

## Current State

`Done` (pending user commit)

## Quick Summary

The demo-mode core gating logic is solid with no dataset drift. The "feels different" gap is two
domains (Observability, DevOps/Pipelines) never ported to the sidecar at all, a dead scripted demo
event, and no UI indication that demo mode is active in Settings.

**Jira:** not linked

## Progress Checklist

- [x] Scope decision: Observability/DevOps demo mode dropped permanently (user decision,
      2026-07-26) — removed the two dead `ProjectReference`s from `SwebKit.Sidecar.csproj`; updated
      `docs/features/README.md`'s canonical order to drop `observability` and note the decision
- [x] Scripted "pod failure" demo event reconnected: `PodsTab.tsx` now diffs pod status on each
      poll and fires a native notification via the existing `showNotification` bridge on a genuine
      transition into `Failed` (not on initial load) — works for both demo mode's scripted tick-2
      event and real clusters
- [x] Settings tabs show a demo-mode-active banner (Service Bus/AKS/Redis/Storage tabs) when demo
      mode is on, explaining those connection fields are inert
- [x] Automated smoke test: exercised via the full e2e suite (demo mode toggled on/off in every
      spec's `beforeEach`/`afterEach`); found and fixed a duplicate `demo-mode-toggle` testid
      (AppLayout's global top-bar toggle vs. DashboardPage's redundant local one, which used
      different label text) that was silently breaking `setDemoMode` for every single e2e test —
      renamed the dashboard one to `dashboard-demo-mode-toggle` and fixed the shared test helper

## Validation

Not started.

## Blockers

Needs a scope decision from the user on Task 1 (Observability/DevOps demo mode) before this can
move to `Planned`.

## Notes

- Found during code review on 2026-07-26 of the sidecar-based demo mode vs. the MAUI original
  (`src/SwebKit.Core/Services/Demo*.cs`).
- Independent of [tauri-security-hardening](../tauri-security-hardening/status.md),
  [aks-migration-fixes](../aks-migration-fixes/status.md), and
  [service-bus-migration-fixes](../service-bus-migration-fixes/status.md) — can be worked in
  parallel.
- Flags a related but out-of-scope gap (Blob Recovery has no backend routes at all, not just in
  demo mode) for a future separate feature.
