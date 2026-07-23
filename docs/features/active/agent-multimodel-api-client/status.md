# Status — Agent multi-modèle et pilotage sécurisé de l'API Client

## Current phase: Implementation

### Delivery order

| # | Milestone | Status |
|---|-----------|--------|
| 1 | Configuration/profils + client neutre + migration + tests | In Progress (code done, tests pending) |
| 2 | Détection de capacités et UI LM Studio | Partial (capability tester done, UI pending) |
| 3 | Historique typé, contexte actif et fiabilité de boucle | Partial (typed session done, loop/context pending) |
| 4 | Extraction du service API Client et synchronisation UI | Pending |
| 5 | Infrastructure proposal/diff/confirmation | Pending |
| 6 | Outils CRUD REST locaux, puis linked roots | Pending |
| 7 | Exécution HTTP confirmée | Pending |
| 8 | Durcissement sécurité, tests complets et documentation | Pending |

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

### What remains for milestone 1

- [ ] New unit tests: config migration, URL normalization, response parsing, capability detection
- [ ] Multi-provider settings UI (`AgentConfigForm.razor`) — still shows Mistral-only form
- [ ] Build verification and fix compilation errors
- [ ] Register `AgentCapabilityTester` in DI

### Key files created

- `src/SwebKit.Core/Domain/ProviderKind.cs`
- `src/SwebKit.Core/Domain/AgentProfile.cs`
- `src/SwebKit.Core/Domain/AgentProfilePresets.cs`
- `src/SwebKit.Agents/IAgentModelClient.cs`
- `src/SwebKit.Agents/OpenAiCompatibleAgentClient.cs`
- `src/SwebKit.Agents/AgentCapabilityTester.cs`

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
- `src/SwebKit.App/Hosting/SwebKitServiceCollectionExtensions.Agents.cs`
- `src/SwebKit.Agent.PocConsole/Program.cs`
- `tests/SwebKit.Agents.Tests/CoreServicesTests.cs`

### Notes

- Backward compatibility: old Mistral-only config migrates automatically to a Mistral profile.
- No API keys are persisted in plain text; credential store keys are logical references.
- `AgentConfigForm.razor` still references `ModelOverride` — needs multi-provider UI update.
