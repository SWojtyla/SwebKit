# Status — Agent PoC

## Current State

`Planned`

**Jira:** not linked

## Progress Checklist

### Project Setup

- [ ] `src/SwebKit.Agents/SwebKit.Agents.csproj` created and added to `SwebKit.slnx`
- [ ] Project references `SwebKit.Core` and `SwebKit.Kubernetes`
- [ ] `MistralConfig` class created with `ApiKey`, `ApiEndpoint`, `Model`, `MaxTokens`
- [ ] API key stored via `ICredentialStore` with key `SwebKit-Agent:Mistral-ApiKey`
- [ ] Agent services registered in `MauiProgram.cs`

### Core PoC Implementation

- [ ] `IMistralClient` interface defined
- [ ] `MistralHttpClient` implemented (raw `HttpClient`, no SDK)
- [ ] `IAgentTool` interface defined
- [ ] `GetPodStatusTool` implemented (wraps `IAksClientFactory`)
- [ ] Single-turn loop: user query → build prompt → call Mistral → parse tool call → execute tool → send result back → return final response

### Validation

- [ ] Prototype running against a real AKS cluster
- [ ] Latency measured (P50, P95 across 10+ runs)
- [ ] Cost per query calculated from Mistral usage dashboard
- [ ] Quality evaluated against the test queries in `test-plan.md`
- [ ] Technical validation report written
- [ ] Go / Iterate / No-Go decision recorded

## Completed

_(nothing yet)_
