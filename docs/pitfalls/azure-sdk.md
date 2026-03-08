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

_See also: [blazor-maui.md](blazor-maui.md) · [dotnet-csharp.md](dotnet-csharp.md)_
