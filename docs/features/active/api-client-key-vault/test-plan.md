# Test Plan — API Client Key Vault Variables

## Unit tests

### Sidecar

Covered in `tests/SwebKit.Sidecar.Tests/SidecarKeyVaultResolverTests.cs` and `ApiClientEndpointsPreviewTests.cs`:

- `SidecarKeyVaultResolver` rebuilds the inner resolver when `AppConfig.KeyVaults` changes (add/rename/remove a vault).
- `SidecarKeyVaultResolver` does not rebuild when the same list is seen again (asserted by inner-instance identity via an internal test-only accessor).
- `SidecarKeyVaultResolver.IsAvailable` is `false` when no vaults are configured.
- Preview endpoint returns `400` for a missing secret name, `Problem` when no vault is configured, `error` status for a failed/unavailable secret, and `ok` status with a masked (length-clamped, not exact-length) value for a present secret.

Covered in `tests/SwebKit.Azure.Tests/MultiVaultKeyVaultSecretResolverTests.cs`:

- Requesting a vault name that doesn't match any configured vault returns `[KV_UNAVAILABLE:...]` rather than silently resolving against the first configured vault.

### Web

No component-test runner is wired up for this repo (Vitest here is scoped to pure `src/lib` logic — see `vitest.config.ts` — component behaviour is covered by Playwright against the real app). Covered instead as e2e in `web/e2e/api-client.spec.ts` ("environment variable source picker switches fields and lists configured key vaults"):

- `EnvironmentManager` renders the correct input fields for each `secretSource`.
- Selecting `AzureKeyVault` shows the vault dropdown (populated from Settings) and secret-name input.
- Selecting `WindowsCredentialStore` shows the credential-key input.
- Selecting `Plain` shows the value input.
- Adding/removing a vault in Settings is reflected in the environment editor's vault dropdown.

## Integration / manual

- Add a Key Vault in Settings, create an environment variable with `AzureKeyVault` source, and verify the sidecar resolves it at request time (requires Azure CLI identity).
- Verify that `collections.json` and `environments.json` store only the secret name and vault name, never the secret value.

## Out of scope

- End-to-end tests against a real Azure Key Vault (credential/tenant requirements make this unstable in CI).
