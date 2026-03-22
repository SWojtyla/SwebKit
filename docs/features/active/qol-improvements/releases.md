# Releases QOL — Detailed Implementation Plan

**Parent:** [QOL Improvements Catalog](index.md)
**Status:** Planned
**Covers:** REL-1 through REL-6

## Relationship to pipelines-revamp

The `pipelines-revamp` feature (status: Planned, see
[`docs/features/active/pipelines-revamp/index.md`](../pipelines-revamp/index.md)) replaces
`ReleasesPage.razor` entirely with a new `PipelinesPage.razor` and reorganises all sub-components.
Every REL item below is scoped to the **current** `ReleasesPage` implementation. When
`pipelines-revamp` lands, each item must be re-evaluated against the new component map:

| REL item | Lives in (today) | Target location after pipelines-revamp |
|---|---|---|
| REL-1 Release selector | `ReleasesPage.razor` | `ReleaseList.razor` (left panel, persistent list — no dropdown needed; item becomes moot) |
| REL-2 Unsaved changes warning | `ReleaseEditor.razor` | `ReleaseEditor.razor` (modal unchanged; apply there directly) |
| REL-3 Approval comment history | `ApprovalCenter.razor` | `ApprovalCenter.razor` (refactored to global scope; apply there) |
| REL-4 Tag confirmation shortcut | `ReadinessGate.razor` | Absorbed into `ReleaseDetail.razor` header; apply in the inline readiness pill |
| REL-5 Pipeline run duration | `ReleaseBoard.razor`, `PipelineTriggerHub.razor` | `ReleaseDetail.razor` matrix + `PipelineDetail.razor` Recent Runs table (duration column already specified in frontend.md) |
| REL-6 Delete confirmation | `ReleasesPage.razor` | `ReleaseDetail.razor` action bar delete handler |

Implement REL items now to improve the shipped experience while `pipelines-revamp` is in planning.
During the revamp, carry each fix forward rather than re-implementing from scratch.

---

## Navigation & Selection

### REL-1 — Release selector search/filter (FluentCombobox replacement)

**Problem.** `ReleasesPage.razor` (lines 19–25) renders a plain `<select>` for release choice:

```razor
<select class="release-selector" value="@SelectedReleaseId" @onchange="OnReleaseSelected">
    @foreach (var r in EffectiveReleases.OrderByDescending(r => r.CreatedAt))
    {
        <option value="@r.Id">@r.Name @StatusLabel(r.Status)</option>
    }
</select>
```

With many releases (>10) the list becomes hard to navigate. There is no typeahead.

**Approach.** Replace with `FluentCombobox` which provides built-in filtering and keyboard
navigation. `FluentCombobox` is available in `Microsoft.FluentUI.AspNetCore.Components` (already
in the tech stack).

```razor
<FluentCombobox TOption="ReleaseRecord"
                Items="@EffectiveReleases.OrderByDescending(r => r.CreatedAt)"
                OptionValue="@(r => r.Id.ToString())"
                OptionText="@(r => $"{r.Name} {StatusLabel(r.Status)}")"
                @bind-Value="SelectedReleaseId"
                @bind-Value:after="OnReleaseSelectedFromCombobox"
                Placeholder="Select a release…"
                Autocomplete="ComboboxAutocomplete.List"
                class="release-selector-combo" />
```

The `@bind-Value:after` callback replaces `@onchange` and receives the selected `Id` string.
Extract a `OnReleaseSelectedFromCombobox()` method that replicates the existing `OnReleaseSelected`
logic (lines 243–250):

```csharp
private void OnReleaseSelectedFromCombobox()
{
    if (Guid.TryParse(SelectedReleaseId, out var id))
        SelectedRelease = ReleaseRepo.GetRelease(id)
            ?? EffectiveReleases.FirstOrDefault(r => r.Id == id);
}
```

**After pipelines-revamp.** `ReleaseList.razor` renders a persistent left-panel list, so the dropdown
is replaced by a sidebar selection — REL-1 becomes moot. No further migration required.

---

### REL-2 — Unsaved changes warning

**Problem.** `ReleaseEditor.razor` is a modal that lets users create or edit a `ReleaseRecord`.
Navigating away (e.g., switching to a different main route via `LeftNav`) while the editor is open
silently discards any unsaved input. There is currently no `NavigationManager.RegisterLocationChangingHandler`
in `ReleaseEditor` or `ReleasesPage`.

