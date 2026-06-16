# Test Plan - style-system-polish-9

---

title: "Test Plan - style-system-polish-9"
owner: ""
status: "Review"
created: "2026-06-14"
updated: "2026-06-14"

---

## Goal

Validate that additional style-system migrations reduce drift without changing feature behavior or degrading the current visual direction.

## Scope

### In scope

- Focused component tests for each migrated feature area.
- App build with local MSIX signing disabled.
- Style inventory before and after each migration wave.
- Manual visual review for migrated dark/light theme surfaces.

### Out of scope

- Full app E2E visual regression automation unless a migrated area already has stable E2E coverage.
- Perfect removal of every raw button/select.
- Removal of compatibility aliases before inventory proves it is safe.

## Automated Scenarios

| Area                     | Expected focused validation                                                                                                |
| ------------------------ | -------------------------------------------------------------------------------------------------------------------------- |
| Incident Timeline config | `dotnet test tests/SwebKit.App.Tests/SwebKit.App.Tests.csproj --filter FullyQualifiedName~IncidentTimelineConfigFormTests` |
| Dashboard                | Dashboard shared/component tests and app build                                                                             |
| Redis                    | `RedisKeyDetailTests` and related Redis component tests                                                                    |
| Observability            | Observability tab tests touched by copy/export controls                                                                    |
| Pipelines/Releases       | Existing Pipelines/Releases tests touched by filter/form migration                                                         |
| Overall                  | `dotnet build src/SwebKit.App/SwebKit.App.csproj /p:AppxPackageSigningEnabled=false`                                       |

## Manual Checks

- Check migrated controls in dark and light themes.
- Confirm compact toolbar buttons preserve their prior density.
- Confirm dropdowns/selects remain readable in native OS popups.
- Confirm destructive actions still communicate risk clearly.
- Confirm no page header action becomes visually oversized.

## Acceptance Criteria

- Focused tests pass for each migrated area.
- App build passes with local MSIX signing disabled.
- Inventory reaches the target thresholds in `index.md` or documents justified exceptions.
- Feature status includes final inventory counts and exceptions.
- No migrated surface loses the visual identity the maintainer wants to preserve.

## Validation Status

- Automated: Passed
- Manual: Not started
