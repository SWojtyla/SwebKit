# Decisions — Agent multi-modèle et pilotage sécurisé de l'API Client

## D1: OpenAI-compatible protocol as V1 boundary

The agent loop uses the OpenAI `/chat/completions` protocol with `tool_calls` as the common denominator. LM Studio, generic OpenAI-compatible endpoints, and Mistral all speak this protocol. Provider-specific divergences are handled in the adapter layer only.

## D2: Explicit profiles, no automatic fallback

The active profile is explicit and observable. No automatic provider switching. If a provider fails, the user gets a diagnostic and must choose to switch manually.

## D3: Strict tool-calling detection

A capability test determines whether the model supports native tool calling. If it fails, the agent falls back to chat-only mode with tools disabled and a clear diagnostic. No prompt-JSON fallback for tool calling in V1.

## D4: Proposal/confirmation protocol

All mutations and HTTP executions go through a propose → preview/diff → confirm → apply flow. No model-generated argument directly triggers a write or HTTP call. A confirmation is per-action, not a blanket "yes".

## D5: Core-owned mutations

API Client mutations are owned by a Core service (`IApiClientAgentService`), not by the Blazor page or the agent. Both the page and agent tools consume the same service to avoid divergent implementations.

## D6: REST-only V1 scope

V1 covers REST requests and organization (collections/folders/requests, local and linked). No agentic management of environments, variables, auth, GraphQL, or WebSocket.

## D7: Credential keys are logical references

Profiles store a logical credential key (e.g., `SwebKit-Agent:Mistral-ApiKey`), never the actual secret. The credential store resolves keys at runtime. No secret appears in config JSON, logs, previews, or model context.
