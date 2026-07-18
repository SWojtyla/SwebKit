using SwebKit.Core.Domain;

namespace SwebKit.App.Components.ApiClient;

/// <summary>
/// Tab lifecycle concern for <see cref="ApiClientPage"/>.
/// </summary>
/// <remarks>
/// Slice 3 of the decomposition tracked in
/// docs/features/active/api-client-page-decomposition/. Pure file-boundary move: no behavior
/// change. These members still mutate the page-owned <c>_state</c>/dialog fields and call other
/// partial-class members (<c>SaveRequestAsync</c>, <c>EnvironmentRepo</c>) directly, by design
/// (DEC-PD-1 in this feature's decisions.md).
/// </remarks>
public partial class ApiClientPage
{
    // Close-dirty-tab confirm dialog state (Phase 3, Task 5)
    private bool _showTabCloseConfirmDialog;
    private string? _pendingCloseTabRequestId;

    private void RestoreSelectedRequest(string requestId)
    {
        if (_state.ActiveCollection is null) return;
        var (_, request) = CollectionRepo.FindRequest(requestId);
        if (request is not null)
        {
            _state.SelectedRequestId = requestId;
            _state.SelectedRequest = request;
        }
    }

    private async Task OnTabSelectedAsync(string requestId)
    {
        // Phase 3, Task 4: focus an existing tab (raised by ApiClientOpenTabsStrip). Mirrors the
        // tab's Request into the existing single-request fields, same as OnRequestSelectedAsync's
        // ON branch above.
        var tab = _state.OpenTabs.FirstOrDefault(t => t.RequestId == requestId);
        if (tab is null)
        {
            await InvokeAsync(StateHasChanged); // BL-2
            return;
        }

        _state.ActiveTabRequestId = requestId;
        _state.SelectedRequest = tab.Request;
        _state.SelectedRequestId = tab.RequestId;
        _state.IsDirty = _state.DirtyByRequestId.GetValueOrDefault(tab.RequestId);
        _state.LinkedSaveError = null;
        _state.LinkedSaveConflict = null;
        _state.LastResult = _state.LastResultByRequestId.GetValueOrDefault(tab.RequestId);
        _state.SubscriptionMessages.Clear();
        if (_state.SubscriptionMessagesByRequestId.TryGetValue(tab.RequestId, out var messages))
        {
            _state.SubscriptionMessages.AddRange(messages);
        }

        await InvokeAsync(StateHasChanged); // BL-2
    }

    // Phase 3, Task 5: raised by ApiClientOpenTabsStrip's close (✕) button. If the tab's request
    // has unsaved changes (DirtyByRequestId), prompts a Save/Discard/Cancel confirm dialog before
    // closing; otherwise closes immediately. See CloseTab for the active-tab reassignment rule.
    private async Task OnTabCloseRequestedAsync(string requestId)
    {
        if (_state.DirtyByRequestId.TryGetValue(requestId, out var isDirty) && isDirty)
        {
            _pendingCloseTabRequestId = requestId;
            _showTabCloseConfirmDialog = true;
            await InvokeAsync(StateHasChanged); // BL-2
            return;
        }

        CloseTab(requestId);
        await InvokeAsync(StateHasChanged); // BL-2
    }

