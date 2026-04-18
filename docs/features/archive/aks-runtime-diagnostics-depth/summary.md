# Archive Summary - aks-runtime-diagnostics-depth

---

title: "Archive Summary - aks-runtime-diagnostics-depth"
owner: "GitHub Copilot"
jira: "not linked"
completed_date: "2026-04-18"
pr: ""
commit: ""

---

## Goal

Deepen the AKS diagnostics experience on the existing `/aks` route by adding namespace quota/limit-range, PDB, probe-failure, placement-constraint, network/ingress analysis, and Helm diff preview panels without creating a separate diagnostics route.

## Delivered

- **Wave 1 — Namespace and workload constraints:**
  - Quota, limit-range, PDB, probe, and placement models in `AksModels.cs`.
  - `IAksClient` and `KubernetesAksClient` extended with additive read methods (quota, limit-range, PDB, probe failure, placement constraints).
  - `NamespaceQuotaPanel`, `PodDisruptionBudgetPanel`, `ProbeFailurePanel`, `PlacementConstraintsPanel` Razor components.
  - Entry points wired from deployment/statefulset context menus.

- **Wave 2 — Network and ingress diagnostics:**
  - Services added as a first-class AKS browse and YAML surface.
  - Services, Ingresses, and Gateway API resources grouped behind an expandable `Network` toolbar menu.
  - HTTPRoute grid switched to non-virtualized render to keep all route rows stable.
  - Typed ingress and network-policy analysis contracts added to `AksModels.cs`.
  - `IngressAnalysisPanel` and `NetworkPolicyAnalysisPanel` self-loading side-panel components.
  - Drill entry points from workloads, pods, and ingresses; wording anchored to evidence not certainty.

- **Wave 3 — Helm diff preview:**
  - `HelmDiffPreview` model and `PreviewHelmUpgradeAsync` method.
  - Capability detection (Full / Degraded / Unsupported) in `KubernetesAksClient` and `DemoAksClient`.
  - `HelmDiffPreviewPanel` Razor component with capability-aware rendering.
  - Helm context menu entry in `AksPage.razor`.

- **All 800+ unit/component tests passing** (2026-04-17); 6 new Wave 1+3 demo-mode tests added.

## Key decisions

- **Stay on the existing `/aks` route** — avoids splitting selection state across two pages; reuses the auto-refresh pause and bootstrap flow.
- **Diagnostics are evidence summaries, not root-cause claims** — all panel wording shows what the cluster reports; no over-translation into certainty.
- **Wave 2 panels load on demand** — network/ingress diagnostics attach to the side-panel column and fetch lazily; they do not join the main browse cache or periodic refresh path.
- **Explicit limitation wording** — network policy analysis surfaces what policies are present, not a full packet-level verdict, because kubectl-level data cannot guarantee connectivity certainty.

## Validation performed

- Unit/component tests: 800+ passing on 2026-04-17 (`AksPageBatchTests`, `AksDetailPanelsTests`, `AksTimelineSignalSourceTests`, `DemoAksClientTests`).
- Build: 0 errors, 0 warnings on net10.0-windows10.0.19041.0.
- Manual (live cluster): deferred; accepted by user (2026-04-18). Degraded-capability paths verified by capability-detection unit tests.

## Lessons learned

- HTTPRoute grids with wrapping cells need a non-virtualized render path; virtualization clips rows when cells wrap.
- Diagnostic panels that load on demand (not on page init) must be explicit about their loading state — a spinner or "Click to load" pattern prevents blank panels from appearing permanently stuck.
- Network policy analysis should always include an explicit "this shows policy presence, not connectivity guarantee" note; without it, operators over-trust the panel output.

## Follow-up

- Broaden `KubernetesAksClient` direct-client tests for new Wave 1 methods (quota, limit-range, PDB, probe, placement) — currently covered by demo-mode only; deferred.

## Archive note

> This file is present because the feature had **no Jira ticket** (Path B). Archive location: `docs/features/archive/aks-runtime-diagnostics-depth/`.
