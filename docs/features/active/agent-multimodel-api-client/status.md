# Status — Agent multi-modèle et pilotage sécurisé de l'API Client

## Current phase: Implementation

### Delivery order

| # | Milestone | Status |
|---|-----------|--------|
| 1 | Configuration/profils + client neutre + migration + tests | Done |
| 2 | Détection de capacités et UI LM Studio | Done |
| 3 | Historique typé, contexte actif et fiabilité de boucle | Done |
| 4 | Extraction du service API Client et synchronisation UI | Done |
| 5 | Infrastructure proposal/diff/confirmation | Done |
| 6 | Outils CRUD REST locaux, puis linked roots | Done |
| 7 | Exécution HTTP confirmée | Done |
| 8 | Durcissement sécurité, tests complets et documentation | Done |

### What was done (session 2025-07-23)

**Phase 0 — Feature documentation**
- Created `docs/features/active/agent-multimodel-api-client/` with `index.md`, `status.md`, `decisions.md`, `test-plan.md`.

**Phase 1 — Multi-provider config model**
- Created `src/SwebKit.Core/Domain/ProviderKind.cs` — enum (LmStudio, OpenAiCompatible, Mistral).
- Created `src/SwebKit.Core/Domain/AgentProfile.cs` — typed profile with provider, base URL, model, credential key, temperature, max tokens, timeout, capability state.
- Created `src/SwebKit.Core/Domain/AgentProfilePresets.cs` — factory presets for LM Studio, Mistral, generic.
- Evolved `src/SwebKit.Core/Domain/AgentConfig.cs` — replaced ModelOverride with Profiles list + ActiveProfileId; added `Migrate()` and `GetActiveProfile()`.
- Updated `src/SwebKit.Core/Configuration/UserSettingsRepository.cs` — calls `AgentConfig.Migrate()` on load.

**Phase 2 — Neutral LLM contract + client**
- Created `src/SwebKit.Agents/IAgentModelClient.cs` — `IAgentModelClient` interface + typed DTOs (`AgentMessage`, `AgentToolCall`, `AgentModelRequest`, `AgentModelResponse`, `AgentChatResult`, `AgentFinishReason`).
- Created `src/SwebKit.Agents/OpenAiCompatibleAgentClient.cs` — replaces `MistralHttpClient`; resolves active profile, normalizes base URL (no double `/v1`), Bearer auth only when key present, tolerant parsing, cancellation, timeout, max tool rounds, duplicate call detection, error sanitization.
- Created `src/SwebKit.Agents/AgentCapabilityTester.cs` — tests server reachability (`GET /models`), mini chat, mini tool call; classifies as ChatOnly or ToolCalling.

**Phase 3 — Typed conversation session (partial)**
- Updated `src/SwebKit.Agents/ConversationSession.cs` — uses typed `AgentMessage` instead of anonymous `object`.
- Updated `src/SwebKit.Agents/AgentChatService.cs` — uses `IAgentModelClient`, `AgentModelRequest`, `AgentChatResult`; applies active profile temperature/maxTokens.

**DI and wiring**
- Updated `src/SwebKit.App/Hosting/SwebKitServiceCollectionExtensions.Agents.cs` — registers `IAgentModelClient` via `AddHttpClient<>`, removed `MistralConfig` and `IMistralClient` registrations.
- Updated `src/SwebKit.Agent.PocConsole/Program.cs` — uses `IAgentModelClient`, typed history, configures Mistral profile from env var.

**Legacy cleanup**
- Marked `IMistralClient`, `MistralHttpClient`, `MistralConfig` as `[Obsolete]`.
- Removed Mistral references from doc comments across `IAgentChatService`, `IAgentToolRegistry`, `IAgentContextBuilder`, `IAgentTool`.

**Tests updated**
- `tests/SwebKit.Agents.Tests/CoreServicesTests.cs` — `ConversationSessionTests` updated to use `AgentMessage`.

### What was done (session 2025-07-24)

