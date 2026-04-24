# Test Plan - winui3-migration

---

title: "Test Plan - winui3-migration"
owner: ""
status: "In Progress"
created: "2026-04-24"
updated: "2026-04-24"

---

## Goal

Validate that the WinUI migration preserves operator-facing feature parity while introducing a reusable UI foundation, global theming model, and consistent shell architecture rather than one-off page implementations.

## Scope

- In scope: WinUI shell architecture, reusable page primitives, theme application, shared workspace surfaces, and migrated domain workflows
- In scope: regression validation for Service Bus, AKS, Redis, Storage, Pipelines/Releases, Observability, Incident Timeline, and Settings as they adopt the shared foundation
- Out of scope: exact pixel-for-pixel parity with the MAUI CSS implementation, exploratory visual experiments, and non-Windows hosts

## Main scenarios (priority)

1. Scenario: shared shell foundation is applied across the host — Expected result: dashboard, settings, and migrated workspaces render through a consistent scaffold, state treatment, and shell chrome.
2. Scenario: theme changes apply globally — Expected result: changing the selected theme updates shell chrome, shared cards, banners, and migrated pages without page-specific breakage or a restart requirement, including curated swaps that stay within the same dark/light family.
3. Scenario: migrated workspaces adopt reusable primitives instead of bespoke layouts — Expected result: Service Bus, AKS, and subsequent pages use shared section/state/detail patterns and keep feature behavior intact.
4. Scenario: production, demo, warning, and recovery cues stay consistent — Expected result: destructive and stateful operator cues are recognizable across shell and workspace contexts.
5. Scenario: cutover-critical feature parity remains intact — Expected result: each migrated domain preserves its documented workflows while fitting the shared WinUI architecture.
6. Scenario: AKS route remains responsive during bootstrap — Expected result: the page paints immediately, shows loading state promptly, and remains visibly responsive while contexts, namespaces, and pods load.

## Automated coverage

- Build validation: `build-winui` remains green after each shared-foundation change and after each domain adopts the new primitives.
- Unit tests: existing `SwebKit.Core.Tests`, `SwebKit.Azure.Tests`, `SwebKit.Kubernetes.Tests`, and `SwebKit.DevOps.Tests` continue to protect domain behavior while the host changes.
- ViewModel coverage target: add focused tests for shell/theme/navigation viewmodels and shared state logic when the WinUI test project is introduced.
- End-to-end coverage: defer full host automation until the WinUI shell is stable, then add smoke coverage for shell navigation, theme switching, and one representative workflow per major workspace.

## Test data and setup

- Demo mode remains available for shell and workflow smoke validation without live dependencies.
- Live validation requires representative Service Bus, AKS, Redis, Storage, DevOps, and Observability configuration in `%APPDATA%/SwebKit` plus credential-store entries.
- Theme validation requires multiple curated theme dictionaries to exist and be selectable through persisted user settings.

## Manual checks

- Check: shared shell coherence — steps: open Dashboard, Settings, Service Bus, and AKS; confirm title/meta/action areas, banners, cards, and status cues follow one consistent structure.
- Check: global theme application — steps: switch between curated themes, including dark-to-dark and light-to-light swaps, navigate across migrated pages, and confirm shell chrome and page surfaces update consistently.
- Check: resize and density behavior — steps: test narrow and wide window states, verify primary/detail panes and toolbar regions remain usable and intentional.
- Check: state treatment consistency — steps: validate loading, empty, error, not-configured, demo, and production-warning states on at least two workspaces.
- Check: proving-ground page adoption — steps: confirm `Settings`, `ServiceBus`, and `AKS` use the shared primitives before Redis/Storage/Pipelines/Observability work begins.
- Check: AKS pod-log slice — steps: open AKS, launch logs from a pod row, switch container/range/live settings, apply a text filter, and confirm the native log panel follows the selected pod without reopening the page.
- Check: AKS first-paint responsiveness — steps: open AKS from another route, confirm the page shell paints before cluster data arrives, and verify the app does not feel blocked while the initial bootstrap runs.
- Check: AKS compact diagnostics state — steps: open AKS with no pod selected, confirm the page shows only a compact diagnostics hint, then select a pod and verify the full diagnostics/log surface expands in place.

## Regression risks & mitigations

- Risk: page-level XAML drift creates visually inconsistent workspaces — Mitigation: require shared scaffold and resource-token adoption before broadening migration.
- Risk: theme support stays a basic light/dark toggle while the MAUI app expects curated variants — Mitigation: validate the semantic theme-coordinator path before more workspaces depend on it.
- Risk: shell refactors break already-migrated workflows — Mitigation: keep `build-winui` green and re-run proving-ground manual checks after each shared UI change.
- Risk: domain parity regresses while pages are refactored onto shared primitives — Mitigation: preserve the domain parity checklist in `frontend.md` and validate one feature slice at a time.

## Acceptance criteria

- The shared WinUI UI foundation exists: semantic resource dictionaries, curated theme application, reusable shell primitives, and a page/workspace scaffold.
- `Settings`, `ServiceBus`, and `AKS` adopt the shared primitives without losing documented behavior.
- Theme switching and shell state cues work consistently across migrated pages.
- Cutover-critical workstreams continue to be validated against the parity checklist in `frontend.md`.

## Validation status

- Automated: `build-winui` green after the same-family theme refresh fix and the AKS pod-log slice
- Manual: Not started

## Sign-off

- **Approved by:**
- **Date:**
- **Conditions (if any):**
