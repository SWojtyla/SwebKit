---
status: Planned
---

# AKS React UX improvements

## Objective
Close three remaining AKS React UX gaps:

1. YAML editing in the YAML viewer currently shows a disabled **Apply (coming soon)** button — it should actually apply the edited manifest.
2. The namespace multi-select combobox is hard to use when many namespaces are selected because selected items are mixed with unselected ones.
3. Switching context or namespace gives no visible loading feedback.

## Scope
- React/Tauri frontend only.
- Sidecar endpoint for applying YAML.
- `NamespaceSelector` and `ContextSelector` improvements.
- Global/context-aware loading indicator in `AksPage`.

## Outcomes
- Users can edit YAML, validate it, and apply it.
- Selected namespaces appear at the top of the namespace dropdown.
- Users see a spinner when the AKS context or selected namespace is changing.

## Non-goals
- Adding an editor with full Monaco/IntelliSense.
- Server-side diff view.

## Verification
- `npm run build`
- `dotnet build src-sidecar/SwebKit.Sidecar.csproj`
- `dotnet test tests/SwebKit.Sidecar.Tests`
- `npx playwright test e2e/aks*.spec.ts`
