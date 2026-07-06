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

_See also: [blazor-maui.md](blazor-maui.md) · [dotnet-csharp.md](dotnet-csharp.md)_
