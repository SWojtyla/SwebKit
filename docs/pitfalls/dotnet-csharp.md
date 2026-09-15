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

## CS-6 — `BuildConfigFromConfigFile` can eagerly execute kubeconfig auth plugins

**Symptom:** AKS client construction fails before the first API call with a deserialization error from `ExecuteExternalCommand`, often because the kubeconfig `exec` command returned empty stdout.

**Cause:** `KubernetesClientConfiguration.BuildConfigFromConfigFile(...)` can execute kubeconfig `exec` or auth-provider flows while building the client configuration. Any Azure fallback logic that runs after config construction is already too late.

**Fix:** Keep kubeconfig exec auth as the primary path. Only when `BuildConfigFromConfigFile(...)` fails with a kubeconfig external-exec error for an AKS host should you load the kubeconfig object, clear the selected user's `ExternalExecution` / `AuthProvider`, and rebuild through the Azure credential fallback path.

---

## CS-7 — OS-gated early returns must preserve the method's contract on every OS

**Symptom:** Sidecar tests pass on a Windows dev machine but fail on the Linux CI runner with `Assert.Throws() Failure: No exception was thrown`.

**Cause:** `AcpProcessLauncher.ResolveExecutable` returned the raw command on non-Windows (`if (!OperatingSystem.IsWindows()) return command;`), so the `FileNotFoundException` contract — and the user-facing "install Node.js"-style message — only existed on Windows. On Linux a missing agent surfaced later as a raw `Win32Exception` (ENOENT) from `Process.Start`.

**Fix:** Gate only the truly platform-specific part (PATHEXT expansion is Windows-only); keep shared semantics — existence check, `Path.PathSeparator`-split PATH search, and the thrown exception — on every OS. When a test asserts behavior behind an OS gate, ask whether the gate should exist in the implementation at all before marking the test Windows-only. And remember `PATH` splits on `:` on Unix, not `;` — use `Path.PathSeparator`.

---

_See also: [blazor-maui.md](blazor-maui.md) · [azure-sdk.md](azure-sdk.md)_
