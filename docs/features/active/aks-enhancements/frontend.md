# Frontend Plan - AKS Enhancements

---

title: "Frontend Plan - AKS Enhancements"
owner: ""
status: "Planned"

---

## Goal

Deliver a usable AKS browser UX with context/namespace selectors, resource tabs, and read-only YAML inspection.

## Impacted areas

- `src/SwebKit.App/Components/Pages/AksPage.razor`
- `src/SwebKit.App/Components/Aks/*`
- Shared loading/error components where needed

## UX notes

- Replace free-text context entry with discovered context selector.
- Keep namespace selector explicit and sticky.
- Use per-tab loading/empty/error states.
- Keep YAML viewer read-only with strong readability and responsive layout.

## Tasks

- [ ] Add discovered context selector on AKS page
- [ ] Add namespace list selector sourced from cluster
- [ ] Implement tab views for pods/deployments/helm/ingresses
- [ ] Add row action to open YAML viewer per resource
- [ ] Add component tests for selector and tab behavior

## Validation

- Component tests: Planned
- Manual checks: Planned (see `test-plan.md`)