**Approach.** Inside `ReleaseEditor.razor`, register a location-changing handler when the editor
becomes visible and dispose it when the editor is closed or cancelled.

`ReleaseEditor.razor` must inject `NavigationManager`:

```razor
@inject NavigationManager Nav
```

In the `@code` block, track dirty state and register the handler when `Visible` changes to `true`:

```csharp
private IDisposable? _navGuard;
private bool _isDirty = false;

protected override void OnParametersSet()
{
    if (Visible && _navGuard is null)
        _navGuard = Nav.RegisterLocationChangingHandler(OnLocationChanging);
    else if (!Visible)
        DisposeNavGuard();
}

private ValueTask OnLocationChanging(LocationChangingContext ctx)
{
    if (_isDirty)
        ctx.PreventNavigation();
    return ValueTask.CompletedTask;
}

private void DisposeNavGuard()
{
    _navGuard?.Dispose();
    _navGuard = null;
}
```

Mark `_isDirty = true` on any field change in the editor form (bind to a local draft copy and
compare with the original on each change, or simply set dirty on first input event).

When `OnCancel` is invoked:

```csharp
private void Cancel()
{
    if (_isDirty)
    {
        // Show a simple confirmation inline or rely on ConfirmDialog
        // For now: reset dirty flag and close
    }
    _isDirty = false;
    DisposeNavGuard();
    OnCancel.InvokeAsync();
}
```

For a richer experience, wire `ConfirmDialog` with the message "Discard unsaved changes?" before
calling `OnCancel`. The `ConfirmDialog` component already exists in the codebase.

**`IDisposable` implementation.** Add `@implements IDisposable` to `ReleaseEditor.razor` and call
`DisposeNavGuard()` in `Dispose()`.

**After pipelines-revamp.** `ReleaseEditor` remains as a modal; apply the same guard there. The
component file path does not change.

---

## Approvals

### REL-3 — Approval comment history persistence

**Problem.** When a user approves or rejects a stage in `ApprovalCenter.razor`, they can enter a
comment. After the action completes the approval row disappears from the list (it is no longer
pending). Comments are never persisted — there is no history of who approved what, with what comment.

**Approach.** Add a local `ApprovalHistoryRepository` that appends an `ApprovalHistoryEntry` record
each time `ApproveAsync` or `RejectAsync` is called. Persist to `approval-history.json` in the same
folder as `releases.json`.

**New model** in `SwebKit.Core/Models/DevOpsModels.cs` or a new `ApprovalHistoryModels.cs`:

```csharp
public record ApprovalHistoryEntry(
    Guid ReleaseId,
    string ComponentName,
    string StageName,
    string Action,         // "Approved" | "Rejected"
    string? Comment,
    string? ApproverName,
    DateTimeOffset OccurredAt);
```

**New `ApprovalHistoryRepository`** in `SwebKit.Core/Configuration/ApprovalHistoryRepository.cs`:
- Singleton registered in DI.
- `LoadAsync()` / `SaveAsync()` using `System.Text.Json`.
- `Append(ApprovalHistoryEntry)` adds to an in-memory list and triggers save.
- `GetHistory(Guid releaseId)` returns entries for a specific release.

**`ApprovalCenter.razor` changes.**

1. Inject `ApprovalHistoryRepository`.
2. After a successful `ApproveAsync` or `RejectAsync` call, append the entry.
3. Add a collapsible "History" section below the pending approvals list. It renders all
   `ApprovalHistoryEntry` records for the current release, grouped by stage, newest first:

```razor
@if (_history.Count > 0)
{
    <details class="approval-history">
        <summary>History (@_history.Count actions)</summary>
        @foreach (var entry in _history.OrderByDescending(e => e.OccurredAt))
        {
            <div class="history-row">
                <span class="history-action @(entry.Action == "Approved" ? "approved" : "rejected")">
                    @entry.Action
                </span>
                <span>@entry.StageName — @entry.ComponentName</span>
                @if (!string.IsNullOrEmpty(entry.Comment))
                {
                    <span class="history-comment">"@entry.Comment"</span>
                }
                <span class="history-time">@entry.OccurredAt.ToString("yyyy-MM-dd HH:mm")</span>
            </div>
        }
    </details>
}
```

