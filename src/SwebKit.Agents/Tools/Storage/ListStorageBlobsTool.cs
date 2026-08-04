using System.Text.Json;
using SwebKit.Core.Abstractions;
using SwebKit.Core.Configuration;
using SwebKit.Core.Services;

namespace SwebKit.Agents.Tools.Storage;

/// <summary>Lists blobs (and virtual folder prefixes) in a container, one page at a time.</summary>
public sealed class ListStorageBlobsTool : IAgentTool
{
    private readonly AppStateService _appState;
    private readonly ProfileRepository _profiles;
    private readonly IStorageClientFactory _factory;

    public ListStorageBlobsTool(AppStateService appState, ProfileRepository profiles, IStorageClientFactory factory)
    {
        _appState = appState;
        _profiles = profiles;
        _factory = factory;
    }

    public string Name => "list_storage_blobs";
    public string Description => "Lists blobs and folders inside a storage container. Returns one page (up to 100 items); use the returned continuation_token to page further.";
    public FeatureArea FeatureArea => FeatureArea.Storage;

    public JsonElement ParametersSchema { get; } = AgentToolSchema.Parse("""
        {
          "type": "object",
          "properties": {
            "container_name": { "type": "string", "description": "The container to list blobs from." },
            "prefix": { "type": "string", "description": "Virtual folder prefix to list within. Empty for the container root." },
            "account_id": { "type": "string", "description": "Which configured storage account to use. If omitted, uses the first configured account." }
          },
          "required": ["container_name"]
        }
        """);

    public async Task<string> ExecuteAsync(JsonElement arguments, CancellationToken ct)
    {
        if (!arguments.TryGetProperty("container_name", out var cEl) || cEl.GetString() is not { Length: > 0 } containerName)
            return """{"error":"Missing required parameter 'container_name'."}""";

        var prefix = arguments.TryGetProperty("prefix", out var p) ? p.GetString() ?? "" : "";
        var accountId = arguments.TryGetProperty("account_id", out var a) ? a.GetString() : null;

        var resolution = StorageToolContext.Resolve(_appState, _profiles, _factory, accountId);
        if (resolution.Error is not null)
            return JsonSerializer.Serialize(new { error = resolution.Error });

        try
        {
            var page = await resolution.Client!.ListBlobsAsync(containerName, prefix, continuationToken: null, pageSize: 100, ct);
            return JsonSerializer.Serialize(new
            {
                account = resolution.Account!.DisplayName,
                container = containerName,
                prefix,
                items = page.Items.Select(i => new
                {
                    name = i.Name,
                    is_folder = i.IsPrefix,
                    size_bytes = i.SizeBytes,
                    content_type = i.ContentType,
                    last_modified = i.LastModified?.ToString("o"),
                }),
                continuation_token = page.ContinuationToken,
            });
        }
        catch (Exception ex)
        {
            return JsonSerializer.Serialize(new { error = ex.Message, container = containerName });
        }
    }
}
