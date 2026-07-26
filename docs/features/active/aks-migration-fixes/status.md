# Status — AKS Feature Migration Bug Fixes

## Current State

`Done` (pending user commit)

## Quick Summary

Fixes concrete bugs found in the React AKS feature vs. its MAUI original and its own sidecar
contract: broken pod log streaming, missing sidecar routes (404s), wrong-pod log lookups, a
regressed production safety guard, a non-functional secret reveal, dead stub buttons, and orphaned
components.

**Jira:** not linked

## Progress Checklist

- [x] Pod log streaming fixed: `MultiPodLogView.tsx` uses SSE (not WebSocket) with correct sidecar
      base URL, fans out one stream per selected pod, and now receives the full namespace pod list
      (or the clicked resource's pods) instead of a single pod; `PodLogView.tsx` URL uses
      `SIDECAR_BASE_URL`; orphaned `useAksPodLogs` left in `hooks.ts` unused (harmless, not deleted
      to keep this diff focused — flag for a future cleanup pass)
- [x] Missing sidecar routes added: StatefulSet restart/scale, Ingress delete
      (`AksEndpoints.cs`), demo-mode gated like every other endpoint
- [x] Deployment/StatefulSet "view logs"/"container details"/"logs for all pods" resolve pods via
      the resource's own `selectorLabels` instead of the last globally-selected pod
- [x] `AksConfirmBar` (extended with an optional type-to-confirm input) wired into all destructive
      actions, gated on `profile.config.isProduction` — replaces raw `confirm()`/`prompt()`
- [x] Secret reveal calls the real `/secrets/{name}/values` endpoint (values kept in local component
      state only, never cached); copy button copies the value, not the key
- [x] YAML Apply / Helm Rollback: disabled with a "coming soon" tooltip instead of a click-then-toast
      fake action; dead header-level Helm Rollback button removed entirely
- [x] Dead `resourceFilter` input removed
- [x] `NetworkPolicyAnalysisPanel`/`PodDisruptionBudgetPanel` removed — confirmed genuinely
      redundant (network analysis already lives in `AnalysisPanel.tsx`/the "Analysis" tab) and
      unimplemented (no backend at all for PDBs) respectively
- [x] Added a "View YAML" button to `PodDetailPanel` (was missing entirely — surfaced by the e2e
      suite) wired to the existing `openYaml` flow
- [x] Automated smoke test in demo mode: full Playwright suite for AKS (`aks.spec.ts`,
      `aks-deferred.spec.ts`, `aks-portforward-analysis.spec.ts`) — 12/12 passing
- [ ] Manual smoke test against a real AKS cluster — needs the user (no real cluster in this
      environment)

## Validation

Not started.

## Blockers

Waiting on [tauri-security-hardening](../tauri-security-hardening/status.md) to land first (sidecar
call auth changes affect how every fix here is tested).

## Notes

- Found during code review on 2026-07-26 of uncommitted changes on `feat/tauri-react-rewrite`.
- MAUI reference implementation for behavior comparison:
  `src/SwebKit.App/Components/Pages/AksPage.razor` + partials, `src/SwebKit.Kubernetes/AksClient/*.cs`.
