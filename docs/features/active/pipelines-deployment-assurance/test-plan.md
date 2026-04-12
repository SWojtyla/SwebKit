# Test Plan - pipelines-deployment-assurance

---

title: "Test Plan - pipelines-deployment-assurance"
owner: "GitHub Copilot"
status: "Not started"
created: "2026-04-12"
updated: "2026-04-12"

---

## Goal

Validate that the Pipelines/Releases hub can show approval urgency, classify failures, detect runtime drift, and run bounded post-deploy validation against AKS and Observability without regressing the current Azure DevOps workflow.

## Scope

- In scope: approval age policy, failure classification, runtime-binding authoring, drift comparison, validation snapshot persistence, partial-source handling, and page-local UX regression safety.
- Out of scope: rollback automation, release governance changes outside the UI, and broad cross-cluster deployment monitoring.

## Main scenarios (priority)

1. Scenario: Pending approvals exceed the configured warning or breach window. - Expected result: approvals render age plus SLA state, sort correctly, and remain actionable through the existing approve or reject flow.
2. Scenario: A pending production approval is aging. - Expected result: the card shows the same urgency signals without bypassing the existing typed `CONFIRM` production protection.
3. Scenario: A failed run stops during build or test stages. - Expected result: the run is classified as build/test failure rather than rollout failure.
4. Scenario: A run is blocked on approval or checks. - Expected result: the classification surfaces `Approval gate` or equivalent rather than generic failure.
5. Scenario: A deployment completed in ADO but AKS is running a different image tag than the release target. - Expected result: drift renders as `Drifted` with enough detail to explain intended versus observed runtime state.
6. Scenario: Runtime binding data is missing. - Expected result: drift and validation render as `Unknown` or `Not configured`, not as matched.
7. Scenario: The operator manually runs deployment validation after a rollout. - Expected result: AKS readiness and Observability health are queried, a validation result is stored, and the UI updates with the outcome.
8. Scenario: Only one validation source is available. - Expected result: the stored snapshot is partial, the UI says which source is missing, and the rest of the Pipelines/Releases page continues to work.
9. Scenario: The operator switches projects or selection while an assurance load is in flight. - Expected result: stale validation or drift results do not overwrite the latest selection.
10. Scenario: Demo mode is active. - Expected result: deterministic approval ages, failure categories, drift states, and validation snapshots are available for UX and test coverage.

## Automated coverage

- Unit tests: `tests/SwebKit.Core.Tests`
- Add likely new tests such as `ApprovalAgingPolicyTests.cs`, `PipelineFailureClassifierTests.cs`, and `DeploymentAssuranceServiceTests.cs`.
- Extend release persistence coverage so `ReleaseRepository` can round-trip additive runtime bindings and validation snapshots.
- DevOps-focused tests: `tests/SwebKit.DevOps.Tests`
- Extend `DevOpsReleaseTimelineSignalSourceTests.cs` where release or deployment snapshot semantics overlap.
- Add likely new tests for DevOps run-to-classification mapping and approval enrichment.
- App tests: `tests/SwebKit.App.Tests`
- Add likely new tests such as `ApprovalCenterTests.cs`, `PipelineDetailTests.cs`, `PipelineActivityTests.cs`, and `ReleaseDetailAssuranceTests.cs`.
- Cover loading, empty, partial, and stale-selection states for new assurance surfaces.
- Supporting runtime tests:
- Add focused tests in `tests/SwebKit.Kubernetes.Tests` and `tests/SwebKit.Core.Tests` or `tests/SwebKit.App.Tests` for AKS or Observability validation adapters depending on where the final seam lands.
- CI gates: all new assurance-focused tests pass and existing Pipelines/Releases tests remain green.

## Test data and setup

- Deterministic ADO run fixtures covering build failure, approval wait, deploy failure, and post-deploy health failure.
- Approval fixtures with created-on timestamps spanning fresh, warning, and breached SLA windows.
- Release record fixtures with target tags plus runtime bindings for AKS deployment name, namespace, container name, and Observability role or resource.
- Validation fixtures for `Passed`, `Warning`, `Failed`, `Partial`, and `Not configured` outcomes.
- Cancellation harnesses that change project, pipeline, or release selection mid-load.

## Manual checks

- Check: Approval aging usability - steps
- Open Approvals, verify age and SLA badges, then approve or reject a non-production item to confirm the new urgency indicators do not interfere with the existing flow.
- Check: Production safety - steps
- Open a production approval, verify typed `CONFIRM` is still required, and confirm that assurance indicators do not imply the action is automatically safe.
- Check: Drift explanation - steps
- Open a release or pipeline with a bound runtime target and verify the drift UI clearly states intended tag versus observed runtime tag or state.
- Check: Validation loop - steps
- Trigger a manual validation on a completed deployment, verify progress and final outcome, and confirm the result persists after navigating away and back.

## Regression risks & mitigations

- Risk: assurance queries make the Pipelines hub too slow. - Mitigation: lazy-load detailed assurance only for the active selection and keep validation manual.
- Risk: drift is shown as a hard failure when bindings are incomplete. - Mitigation: explicit `Unknown` and `Not configured` states plus tests for incomplete bindings.
- Risk: failure classification becomes brittle if stage-name heuristics are the only source. - Mitigation: combine run timeline state, waiting-stage metadata, and explicit validation outcomes where available.
- Risk: partial AKS or Observability availability is rendered as healthy. - Mitigation: persist partial-source metadata and assert it in tests.

## Acceptance criteria

- All high-priority scenarios pass in focused test slices.
- Approval aging remains additive and does not bypass existing approval safety controls.
- Drift detection requires explicit bindings and never silently guesses ownership.
- Validation results are persisted and remain clearly advisory.
- Release and architecture docs are updated together with implementation.

## Validation status

- Automated: Not started
- Manual: Not started

## Sign-off

- Approved by:
- Date:
- Conditions (if any):
