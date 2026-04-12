# Frontend Plan - aks-runtime-diagnostics-depth

---

title: "Frontend Plan - aks-runtime-diagnostics-depth"
owner: "GitHub Copilot"
status: "Planned"

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
- `src/SwebKit.App/Components/Aks/IngressGrid.razor`
- `src/SwebKit.App/Components/Aks/HelmGrid.razor`
- `src/SwebKit.App/Components/Aks/AksHelmPanel.razor`
- `src/SwebKit.App/Components/Aks/HpaPanel.razor`
- Planned new diagnostics panels:
- `src/SwebKit.App/Components/Aks/NamespaceQuotaPanel.razor`
- `src/SwebKit.App/Components/Aks/PodDisruptionBudgetPanel.razor`
- `src/SwebKit.App/Components/Aks/ProbeFailurePanel.razor`
- `src/SwebKit.App/Components/Aks/NetworkPolicyAnalysisPanel.razor`
- `src/SwebKit.App/Components/Aks/PlacementConstraintsPanel.razor`
- `src/SwebKit.App/Components/Aks/HelmDiffPreviewPanel.razor`

## UX notes

- Panel model.
- Reuse the existing `AksDetailPanels` column rather than create a new route or modal-first experience.
- New diagnostics should open from the currently selected resource row, namespace context, or Helm selection.
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
- Keep the current resource grids intact; add panel entry points and badges rather than duplicate the existing rows in new views.

## Tasks

### Wave 1 - namespace and workload diagnostics [blazor-expert]

- [ ] Add quota, limit-range, PDB, probe, and placement panel entry points.
- [ ] Render concise summaries plus supporting details for each diagnostic class.
- [ ] Keep existing grid interactions stable.

### Wave 2 - network and ingress diagnostics [blazor-expert]

- [ ] Add ingress and network-policy analysis panels.
- [ ] Add links from ingress and workload rows into those panels.
- [ ] Keep wording explicit about what was inspected.

### Wave 3 - Helm preview [blazor-expert]

- [ ] Add preview entry points from Helm actions.
- [ ] Render read-only diff or fallback content with search support.
- [ ] Ensure unsupported capability states are visible and not treated as silent failure.

## Validation

- Component tests: Not started. Extend `AksDetailPanelsTests`, `AksHelmPanelTests`, and add focused tests for each new diagnostics panel.
- Manual UX checks:
- Verify a constrained namespace is diagnosable without leaving the page.
- Verify probe and placement panels distinguish observed evidence from interpretation.
- Verify Helm preview does not interfere with existing history and rollback flows.

## Notes

- Follow `blazor-maui.md` guidance for any new AKS child components and JS-assisted diff or viewer surfaces.
- Keep panel count manageable. If too many diagnostics open at once, prefer a stronger single-panel navigation model over stacking visually noisy content.