4. Load history on component init:
   `_history = HistoryRepo.GetHistory(Release?.Id ?? Guid.Empty).ToList();`

**After pipelines-revamp.** `ApprovalCenter.razor` is refactored (global scope, project column
added). The history section applies identically — load history without filtering by `ReleaseId`
when release is null (global view), or filter when a release is selected.

---

### REL-4 — Tag confirmation keyboard shortcut + "Confirm All" button

**Problem.** `ReadinessGate.razor` renders a per-component readiness row. Confirming a tag requires
toggling a checkbox (`TagConfirmed`) for each component individually. There is no batch action and no
keyboard shortcut.

**Approach — "Confirm All" button.** In `ReadinessGate.razor`, add a "Confirm All" button that
iterates all in-scope components that have a `TargetTag` set but `TagConfirmed == false` and sets
`TagConfirmed = true` for each, then saves:

```csharp
private async Task ConfirmAllTagsAsync()
{
    if (Release is null) return;
    var toConfirm = Release.Components
        .Where(c => c.InScope && !string.IsNullOrEmpty(c.TargetTag) && !c.TagConfirmed)
        .ToList();
    if (toConfirm.Count == 0) return;
    foreach (var comp in toConfirm)
        comp.TagConfirmed = true;
    await ReleaseRepo.SaveAsync();
    StateHasChanged();
}
```

Render the button in the readiness summary row (above or below the component rows):

```razor
@if (UnconfirmedCount > 0)
{
    <FluentButton @onclick="ConfirmAllTagsAsync" Appearance="Appearance.Accent">
        Confirm All (@UnconfirmedCount)
    </FluentButton>
}
```

**Keyboard shortcut — `Ctrl+K`.** Register a keyboard shortcut in `wwwroot/js/keyboardShortcuts.js`
that fires a custom event `swk:confirm-all-tags`. In `ReadinessGate.razor`, subscribe via
`JSRuntime`:

```csharp
protected override async Task OnAfterRenderAsync(bool firstRender)
{
    if (firstRender)
        _shortcutRegistration = await JS.InvokeAsync<IJSObjectReference>(
            "registerShortcut", "ctrl+k", "swk:confirm-all-tags");
}
```

Listen for the event using `EventCallback` wired through `IAppEventBus` or a direct JS
`addEventListener` bridged via `DotNetObjectReference`:

```csharp
[JSInvokable]
public Task OnConfirmAllShortcutAsync() => ConfirmAllTagsAsync();
```

Register `DotNetObjectReference.Create(this)` with the JS shortcut handler.

**Guard.** In production environments (`IsProduction`), gate "Confirm All" behind the same
`ConfirmDialog` pattern used for destructive actions.

**After pipelines-revamp.** `ReadinessGate.razor` is absorbed into `ReleaseDetail.razor` inline
readiness pill (see `pipelines-revamp/frontend.md` line 203). Migrate `ConfirmAllTagsAsync` and the
keyboard handler to `ReleaseDetail.razor`.

---

## Release Board

### REL-5 — Pipeline run duration column

**Problem.** `ReleaseBoard.razor` shows per-component run status (stage reached) but not how long
each run took. `PipelineTriggerHub.razor` shows pipeline runs without duration. Users cannot quickly
identify slow or stuck runs.

**Data available.** `AdoPipelineRun` in `DevOpsModels.cs` should already carry `StartedAt` and
`FinishedAt` (check `SwebKit.Core/Models/DevOpsModels.cs`). If they are absent, add them:

```csharp
public record AdoPipelineRun(
    int Id,
    string Name,
    string State,
    string Result,
    string Branch,
    string? TriggeredBy,
    DateTimeOffset? StartedAt,    // add if missing
    DateTimeOffset? FinishedAt,   // add if missing
    int PipelineId);
```

Map them from the ADO REST response in `DevOpsClient.cs`:
`run.startTime` and `run.finishTime` (ISO 8601 strings in the ADO API v7.1 response body).

**`DemoDevOpsClient`.** Seed `StartedAt` and `FinishedAt` on all synthetic runs with realistic
durations (e.g., 4m 12s for DEV, 7m 45s for STG).

**Helper.** Add `FormatDuration(DateTimeOffset? start, DateTimeOffset? finish)` to a static helper
class (e.g., `DurationFormatter` in `SwebKit.Core/Services/`):

