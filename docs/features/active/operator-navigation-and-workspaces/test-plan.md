# Test Plan - operator-navigation-and-workspaces

---

title: "Test Plan - operator-navigation-and-workspaces"
owner: "GitHub Copilot"
status: "Planned"
created: "2026-04-12"
updated: "2026-04-12"

---

## Goal

Validate that operators can search for resources accurately, revisit recent or favorite context quickly, and save/restore named investigation workspaces without stale state, broken routing, or fragile page coupling.

## Scope

- In scope: command palette precision, resource search, recents/favorites, named workspaces, route restore, and cross-page shell-state persistence.
- Out of scope: shell visual polish, environment readiness checks, and new domain-specific operator actions.

## Main scenarios (priority)

1. Scenario: command search - Expected result: command palette ranks commands appropriately for the current area and keyboard execution still works.
2. Scenario: resource search - Expected result: operators can search across supported resource types from one shell surface and navigate to the selected result.
3. Scenario: recent resource revisit - Expected result: recently visited resources are recorded and can be reopened quickly.
4. Scenario: favorites persistence - Expected result: favorite resources persist for the current environment and remain available after restart.
5. Scenario: named workspace save - Expected result: an operator can save a workspace from a supported page with meaningful route/resource/filter state.
6. Scenario: named workspace restore - Expected result: restoring a workspace reopens the correct route and supported page context without replaying stale async work.
7. Scenario: unsupported or stale workspace state - Expected result: restore degrades gracefully and explains what could not be restored.
8. Scenario: cross-page participation - Expected result: at least Service Bus, AKS, Observability, and Incident Timeline can contribute resource/workspace context through the same shell contract.
9. Scenario: rapid restore/navigation changes - Expected result: no disposed-component updates or stale restore flashes occur.
10. Scenario: dashboard pinned alignment - Expected result: dashboard pinned items and shell favorites are backed by the same canonical favorite model.

## Automated coverage

- Component tests: `tests/SwebKit.App.Tests`
- Cover `CommandPalette`, `TopBar`, any workspace/favorites shell components, and page integration points that publish or consume resource/workspace context.
- Add targeted coverage for `CommandRegistry`, `SelectionContext`, and `TabService` behavior where those services remain app-layer.
- Unit tests: `tests/SwebKit.Core.Tests`
- Cover resource reference normalization, workspace snapshot serialization, persistence migration, and version-safe restore behavior.
- End-to-end tests: `tests/SwebKit.E2E.Tests`
- Cover command palette search, favorite/recent resource navigation, workspace save/restore, and direct-route restore behavior.

## Test data and setup

- Supported resource fixtures for Service Bus namespaces/entities, AKS contexts/workloads, Redis caches, Storage accounts, Observability resources, and pipeline/project references.
- Named workspace fixtures that exercise valid, partially stale, and unsupported restore content.
- Both production and non-production environments to ensure safety context survives route/workspace restore.

## Manual checks

- Check: command palette precision - verify command vs resource ranking and keyboard-only execution.
- Check: favorite/recent resource ergonomics - verify operators can revisit context without going through the original page tree.
- Check: workspace trust - save an investigation, restart the app, restore it, and confirm the app lands in a believable state.
- Check: degraded restore - intentionally remove or rename a resource referenced by a saved workspace and verify graceful restore messaging.

## Regression risks & mitigations

- Risk: workspaces serialize page internals that cannot be restored safely. Mitigation: test only semantic snapshot contracts and version the payload.
- Risk: favorites and recents diverge across shell surfaces. Mitigation: ensure dashboard and shell surfaces share one canonical contract.
- Risk: workspace restore triggers duplicate loads or stale updates. Mitigation: apply cancellation-aware restore contracts and component tests for overlapping restores.

## Acceptance criteria

- Operators can search commands and resources from one shell-level workflow.
- Recent and favorite resources are persistent and coherent across shell surfaces.
- Named workspaces restore meaningful cross-page context without brittle component coupling.
- Core participating pages share one shell-level contributor model.
- No critical regressions appear in component, unit, or E2E coverage.

## Validation status

- Automated: Not started.
- Manual: Not started.

## Sign-off

- Approved by:
- Date:
- Conditions (if any):
