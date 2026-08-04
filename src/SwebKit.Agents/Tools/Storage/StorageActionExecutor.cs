using System.Text.Json;
using SwebKit.Core.Configuration;
using SwebKit.Core.Domain;
using SwebKit.Core.Services;

namespace SwebKit.Agents.Tools.Storage;

/// <summary>
/// Applies confirmed Storage actions (copy blob). The <see cref="IAgentActionExecutor"/>
/// implementation for the Storage area — see <c>AgentActionApplier</c> for dispatch.
/// </summary>
public sealed class StorageActionExecutor : IAgentActionExecutor
{
    private readonly AppStateService _appState;
    private readonly ProfileRepository _profiles;
    private readonly SwebKit.Core.Abstractions.IStorageClientFactory _factory;

    public StorageActionExecutor(AppStateService appState, ProfileRepository profiles, SwebKit.Core.Abstractions.IStorageClientFactory factory)
    {
        _appState = appState;
        _profiles = profiles;
        _factory = factory;
    }

    public bool CanHandle(AgentActionType type) => type == AgentActionType.CopyBlob;

    public async Task<AgentActionResult> ApplyAsync(PendingAgentAction action, CancellationToken ct)
    {
        if (action.Payload is not { } payload)
            return Fail("Missing structured payload.");

        string? Get(string prop) => payload.TryGetProperty(prop, out var v) ? v.GetString() : null;
        var sourceContainer = Get("source_container");
        var sourceBlob = Get("source_blob_name");
        var destContainer = Get("destination_container");
        var destBlob = Get("destination_blob_name");
        if (string.IsNullOrEmpty(sourceContainer) || string.IsNullOrEmpty(sourceBlob) ||
            string.IsNullOrEmpty(destContainer) || string.IsNullOrEmpty(destBlob))
        {
            return Fail("Missing one or more required fields in the proposed action's payload.");
        }

        var overwrite = payload.TryGetProperty("overwrite", out var o) && o.ValueKind == JsonValueKind.True;
        var accountId = Get("account_id");
        var resolution = StorageToolContext.Resolve(_appState, _profiles, _factory, accountId);
        if (resolution.Error is not null)
            return Fail(resolution.Error);

        var result = await resolution.Client!.CopyBlobAsync(
            new BlobCopyOptions(sourceContainer, sourceBlob, destContainer, destBlob, Overwrite: overwrite),
            ct);

        return new AgentActionResult
        {
            IsSuccess = result.Success,
            ErrorMessage = result.ErrorMessage,
            ResultSummary = result.Success ? $"Copied to '{result.ResultBlobPath ?? $"{destContainer}/{destBlob}"}'" : null,
        };
    }

    private static AgentActionResult Fail(string message) => new() { IsSuccess = false, ErrorMessage = message };
}
