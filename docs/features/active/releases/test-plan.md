# Test Plan

## Objectives

- Verify pipeline discovery, trigger, and status polling flows for both YAML (CI) and classic CD pipelines.
- Validate sequential "Deploy All" orchestration and fail-fast behavior.
- Verify approval gate detection and approve flow from UI.
- Ensure PAT storage and `CredentialRef` usage behaves securely and resiliently.

## Tests

- Unit tests
  - `IAzureDevOpsClient` serialization and mapping unit tests
  - `PipelineLink` stage mapping validation
  - Deploy orchestration state machine (success/fail paths)

- Integration tests (mocked ADO)
  - Trigger YAML pipeline, poll to completion
  - Trigger classic release, poll to completion
  - Waiting-for-approval detection and approve API call

- End-to-end (app-level, mocked backend)
  - Add pipeline, map stages, Deploy single pipeline
  - Deploy All (multiple pipelines) — success and fail-fast scenarios
  - Approve gate via UI

## Validation steps

- Run unit tests: `dotnet test` for relevant test projects
- Run UI component tests (bUnit) for Blazor components
- Verify credential reference is stored, PAT is not persisted in repo
