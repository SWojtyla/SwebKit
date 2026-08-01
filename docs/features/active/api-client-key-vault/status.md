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
- [x] `MultiVaultKeyVaultSecretResolver` no longer silently falls back to the first vault when an
      unrecognized vault name is requested (would otherwise let a typo'd vault name resolve
      against the wrong vault without any error)
- [x] Preview response no longer includes the secret's exact length; the masked value's dot count
      is clamped to a narrow range instead
- [x] `ResourceTable`'s `React.memo` is now effective — all 14 AKS tab components pass memoized
      `columns`/row-callback props instead of new literals every render
- [x] Validation matrix green

## Verification

| Check | Result |
| --- | --- |
| `dotnet build` on `src-sidecar` | Pass |
| `npm --prefix web run build` (`tsc -b`) | Pass |
| `npm --prefix web run test:unit` | 116 passed |
| `dotnet test SwebKit.Core.Tests` | 798 passed |
| `dotnet test SwebKit.Azure.Tests` | 43 passed (incl. `MultiVaultKeyVaultSecretResolverTests`) |
| `dotnet test tests/SwebKit.Sidecar.Tests` | 22 passed (incl. `SidecarKeyVaultResolverTests`, `ApiClientEndpointsPreviewTests`) |
| `npx playwright test` (full suite) | 191 passed |

## Definition of Done

1. A user can configure one or more named Azure Key Vaults in Settings.
2. The environment editor supports `AzureKeyVault` as a variable source with a vault selector and secret-name input.
3. The "Preview" action reports whether the secret is present, absent, or errored, without exposing the raw value.
4. Request execution resolves `AzureKeyVault` variables using the configured resolver.
5. `dotnet build` and `npm run build` succeed.
