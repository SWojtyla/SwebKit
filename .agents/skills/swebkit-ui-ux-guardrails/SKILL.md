---
name: SwebKit UI/UX Quality Guardrails
description: Pre-merge checklist and conventions to keep React/Tauri SwebKit UI consistent, MAUI-parity correct, and free of "looks like it works" bugs.
---

# SwebKit UI/UX Quality Guardrails

## Scope

Use this skill for every SwebKit React/Tauri feature, fix, or MAUI-parity change that touches the web UI.

## Before writing code

1. Read `docs/README.md` and `docs/features/README.md` for the feature model.
2. Open or create `docs/features/active/<feature>/index.md` and `technical-plan.md` per the repo's documentation rules.
3. List acceptance criteria that explicitly cover:
   - empty, loading, error, and single-item states
   - values with special characters (`/`, spaces, unicode, URL-encoded chars) in resource names or IDs
   - keyboard and accessibility basics (`aria-expanded`, focus, `Escape` to close)
   - demo-mode behavior
   - MAUI/Blazor parity if the feature already exists there

## During implementation

1. Reuse shared components (`Button`, `Dropdown`, `ResizablePanel`, `NotificationSystem`, `YamlViewer`, `PodLogView`, etc.) before inventing new markup.
2. Add `data-testid` to every control that changes state, not only to outer containers.
3. Thread `useNotification()` through every mutating action so the user gets success/error feedback.
4. Prefer query/body parameters over route segments for resource names that can contain `/` or `%`.
5. Keep TanStack Query keys stable and invalidate base keys (e.g. `['aks-deployments']`) from mutations so namespace-token queries refresh.
6. When adding a new tab, panel, or viewer that already exists in MAUI/Blazor, copy the behavior set (buttons, ranges, filters, copy/export/clear actions) and not just the visual layout.

## Before opening a PR

1. Run the full local validation matrix:
   - `(cd web && npm run build)`
   - `(cd src-sidecar && dotnet build)`
   - `(cd tests/SwebKit.Sidecar.Tests && dotnet test)`
   - `(cd web && npx playwright test)`
2. If a change adds or modifies a UI flow, add or update a Playwright spec that exercises the happy path and one failure path.
3. For MAUI-parity work, include a side-by-side description in the PR body showing which MAUI behavior was copied.
4. Verify every `mutate` or `fetch` has `onSuccess`/`onError` feedback and invalidates the right query key.
5. Check that disabled buttons and loading states are visually obvious; never leave a click with no response.

## Known escape hatches

- Native file inputs, `<select>` dropdowns, and `window.confirm` are not reliably automatable through `computer` clicks. Provide an alternative automation path (dispatch events, hidden inputs, or console JS) and document it in the skill or test plan.
- Demo mode must be enabled before AKS/Service Bus/Storage pages render meaningful data; the toggle is in the top-right header (`title="Toggle demo mode"`).
