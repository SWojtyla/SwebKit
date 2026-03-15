# AKS UX Improvements

---

title: "AKS UX Improvements"
owner: ""
status: "In Progress"
created: "2026-03-15"
updated: "2026-03-15"

---

## Goal

Improve the AKS page with YAML editing, syntax highlighting, better scale UX, smarter namespace selection, and corrected Helm history ordering.

## Value

Reduce friction for common day-to-day Kubernetes operations: editing resources in-place, quickly switching namespaces, and reading Helm history without visual noise.

## Scope

### 1. YAML editing for Deployments and Ingresses
- YAML viewer panels for Deployments and Ingresses gain an **Edit** toggle
- Editing uses a styled `<textarea>` (monospace, full-height)
- **Save** applies the YAML via `kubectl apply -f` (new `ApplyResourceYamlAsync` on `IAksClient`)
- Pods and Helm YAML remain read-only (ephemeral / managed by Helm)
- Production environments require confirmation before applying

### 2. YAML syntax highlighting
- A lightweight client-side YAML tokenizer (`yamlHighlight.js`) replaces the plain `<pre>` in view mode
- Highlights: keys (blue), strings (orange), numbers (green), booleans (blue), comments (green), list dashes (purple), document markers (grey)
- View mode uses `@((MarkupString)…)` with highlighted HTML; edit mode switches to a raw `<textarea>`

### 3. Scale deployment UI redesign
- Scale input moved from the hidden bottom bar (inside the CSS grid, below the fold) to a dedicated **side panel** (ResizablePanel, 260px default)
- Panel shows deployment name, current replica count, +/- stepper, number input, Apply and Cancel
- Appears in the same column as YAML/Log panels (mutually exclusive)

### 4. Helm history — oldest-first ordering
- History rows rendered with `.OrderBy(r => r.Revision)` so revision 1 (oldest) is always the top row
- Both `OnCtxViewHelmHistory` and `OnCtxRollbackHelm` sort before assigning `HelmHistory`

### 5. Namespace quick-find
- Namespace `<select>` replaced with `<input list="ns-datalist">` + `<datalist>`
- Browser native autocomplete lets users type to filter a long namespace list instantly

## Dependencies

- Depends on: existing AKS page (`AksPage.razor`) and `IAksClient`/`KubernetesAksClient`
- `kubectl` must be on PATH for `ApplyResourceYamlAsync` (consistent with existing Helm rollback)

## Deliverables

- `docs/features/active/aks-improvements/index.md` (this file)
- `src/SwebKit.Core/Abstractions/IAksClient.cs` — new `ApplyResourceYamlAsync` method
- `src/SwebKit.Core/Services/DemoAksClient.cs` — no-op implementation
- `src/SwebKit.Kubernetes/AksClient/KubernetesAksClient.cs` — kubectl apply implementation
- `src/SwebKit.App/wwwroot/js/yamlHighlight.js` — YAML tokenizer
- `src/SwebKit.App/wwwroot/index.html` — script reference
- `src/SwebKit.App/Components/Pages/AksPage.razor` — all UI changes
- `src/SwebKit.App/Components/Pages/AksPage.razor.css` — new styles
