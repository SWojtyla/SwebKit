# Redis QOL — Detailed Implementation Plan

**Parent:** [QOL Improvements Catalog](index.md)
**Status:** Planned
**Covers:** RDS-1 through RDS-12

---

## Key Browser

### RDS-1 — Key scan pagination (cursor-based, "Load more", lazy expand)

**Problem.** `RedisPage.ScanAsync` (lines 217–259) runs a tight `do/while` loop over every cursor
page until `IsComplete || cursor == 0`, loading the entire keyspace into `Keys` before the tree
renders. On large databases (>100 K keys) this blocks the UI for seconds.

**Approach.** Break the scan into two phases:

1. **Initial load** — scan until `Keys.Count >= PageSize` (default 1 000), then stop and store the
   remaining cursor in a new `_scanCursor` field.
2. **"Load more"** — a button below the tree fires `LoadMoreKeysAsync()`, which resumes from
   `_scanCursor` for another `PageSize` keys, then calls `RebuildNamespaceTree()` incrementally.

**Interface changes.** `IRedisClient.ScanKeysAsync` already accepts a `cursor` parameter and returns
`KeyScanResult.Cursor + IsComplete`. No interface changes required.

**State additions to `RedisPage`:**

```csharp
private long _scanCursor = 0;
private bool _hasMoreKeys = false;
private const int ScanPageSize = 1000;
```

**`ScanAsync` change** (replace the `do/while` at line 233–244):

```csharp
var result = await Client.ScanKeysAsync(pattern, cursor: 0, ScanPageSize);
var newKeys = result.Keys.Where(k => seen.Add(k)).ToList();
Keys.AddRange(newKeys);
_scanCursor = result.Cursor;
_hasMoreKeys = !result.IsComplete && result.Cursor != 0;
```

**`LoadMoreKeysAsync`** (new method, wired to a "Load more" button in the tree panel toolbar):

```csharp
private async Task LoadMoreKeysAsync()
{
    if (Client is null || !_hasMoreKeys) return;
    IsLoading = true;
    await InvokeAsync(StateHasChanged);
    try
    {
        var seen = new HashSet<string>(Keys, StringComparer.Ordinal);
        var result = await Client.ScanKeysAsync(Pattern, _scanCursor, ScanPageSize);
        var newKeys = result.Keys.Where(k => seen.Add(k)).ToList();
        Keys.AddRange(newKeys);
        _scanCursor = result.Cursor;
        _hasMoreKeys = !result.IsComplete && result.Cursor != 0;
        await LoadVisibleKeyTypesAsync(newKeys);
        RebuildNamespaceTree();
    }
    finally { IsLoading = false; await InvokeAsync(StateHasChanged); }
}
```

**UI.** Add below `RedisNamespaceTree` in `RedisPage.razor`:

```razor
@if (_hasMoreKeys && !IsLoading)
{
    <FluentButton Appearance="Appearance.Lightweight" @onclick="LoadMoreKeysAsync">
        Load more (@Keys.Count loaded)
    </FluentButton>
}
```

**Demo.** `DemoRedisClient` always returns `IsComplete = true` on the first page — no changes needed.

**Tests.** Add a test case to `DemoRedisClientTests.cs` and integration test in `RedisClient` verifying
that cursor advances and second call returns a new page.

---

### RDS-2 — Binary content detection (non-printable byte check)

**Problem.** `RedisKeyDetail.razor` displays the string value raw via `DisplayStringValue`
(line 149), which delegates to `RedisValueHelpers.TruncateValue(RedisValueHelpers.FormatJsonIfValid(...))`.
If the string contains binary bytes the result is garbled or blank.

**Approach.** Add a static helper `RedisValueHelpers.IsBinaryContent(string? value)` that returns
`true` when more than 5% of the first 512 characters are non-printable (code points < 32 excluding
`\t`, `\r`, `\n`, or >= 127). The threshold avoids false positives on escaped JSON.

