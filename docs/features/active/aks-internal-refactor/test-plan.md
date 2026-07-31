# AKS Internal Refactor — Test Plan

## Unit / build verification

- `npm run build` must pass with zero TypeScript errors.
- `dotnet build src-sidecar/SwebKit.Sidecar.csproj` must pass (no sidecar changes, regression guard).
- `dotnet test tests/SwebKit.Sidecar.Tests` must pass.

## End-to-end regression

Run the full suite:

```bash
cd web && npx playwright test
```

Pay special attention to these specs:

- `e2e/aks.spec.ts` — tab switching, namespace selection, deployments, pod detail.
- `e2e/aks-deferred.spec.ts` — multi-pod logs, YAML viewer, context menu.
- `e2e/aks-url-state.spec.ts` — URL params survive reload.
- `e2e/aks-portforward-analysis.spec.ts` — port-forward and analysis tabs.

## Visual / behavioral verification

For each tab, verify:

1. Table headers, row text, badge colors, and hover state are unchanged.
2. Right-clicking a row opens the same context menu items in the same order.
3. Context menu actions still work: View YAML, View Logs, Container Details, Restart, Scale, Suspend, Delete, Copy name.
4. Detail panels (pod, YAML, Helm, secret, container, multi-pod logs) open and close the same way.
5. URL updates and survives a browser reload.
6. Mutations show the same success/error toasts and refresh the table.
7. Namespace column appears only when multiple namespaces are selected (`isMulti`).

## No-visual-change checklist

- Do not change any Tailwind class names inside `ResourceTable` unless required to preserve identical rendering.
- Preserve `data-testid` values exactly.
- Preserve `ContextMenu` item labels, icons, and disabled/destructive flags.
- Preserve `ResizablePanel` widths and storage keys.

## Test data

Tests use demo mode; no real Azure/AKS credentials are required.
