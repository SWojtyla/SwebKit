using Microsoft.Extensions.Logging;
using SwebKit.Core.Domain;

namespace SwebKit.App.Components.ApiClient;

/// <summary>
/// Request lifecycle, autosave, and result concern for <see cref="ApiClientPage"/>.
/// </summary>
/// <remarks>
/// Slice 7 of the decomposition tracked in
/// docs/features/active/api-client-page-decomposition/. Pure file-boundary move: no behavior
/// change. These members still mutate the page-owned <c>_state</c> field and call other
/// partial-class members (<c>PersistLastSelectionAsync</c>, <c>SyncNodeName</c>,
/// <c>SaveActiveCollectionAsync</c>) directly, by design (DEC-PD-1 in this feature's
/// decisions.md).
/// </remarks>
public partial class ApiClientPage
{
    // Phase 8 — request history (last 20 per request, in-memory only)
    private const int HistoryCap = 20;

    // Phase 5 — subscription state (capped at 1 000; oldest entry dropped when full)
    private const int SubscriptionMessageCap = 1_000;

    // Auto-save debounce
    private PeriodicTimer? _autoSaveTimer;
    private const int AutoSaveDebounceMs = 500;

    private async Task OnRequestSelectedAsync(HttpRequestEntry request)
    {
        if (!UserSettings.Settings.ApiClientRequestTabs)
        {
            _state.SelectedRequest = request;
            _state.SelectedRequestId = request.Id;
            _state.IsDirty = _state.DirtyByRequestId.GetValueOrDefault(request.Id);
            _state.LinkedSaveError = null;
            _state.LinkedSaveConflict = null;
            _state.LastResult = _state.LastResultByRequestId.GetValueOrDefault(request.Id);
            _state.SubscriptionMessages.Clear();
            if (_state.SubscriptionMessagesByRequestId.TryGetValue(request.Id, out var messages))
            {
                _state.SubscriptionMessages.AddRange(messages);
            }
        }
        else
        {
            // Phase 3, Task 4 (DEC-UX-1/DEC-UX-7): open (or focus) a tab for the selected request,
            // then mirror the active tab's request into the existing single-request fields so
            // RequestBuilderPanel/ResponseViewerPanel (bound via ApiClientRequestWorkspace) keep
            // working unchanged. Per-tab isolated render state is a later task (Task 6).
            var existingTab = _state.OpenTabs.FirstOrDefault(t => t.RequestId == request.Id);
            if (existingTab is null)
            {
                _state.OpenTabs.Add(new ApiClientOpenTab { RequestId = request.Id, Request = request });
            }
            _state.ActiveTabRequestId = request.Id;

            _state.SelectedRequest = request;
            _state.SelectedRequestId = request.Id;
            _state.IsDirty = _state.DirtyByRequestId.GetValueOrDefault(request.Id);
            _state.LinkedSaveError = null;
            _state.LinkedSaveConflict = null;
            _state.LastResult = _state.LastResultByRequestId.GetValueOrDefault(request.Id);
            _state.SubscriptionMessages.Clear();
            if (_state.SubscriptionMessagesByRequestId.TryGetValue(request.Id, out var messages))
            {
                _state.SubscriptionMessages.AddRange(messages);
            }
        }

        if (_state.ActiveCollection is not null)
        {
            // Fire-and-forget persistence of last selection; non-blocking
            _ = PersistLastSelectionAsync(_state.ActiveCollection.Id, request.Id);
        }

        await InvokeAsync(StateHasChanged); // BL-2
    }

    private async Task OnRequestChangedAsync()
    {
        _state.IsDirty = true;
        _state.LinkedSaveError = null;

        if (_state.SelectedRequestId is not null)
        {
            _state.DirtyByRequestId[_state.SelectedRequestId] = true;
        }

        // Sync ApiCollectionNode.Name from the live request name so the tree view reflects renames
        if (_state.SelectedRequest is not null && _state.ActiveCollection is not null)
            SyncNodeName(_state.ActiveCollection.Nodes, _state.SelectedRequest.Id, _state.SelectedRequest.Name);

        if (_state.AutoSave)
        {
            _autoSaveTimer?.Dispose();
            _autoSaveTimer = new PeriodicTimer(TimeSpan.FromMilliseconds(AutoSaveDebounceMs));
            _ = AutoSaveLoopAsync();
        }

        await InvokeAsync(StateHasChanged); // BL-2
    }

