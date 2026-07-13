using Microsoft.JSInterop;
using SwebKit.Core.Domain;

namespace SwebKit.App.Components.ApiClient;

/// <summary>
/// Curl import/export concern for <see cref="ApiClientPage"/>.
/// </summary>
/// <remarks>
/// Slice 1 of the decomposition tracked in
/// docs/features/active/api-client-page-decomposition/. Pure file-boundary move: no behavior
/// change. These methods still mutate the page-owned <c>_state</c> field and call other
/// partial-class members (<c>GetRequestTargetCollection</c>, <c>ActivateCollection</c>,
/// <c>SaveActiveCollectionAsync</c>) directly, by design (DEC-PD-1 in this feature's
/// decisions.md) — the goal here is organization, not isolating this concern behind an
/// injected/delegate-based abstraction.
/// </remarks>
public partial class ApiClientPage
{
    private bool _showCurlImportDialog;
    private string _curlImportText = string.Empty;
    private string? _curlImportError;

    private void OpenCurlImportDialog()
    {
        _curlImportText = string.Empty;
        _curlImportError = null;
        _showCurlImportDialog = true;
    }

    private void OpenCurlImportDialogFromMenu()
    {
        OpenCurlImportDialog();
    }

    private async Task ImportCurlAsync()
    {
        var targetCollection = GetRequestTargetCollection();
        if (targetCollection is null) return;
        ActivateCollection(targetCollection);

        var result = WorkflowService.ImportCurl(_curlImportText);
        if (!result.IsSuccess || result.Request is null)
        {
            _curlImportError = result.ErrorMessage ?? "Could not import the cURL command.";
            return;
        }

        var request = result.Request;
        targetCollection.Nodes.Add(new ApiCollectionNode
        {
            Id = Guid.NewGuid().ToString("N"),
            Type = ApiCollectionNodeType.Request,
            Name = request.Name,
            Request = request,
        });
        _state.SelectedRequest = request;
        _state.SelectedRequestId = request.Id;
        _showCurlImportDialog = false;
        _state.WorkflowMessage = "Imported cURL request.";
        _state.WorkflowMessageIsError = false;

        if (await SaveActiveCollectionAsync())
        {
            _state.IsDirty = false;
        }

        await InvokeAsync(StateHasChanged);
    }

    private async Task CopyCurlAsync()
    {
        if (_state.SelectedRequest is null || _state.ActiveCollection is null) return;

        var curl = await WorkflowService.BuildCurlAsync(_state.SelectedRequest, _state.ActiveCollection, ActiveEnvironment);
        try
        {
            await JS.InvokeVoidAsync("navigator.clipboard.writeText", curl);
            _state.WorkflowMessage = "Copied masked cURL command.";
            _state.WorkflowMessageIsError = false;
        }
        catch (Exception ex) when (ex is InvalidOperationException or JSException)
        {
            _state.WorkflowMessage = "Clipboard copy failed.";
            _state.WorkflowMessageIsError = true;
        }

        await InvokeAsync(StateHasChanged);
    }
}
