# Archive Summary - winui3-migration

---

title: "Archive Summary - winui3-migration"
owner: ""
jira: "not linked"
completed_date: "2026-04-25"
pr: "not linked"
commit: "not captured"

---

## Goal

Replace the MAUI Blazor Hybrid host with a native WinUI 3 host that brings the shell, settings, dashboard, and the major operator workspaces onto native XAML and view models while keeping the domain and integration layers intact.

## Delivered

- Added `src/SwebKit.WinUI/` as a real native WinUI 3 host wired through `Microsoft.Extensions.Hosting` and the existing domain/integration services.
- Delivered native routed coverage for Dashboard, Settings, Service Bus, AKS, Redis, Storage, Pipelines, and Observability so the WinUI host no longer depends on the MAUI shell for those baseline routes.
- Established the first shared WinUI shell foundation: curated theming, shell chrome, dashboard/readiness path, and page scaffold primitives that later hardening work can build on.

## Key decisions

- Keep the WinUI migration as a baseline checkpoint and move parity closure, hardening, and cutover readiness into `winui3-cutover-audit-hardening` so the repo has an honest active execution record.
- Build the shared WinUI foundation early through semantic theming, reusable shell primitives, and scaffolded page composition instead of letting page-local XAML drift grow unchecked.
- Preserve `SwebKit.Core` and the integration projects as the stable backend surface; migrate the host and UI architecture without broad domain rewrites.

## Validation performed

- Build validation: `build-winui` stayed green across the baseline route-delivery work, including the final 2026-04-25 checkpoint build.
- Domain validation: existing core and integration test projects remained the regression backstop while the host migration stayed focused on the WinUI surface.
- Manual checks: baseline routed coverage and shell migration progress were reviewed during implementation, but the full cutover smoke pass remains part of the active hardening feature.

## Lessons learned

- Broad migration phase labels can hide real parity debt; a separate hardening feature is a better control surface once the native host baseline exists.
- Shared WinUI primitives need to land before too many routed pages accumulate bespoke layout and lifecycle code.
- Keeping the MAUI host alive during the parallel WinUI branch made the checkpoint honest and reversible instead of forcing premature cutover claims.

## Follow-up

- Remaining parity, shared-state primitives, auth/readiness hardening, manual smoke coverage, and the final cutover recommendation now live under `docs/features/active/winui3-cutover-audit-hardening/` — owner: active feature.

## Archive note

> This file is present because the feature had no Jira ticket. Archive location: `docs/features/archive/winui3-migration/`.
