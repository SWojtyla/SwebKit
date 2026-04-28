# Pitfalls — General .NET / C#

---

## CS-1 — `required` properties are not null-safe at runtime

**Symptom:** `NullReferenceException` on a property marked `required string`.

**Cause:** `required` enforces initialisation in object initialisers at compile time but does not prevent null at runtime (e.g., when deserialized from JSON with a missing field, or when a test sets a value via reflection).

**Fix:** Treat `required` as a compile-time hint only. Validate at deserialization boundaries (`ProfileRepository.LoadAsync`) and provide sensible defaults.

---

## CS-2 — `catch (Exception)` catches `OperationCanceledException`

**Symptom:** Cancellation is swallowed silently; the operation appears to complete instead of being cancelled.

**Cause:** `OperationCanceledException` derives from `Exception`, so a bare `catch (Exception)` will catch it. If you want cancellation to propagate cleanly, you must re-throw it explicitly.

**Fix:**

```csharp
catch (OperationCanceledException) { throw; }
catch (Exception ex) { /* handle non-cancellation errors */ }
```

---

## CS-3 — `DelegatingHandler` registrations must not be singleton

**Symptom:** `The 'InnerHandler' property must be null. 'DelegatingHandler' instances provided to 'HttpMessageHandlerBuilder' must not be reused or cached.`

**Cause:** A custom `DelegatingHandler` registered for `HttpClientFactory` was added as a singleton or otherwise reused across multiple client pipelines.

**Fix:** Register custom handlers as transient and let `AddHttpMessageHandler<THandler>()` resolve a fresh instance for each pipeline build.

```csharp
services.AddTransient<MyAuthHandler>();
services.AddHttpClient("MyClient")
	.AddHttpMessageHandler<MyAuthHandler>();
```

---

## CS-4 — Persisted JSON state must not be overwritten in place

**Symptom:** A desktop app occasionally restarts with default-looking configuration or empty UI state after a rebuild, crash, or abrupt shutdown.

**Cause:** The repository overwrote `profiles.json` or `ui-state.json` directly with `File.WriteAllTextAsync(...)`. If the process exits mid-write, the next launch can see a truncated or invalid JSON file and fall back to fresh in-memory defaults.

**Fix:** Write to a temp file in the same directory, replace the primary file atomically, and refresh a sibling `.bak` copy after every successful save. On load, try the primary file first and fall back to the backup before treating startup as a fatal persistence failure.

---

## CS-5 — Do not reuse kubectl CLI flags for Helm commands

**Symptom:** Helm operations fail with `unknown flag: --context` even though the same kubeconfig and context work with `kubectl`.

**Cause:** A shared CLI argument builder emitted kubectl's `--context` flag for both tools. Helm uses `--kube-context` instead.

**Fix:** Keep kubectl and Helm argument builders separate, or parameterize the context flag name explicitly when constructing process arguments.

---

_See also: [blazor-maui.md](blazor-maui.md) · [azure-sdk.md](azure-sdk.md)_