```csharp
public static string FormatDuration(DateTimeOffset? start, DateTimeOffset? finish)
{
    if (start is null) return "—";
    var end = finish ?? DateTimeOffset.UtcNow;
    var d = end - start.Value;
    if (d.TotalSeconds < 60) return $"{(int)d.TotalSeconds}s";
    if (d.TotalHours < 1)    return $"{(int)d.TotalMinutes}m {d.Seconds:D2}s";
    return $"{(int)d.TotalHours}h {d.Minutes:D2}m";
}
```

**`ReleaseBoard.razor`.** Add a "Duration" column to the run status grid. Render
`FormatDuration(run.StartedAt, run.FinishedAt)`. For in-progress runs, show elapsed time with a
live 1-second tick using `PeriodicTimer` (same pattern as TTL countdown in `RedisKeyDetail`), or
simply compute elapsed at render time without ticking.

**`PipelineTriggerHub.razor`.** Add "Duration" column to the pipeline runs table, using the same
`FormatDuration` helper.

**After pipelines-revamp.** `PipelineDetail.razor` Recent Runs table already specifies a "Duration"
column in `pipelines-revamp/frontend.md` (line 117). Use `FormatDuration` there. `ReleaseDetail.razor`
replaces `ReleaseBoard.razor` — carry the column across.

---

### REL-6 — Delete release confirmation using ConfirmDialog component

**Problem.** `ReleasesPage.razor` implements delete confirmation with a bespoke overlay (lines 97–116
in `ReleasesPage.razor`) using raw `<div>` and inline `<style>` blocks (lines 118–138). The
`ConfirmDialog` component already exists and is used consistently across Redis, AKS, and other pages.

**Change.** Remove the custom overlay markup (the `@if (ShowDeleteConfirm ...)` block and the
`<style>` block) and replace with:

```razor
<ConfirmDialog Visible="@ShowDeleteConfirm"
               Title="Delete Release"
               Message="@($"Delete '{SelectedRelease?.Name}'? This cannot be undone.")"
               ConfirmLabel="Delete"
               IsProduction="@IsProduction"
               RequireTyping="false"
               OnConfirm="DeleteReleaseAsync"
               OnCancel="@(() => ShowDeleteConfirm = false)" />
```

`ConfirmDialog` supports `IsProduction` to add a warning banner. Wire `IsProduction` to
`AppContext.Environment.IsProduction` when that flag is available (consistent with the intent of
`IsProduction => false` currently hardcoded in `RedisPage`).

**Update `DeleteReleaseAsync` (line 257–274).** Remove `ShowDeleteConfirm = false;` from the start
of the method — the `ConfirmDialog` fires `OnConfirm` only after the user clicks Confirm, so the
visible flag is already cleared by the `OnConfirm` binding. The rest of the method (delete from
repo, select next release) is unchanged.

**Remove bespoke `<style>` block.** The `.releases-delete-overlay` and `.releases-delete-dialog`
CSS rules (lines 119–138) are no longer needed. Delete them.

**Verify.** Ensure `ConfirmDialog` is imported in `_Imports.razor` or the component namespace. It
is already used in `RedisPage.razor` so the import exists.

**After pipelines-revamp.** `ReleasesPage.razor` is removed; the delete action moves to
`ReleaseDetail.razor` action bar. Use `ConfirmDialog` there from the start — no custom overlay.

---

## Implementation Order

1. **REL-6** (delete confirmation migration) — pure UI refactoring, no logic change, removes 30 lines of bespoke overlay code.
2. **REL-2** (unsaved changes warning) — self-contained to `ReleaseEditor.razor`; adds `NavigationManager` guard with no data model changes.
3. **REL-1** (release selector with FluentCombobox) — replaces `<select>` with `FluentCombobox`; test that existing `OnReleaseSelected` logic still works.
4. **REL-5** (pipeline run duration column) — requires model field additions to `AdoPipelineRun` and `DevOpsClient` mapping; add `DurationFormatter` helper with tests before wiring UI.
5. **REL-4** (tag confirmation shortcut + "Confirm All") — requires JS shortcut registration and `DotNetObjectReference` bridge; do after simpler items to avoid debugging JS interop alongside component changes.
6. **REL-3** (approval comment history) — largest change: new repository, new model, new persistence file, UI section in `ApprovalCenter`; do last to allow the simpler items to ship first.