**Change in `RedisKeyDetail.razor`** — replace the `<pre>` block (line 60):

```razor
@if (RedisValueHelpers.IsBinaryContent(StringValue))
{
    <div class="binary-badge">Binary content — cannot display</div>
}
else
{
    <pre class="blob">@DisplayStringValue</pre>
}
```

Add `.binary-badge` to `RedisKeyDetail.razor.css` (or inline): `font-style: italic; color: var(--color-text-muted); padding: 8px;`.

**`RedisValueHelpers.IsBinaryContent` sketch:**

```csharp
public static bool IsBinaryContent(string? value)
{
    if (string.IsNullOrEmpty(value)) return false;
    var sample = value.Length > 512 ? value.AsSpan(0, 512) : value.AsSpan();
    int nonPrintable = 0;
    foreach (var c in sample)
        if (c < 32 && c != '\t' && c != '\r' && c != '\n') nonPrintable++;
        else if (c >= 127 && c <= 159) nonPrintable++;
    return nonPrintable > sample.Length * 0.05;
}
```

**Tests.** Add to `RedisValueHelpersTests.cs`: pure ASCII → false; base64-encoded blob → true; valid
JSON containing escape sequences → false.

---

### RDS-3 — Sorted set score editing (ZADD XX)

**Problem.** `RedisKeyDetail.razor` renders sorted set items as a read-only table (lines 111–122).
Scores are shown but not editable.

**Interface change.** Add to `IRedisClient`:

```csharp
Task UpdateSortedSetScoreAsync(string key, string member, double score, CancellationToken ct = default);
```

Implement in `SwebKit.Redis.RedisClient` using `db.SortedSetAddAsync(key, member, score, SortedSetWhen.Exists)`.
Implement a no-op or simple assignment in `DemoRedisClient`.

**UI change in `RedisKeyDetail.razor`.** Convert the zset table row to inline-edit on click, mirroring
the hash edit pattern already in place:

```razor
@foreach (var item in SortedSetItems)
{
    <tr>
        <td>@item.Member</td>
        <td>
            @if (_editingZsetMember == item.Member)
            {
                <input type="number" step="any" @bind="_zsetScoreEditor" />
            }
            else { @item.Score }
        </td>
        <td>
            @if (_editingZsetMember == item.Member)
            {
                <FluentButton Appearance="Appearance.Accent" @onclick="SaveZsetScoreAsync">Save</FluentButton>
                <FluentButton @onclick="() => _editingZsetMember = null">Cancel</FluentButton>
            }
            else
            {
                <FluentButton @onclick="() => BeginZsetEdit(item.Member, item.Score)">Edit</FluentButton>
            }
        </td>
    </tr>
}
```

Add a new `EventCallback<(string Member, double Score)> OnUpdateSortedSetScore` parameter. Wire in
`RedisPage.razor` to a handler that calls `Client.UpdateSortedSetScoreAsync`, then calls
`RefreshSelectedAsync()`.

**Tests.** Unit test `DemoRedisClient.UpdateSortedSetScoreAsync` verifies the member score is updated
in the in-memory store.

---

### RDS-4 — List/set pagination (LRANGE/SSCAN offset, "Load more")

**Problem.** `RedisPage.RefreshSelectedAsync` (lines 313, 316) hard-codes `0, 100` for lists and
fetches all members for sets:

```csharp
SelectedItems = (await Client.GetListItemsAsync(SelectedKey, 0, 100)).ToList();
SelectedItems = (await Client.GetSetMembersAsync(SelectedKey)).ToList();
```

**Interface change.** Add overload for set scanning:

```csharp
Task<IReadOnlyList<string>> GetSetMembersPageAsync(
    string key, long cursor, int pageSize, CancellationToken ct = default);
```

Returns a new `SetScanResult` with a `Cursor` and `IsComplete` flag, matching the SSCAN pattern.

**State additions to `RedisPage`:**

```csharp
private long _itemsCursor = 0;
private bool _hasMoreItems = false;
private const int ItemPageSize = 100;
```