- Registered `AgentCapabilityTester` in DI.
- Fixed compilation errors: unused variable, Index conversion, enum FluentSelect binding.
- Added `InternalsVisibleTo` for test project in `SwebKit.Agents.csproj`.
- Replaced `MistralConfigTests` with `AgentProfilePresetsTests` (5 tests).
- Created `AgentConfigMigrationTests` (8 tests: migration, idempotency, active profile).
- Created `OpenAiCompatibleAgentClientTests` (20+ tests: URL normalization, response parsing, wire format).
- Rewrote `AgentConfigForm.razor` as multi-provider UI with profile selector, provider dropdown, connection test with capability badges, add/delete profiles.
- Build verification: Agents, App, PocConsole, Tests — all pass. 104 tests green.

### Milestone 1 — Complete

All items done:
- [x] New unit tests: config migration, URL normalization, response parsing
- [x] Multi-provider settings UI (`AgentConfigForm.razor`)
- [x] Build verification and fix compilation errors
- [x] Register `AgentCapabilityTester` in DI

### What was done (session 2025-07-24, Phase 3)

**Phase 3 — Orchestrator improvements**
- Added `ToolKind` (Read/Mutate) and `ToolRisk` (None/Low/High) enums to `IAgentTool`.
- Added default interface members: `Kind`, `Risk`, `RequiredCapability` on `IAgentTool`.
- Enriched `ToolDefinition` with `Kind`, `Risk`, `RequiredCapability` fields.
- Updated `AgentToolRegistry` to propagate metadata into `ToolDefinition`.
- Rewrote `AgentChatService` with:
  - Structured system prompt in sections (role, context, tool policy, confirmation policy, limits, format).
  - Capability-based tool filtering (chat-only mode disables tools).
  - Step tracking: `AgentChatStep` records for each tool call/result.
  - Status reporting: `AgentStatus` enum (Thinking, ReadingContext, PreparingChange, AwaitingConfirmation, Applying, Done, Failed).
  - Error handling with `AgentStatus.Failed`.
- Added `AgentChatStep`, `AgentActionSummary`, `AgentStatus` types to `IAgentChatService.cs`.
- Enriched `AgentChatReply` with `Steps`, `PendingActions`, `Status`.
- Updated `AgentChatPanel.razor`:
  - `ChatMessage` record includes `Status` and `Steps`.
  - Steps display with tool call/result icons and timing.
  - Failed status indicator.
- Created `ToolMetadataTests` (4 tests: defaults, mutation override, interface defaults, override).
- All 108 tests pass, all projects build.

### What was done (session 2025-07-24, Phases 4-8)

**Phase 4 — API Client service extraction**
- Created `IApiClientAgentService` contract in `SwebKit.Core.Abstractions` with:
  - `SearchRequestsAsync`, `GetRequestAsync`, `CreateRequestAsync`, `UpdateRequestAsync`,
    `DuplicateRequestAsync`, `MoveRequestAsync`, `RenameFolderAsync`, `DeleteRequestAsync`,
    `DeleteFolderAsync`, `GetCollectionsAsync`
- Created `ApiClientAgentService` implementation in `SwebKit.Core.Services`:
  - Operates on both local (`CollectionRepository`) and linked (`LinkedCollectionFileService`) collections
  - Secret masking in `ApiRequestSnapshot` (authorization, token, api-key, password, credential headers)
  - Publishes `ApiClientDataChanged` events via `IAppEventBus` after each mutation
- Created DTOs: `ApiRequestSnapshot`, `ApiRequestSummary`, `ApiClientMutationResult`, `ApiClientDataChanged`

**Phase 5 — Proposal/diff/confirmation infrastructure**
- Created `IAgentActionCoordinator` and `AgentActionCoordinator` in `SwebKit.Agents`:
  - Bounded in-memory store (max 10 pending actions)
  - Expiration (5 min default), cleanup, reject
  - `PendingAgentAction` with type, summary, target, risk, preview, expected fingerprint
- Created `AgentActionType` enum (Create/Update/Delete/Duplicate/Move/Rename/DeleteFolder/ExecuteHttpRequest)
- Created `AgentActionRisk` enum (None/Low/High)

