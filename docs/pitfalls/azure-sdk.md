# Pitfalls — Azure SDK

---

## AZ-1 — `GetNamespacePropertiesAsync` and listing methods require the same `Manage` claim

**Symptom:** Connection test passes (green dot) but entity listing returns empty or throws.

**Cause:** Both `GetNamespacePropertiesAsync` and `GetQueuesRuntimePropertiesAsync` require the `Manage` SAS claim. However, edge cases exist where the two calls hit slightly different auth evaluation paths on the server, producing inconsistent results with restricted policies.

**Fix:** Use the same listing method for the connection test as for the actual listing, so that a passing test guarantees a working list.

```csharp
// Safer connection test — validates the same operation used for listing
await foreach (var _ in _adminClient.GetQueuesAsync(ct))
    break;
```

---

## AZ-2 — Entity-scoped connection strings silently return empty for namespace-level listing

**Symptom:** `ListQueuesAsync` returns zero results even though queues exist.

**Cause:** A connection string with `EntityPath=my-queue` is scoped to that entity. `ServiceBusAdministrationClient` created from it cannot enumerate all namespace queues.

**Fix:** `AzureServiceBusClient` already detects `_scopedEntityPath` and falls back to fetching just that entity when the list is empty. Users should prefer namespace-level connection strings for the global namespace panel.

---

## AZ-3 — `AsyncPageable` enumerators must be disposed

**Symptom:** Resource leak; connections not returned to the pool.

**Cause:** Calling `.GetAsyncEnumerator()` without `await using` leaves the enumerator undisposed.

**Fix:** Either enumerate fully with `await foreach`, or dispose explicitly.

```csharp
// Preferred for "just check one page"
await foreach (var _ in _adminClient.GetQueuesAsync(ct))
    break;
```

---

## AZ-4 — `DefaultAzureCredential` silently prefers `EnvironmentCredential` over the signed-in developer

**Symptom:** Azure AD (Entra) authenticated calls (Storage, Service Bus, Key Vault, App Insights) fail with `AuthorizationPermissionMismatch` even though the developer's own Azure AD account has the correct RBAC role on the resource (directly or via group membership). Re-running `az login`, clearing the CLI token cache, or confirming RBAC in the Portal changes nothing.

**Cause:** `DefaultAzureCredential` tries credential sources in a fixed order, and `EnvironmentCredential` is tried **before** `AzureCliCredential`/`VisualStudioCredential`. If the machine has `AZURE_CLIENT_ID`/`AZURE_TENANT_ID`/`AZURE_CLIENT_SECRET` set anywhere in its environment (commonly at **Machine** scope, for an unrelated local automation tool/service principal), `EnvironmentCredential` succeeds immediately and `DefaultAzureCredential` never falls through to the developer's own interactive credential — with no visible indication of which identity was actually used. The service principal from those env vars may have zero RBAC on the resource being called, while the developer's own account is completely fine.

**Diagnosis:** Check `[Environment]::GetEnvironmentVariables('Machine')` (and `'User'`) for `AZURE_CLIENT_ID`/`AZURE_TENANT_ID`/`AZURE_CLIENT_SECRET`. If present, that's very likely the culprit — not a code regression, not a token cache issue, not a missing role assignment.

**Fix:** Don't rely on the bare `DefaultAzureCredential()` constructor in an interactive desktop app. Exclude `EnvironmentCredential` explicitly so ambient env vars set for unrelated automation never shadow the developer's own identity:

```csharp
// Wrong — silently authenticates as whatever AZURE_CLIENT_ID/SECRET happens to be set on the machine
new DefaultAzureCredential();

// Correct — use the shared factory (SwebKit.Core.Services.AzureCredentialFactory)
AzureCredentialFactory.CreateDefault();
```

All Entra ID authenticated clients in this repo (Storage, Service Bus, Key Vault, App Insights) go through `SwebKit.Core.Services.AzureCredentialFactory.CreateDefault()` — do not construct `DefaultAzureCredential` inline in a new call site; use the factory instead.

---

## AZ-5 — A broken `az` install makes AKS look empty, not broken

**Symptom:** Contexts still list and switch fine, but the namespace picker shows `0 total` / "No namespaces found" and **no error appears anywhere** — in both the WinUI and the Tauri/React frontends at once. Nothing changed in the code or the NuGet packages.

**Cause:** Two independent things compound.

1. **Environment.** The kubeconfig AKS writes uses an exec credential plugin: `kubelogin get-token --login azurecli`, which shells out to `az`. If the Azure CLI is uninstalled or half-installed (the classic leftover is `C:\Program Files\Microsoft SDKs\Azure\CLI2` retaining only `Lib\site-packages` and `Scripts\__pycache__`, with no `wbin\az.cmd`), the plugin exits non-zero with `failed to get token: AzureCLICredential: Azure CLI not found on path`. `KubernetesClientConfiguration` leaves `AccessToken` empty, so every API call gets **401**.
2. **Code.** The 401 was then swallowed three times over: `TryApplyAzureCredentialFallback` had a bare `catch {}`, `WithAuthRetryAsync` only classified **403**, and `AksClientBootstrapper.TryLoadNamespacesAsync`'s generic catch logged at `Debug` and returned an empty list with a `null` warning. An empty namespace list is indistinguishable from a cluster that has none.

**Diagnosis:** Run `kubectl get namespaces` in a terminal — it prints the plugin's real stderr, which the app never captured. Then check `Get-Command az` and whether the install directory actually contains `wbin\az.cmd`. Note that a leftover `%USERPROFILE%\.azure\azureProfile.json` makes it *look* like the CLI is still installed.

**Fix (environment):** Reinstall the Azure CLI and `az login`. Nothing in the app can mint a token otherwise: `AzureCredentialOptions` excludes `InteractiveBrowserCredential`, and the remaining `DefaultAzureCredential` legs need either `az` or the `Az.Accounts` PowerShell module.

**Fix (code — done):** 401 is now first-class, so an auth failure can never again render as "no data":

- `AksAuthenticationException` (401, no identity) sits alongside `AksAccessDeniedException` (403, identity without RBAC).
- `WithAuthRetryAsync` retries on 401 as well as 403, then classifies via `ToAuthExceptionAsync`.
- `AksExecCredentialDiagnostics` re-runs the kubeconfig's exec plugin **only on the 401 path** and folds its (whitespace-collapsed, JWT-redacted, truncated) stderr into the message, so the cause reaches the UI.
- The credential fallback logs each scope failure at `Debug` and one `Warning` when all of them fail.
- The sidecar maps `AksAuthenticationException` → 401 and `AksAccessDeniedException` → 403 so their curated messages pass through instead of collapsing into a generic 500 `"Internal server error"`.

**Rule:** when a list-shaped AKS call fails, never return an empty collection with no warning — an empty list is a legitimate answer and therefore cannot carry an error. Attach a warning (`NamespacesWarning`, `nsError`) or let the exception propagate.

---

_See also: [blazor-maui.md](blazor-maui.md) · [dotnet-csharp.md](dotnet-csharp.md)_
