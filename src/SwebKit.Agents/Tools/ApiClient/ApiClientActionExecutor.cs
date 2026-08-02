using SwebKit.Core.Abstractions;
using SwebKit.Core.Domain;

namespace SwebKit.Agents.Tools.ApiClient;

/// <summary>
/// Applies confirmed API Client actions (create/update/duplicate/move/delete a request, execute an
/// HTTP request). The <see cref="IAgentActionExecutor"/> implementation for the API Client area —
/// see <c>AgentActionApplier</c> for how executors are dispatched by <c>AgentActionType</c>.
/// </summary>
public sealed class ApiClientActionExecutor : IAgentActionExecutor
{
    private readonly IApiClientAgentService _apiClient;

    public ApiClientActionExecutor(IApiClientAgentService apiClient)
    {
        _apiClient = apiClient;
    }

    public bool CanHandle(AgentActionType type) => type is
        AgentActionType.CreateRequest or
        AgentActionType.UpdateRequest or
        AgentActionType.DeleteRequest or
        AgentActionType.DuplicateRequest or
        AgentActionType.MoveRequest or
        AgentActionType.RenameFolder or
        AgentActionType.DeleteFolder or
        AgentActionType.ExecuteHttpRequest;

    public Task<AgentActionResult> ApplyAsync(PendingAgentAction action, CancellationToken ct) => action.Type switch
    {
        AgentActionType.CreateRequest => ApplyCreateAsync(action, ct),
        AgentActionType.UpdateRequest => ApplyUpdateAsync(action, ct),
        AgentActionType.DeleteRequest => ApplyDeleteAsync(action, ct),
        AgentActionType.DuplicateRequest => ApplyDuplicateAsync(action, ct),
        AgentActionType.MoveRequest => ApplyMoveAsync(action, ct),
        AgentActionType.ExecuteHttpRequest => ApplyExecuteHttpAsync(action, ct),
        // No tool proposes RenameFolder/DeleteFolder yet (ApiClientTools.cs has no folder-rename/
        // delete proposal tool), so these are unreachable today — handled explicitly rather than
        // silently falling through, so a future tool that *does* propose one gets a clear signal
        // this executor needs a branch added, not a confusing generic failure.
        _ => Task.FromResult(Fail($"'{action.Type}' is not yet implemented in {nameof(ApiClientActionExecutor)}.")),
    };

    private async Task<AgentActionResult> ApplyCreateAsync(PendingAgentAction action, CancellationToken ct)
    {
        if (action.Payload is not { } payload)
            return Fail("Missing structured payload for create.");

        var collectionId = GetString(payload, "collection_id");
        var name = GetString(payload, "name");
        if (string.IsNullOrEmpty(collectionId) || string.IsNullOrEmpty(name))
            return Fail("Missing 'collection_id' or 'name' in the proposed action's payload.");

        var method = TryGetMethod(payload) ?? ApiRequestMethod.Get;
        var url = GetString(payload, "url") ?? "";
        var folderPath = GetString(payload, "folder_path");

        var result = await _apiClient.CreateRequestAsync(collectionId, folderPath, name, method, url, ct);
        return ToResult(result, result.IsSuccess ? $"Created request '{name}'" : null);
    }

    private async Task<AgentActionResult> ApplyUpdateAsync(PendingAgentAction action, CancellationToken ct)
    {
        if (action.Payload is not { } payload)
            return Fail("Missing structured payload for update.");

        var requestId = GetString(payload, "request_id");
        if (string.IsNullOrEmpty(requestId))
            return Fail("Missing 'request_id' in the proposed action's payload.");

        var result = await _apiClient.UpdateRequestAsync(
            requestId,
            name: GetString(payload, "name"),
            method: TryGetMethod(payload),
            url: GetString(payload, "url"),
            ct: ct);
        return ToResult(result, result.IsSuccess ? "Request updated" : null);
    }

