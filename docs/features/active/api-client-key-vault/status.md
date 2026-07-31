# Status — API Client Key Vault Variables

## Current State

`Review`

**Jira:** not linked

## Progress

- [x] Feature docs created
- [x] Sidecar resolver implemented (`SidecarKeyVaultResolver`)
- [x] Sidecar preview endpoint implemented (`/api/api-client/preview-keyvault-secret`)
- [x] Settings UI for Key Vault endpoints
- [x] Environment Manager source picker and preview
- [x] Validation matrix green

## Verification

| Check | Result |
| --- | --- |
| `dotnet build` on `src-sidecar` | Pass |
| `npm --prefix web run build` | Pass |
| `npm --prefix web run test:unit` | 116 passed |
| `dotnet test SwebKit.Core.Tests` | 793 passed |
| `dotnet test SwebKit.Azure.Tests` | 37 passed |

## Definition of Done

1. A user can configure one or more named Azure Key Vaults in Settings.
2. The environment editor supports `AzureKeyVault` as a variable source with a vault selector and secret-name input.
3. The "Preview" action reports whether the secret is present, absent, or errored, without exposing the raw value.
4. Request execution resolves `AzureKeyVault` variables using the configured resolver.
5. `dotnet build` and `npm run build` succeed.