    // "Save" choice in the close-dirty-tab confirm dialog. SaveRequestAsync/SaveActiveCollectionAsync
    // operate on _state.SelectedRequest + _state.ActiveCollection (there is no overload taking a
    // request explicitly), so this temporarily points SelectedRequest/SelectedRequestId at the
    // tab being closed, saves via the existing method, then restores whatever was selected before
    // (CloseTab below reassigns it again if the closed tab was itself the active one). This is the
    // simpler of the two options considered — an explicit-request overload of SaveActiveCollectionAsync
    // would avoid the temporary swap but touches more call sites for no behavioural gain, since tabs
    // in this phase are always scoped to the single ActiveCollection anyway.
    private async Task SaveAndCloseTabAsync()
    {
        var requestId = _pendingCloseTabRequestId;
        _showTabCloseConfirmDialog = false;
        _pendingCloseTabRequestId = null;
        if (requestId is null)
        {
            await InvokeAsync(StateHasChanged); // BL-2
            return;
        }

        var tab = _state.OpenTabs.FirstOrDefault(t => t.RequestId == requestId);
        if (tab is not null)
        {
            var previousSelectedRequest = _state.SelectedRequest;
            var previousSelectedRequestId = _state.SelectedRequestId;

            _state.SelectedRequest = tab.Request;
            _state.SelectedRequestId = tab.RequestId;

            await SaveRequestAsync();

            _state.SelectedRequest = previousSelectedRequest;
            _state.SelectedRequestId = previousSelectedRequestId;
            _state.IsDirty = previousSelectedRequestId is not null
                && _state.DirtyByRequestId.GetValueOrDefault(previousSelectedRequestId);
        }

        CloseTab(requestId);
        await InvokeAsync(StateHasChanged); // BL-2
    }

    // "Discard" choice in the close-dirty-tab confirm dialog: closes the tab without saving.
    private async Task DiscardAndCloseTabAsync()
    {
        var requestId = _pendingCloseTabRequestId;
        _showTabCloseConfirmDialog = false;
        _pendingCloseTabRequestId = null;
        if (requestId is not null)
        {
            CloseTab(requestId);
        }

        await InvokeAsync(StateHasChanged); // BL-2
    }

    // "Cancel" choice in the close-dirty-tab confirm dialog: closes the dialog, nothing changes.
    private async Task CancelTabCloseConfirm()
    {
        _showTabCloseConfirmDialog = false;
        _pendingCloseTabRequestId = null;
        await InvokeAsync(StateHasChanged); // BL-2
    }

    /// <summary>
    /// Removes a tab from <see cref="ApiClientState.OpenTabs"/> and clears its dirty flag. If the
    /// closed tab was the active one, reassigns <see cref="ApiClientState.ActiveTabRequestId"/> (and
    /// mirrors SelectedRequest/SelectedRequestId) to the previous tab in list order, or the next
    /// tab if the closed tab was first — or to the empty state (all null) if no tabs remain.
    /// </summary>
    private void CloseTab(string requestId)
    {
        var index = _state.OpenTabs.FindIndex(t => t.RequestId == requestId);
        if (index < 0) return;

        _state.OpenTabs.RemoveAt(index);
        _state.DirtyByRequestId.Remove(requestId);

        if (_state.ActiveTabRequestId != requestId)
        {
            return;
        }

        if (_state.OpenTabs.Count == 0)
        {
            _state.ActiveTabRequestId = null;
            _state.SelectedRequestId = null;
            _state.SelectedRequest = null;
            _state.IsDirty = false;
            _state.LastResult = null;
            _state.SubscriptionMessages.Clear();
            return;
        }

        var newIndex = Math.Min(index > 0 ? index - 1 : 0, _state.OpenTabs.Count - 1);
        var newActiveTab = _state.OpenTabs[newIndex];
        _state.ActiveTabRequestId = newActiveTab.RequestId;
        _state.SelectedRequest = newActiveTab.Request;
        _state.SelectedRequestId = newActiveTab.RequestId;
        _state.IsDirty = _state.DirtyByRequestId.GetValueOrDefault(newActiveTab.RequestId);
        _state.LinkedSaveError = null;
        _state.LinkedSaveConflict = null;
        _state.LastResult = _state.LastResultByRequestId.GetValueOrDefault(newActiveTab.RequestId);
        _state.SubscriptionMessages.Clear();
        if (_state.SubscriptionMessagesByRequestId.TryGetValue(newActiveTab.RequestId, out var messages))
        {
            _state.SubscriptionMessages.AddRange(messages);
        }
    }

    private Task PersistLastSelectionAsync(string collectionId, string requestId)
    {
        return EnvironmentRepo.SetLastSelectedRequestAsync(collectionId, requestId);
    }
}
