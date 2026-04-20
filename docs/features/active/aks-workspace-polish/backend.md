# Backend Plan — aks-workspace-polish

---

title: "Backend Plan — aks-workspace-polish"
owner: ""
status: "Not started"

---

## Goal

Support the 11 frontend improvements with the minimal backend changes needed: one new domain model property for pinned port-forward targets, and confirmation that all required `IAksClient` methods are already available (no contract changes needed).

## Impacted areas

- `src/SwebKit.Core/Domain/UserSettings.cs` — add `PinnedPortForwards` property (item #11)
- `src/SwebKit.App/Services/PinnedPortForwardService.cs` — new lightweight app-layer service (item #11)
- `src/SwebKit.Kubernetes/AksClient/KubernetesAksClient.cs` — no changes
- `src/SwebKit.Core/Abstractions/IAksClient.cs` — no changes
- `src/SwebKit.Core/Models/` — no changes (existing `ContainerDetail`, `PodMetrics` models are sufficient)

## IAksClient surface — confirmed sufficient

All items use existing methods:

| Item | Method used                                                                |
| ---- | -------------------------------------------------------------------------- |
| #1   | `StreamPodLogsAsync` (unchanged)                                           |
| #2   | `GetDeploymentsAsync`, `GetPodsAsync`, `GetStatefulSetsAsync` (unchanged)  |
| #3   | `GetEventsAsync` (unchanged)                                               |
| #4   | `GetPodsAsync` (unchanged)                                                 |
| #5   | `GetCronJobsAsync` (unchanged)                                             |
| #6   | `GetNamespacesAsync` (unchanged)                                           |
| #10  | `StartPortForwardAsync` (unchanged)                                        |
| #11  | No new API calls — persists to `UserSettings` only                         |
| #13  | `GetHelmReleaseHistoryAsync` (unchanged); helm-diff invoked via subprocess |
| #14  | `ApplyResourceYamlAsync` (unchanged)                                       |
| #16  | `GetContainerDetailsAsync` + `GetPodMetricsAsync` (both unchanged)         |

**No changes to `IAksClient`, `KubernetesAksClient`, or any model type in `SwebKit.Core/Models/`.**

---

## Item #11 — Pinned port-forward targets

### Domain model change — `UserSettings`

Add to `SwebKit.Core/Domain/UserSettings.cs`:

```csharp
/// <summary>
/// Port-forward targets pinned by the user, keyed by kubeconfig context name.
/// Capped at 20 entries per context; oldest entry evicted on overflow.
/// </summary>
public Dictionary<string, List<PinnedPortForwardEntry>> PinnedPortForwards { get; set; } = [];
```

Add a new record in the same file (or in `SwebKit.Core/Models/`):

```csharp
public sealed record PinnedPortForwardEntry(
    string Label,          // display name, e.g. "api-service:8080"
    string? Namespace,
    string? PodLabelSelector,
    int RemotePort,
    int LocalPort,
    DateTimeOffset PinnedAt);
```

`PinnedAt` is used for eviction (oldest-first). Cap enforcement logic lives in the service layer, not in the model.

### New service — `PinnedPortForwardService`

Location: `src/SwebKit.App/Services/PinnedPortForwardService.cs`

Responsibilities:

- Load pinned entries for a given kubeconfig context from `UserSettings`
- Add a pinned entry; enforce the 20-entry cap per context (evict oldest by `PinnedAt`)
- Remove a pinned entry by identity
- Delegate all persistence to `UserSettingsRepository.SaveAsync()` (follow CS-4 — atomic write)

Registration: singleton in `MauiProgram.cs`.

```csharp
public sealed class PinnedPortForwardService(
    AppStateService appState,
    UserSettingsRepository settingsRepo)
{
    private const int MaxPinsPerContext = 20;

    public IReadOnlyList<PinnedPortForwardEntry> GetPins(string kubeconfigContext) { ... }

    public async Task AddPinAsync(string kubeconfigContext, PinnedPortForwardEntry entry) { ... }

    public async Task RemovePinAsync(string kubeconfigContext, PinnedPortForwardEntry entry) { ... }
}
```

### Persistence notes

`UserSettings` is already serialized/deserialized by `UserSettingsRepository`. The new `PinnedPortForwards` dictionary will serialize correctly with `System.Text.Json` as long as the `PinnedPortForwardEntry` record has a parameterized constructor (which records provide automatically).

If `JsonSerializerContext` is used (source-gen mode), add `PinnedPortForwardEntry` and `Dictionary<string, List<PinnedPortForwardEntry>>` to the context. Check `src/SwebKit.Core/Serialization/` for the active context file.

---

## Item #5 — CronNextRun utility

Location: `src/SwebKit.App/Services/CronNextRun.cs` (or as a static helper in `Components/Aks/` if no DI needed)

```csharp
public static class CronNextRun
{
    /// <summary>
    /// Attempts to calculate the next run of a standard 5-field Unix cron expression.
    /// Returns false for unsupported or non-standard expressions.
    /// </summary>
    public static bool TryCalculate(string schedule, DateTimeOffset from, out DateTimeOffset next) { ... }
}
```

- Supports standard 5-field expressions (`minute hour day-of-month month day-of-week`)
- Does NOT support `@yearly`, `@monthly`, `@weekly`, `@daily`, `@hourly`, `@reboot`, step values (`*/5`), and ranges (`1-5`) beyond a basic implementation
- Returns `false` for anything it cannot parse; the UI falls back to the raw schedule string

Unit tests: `SwebKit.App.Tests/Services/CronNextRunTests.cs` — cover standard expressions, midnight boundary, DST-safe UTC-only operation.

---

## Item #14 — YAML pre-validation

No backend changes. The `YamlDotNet` deserializer is already available in the solution. Confirm `YamlDotNet` NuGet package is referenced in `SwebKit.App.csproj` (it is already in `SwebKit.Kubernetes.csproj`). If not, add:

```xml
<PackageReference Include="YamlDotNet" Version="..." />
```

Use the lowest version already present in the solution lock file to avoid version conflicts.

---

## MauiProgram.cs registration

Add to `MauiProgram.cs`:

```csharp
services.AddSingleton<PinnedPortForwardService>();
```

---

## Migration and runtime changes

None. `PinnedPortForwards` defaults to an empty dictionary; existing `user-settings.json` files without the property deserialize cleanly (System.Text.Json ignores missing properties for types with default constructors).

## Tasks

- [ ] Add `PinnedPortForwardEntry` record and `PinnedPortForwards` property to `UserSettings.cs`
- [ ] Create `PinnedPortForwardService` in `src/SwebKit.App/Services/`
- [ ] Register `PinnedPortForwardService` as singleton in `MauiProgram.cs`
- [ ] Update `JsonSerializerContext` if source-gen is active for `UserSettings` serialization
- [ ] Add `CronNextRun` static helper in `src/SwebKit.App/Services/`
- [ ] Confirm `YamlDotNet` is available in `SwebKit.App.csproj`; add package reference if missing
- [ ] Add unit tests for `CronNextRun` (`SwebKit.App.Tests`)
- [ ] Add unit tests for `PinnedPortForwardService` cap/eviction logic (`SwebKit.App.Tests`)

## Validation

- Unit tests: Not started
- Manual checks: see `test-plan.md` items #11, #5

## Notes

- `PinnedPortForwardService` must call `settingsRepo.SaveAsync()` using the atomic-write pattern (CS-4). Do not call `File.WriteAllTextAsync` directly.
- The 20-entry cap is a policy constant, not a hard limit in the model. Place it in `PinnedPortForwardService` so it can be adjusted without touching domain types.
