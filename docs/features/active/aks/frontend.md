# Frontend Plan - aks

---

title: "Frontend Plan - aks"
owner: ""
status: "Planned"

---

## Goal

Provide a clean AKS UI flow where users can select a kubeconfig, choose context and namespace, browse pods/deployments/helm releases/ingresses, and open read-only YAML for each item.

## Impacted areas

- Files / components: `src/SwebKit.App/Components/Aks/*`, `src/SwebKit.App/Components/Pages/AksPage.razor`
- Pages / routes: AKS page
- Shared components: table/list components, details pane, empty/error state callouts

## UX and accessibility notes

- Primary flow: `kubeconfig` -> `context` -> `namespace` -> resource tab -> YAML drawer/modal.
- Namespace selection should be explicit and sticky during page session.
- Each resource tab must support loading, empty, unauthorized, and error states.
- YAML viewer should use readable typography and keyboard-friendly scroll/focus behavior.

## API / contract changes

- UI view models for:
  - kubeconfig/context metadata
  - namespace list and active namespace
  - pods, deployments, helm releases, ingresses rows
  - YAML payload result object (kind/name/namespace/yaml text)

Backward compatibility notes:

- Existing AKS log/terminal UI can remain hidden or unchanged while resource browser flows are prioritized.

## Tasks

- [ ] Update AKS page view model for kubeconfig + context + namespace state
- [ ] Implement kubeconfig picker and context selector UI
- [ ] Implement namespace selector and namespace-scoped refresh behavior
- [ ] Implement tabs or sections for Pods, Deployments, Helm Releases, and Ingresses
- [ ] Implement consistent loading/error/empty states for each resource section
- [ ] Wire resource row action to open YAML viewer (drawer/modal)
- [ ] Add unit/component tests for core AKS browsing flows
- [ ] Add e2e tests for context/namespace switching and YAML view
- [ ] Accessibility review for keyboard navigation and tab order

## Validation

- Component tests: Planned
- Manual UX checks:
  - Select custom kubeconfig and verify contexts load
  - Select namespace and verify all tabs scope correctly
  - Open YAML view from pods/deployments/helm releases/ingresses
  - Validate behavior for empty namespace and RBAC denied responses

## Notes

- Follow `docs/pitfalls/blazor-maui.md` guidance for `InvokeAsync(StateHasChanged)` and JS interop timing.
- Keep YAML view read-only in this phase.