Reset both fields whenever `SelectedKey` changes (at the top of `RefreshSelectedAsync`).

**Load-more button in `RedisKeyDetail.razor`.** Add an `EventCallback OnLoadMoreItems` parameter.
Below the `<ul>` for list/set types, render:

```razor
@if (HasMoreItems)
{
    <FluentButton Appearance="Appearance.Lightweight" @onclick="OnLoadMoreItems">
        Load more
    </FluentButton>
}
```

Add `[Parameter] public bool HasMoreItems { get; set; }` parameter.

`RedisPage` passes `HasMoreItems="@_hasMoreItems"` and wires `OnLoadMoreItems` to a handler that
appends the next page to `SelectedItems` using `LRANGE start _offset` for lists or
`GetSetMembersPageAsync` for sets, then calls `StateHasChanged`.

---

### RDS-5 — Copy key name button in detail header

**Problem.** No one-click copy of the full key name from `RedisKeyDetail.razor`.

**Change.** In the `<div class="key-name">` block (line 18) of `RedisKeyDetail.razor`, add a copy
icon button immediately after the key text:

```razor
<div class="key-name">
    @KeyInfo.Key
    <button class="icon-btn" title="Copy key name" @onclick="CopyKeyNameAsync">⎘</button>
</div>
```

Inject `IJSRuntime JS` and `INotificationService Notifications` into `RedisKeyDetail.razor`.

```csharp
private async Task CopyKeyNameAsync()
{
    await JS.InvokeVoidAsync("navigator.clipboard.writeText", KeyInfo!.Key);
    Notifications.ShowSuccess("Copied", KeyInfo.Key);
}
```

No interface or backend change required.

---

### RDS-6 — Key rename (RENAME command, inline input)

**Problem.** There is no way to rename a key. The `RENAME` command is not in `IRedisClient`.

**Interface change.** Add to `IRedisClient`:

```csharp
Task RenameKeyAsync(string oldKey, string newKey, CancellationToken ct = default);
```

Implement in `SwebKit.Redis.RedisClient` using `db.KeyRenameAsync(oldKey, newKey)`.
Implement in `DemoRedisClient` by removing the old key entry and inserting under the new name.

**UI.** In `RedisKeyDetail.razor`, add a rename mode alongside the existing edit button row:

```razor
@if (_renamingKey)
{
    <input @bind="_renameValue" @onkeydown="OnRenameKeyDown" class="rename-input" />
    <FluentButton Appearance="Appearance.Accent" @onclick="ConfirmRenameAsync">Rename</FluentButton>
    <FluentButton @onclick="() => _renamingKey = false">Cancel</FluentButton>
}
else
{
    <FluentButton @onclick="() => { _renamingKey = true; _renameValue = KeyInfo!.Key; }">Rename</FluentButton>
}
```

`OnRenameKeyDown`: confirm on `Enter`, cancel on `Escape`.

Add `EventCallback<(string OldKey, string NewKey)> OnRenameKey` parameter. Wire in `RedisPage.razor`
to a handler that calls `Client.RenameKeyAsync`, updates `Keys` in-place, updates `KeyTypes`,
sets `SelectedKey = newKey`, and calls `RebuildNamespaceTree()`.

**Tests.** `DemoRedisClientTests.cs`: rename existing key, verify old key absent, new key has same
value and type. Rename to conflicting key: verify overwrite matches Redis semantics.

---

## TTL & Expiry

### RDS-7 — Preserve TTL countdown across separator change

**Problem.** `OnSeparatorChangedAsync` (line 488–499) calls `RebuildNamespaceTree()`, which calls
`RedisKeyGrouper.BuildNamespaceTree(Keys, CurrentSeparator)`. This replaces `NamespaceNodes`
entirely, causing `RedisNamespaceTree` to re-render all nodes. As a downstream effect, any component
holding a TTL countdown loop (`RunCountdownAsync` in `RedisKeyDetail.razor`) is not directly
affected — the countdown lives in the detail pane, not the tree. However, `RebuildNamespaceTree()`
triggers `StateHasChanged`, which can cause `RedisKeyDetail` to receive new `KeyInfo` parameters if
the parent re-evaluates `SelectedInfo`, causing `RestartCountdown` to fire (line 183–185, inside the
`!ReferenceEquals` guard).

