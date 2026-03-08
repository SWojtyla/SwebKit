# Decisions — Releases

## Key Design Decisions

- Pipeline links are stored per-project; stage names map environments to ADO stages (`EnvironmentStageMap`).
- Support both CI (YAML) and CD (classic) pipelines via `PipelineKind`.
- Authentication uses PAT only, stored via `ICredentialStore` and referenced from `AdoConnectionConfig.CredentialRef`.
- "Deploy All" is sequential (by `SortOrder`) and fail-fast on pipeline failure.
- No in-app log viewer: open the ADO run URL in browser for logs.

## Resolved Design Decisions (snapshot)

| Question                                       | Resolution                                                                                                                                                               |
| ---------------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------ |
| Pipeline links per-project or per-environment? | **Per-project.** Stages within each pipeline track environments via `EnvironmentStageMap`.                                                                               |
| CI and/or CD pipelines?                        | **Both.** Each project may have multiple CI and CD pipelines. `PipelineKind` differentiates them.                                                                        |
| Auth method                                    | **PAT only** (stored in Windows Credential Manager). Entra ID / OAuth deferred.                                                                                          |
| Multi-org                                      | **Single ADO org per SwebKit project.**                                                                                                                                  |
| Multi ADO-project                              | **Supported.** `AdoProjectName` lives on `PipelineLink`, not on the shared connection config.                                                                            |
| Log viewer                                     | **Not in scope.** "Open in Azure DevOps" browser link per run is sufficient.                                                                                             |
| Approval gates                                 | **In scope.** Surface pending approvals in the app and allow approving/rejecting inline.                                                                                 |
| Deploy All order                               | **Sequential** (by `SortOrder`), fail-fast on pipeline failure.                                                                                                          |
| Stage map — required?                          | **Yes, explicit setup required.** A missing `EnvironmentStageMap` entry for the current environment blocks the deploy; the UI shows a configuration warning on the card. |
