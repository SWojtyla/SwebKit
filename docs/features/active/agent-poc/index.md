# Agent PoC

## Goal

Validate the core hypothesis: Mistral AI can understand SwebKit's AKS, Service Bus, and Observability domain well enough to provide actionable operational insights that justify building the full agent (Phase 1–3).

## Outcomes

- Go / Iterate / No-Go decision on full agent integration
- Baseline latency and cost measurements for `mistral-medium`
- Working end-to-end prototype: `GetPodStatusTool` with real AKS data
- Technical validation report

## Scope

- New `SwebKit.Agents` project added to `SwebKit.slnx`
- `IMistralClient` + `IAgentTool` interfaces (minimal, disposable)
- One tool: `GetPodStatusTool` backed by the existing `IAksClientFactory`
- API key stored via existing `ICredentialStore` (Windows DPAPI)
- Console / minimal interface for test conversations
- Manual quality, latency, and cost measurement

## Out of Scope

Everything in Phase 1–3 of `docs/agents/`. This is a throwaway validation experiment — no production code, no full error handling, no UI integration.

## Dependencies

- Mistral API key (stored via `ICredentialStore`, key `SwebKit-Agent:Mistral-ApiKey`)
- Access to a live AKS cluster or SwebKit demo data

## Detailed Design

| Document                                                                               | Purpose                                                    |
| -------------------------------------------------------------------------------------- | ---------------------------------------------------------- |
| [`docs/agents/phase-0-poc.md`](../../../agents/phase-0-poc.md)                         | Validation questions, success criteria, decision framework |
| [`docs/agents/architecture.md`](../../../agents/architecture.md)                       | Technical design reference                                 |
| [`docs/agents/security-considerations.md`](../../../agents/security-considerations.md) | API key handling constraints                               |
| [`docs/agents/testing-strategy.md`](../../../agents/testing-strategy.md)               | Manual validation approach                                 |