**Fix.** Make separator change avoid re-fetching `SelectedInfo`. In `OnSeparatorChangedAsync`, call
only `RebuildNamespaceTree()` and `StateHasChanged()` — do not call `RefreshSelectedAsync()` or
touch `SelectedInfo`. The tree rebuild only mutates `NamespaceNodes`; the `SelectedKey`,
`SelectedInfo`, and all data passed to `RedisKeyDetail` remain stable. The `ReferenceEquals` guard in
`RedisKeyDetail.OnParametersSet` (line 175) will then correctly skip restarting the countdown
because `KeyInfo` reference has not changed.

Verify by auditing `OnSeparatorChangedAsync`: it currently calls `RebuildNamespaceTree()` then
`InvokeAsync(StateHasChanged)` — there is no `RefreshSelectedAsync()` call, so the countdown is
already preserved as long as the parent doesn't re-assign `SelectedInfo`. Add a comment marking
this invariant to prevent accidental regression:

```csharp
// Do NOT call RefreshSelectedAsync here — changing the separator must not
// restart the TTL countdown in RedisKeyDetail (RDS-7).
RebuildNamespaceTree();
await InvokeAsync(StateHasChanged);
```

**Regression test.** Manual verification scenario: scan keys, select a key with TTL, change separator,
confirm countdown continues without resetting.

---

### RDS-8 — TTL set dialog pre-populate with current remaining TTL

**Problem.** The TTL input in `RedisKeyDetail.razor` (line 142) initialises as `TtlSeconds = 300`
regardless of the current remaining TTL, so it does not default to the actual expiry.

**Fix.** In `OnParametersSet` inside `RedisKeyDetail.razor`, after updating `_displayedTtl`, also
update `TtlSeconds` from the key's current TTL when the key first opens:

```csharp
if (SelectedKey != _originalKey)
{
    _originalKey = SelectedKey;
    _originalTtl = KeyInfo?.Ttl;
    _displayedTtl = KeyInfo?.Ttl;
    // Pre-populate input with current TTL for easy extension (RDS-8)
    if (KeyInfo?.Ttl is { } ttl && ttl > TimeSpan.Zero)
        TtlSeconds = (int)Math.Ceiling(ttl.TotalSeconds);
    else
        TtlSeconds = 300;
    RestartCountdown();
}
```

This means the input shows, for example, `7320` when the key has 7 320 seconds remaining, making
it easy to double it or adjust it without mental arithmetic. No interface or backend change needed.

---

## Operations & Config

### RDS-9 — Multi-key delete (checkbox multi-select + batch delete)

**Problem.** Single-key delete works, but there is no way to select multiple keys and batch-delete.

**Interface.** `IRedisClient.DeleteKeysAsync(IReadOnlyList<string> keys)` already accepts a list.
No interface change required.

**State additions to `RedisPage`:**

```csharp
private readonly HashSet<string> _selectedKeys = new(StringComparer.Ordinal);
private bool _multiSelectMode = false;
```

**UI changes in `RedisNamespaceTree.razor`.** Pass `MultiSelectMode` and `SelectedKeys` as
parameters. Render a checkbox at the start of each key leaf row when `MultiSelectMode == true`.
Checking a key adds it to `_selectedKeys`; unchecking removes it.

Add a toolbar toggle button in `RedisPage.razor`:

```razor
<FluentButton @onclick="ToggleMultiSelect">
    @(_multiSelectMode ? "Cancel Selection" : "Select")
</FluentButton>
@if (_multiSelectMode && _selectedKeys.Count > 0)
{
    <FluentButton @onclick="DeleteSelectedKeysAsync"
                  style="color:var(--color-error);">
        Delete @_selectedKeys.Count key(s)
    </FluentButton>
}
```

