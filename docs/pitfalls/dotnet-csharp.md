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

## CS-8 — YamlDotNet (YAML 1.2) emits plain scalars that kubectl's go-yaml (YAML 1.1) retypes

**Symptom:** `kubectl apply` of a manifest shown in the YAML editor fails with `The request is invalid: patch: Invalid value: ... cannot convert int64 to string`, even though the user only changed an unrelated field — or nothing at all.

**Cause:** `KubernetesYaml.Serialize` writes YAML under 1.2 rules, where a string like `9010_31` is a legal plain scalar. kubectl parses with go-yaml (YAML 1.1), where `9010_31` is an integer with an underscore separator (901031). The same applies to `yes/no/on/off/y/n`, `0x`/`0o`/`0b` literals, sexagesimal (`1:30`), `.inf`/`.nan`, timestamps, and plain numbers a user types unquoted — any of these in a string-typed field (`env[].value`, labels, annotations, ConfigMap `data`, `command`/`args`) reaches the API server as the wrong JSON type.

**Fix:** Never trust plain-scalar style to survive the YamlDotNet → go-yaml round-trip. `KubernetesAksClient.SanitizeYamlForApply` (also run inside `CleanEditableYaml` so the editor displays honest quoting) re-quotes plain scalars in string-typed positions when they would resolve to a non-string under YAML 1.1. Reuse it rather than inventing another yaml-fixing pass; it only rewrites the file when something actually changed so comments/formatting are preserved.

---

## CS-9 — ScriptDom's API surface differs from common blog/doc examples

**Symptom:** `Microsoft.SqlServer.TransactSql.ScriptDom` code copied from examples fails to compile: the parser constructor has no `bool` parameter named the way you expect, `Parse` doesn't return the script directly, and `is SetStatement` can't pattern-match (it's abstract).

**Cause:** In ScriptDom 180.x: `TSql160Parser`'s constructor takes `initialQuotedIdentifiers` (not a positional bool you can omit); `parser.Parse(reader, out errors)` returns `TSqlFragment` which must be cast to `TSqlScript`; and `SetStatement` is an abstract base — `SET` options materialize as concrete subclasses like `PredicateSetStatement`/`SetOnOffStatement`, which means SET handling can't be whitelisted by matching the base type alone.

**Fix:** For a read-only/write guard, default-deny is the safe default anyway: allow only the concrete read-safe statement types you enumerate and treat everything else — including every `SetStatement` subclass, unknown future types, and parse errors — as a write/deny. See `src/SwebKit.Sql/SqlStatementGuard.cs`. Also remember `SelectStatement` with an `Into` clause is mutating, and identifiers (schema/table/column names) can never be parameters — bracket-quote with `]`→`]]` escaping instead of interpolating raw names.

---

## CS-10 — A pooled-client cache key and the factory argument must describe the same target

**Symptom:** A monitoring rule configured with an explicit AKS context queried the _profile's_ context instead; per-rule contexts silently all hit the same cluster. Separately, switching AKS context made switching _back_ pay the ~18s namespace list every time.

**Cause:** Two sides of the same pooling contract were broken in `SidecarMonitoringConnectionPool`. `GetAksClient(context)` cached under the requested context but passed `null` (→ profile context) to the client factory, so the cache key lied about what it held. And `POST /api/aks/context` called `InvalidateStaleConnections()`, which disposed _every_ pooled client — including each client's internal 5-minute namespace cache — even though only the profile's active context changed.

**Fix:** Derive the cache key and pass the _same_ explicit context to the factory; never let the factory fall back to a different default than the key describes. Reserve blanket invalidation for actual configuration changes (`SaveProfileAsync` now snapshots the profile and evicts only entries whose AKS/SB/Redis config diffed) — a user-driven context switch is not a config change. When a pool gains targeted-eviction members, every test fake implementing the interface needs them too; a tracking fake is the cheapest way to assert "evicted exactly X, nothing else".

---

_See also: [azure-sdk.md](azure-sdk.md)_
