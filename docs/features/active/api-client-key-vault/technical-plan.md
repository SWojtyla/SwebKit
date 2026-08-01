# Technical Plan — API Client Key Vault Variables

Touches `web/src`, `src-sidecar`, and `src/SwebKit.Core`. No Tauri/Rust changes.

## Module 1 — Sidecar resolver

### 1.1 Dynamic Key Vault resolver

Replace the static `NoopKeyVaultSecretResolver` registration with a resolver that reads the current `ProfileRepository` config and builds a `MultiVaultKeyVaultSecretResolver` on demand.

- New file: `src-sidecar/Services/SidecarKeyVaultResolver.cs`
- Implements `IKeyVaultSecretResolver`.
- Constructor takes `ProfileRepository` and `ILogger<SidecarKeyVaultResolver>`.
- On each call, compare the current `AppConfig.KeyVaults` list to a cached snapshot.
- Rebuild the inner `MultiVaultKeyVaultSecretResolver` when the list changes (by count or by name/URL).
- Delegates `GetSecretAsync` and `IsAvailable` to the inner resolver.

### 1.2 DI registration

In `src-sidecar/Program.cs`:

```csharp
builder.Services.AddSingleton<IKeyVaultSecretResolver, SidecarKeyVaultResolver>();
```

Remove or demote `NoopKeyVaultSecretResolver` (keep it in Core for tests/headless fallback).

### 1.3 Preview endpoint

In `src-sidecar/Endpoints/ApiClientEndpoints.cs`:

```csharp
app.MapPost("/api/api-client/preview-keyvault-secret", async (
    PreviewKeyVaultSecretRequest req,
    IKeyVaultSecretResolver resolver,
    CancellationToken ct) =>
{
    if (string.IsNullOrWhiteSpace(req.SecretName))
        return Results.BadRequest(new { error = "Secret name is required" });

    if (!resolver.IsAvailable)
        return Results.Problem("No key vaults are configured");

    var raw = await resolver.GetSecretAsync(req.SecretName, req.KeyVaultName, ct).ConfigureAwait(false);
    var status = raw.StartsWith("[KV_ERROR:") || raw.StartsWith("[KV_UNAVAILABLE:")
        ? "error"
        : "ok";

    return Results.Ok(new KeyVaultPreviewResponse(
        status,
        MaskSecret(raw),
        raw.Length,
        status == "error" ? raw : null));
});
```

`MaskSecret` returns a masked placeholder for the value so the UI never receives plaintext secrets in a preview.

## Module 2 — React UI

### 2.1 Settings: Key Vault endpoint editor

In `web/src/components/settings/GeneralSettings.tsx`:

- Add an "Azure Key Vaults" section under the existing API Client section.
- Render rows of `name` + `url` inputs and a remove button.
- Add "+ Add Key Vault" button.
- Persist via `useUpdateProfile` by mutating `profile.config.keyVaults`.

### 2.2 Environment variable source picker

In `web/src/components/api-client/EnvironmentManager.tsx`:

- Replace the single plain-value input with a per-row type picker.
- When `secretSource` is `AzureKeyVault`, show a vault dropdown (from `profile.config.keyVaults`) and a "Secret name" input bound to `credentialKey`.
- When `secretSource` is `WindowsCredentialStore`, show a "Credential key" input.
- When `secretSource` is `Plain`, show the existing value input.
- `Generated` is out of scope for this feature; keep it selectable but fall back to plain value input to avoid breaking the type.

### 2.3 Key Vault preview action

- Add a small "Preview" button next to the secret-name input when `AzureKeyVault` is selected.
- Call `POST /api/api-client/preview-keyvault-secret`.
- Show a compact status label: "Present" / "Not found" / "Error" plus the masked value length.
- Use `useNotification()` for errors.

### 2.4 API helper

In `web/src/lib/api.ts` add:

```typescript
export async function previewKeyVaultSecret(keyVaultName: string | null, secretName: string): Promise<{ status: string; maskedValue: string; length: number; error?: string }>
```

## Module 3 — Tests

- Sidecar: unit test `SidecarKeyVaultResolver` rebuild logic with a mocked `IKeyVaultSecretResolver` or in-memory `ProfileRepository`.
- Web: Vitest for `EnvironmentManager` row type switching and vault selection.
- E2E: not required for this feature; the Azure SDK path is hard to automate without real credentials.
