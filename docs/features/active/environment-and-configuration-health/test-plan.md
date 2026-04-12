# Test Plan - environment-and-configuration-health

---

title: "Test Plan - environment-and-configuration-health"
owner: "GitHub Copilot"
status: "Planned"
created: "2026-04-12"
updated: "2026-04-12"

---

## Goal

Validate that SwebKit can explain readiness clearly and safely for the current configuration: what is configured, what credentials exist, which workflows are ready, and what the operator should do next when something is missing.

## Scope

- In scope: first-run checklist, credential/config health, connection-health overview, configuration-gap summaries, and Azure-focused readiness summaries.
- Out of scope: new domain operations, shell navigation improvements, and mutating repair actions against Azure resources.

## Main scenarios (priority)

1. Scenario: first run with minimal profile data - Expected result: the app shows a clear checklist with actionable configuration CTAs.
2. Scenario: partially configured environment - Expected result: the app distinguishes configured-but-not-ready from fully ready.
3. Scenario: missing credential reference - Expected result: the app explains which referenced credential is absent without exposing secret values.
4. Scenario: Azure identity readiness gap - Expected result: Observability, Storage, or AKS readiness can explain likely `DefaultAzureCredential` prerequisites without pretending the workflow is healthy.
5. Scenario: connection-health overview - Expected result: Service Bus, AKS, Redis, Storage, Pipelines, and Incident Timeline prerequisites show configured/not configured/ready/error states accurately.
6. Scenario: configuration-gap summary - Expected result: operators can see meaningful missing or risky config without diffing secrets or unstable runtime fields.
7. Scenario: profile-load failure - Expected result: readiness UI respects blocked profile persistence and does not imply that a broken config file was repaired.
8. Scenario: probe timeout or partial failure - Expected result: health reporting remains useful and explicit even when one probe fails or times out.
9. Scenario: production environment - Expected result: readiness and checklist surfaces keep production context visible and avoid encouraging unsafe trial actions.

## Automated coverage

- Component tests: `tests/SwebKit.App.Tests`
- Cover checklist rendering, CTA deep links into Settings, health-state badges/cards, configuration-gap UI, and blocked-persistence messaging.
- Unit tests: `tests/SwebKit.Core.Tests`
- Cover report normalization, configuration-gap logic, readiness-state calculation, and health-result aggregation.
- Integration tests: `tests/SwebKit.Azure.Tests`, `tests/SwebKit.Kubernetes.Tests`, `tests/SwebKit.DevOps.Tests`
- Cover any read-only probe or adapter logic added for Service Bus, AKS, Storage, Observability, or DevOps readiness.
- End-to-end tests: `tests/SwebKit.E2E.Tests`
- Cover first-run or partially configured flows, settings handoff, and readiness view behavior after restart.

## Test data and setup

- Fresh or sparse profile data with no usable app config.
- Partially configured profiles that intentionally lack credentials or resource configuration in one or more capability areas.
- Credential-store fixtures where keys exist, are missing, or point to incomplete configuration.
- Probe fixtures for success, timeout, not-configured, and auth-failure outcomes.

## Manual checks

- Check: first-run onboarding - verify the checklist tells the operator what to configure first and where to do it.
- Check: credential hygiene - inspect health UI and confirm no secret values or sensitive details are rendered.
- Check: configuration-gap summary - confirm the app highlights meaningful missing prerequisites and stable next steps.
- Check: readiness trust - force a partial failure and confirm the app remains explicit about what is and is not ready.

## Regression risks & mitigations

- Risk: readiness probes trigger expensive or mutating external operations. Mitigation: keep checks read-only, budgeted, and covered by tests.
- Risk: health cards overstate readiness based on weak signals. Mitigation: distinguish configured, ready, degraded, and unknown explicitly.
- Risk: configuration-gap output becomes noisy because of non-deterministic or low-signal fields. Mitigation: normalize and whitelist fields before summarizing.

## Acceptance criteria

- Operators can tell what to configure next from a first-run or partially configured state.
- Credential and readiness reporting does not leak secrets.
- Configuration-gap summaries surface meaningful missing or risky setup for the current profile.
- Major Azure-focused workflows have an explicit readiness summary.
- Partial failures remain visible instead of being silently ignored.

## Validation status

- Automated: Not started.
- Manual: Not started.

## Sign-off

- Approved by:
- Date:
- Conditions (if any):