**`DeleteSelectedKeysAsync`:**

```csharp
private Task DeleteSelectedKeysAsync()
{
    var keys = _selectedKeys.ToList();
    return ShowConfirmationAsync(
        title: "Delete Keys",
        message: $"Delete {keys.Count} selected key(s)?",
        label: "Delete",
        action: async () =>
        {
            await Client!.DeleteKeysAsync(keys);
            foreach (var k in keys) { Keys.Remove(k); KeyTypes.Remove(k); }
            _selectedKeys.Clear();
            _multiSelectMode = false;
            if (keys.Contains(SelectedKey)) { SelectedKey = null; SelectedInfo = null; }
            RebuildNamespaceTree();
            Notifications.ShowSuccess("Keys deleted", $"{keys.Count} key(s) removed.");
        });
}
```

**Tests.** `DemoRedisClientTests.cs`: delete two keys in one call, verify both absent, third key
unaffected.

---

### RDS-10 — Connection string masking in RedisConfigForm

**Problem.** `RedisConfigForm.razor` renders the connection string in a plain text input, risking
accidental on-screen credential exposure during screen sharing.

**Approach.** The connection string field should behave like a password field with a "show" toggle.
Replace the current `<input type="text">` for the connection string with a new shared component
`<PasswordField>` (planned as UI-19 in the catalog). If `PasswordField` is not yet available, apply
an interim fix directly in `RedisConfigForm.razor`:

```razor
<input type="@(_showConnectionString ? "text" : "password")"
       @bind="Entry.ConnectionString"
       class="form-input" autocomplete="off" />
<button type="button" @onclick="() => _showConnectionString = !_showConnectionString"
        class="icon-btn" title="@(_showConnectionString ? "Hide" : "Show")">
    @(_showConnectionString ? "🙈" : "👁")
</button>
```

Add `private bool _showConnectionString = false;` to the component code block.

When the `PasswordField` shared component from UI-19 lands, replace the above with
`<PasswordField @bind-Value="Entry.ConnectionString" />`.

No backend or interface changes required.

---

### RDS-11 — Hash field add/delete (+ and – row actions)

**Problem.** `RedisKeyDetail.razor` supports per-field value editing for hashes but has no way to
add new fields or delete existing ones.

**Interface changes.** Add to `IRedisClient`:

```csharp
Task DeleteHashFieldAsync(string key, string field, CancellationToken ct = default);
```

`AddHashField` is covered by the existing `SetHashFieldAsync` — calling it with a new field name
creates the field in Redis. No new method needed for add.

Implement `DeleteHashFieldAsync` in `SwebKit.Redis.RedisClient` using
`db.HashDeleteAsync(key, field)`. In `DemoRedisClient`, remove the field from the in-memory
dictionary.

**UI.** In the hash table, add a delete button per row after the existing edit/save/cancel buttons:

```razor
<td class="hash-actions">
    @* ...existing edit buttons... *@
    @if (!(IsEditingHash && EditingHashField == field.Field))
    {
        <FluentButton @onclick="() => DeleteHashFieldAsync(field.Field)"
                      title="Delete field" style="color:var(--color-error);">–</FluentButton>
    }
</td>
```

Add a "Add field" row at the bottom of the hash table:

```razor
<tr class="hash-add-row">
    <td><input @bind="_newHashField" placeholder="Field name" class="hash-editor" /></td>
    <td><input @bind="_newHashValue" placeholder="Value" class="hash-editor" /></td>
    <td><FluentButton Appearance="Appearance.Accent" @onclick="AddHashFieldAsync">+</FluentButton></td>
</tr>
```

Add `EventCallback<string> OnDeleteHashField` and `EventCallback<(string Field, string Value)> OnAddHashField`
parameters. Wire in `RedisPage.razor` to handlers calling the new client methods and then
`RefreshSelectedAsync`.

