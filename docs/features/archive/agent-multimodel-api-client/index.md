# Agent Multi-Model / API Client Credential Direction (recreated stub)

## Status

`Archived`

## Why this stub exists

Cited from `docs/features/active/post-migration-ux-review/status.md` for "the credential-store
direction" relevant to the AI Agent's API key handling, and implicitly for multi-model LLM support.
The original document is missing from this repository state. Recreated as a minimal stub so the
link resolves.

## What this doc covered (inferred from citing docs)

The credential-store direction it's cited for is already implemented: `SidecarCredentialStore.cs`
(sidecar) and `secrets.rs` (Tauri) both provide OS-keyring-backed secret storage, used by the
Agent's API-key path — see
`docs/features/active/tauri-react-primary-tool/production-readiness-review.md` §1 for confirmation
this is in place. Multi-model support (choosing between LLM providers) was not found implemented in
the sidecar's `SidecarAgentChatService.cs` during that review — if this is still wanted, it should
be scoped as new work under `docs/features/active/tauri-react-primary-tool/ux-plan.md`'s Agent
section rather than reconstructed from this stub.

## See also

- `docs/features/active/tauri-react-primary-tool/production-readiness-review.md`
- `docs/features/active/tauri-react-primary-tool/ux-plan.md` (§2.1, Agent tool-calling)
