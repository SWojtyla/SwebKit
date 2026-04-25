# Status - winui3-settings-completeness

---

title: "Status - winui3-settings-completeness"
owner: ""
state: "Review"
jira: "not linked"
branch: "winui-rewrite"
started: "2026-04-25"
last_updated: "2026-04-25"

---

## Quick summary

The native Settings page now owns sectioned repair surfaces for Service Bus, AKS, Redis, Azure DevOps, Storage, and Observability, and the WinUI route-level repair actions can target those sections directly. Automated validation is green; the remaining work is a manual UI smoke pass before final close-out.

**Jira:** not linked

**Current focus:** run a manual UI smoke pass across the new sections and section-targeted readiness entry points before marking the feature done.

## Progress checklist

- [x] MAUI versus WinUI settings gap documented
- [x] Native section list confirmed for all in-scope domains
- [x] Validation and readiness handoff defined per section
- [x] Persistence and credential flows verified against current services
- [x] Downstream feature dependencies updated after implementation starts
- [x] Tests and docs aligned

## Completed

- Confirmed that the current WinUI Settings page is materially narrower than the MAUI Settings page.
- Identified Settings as a cutover-critical dependency rather than a cosmetic follow-up.
- Linked this feature to all downstream parity slices that depend on live configuration repair.
- Rebuilt the native Settings page around section navigation, shared readiness summaries, and per-section repair content for Service Bus, AKS, Redis, Azure DevOps, Storage, and Observability.
- Added section-targeted navigation requests so Pipelines, Observability, AKS, Storage, and dashboard readiness flows can open the owning native Settings section instead of a generic route.
- Added native save/test flows for the in-scope configuration areas while keeping Incident Timeline explicitly deferred in the settings IA.
- Added focused WinUI tests for the new section-targeted settings navigation contract.

## Remaining

- Run a manual WinUI smoke pass across the new settings sections.
- Verify one end-to-end repair loop from Pipelines or Observability into Settings and back.

## Blockers

- None.

## Validation

- Test Plan: link to `test-plan.md`
- Validation status: `build-winui` green, focused `ReadinessStateViewModelTests` command-level settings-navigation coverage passing, and dashboard/frame handoff plus section-form save flows still need manual smoke coverage.

## Notes

- Incident Timeline stays explicitly deferred unless a separate migration feature is created for it.
- Service Bus namespace connectivity remains profile-backed, but the native Settings route now surfaces credential health and pinned-entity repair instead of leaving the omission hidden.
