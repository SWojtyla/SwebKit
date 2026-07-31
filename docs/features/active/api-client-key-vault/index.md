# API Client Key Vault Variables

## Summary

The React API Client lost the Key Vault variable source that existed in the MAUI/Blazor app. Environment variables today are always plain text: the type picker, vault selector, and secret-name preview are missing from `EnvironmentManager.tsx`, and the sidecar still registers `NoopKeyVaultSecretResolver` so any `AzureKeyVault` variable resolves to a `[KV_UNAVAILABLE:{name}]` sentinel at request time.

This feature restores the Key Vault source end-to-end: configure named vaults in Settings, select a vault and secret name in the environment editor, and preview whether the secret is present before saving.

**Jira:** not linked

## Goal

A developer can store API secrets in Azure Key Vault, reference them by name from an environment variable, and have SwebKit resolve them at request time — without persisting the secret value to disk or to the UI.

## Scope

### In scope

- Settings UI for adding/removing named Key Vault endpoints in `AppConfig.keyVaults`.
- `EnvironmentManager.tsx`: variable source picker (`Plain`, `Secret Store`, `Azure Key Vault`), vault selector, and secret-name input.
- Sidecar registration of a real, config-driven `IKeyVaultSecretResolver`.
- Sidecar endpoint to preview a Key Vault secret (`/api/api-client/preview-keyvault-secret`) returning a masked presence indicator, not the raw value.
- Request execution already resolves Key Vault variables through `IVariableSubstitutionService.BuildScopeAsync`; this is wired end-to-end once the resolver is real.

### Out of scope

- Bulk import of all vault secrets into an environment.
- Key Vault secret versioning or editing.
- Windows Credential Store / generated variable UI parity beyond the source selector scaffold needed to support the picker.

## Related docs

- [technical-plan.md](technical-plan.md)
- [test-plan.md](test-plan.md)
- [status.md](status.md)
- [docs/architecture/functionalities/api-client.md](../../../architecture/functionalities/api-client.md)
