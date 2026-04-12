# Test Plan - remove-environment-model

---

title: "Test Plan - remove-environment-model"
owner: ""
status: "In progress"
created: "2026-04-12"
updated: "2026-04-12"

---

## Goal

Validate that removing the abandoned local environment/profile model does not break shell startup, settings persistence, incident-timeline scoping, or Azure DevOps release evidence behavior.

## Scope

- In scope:
  - `ProfileRepository` migration from legacy multi-environment JSON to a single-config model
  - `AppStateService` API cleanup and any consumers in `SwebKit.App`
  - shell context cleanup in `MainLayout`, `ShellNavigation`, and `TopBar`
  - `IncidentWorkloadScope` shape and request-key changes across incident timeline backend and frontend
  - tests and docs affected by removed environment/profile UI and profile fixtures
- Out of scope:
  - demo-mode behavior
  - Azure DevOps API support for real pipeline/release environments
  - unrelated AKS, Service Bus, Redis, or Storage behavior changes

## Main scenarios (priority)

1. Scenario: load a legacy `profiles.json` containing `Environments` and `ActiveEnvironmentName` — Expected result: the app normalizes to a single `Config`, keeps the intended config values, and does not fail startup.
2. Scenario: save settings after migration — Expected result: persistence writes the simplified profile shape and subsequent loads use the same config without needing `ActiveEnvironmentName`.
3. Scenario: incident timeline refresh after removing `IncidentWorkloadScope.EnvironmentName` — Expected result: request fingerprints remain deterministic and evidence loading still works for the selected context/namespace/workload.
4. Scenario: Azure DevOps release evidence and pipeline environment views — Expected result: remote stage/environment metadata continues to work because only the local profile model was removed.
5. Scenario: shell and routed pages no longer render local environment labels — Expected result: no visible environment/profile UI remains while demo/production/status affordances still render.

## Automated coverage

- Unit tests:
  - `tests/SwebKit.Core.Tests` for profile migration, app state, incident scope models, and timeline aggregation
  - `tests/SwebKit.Azure.Tests`, `tests/SwebKit.Kubernetes.Tests`, and `tests/SwebKit.DevOps.Tests` for signal-source fixtures that currently seed `ActiveEnvironmentName` / `Environments`
- Component tests:
  - `tests/SwebKit.App.Tests` for shell/header behavior and any settings or incident-timeline projections affected by removed UI labels
- End-to-end tests:
  - `tests/SwebKit.E2E.Tests` for shell/header assertions, navigation, and incident-timeline smoke behavior

## Test data and setup

- Add legacy profile fixtures that include `Config`, `Environments`, and `ActiveEnvironmentName` so migration is exercised intentionally.
- Update single-config fixtures to omit deprecated fields entirely.
- Preserve demo-mode fixtures and fake providers; they are not part of this migration.

## Manual checks

- Check: shell startup with an existing local profile — steps: launch the app with a legacy `profiles.json`, confirm the shell loads, no profile-load error appears, and no environment label is shown.
- Check: settings persistence after save — steps: change one settings value, save, relaunch, confirm the value persists from the simplified profile format.
- Check: incident timeline investigation flow — steps: choose context, namespace, workload, refresh, and verify evidence, coverage strip, and mapping guidance still behave normally.
- Check: pipeline and release surfaces — steps: open pipeline detail and release detail, confirm Azure DevOps environment labels sourced from ADO still appear where relevant.

## Regression risks & mitigations

- Risk: profile migration drops data when multiple legacy environments exist.
  - Mitigation: document the winner-selection rule and add explicit migration tests for active-name and fallback behavior.
- Risk: scope-key changes invalidate request or cache behavior in incident timeline.
  - Mitigation: add deterministic key tests and run focused incident-timeline suites before broader validation.
- Risk: shell cleanup accidentally removes non-environment badges or accessibility hooks.
  - Mitigation: keep `Demo`, `Production`, and connection-status assertions; preserve `h1`-based navigation behavior.

## Acceptance criteria

- All high-priority scenarios pass.
- No core feature still depends on local `Environments` / `ActiveEnvironmentName` APIs.
- No visible local environment/profile label remains in the app shell or routed pages.
- Architecture and functionality docs reflect the simplified single-config model.

## Validation status

- Automated: Focused pass complete; full E2E blocked locally by CDP fixture startup failure before assertions ran
- Manual: Not started

## Sign-off

- **Approved by:**
- **Date:**
- **Conditions (if any):** Full Windows E2E pass before merge
