# Test Plan — Agent multi-modèle et pilotage sécurisé de l'API Client

## Unit tests — Agent config & migration

- [ ] Migration of old Mistral-only config creates a Mistral profile
- [ ] LM Studio preset: correct base URL, no credential key required
- [ ] Mistral preset: correct endpoint and credential key
- [ ] Generic preset: user-provided endpoint/model/credential
- [ ] AgentConfig with no profiles has safe defaults
- [ ] Active profile ID references an existing profile

## Unit tests — Neutral LLM client

- [ ] URL construction without double `/v1`
- [ ] Bearer auth only when API key is present
- [ ] Parsing: simple chat response
- [ ] Parsing: single tool call
- [ ] Parsing: multiple tool calls
- [ ] Parsing: null content in assistant response
- [ ] HTTP error produces clean error without payload leakage
- [ ] Cancellation token propagation
- [ ] Explicit timeout enforcement
- [ ] Max tool rounds limit reached → graceful message
- [ ] Duplicate call detection

## Unit tests — Capability detection

- [ ] `GET /models` available → model list parsed
- [ ] Mini chat call succeeds → ChatOnly classification
- [ ] Mini tool call succeeds → ToolCalling classification
- [ ] Server unreachable → clear diagnostic, application stable
- [ ] Server reachable but no model loaded → dedicated state

## Unit tests — Typed conversation session

- [ ] User/assistant/tool message sequences preserved correctly
- [ ] Tool call + tool result pairing is valid
- [ ] Trimming removes oldest complete exchange
- [ ] Profile/model change resets history

## Unit tests — API Client service (Phase 4+)

- [ ] CRUD local on nested trees; IDs, timestamps, node/request name sync
- [ ] CRUD linked using LinkedCollectionFileService primitives
- [ ] Content stamp conflict between preview and confirmation
- [ ] Move/reorder, duplicate, recursive delete
- [ ] Path traversal refusal, out-of-root target refusal
- [ ] Expired/refused/replayed/double-confirmed action
- [ ] HTTP execution impossible without confirmation
- [ ] HTTP response truncated and sanitized; capture rule announced

## UI/bUnit tests

- [ ] Profile form and visible migration
- [ ] Connection/capability test and chat-only mode display
- [ ] Diff/confirmation/refuse/expiration card
- [ ] External change received by open API Client page
- [ ] Dirty draft protected against agentic refresh
- [ ] Tool statuses and actionable errors
- [ ] DI host updates when new services injected

## Manual validation

1. LM Studio stopped: clear diagnostic, app stable
2. LM Studio running without model: dedicated state
3. Model without tools: chat works, actions disabled
4. Model with tools: read then propose local creation, confirm, immediate refresh
5. Local modification and deletion refused then accepted
6. Same flow in a linked root, then conflict from external modification
7. GET then POST preview and confirmation; no network request before confirmation
8. Restart and migration of a user with old Mistral config

## Quality commands

```powershell
# Agent tests
dotnet test tests/SwebKit.Agents.Tests

# API Client tests
dotnet test tests/SwebKit.Core.Tests --filter "ApiClient"
dotnet test tests/SwebKit.App.Tests --filter "ApiClient"

# Full build
dotnet build SwebKit.slnx
```