**Tests.** `DemoRedisClientTests.cs`: add field, verify present; delete field, verify absent;
delete non-existent field is a no-op.

---

### RDS-12 — Export keys to JSON download

**Problem.** There is no way to export the visible keyspace for offline inspection or backup.

**Approach.** Add an "Export" button to the toolbar in `RedisPage.razor`. When clicked, it iterates
over `Keys` (already loaded), fetches each key's full value (string/hash/list/set/zset), and
serialises the result to a JSON array. The file is then saved to the user's Downloads folder using
`System.IO.File.WriteAllTextAsync`, matching the pattern used by `DownloadAsync` in `StorageBlobList`.

**Button placement.** Add next to "Purge All" in the toolbar, disabled while `IsLoading`:

```razor
<FluentButton @onclick="ExportKeysAsync" Disabled="@(IsLoading || Keys.Count == 0)">
    Export JSON
</FluentButton>
```

**Export model.** In `RedisModels.cs` or an anonymous record:

```csharp
record RedisExportEntry(string Key, string Type, object? Value, long? TtlSeconds);
```

**`ExportKeysAsync` (abbreviated):**

```csharp
private async Task ExportKeysAsync()
{
    if (Client is null || Keys.Count == 0) return;
    IsLoading = true;
    await InvokeAsync(StateHasChanged);
    try
    {
        var entries = new List<RedisExportEntry>();
        foreach (var key in Keys)
        {
            var info = await Client.GetKeyInfoAsync(key);
            object? value = info.Type switch {
                "string" => await Client.GetKeyValueAsync(key),
                "hash"   => (await Client.GetHashFieldsAsync(key))
                                .ToDictionary(f => f.Field, f => f.Value),
                "list"   => await Client.GetListItemsAsync(key, 0, -1),
                "set"    => await Client.GetSetMembersAsync(key),
                "zset"   => (await Client.GetSortedSetMembersAsync(key, 0, -1))
                                .Select(e => new { e.Member, e.Score }),
                _        => null
            };
            entries.Add(new(key, info.Type, value, (long?)info.Ttl?.TotalSeconds));
        }
        var json = System.Text.Json.JsonSerializer.Serialize(entries,
            new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
        var path = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "Downloads", $"redis-export-{DateTime.Now:yyyyMMdd-HHmmss}.json");
        await System.IO.File.WriteAllTextAsync(path, json);
        Notifications.ShowSuccess("Export complete", path);
    }
    catch (Exception ex) { ErrorMessage = ex.Message; }
    finally { IsLoading = false; await InvokeAsync(StateHasChanged); }
}
```

**Note.** For very large keysets export may be slow. If `Keys.Count > 5000`, show a confirmation
dialog warning about export size before proceeding.

---

## Implementation Order

1. **RDS-10** (connection string masking) — isolated CSS/UI change, zero risk, highest visibility during demos.
2. **RDS-5** (copy key name) — one-liner, no interface changes.
3. **RDS-8** (TTL pre-populate) — single `OnParametersSet` change, no interface changes.
4. **RDS-2** (binary detection) — adds `IsBinaryContent` helper + test; self-contained.
5. **RDS-7** (preserve TTL across separator change) — add a protective comment and verify invariant; trivial.
6. **RDS-1** (scan pagination) — moderate risk (changes core scan loop); do before RDS-4 to establish the pagination pattern.
7. **RDS-4** (list/set pagination) — builds on pagination state model established in RDS-1.
8. **RDS-6** (key rename) — requires `IRedisClient` extension + demo impl + UI.
9. **RDS-3** (sorted set score editing) — requires `IRedisClient` extension + demo impl + UI.
10. **RDS-11** (hash field add/delete) — requires `IRedisClient` extension + demo impl + UI.
11. **RDS-9** (multi-key delete) — requires tree checkbox state + confirmation; do after RDS-11 to batch-test deletes.
12. **RDS-12** (JSON export) — do last; depends on all value-fetch paths being tested.
