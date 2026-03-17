# Backend Plan — AKS Enhancements (Batch 2)

---

title: "Backend Plan — AKS Enhancements Batch 2"
owner: ""
status: "Done"

---

## Goal

Extend the client layer with CronJob retrieval and extend the JS layer with YAML search.
No server-side or Azure infrastructure changes needed — all work is in the local client
and UI code.

## Impacted areas

- `src/SwebKit.Core/Models/AksModels.cs` — new `CronJobInfo` type
- `src/SwebKit.Core/Abstractions/IAksClient.cs` — new `GetCronJobsAsync` signature
- `src/SwebKit.Core/Services/DemoAksClient.cs` — demo implementation
- `src/SwebKit.Kubernetes/AksClient/KubernetesAksClient.cs` — real K8s implementation
- `src/SwebKit.App/wwwroot/js/yamlHighlight.js` — search functions

## Design

### CronJob retrieval

`GetCronJobsAsync(string ns, CancellationToken ct)` follows the same pattern as all
other resource methods: single-namespace call to the K8s API, mapped to a flat
`CronJobInfo` record, returned as `IReadOnlyList<CronJobInfo>`.

The real client uses `_client.BatchV1.ListNamespacedCronJobAsync`. The "all namespaces"
variant falls back to `ListCronJobForAllNamespacesAsync` when `ns == ""`.

YAML view for a CronJob goes through the existing `GetResourceYamlAsync` switch with a
new `"cronjob"` case calling `BatchV1.ReadNamespacedCronJobAsync`.

### YAML search

Implemented entirely in JS — no Blazor state is involved per keystroke. Three functions
added to the `yamlHighlight` namespace in `yamlHighlight.js`:

- `searchInPre(preEl, query)` — walks DOM text nodes inside the `<pre>`, wraps every
  case-insensitive match with `<mark class="yml-search-match">`, scrolls the first match
  into view, returns match count as an integer.
- `clearSearch(preEl)` — removes all `<mark>` wrappers and normalises the text nodes.
- `yamlClearMarks(container)` — internal helper that unwraps marks recursively.

Blazor holds the match count as `_yamlSearchCount` (int) and the current query as
`_yamlSearch` (string). `OnYamlSearchInput` calls `clearSearch` then `searchInPre` on
each input event. `ClearYamlSearch` calls `clearSearch` and resets the state fields.

## API / Contracts

### New model

```csharp
public class CronJobInfo {
    public required string Name { get; set; }
    public required string Namespace { get; set; }
    public string? Schedule { get; set; }
    public bool Suspend { get; set; }
    public int ActiveCount { get; set; }
    public DateTimeOffset? LastScheduleTime { get; set; }
    public DateTimeOffset? LastSuccessfulTime { get; set; }
    public Dictionary<string, string> Labels { get; set; } = [];
}
```

### New interface method

```csharp
Task<IReadOnlyList<CronJobInfo>> GetCronJobsAsync(string ns, CancellationToken ct = default);
```

No breaking changes — additive only.

## Tasks

- [x] Add `CronJobInfo` to `AksModels.cs`
- [x] Add `GetCronJobsAsync` to `IAksClient.cs`
- [x] Implement `GetCronJobsAsync` in `DemoAksClient` with 5 realistic entries
- [x] Implement `GetCronJobsAsync` in `KubernetesAksClient` using `BatchV1` API
- [x] Add "cronjob" case to `GetResourceYamlAsync` in `KubernetesAksClient`
- [x] Add `searchInPre`, `clearSearch`, `yamlClearMarks` to `yamlHighlight.js`

## Validation

- Unit tests: Passed — 113/113 (`SwebKit.Core.Tests`)
- Integration tests: N/A (no network calls in tests)
- Manual checks: see `test-plan.md`

## Notes

- Demo data includes a suspended CronJob (`audit-log-archiver`) to exercise the
  suspended badge rendering path.
- `DemoAksClient.GetCronJobsAsync` is not currently covered by a dedicated test;
  it is exercised implicitly through `DemoAksClientTests` pattern but a specific
  test case should be added in a follow-up.
