# Test Plan — API Client Key Vault Variables

## Unit tests

### Sidecar

- `SidecarKeyVaultResolver` rebuilds the inner resolver when `AppConfig.KeyVaults` changes.
- `SidecarKeyVaultResolver` does not rebuild when the same list is seen again.
- `SidecarKeyVaultResolver.IsAvailable` is `false` when no vaults are configured.
- Preview endpoint returns `error` status and an error message for a missing secret.
- Preview endpoint returns `ok` status and a masked value for a present secret.

### Web

- `EnvironmentManager` renders the correct input fields for each `secretSource`.
- Selecting `AzureKeyVault` shows the vault dropdown and secret-name input.
- Selecting `WindowsCredentialStore` shows the credential-key input.
- Selecting `Plain` shows the value input.

## Integration / manual

- Add a Key Vault in Settings, create an environment variable with `AzureKeyVault` source, and verify the sidecar resolves it at request time (requires Azure CLI identity).
- Verify that `collections.json` and `environments.json` store only the secret name and vault name, never the secret value.

## Out of scope

- End-to-end tests against a real Azure Key Vault (credential/tenant requirements make this unstable in CI).
