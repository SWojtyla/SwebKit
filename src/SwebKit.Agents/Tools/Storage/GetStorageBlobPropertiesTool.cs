using System.Text.Json;
using SwebKit.Core.Abstractions;
using SwebKit.Core.Configuration;
using SwebKit.Core.Services;

namespace SwebKit.Agents.Tools.Storage;

/// <summary>Returns full properties (size, content type, tier, metadata, tags) for a single blob.</summary>
public sealed class GetStorageBlobPropertiesTool : IAgentTool
{
    private readonly AppStateService _appState;
    private readonly ProfileRepository _profiles;
    private readonly IStorageClientFactory _factory;

    public GetStorageBlobPropertiesTool(AppStateService appState, ProfileRepository profiles, IStorageClientFactory factory)
    {
        _appState = appState;
        _profiles = profiles;
        _factory = factory;
    }

    public string Name => "get_storage_blob_properties";
    public string Description => "Returns full properties for a single blob: size, content type, last modified, access tier, lease state, metadata, and tags.";
    public FeatureArea FeatureArea => FeatureArea.Storage;

    public JsonElement ParametersSchema { get; } = AgentToolSchema.Parse("""
        {
          "type": "object",
          "properties": {
            "container_name": { "type": "string", "description": "The container the blob is in." },
            "blob_name": { "type": "string", "description": "The full path of the blob within the container." },
            "account_id": { "type": "string", "description": "Which configured storage account to use. If omitted, uses the first configured account." }
          },
          "required": ["container_name", "blob_name"]
        }
        """);

    public async Task<string> ExecuteAsync(JsonElement arguments, CancellationToken ct)
    {
        if (!arguments.TryGetProperty("container_name", out var cEl) || cEl.GetString() is not { Length: > 0 } containerName)
            return """{"error":"Missing required parameter 'container_name'."}""";
        if (!arguments.TryGetProperty("blob_name", out var bEl) || bEl.GetString() is not { Length: > 0 } blobName)
            return """{"error":"Missing required parameter 'blob_name'."}""";

        var accountId = arguments.TryGetProperty("account_id", out var a) ? a.GetString() : null;
        var resolution = StorageToolContext.Resolve(_appState, _profiles, _factory, accountId);
        if (resolution.Error is not null)
            return JsonSerializer.Serialize(new { error = resolution.Error });

        try
        {
            var props = await resolution.Client!.GetBlobPropertiesAsync(containerName, blobName, ct);
            return JsonSerializer.Serialize(new
            {
                account = resolution.Account!.DisplayName,
                container = containerName,
                name = props.Name,
                size_bytes = props.SizeBytes,
                content_type = props.ContentType,
                last_modified = props.LastModified.ToString("o"),
                etag = props.ETag,
                access_tier = props.AccessTier,
                lease_state = props.LeaseState,
                metadata = props.Metadata,
                tags = props.Tags,
            });
        }
        catch (Exception ex)
        {
            return JsonSerializer.Serialize(new { error = ex.Message, container = containerName, blob = blobName });
        }
    }
}