**Phase 6 — API Client V1 tools**
- Created 5 agent tools in `SwebKit.Agents.Tools.ApiClient`:
  - `search_api_requests` — search/list with IDs, paths, methods, origin (Read)
  - `get_api_request` — full read with secrets masked (Read)
  - `propose_api_request_change` — create/update/duplicate/move proposals (Mutate, Low risk)
  - `propose_api_request_delete` — explicit deletion proposal (Mutate, High risk)
  - `prepare_api_request_execution` — prepare HTTP execution with confirmation (Mutate, High risk)
- All tools registered in DI via `SwebKitServiceCollectionExtensions.AddSwebKitAgents`

**Phase 7 — Confirmed HTTP execution**
- Created `AgentActionApplier` in `SwebKit.Agents`:
  - Dispatches confirmed actions to appropriate services
  - Fingerprint validation for freshness check
  - Single-use enforcement (prevents double-apply)
  - HTTP execution via `IHttpRequestExecutor` (wired in UI confirmation handler)

**Phase 8 — Security hardening and tests**
- Secret masking in `ApiClientAgentService.BuildSnapshot` (authorization, token, api-key, password, credential)
- Body preview truncation (200 chars max)
- Created `AgentActionCoordinatorTests` (10 tests: registration, expiration, pending filter, reject, cleanup, bounded store, confirm/apply state)
- All 118 tests pass, all projects build.

### All milestones complete

### Key files created

- `src/SwebKit.Core/Domain/ProviderKind.cs`
- `src/SwebKit.Core/Domain/AgentProfile.cs`
- `src/SwebKit.Core/Domain/AgentProfilePresets.cs`
- `src/SwebKit.Agents/IAgentModelClient.cs`
- `src/SwebKit.Agents/OpenAiCompatibleAgentClient.cs`
- `src/SwebKit.Agents/AgentCapabilityTester.cs`
- `tests/SwebKit.Agents.Tests/AgentProfilePresetsTests.cs`
- `tests/SwebKit.Agents.Tests/AgentConfigMigrationTests.cs`
- `tests/SwebKit.Agents.Tests/OpenAiCompatibleAgentClientTests.cs`

### Key files modified

- `src/SwebKit.Core/Domain/AgentConfig.cs`
- `src/SwebKit.Core/Configuration/UserSettingsRepository.cs`
- `src/SwebKit.Agents/ConversationSession.cs`
- `src/SwebKit.Agents/AgentChatService.cs`
- `src/SwebKit.Agents/IAgentChatService.cs`
- `src/SwebKit.Agents/IAgentToolRegistry.cs`
- `src/SwebKit.Agents/IAgentContextBuilder.cs`
- `src/SwebKit.Agents/IMistralClient.cs`
- `src/SwebKit.Agents/MistralHttpClient.cs`
- `src/SwebKit.Agents/MistralConfig.cs`
- `src/SwebKit.Agents/Tools/IAgentTool.cs`
- `src/SwebKit.Agents/SwebKit.Agents.csproj`
- `src/SwebKit.App/Hosting/SwebKitServiceCollectionExtensions.Agents.cs`
- `src/SwebKit.App/Components/Pages/AgentConfigForm.razor`
- `src/SwebKit.Agent.PocConsole/Program.cs`
- `tests/SwebKit.Agents.Tests/CoreServicesTests.cs`

### Next steps (milestone 3+)

- Enrich `AgentContextBuilder` with API Client snapshot (active page, selected collection/request, dirty state)
- Add tool metadata (read vs mutation, required capability, risk level)
- Improve loop reliability: JSON validation, structured error correction, duplicate detection
- Expose step/tool/pending-proposal info in `AgentChatReply`
- UI statuses: thinking, reading context, preparing change, awaiting confirmation, applying, done/failed

### Notes

- Backward compatibility: old Mistral-only config migrates automatically to a Mistral profile.
- No API keys are persisted in plain text; credential store keys are logical references.
- App build requires `-p:AppxPackageSigningEnabled=false` due to certificate issue unrelated to this feature.