    private async Task<AgentActionResult> ApplyDeleteAsync(PendingAgentAction action, CancellationToken ct)
    {
        var requestId = ExtractRequestIdFromTarget(action.Target);
        var result = await _apiClient.DeleteRequestAsync(requestId, ct);
        return ToResult(result, result.IsSuccess ? $"Deleted request '{requestId}'" : null);
    }

    private async Task<AgentActionResult> ApplyDuplicateAsync(PendingAgentAction action, CancellationToken ct)
    {
        var requestId = ExtractRequestIdFromTarget(action.Target);
        var result = await _apiClient.DuplicateRequestAsync(requestId, ct);
        return ToResult(result, result.IsSuccess ? "Request duplicated" : null);
    }

    private async Task<AgentActionResult> ApplyMoveAsync(PendingAgentAction action, CancellationToken ct)
    {
        if (action.Payload is not { } payload)
            return Fail("Missing structured payload for move.");

        var requestId = GetString(payload, "request_id") ?? ExtractRequestIdFromTarget(action.Target);
        var folderPath = GetString(payload, "folder_path");
        var newIndex = payload.TryGetProperty("new_index", out var ni) && ni.ValueKind == System.Text.Json.JsonValueKind.Number
            ? ni.GetInt32()
            : (int?)null;

        var result = await _apiClient.MoveRequestAsync(requestId, folderPath, newIndex, ct);
        return ToResult(result, result.IsSuccess ? "Request moved" : null);
    }

    private async Task<AgentActionResult> ApplyExecuteHttpAsync(PendingAgentAction action, CancellationToken ct)
    {
        var requestId = ExtractRequestIdFromTarget(action.Target);
        var snapshot = await _apiClient.GetRequestAsync(requestId, ct);
        if (snapshot is null)
            return Fail("Request not found.");

        if (action.ExpectedFingerprint is not null)
        {
            var currentFingerprint = snapshot.UpdatedAt.ToString("O");
            if (currentFingerprint != action.ExpectedFingerprint)
                return Fail("Request has changed since the preview was generated. Please regenerate the proposal.");
        }

        // Deliberately still not implemented: IApiClientAgentService only exposes a masked
        // ApiRequestSnapshot, not the full HttpRequestEntry/ApiCollection/active-environment
        // IHttpRequestExecutor.ExecuteAsync needs. Doing this properly means either adding a new
        // method to IApiClientAgentService to resolve those, or resolving them directly from
        // CollectionRepository/EnvironmentRepository — a bigger, security-sensitive addition
        // (real outbound HTTP against a possibly-external server) that deserves its own careful
        // pass rather than being rushed alongside the rest of this module. See
        // docs/features/active/ai-augmented-app/technical-plan.md Module 3.
        return Fail(
            "HTTP execution requires the full request entry and active environment, which " +
            "IApiClientAgentService doesn't expose yet — not implemented in this pass.");
    }

    private static string? GetString(System.Text.Json.JsonElement payload, string property) =>
        payload.TryGetProperty(property, out var value) && value.ValueKind == System.Text.Json.JsonValueKind.String
            ? value.GetString()
            : null;

    private static ApiRequestMethod? TryGetMethod(System.Text.Json.JsonElement payload) =>
        payload.TryGetProperty("method", out var m) && Enum.TryParse<ApiRequestMethod>(m.GetString(), out var parsed)
            ? parsed
            : null;

    private static string ExtractRequestIdFromTarget(string target)
    {
        // Target format: "Request 'Name' (id)"
        var start = target.LastIndexOf('(');
        var end = target.LastIndexOf(')');
        if (start > 0 && end > start)
            return target.Substring(start + 1, end - start - 1);
        return target;
    }

    private static AgentActionResult Fail(string message) => new() { IsSuccess = false, ErrorMessage = message };

    private static AgentActionResult ToResult(ApiClientMutationResult result, string? successSummary) => new()
    {
        IsSuccess = result.IsSuccess,
        ErrorMessage = result.ErrorMessage,
        ResultSummary = result.IsSuccess ? successSummary : null,
    };
}
