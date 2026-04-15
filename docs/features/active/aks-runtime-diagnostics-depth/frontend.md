# Frontend Plan - aks-runtime-diagnostics-depth

---

title: "Frontend Plan - aks-runtime-diagnostics-depth"
owner: "GitHub Copilot"
status: "In Progress"

---

## Goal

Extend the existing `/aks` diagnostics experience with higher-signal runtime panels so operators can see namespace and workload constraints, network or ingress clues, and Helm change previews from the same page.

## Impacted areas

- Existing AKS page and layout:
- `src/SwebKit.App/Components/Pages/AksPage.razor`
- `src/SwebKit.App/Components/Pages/AksPage.razor.css`
- `src/SwebKit.App/Components/Aks/AksDetailPanels.razor`
- Existing AKS components likely to gain entry points or badges:
- `src/SwebKit.App/Components/Aks/PodGrid.razor`
- `src/SwebKit.App/Components/Aks/DeploymentGrid.razor`
- `src/SwebKit.App/Components/Aks/StatefulSetGrid.razor`
- `src/SwebKit.App/Components/Aks/AksConnectionBar.razor`
- `src/SwebKit.App/Components/Aks/ServiceGrid.razor`
- `src/SwebKit.App/Components/Aks/IngressGrid.razor`
- `src/SwebKit.App/Components/Aks/HelmGrid.razor`
- `src/SwebKit.App/Components/Aks/AksHelmPanel.razor`
- `src/SwebKit.App/Components/Aks/HpaPanel.razor`
- Wave 2 diagnostics panels now implemented:
- `src/SwebKit.App/Components/Aks/IngressAnalysisPanel.razor`
- `src/SwebKit.App/Components/Aks/NetworkPolicyAnalysisPanel.razor`
- Planned later diagnostics panels:
- `src/SwebKit.App/Components/Aks/NamespaceQuotaPanel.razor`
- `src/SwebKit.App/Components/Aks/PodDisruptionBudgetPanel.razor`
- `src/SwebKit.App/Components/Aks/ProbeFailurePanel.razor`
- `src/SwebKit.App/Components/Aks/PlacementConstraintsPanel.razor`
- `src/SwebKit.App/Components/Aks/HelmDiffPreviewPanel.razor`

## UX notes

- Panel model.
- Reuse the existing `AksDetailPanels` column rather than create a new route or modal-first experience.
- New diagnostics should open from the currently selected resource row, namespace context, or Helm selection.
- Wave 2 panels should self-load when opened and stay out of the page's main browse-data refresh loop.
- Navigation density.
- Keep the main AKS tab row workload-focused. Group Services, Ingresses, and Gateway API resources behind one expandable `Network` menu instead of flattening every network resource into the top row.
- Evidence phrasing.
- Probe panels should say what failed, how often, and what recent events support the observation.
- Placement panels should separate declared constraints from observed scheduling failures.
- Network and ingress panels should separate Kubernetes object evidence from anything the app cannot prove, such as actual packet path success.
- Helm preview.
- Preview should be read-only and searchable like the YAML viewer.
- Capability limitations must be explicit if the underlying Helm diff path is unavailable.
- Responsiveness.
- New panels should inherit the page's existing auto-refresh pause behavior.
- Large diagnostics summaries should default to concise cards or tables before exposing raw object details.
- Accessibility.
- Panel entry points must be reachable from keyboard navigation in the grids.
- Read-only diagnostic badges should include text and not rely on color alone.

## API / contract changes

- UI will rely on additive `IAksClient` models for quota, disruption-budget, probe, network, placement, and Helm preview data.
- Avoid pushing analysis logic down into Razor components. Panels should render pre-normalized summaries and raw supporting data from the backend.
- Ingress and network-policy panels now call additive `IAksClient` analysis methods on parameter change and expose refresh explicitly from inside the panel.
- Keep the current resource grids intact; add panel entry points and badges rather than duplicate the existing rows in new views.

## Tasks

### Wave 1 - namespace and workload diagnostics [blazor-expert]

- [ ] Add quota, limit-range, PDB, probe, and placement panel entry points.
- [ ] Render concise summaries plus supporting details for each diagnostic class.
- [ ] Keep existing grid interactions stable.

### Wave 2 - network and ingress diagnostics [blazor-expert]

- [x] Group network resource browse tabs under an expandable `Network` menu.
- [x] Add first-class Services browse support with namespace-aware YAML viewing.
- [x] Keep HTTPRoute list rendering stable when route rows wrap.
- [x] Add ingress and network-policy analysis panels.
- [x] Add links from ingress and workload rows into those panels.
- [x] Keep wording explicit about what was inspected.

### Wave 3 - Helm preview [blazor-expert]

- [ ] Add preview entry points from Helm actions.
- [ ] Render read-only diff or fallback content with search support.
- [ ] Ensure unsupported capability states are visible and not treated as silent failure.

## Validation

- Component tests: Focused Wave 2 coverage added in `AksDetailPanelsTests` and `AksPageBatchTests` for panel rendering plus row, context-menu, and keyboard entry points.
- Add or extend focused AKS page tests for the `Network` menu, Services tab, and multi-route HTTPRoute rendering.
- Focused validation passed on 2026-04-15 for `AksPageBatchTests` and `AksDetailPanelsTests`.
- Manual UX checks:
- Verify a constrained namespace is diagnosable without leaving the page.
- Verify probe and placement panels distinguish observed evidence from interpretation.
- Verify Helm preview does not interfere with existing history and rollback flows.

## Notes

- Follow `blazor-maui.md` guidance for any new AKS child components and JS-assisted diff or viewer surfaces.
- Keep panel count manageable. If too many diagnostics open at once, prefer a stronger single-panel navigation model over stacking visually noisy content.