    private async Task AutoSaveLoopAsync()
    {
        if (_autoSaveTimer is null) return;

        try
        {
            while (await _autoSaveTimer.WaitForNextTickAsync())
            {
                if (_state.ActiveCollection is null || _state.SelectedRequest is null) continue;
                try
                {
                    if (await SaveActiveCollectionAsync())
                    {
                        _state.IsDirty = false;
                    }
                    await InvokeAsync(StateHasChanged); // BL-2
                }
                catch (Exception ex)
                {
                    Logger.LogError(ex, "Auto-save failed");
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Timer disposed - normal exit
        }
    }

    private async Task OnRequestResultAsync(HttpRequestResult result)
    {
        // Clear any previous subscription messages when a normal request completes
        _state.SubscriptionMessages.Clear();
        _state.LastResult = result;

        // Record in history for the active request
        if (_state.SelectedRequest is not null)
        {
            _state.LastResultByRequestId[_state.SelectedRequest.Id] = result;
            if (!_state.RequestHistory.TryGetValue(_state.SelectedRequest.Id, out var hist))
            {
                hist = [];
                _state.RequestHistory[_state.SelectedRequest.Id] = hist;
            }
            // Prepend so newest is first; cap at HistoryCap
            hist.Insert(0, result);
            if (hist.Count > HistoryCap)
                hist.RemoveAt(hist.Count - 1);
        }

        await InvokeAsync(StateHasChanged); // BL-2
    }

    private async Task OnSubscriptionMessageAsync(GraphQlSubscriptionMessage msg)
    {
        // Clear the last HTTP result when subscription starts
        if (_state.SubscriptionMessages.Count == 0)
            _state.LastResult = null;

        // Drop oldest when cap reached to prevent unbounded memory growth
        if (_state.SubscriptionMessages.Count >= SubscriptionMessageCap)
            _state.SubscriptionMessages.RemoveAt(0);

        _state.SubscriptionMessages.Add(msg);
        if (_state.SelectedRequestId is not null)
        {
            if (!_state.SubscriptionMessagesByRequestId.TryGetValue(_state.SelectedRequestId, out var messages))
            {
                messages = [];
                _state.SubscriptionMessagesByRequestId[_state.SelectedRequestId] = messages;
            }

            if (messages.Count >= SubscriptionMessageCap)
            {
                messages.RemoveAt(0);
            }

            messages.Add(msg);
        }
        await InvokeAsync(StateHasChanged); // BL-2
    }

    private static Task OnSubscriptionStoppedAsync()
    {
        // Nothing extra; the subscription message list persists until the next send
        return Task.CompletedTask;
    }

    private Task OnResendHistoryEntryAsync(HttpRequestResult entry)
    {
        // Load the historical result back into the viewer — does not re-execute the request
        _state.LastResult = entry;
        _state.SubscriptionMessages.Clear();
        return InvokeAsync(StateHasChanged);
    }

    private async Task SaveResponseExampleAsync(HttpRequestResult result)
    {
        if (_state.SelectedRequest is null || _state.ActiveCollection is null) return;

        var example = WorkflowService.CreateResponseExample(
            result,
            string.Empty,
            ActiveEnvironment?.Name);
        _state.SelectedRequest.ResponseExamples.Insert(0, example);
        if (await SaveActiveCollectionAsync())
        {
            _state.WorkflowMessage = "Saved response example.";
            _state.WorkflowMessageIsError = false;
        }
        else
        {
            _state.WorkflowMessage = "Response example needs conflict resolution before it can be saved.";
            _state.WorkflowMessageIsError = true;
        }

        await InvokeAsync(StateHasChanged);
    }
}
