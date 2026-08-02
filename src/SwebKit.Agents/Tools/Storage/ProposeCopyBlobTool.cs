using System.Text.Json;
using SwebKit.Core.Configuration;
using SwebKit.Core.Services;

namespace SwebKit.Agents.Tools.Storage;

/// <summary>
/// Proposes copying a blob within the same storage account. Propose-only, low risk (copying
/// doesn't touch the source blob). Note: <c>IStorageClient</c> has no delete-blob method today, so
/// unlike Redis there is no "propose delete" tool for Storage — only mutations the client actually
/// supports are proposable (see ai-augmented-app technical-plan.md Module 4 for why).
/// </summary>
public sealed class ProposeCopyBlobTool : IAgentTool
{
    private readonly IAgentActionCoordinator _coordinator;

    public ProposeCopyBlobTool(IAgentActionCoordinator coordinator) => _coordinator = coordinator;

    public string Name => "propose_copy_blob";
    public string Description => "Propose copying a blob to a new location within the same storage account. Returns a pending action for user confirmation.";
    public FeatureArea FeatureArea => FeatureArea.Storage;
    public ToolKind Kind => ToolKind.Mutate;
    public ToolRisk Risk => ToolRisk.Low;

    public JsonElement ParametersSchema { get; } = AgentToolSchema.Parse("""
        {
          "type": "object",
          "properties": {
            "source_container": { "type": "string", "description": "Container the source blob is in." },
            "source_blob_name": { "type": "string", "description": "Path of the source blob." },
            "destination_container": { "type": "string", "description": "Container to copy into." },
            "destination_blob_name": { "type": "string", "description": "Path for the copy." },
            "overwrite": { "type": "boolean", "description": "Whether to overwrite an existing blob at the destination. Defaults to false." },
            "account_id": { "type": "string", "description": "Which configured storage account to use. If omitted, uses the first configured account." }
          },
          "required": ["source_container", "source_blob_name", "destination_container", "destination_blob_name"]
        }
        """);

    public Task<string> ExecuteAsync(JsonElement arguments, CancellationToken ct)
    {
        string? Get(string prop) => arguments.TryGetProperty(prop, out var v) ? v.GetString() : null;

        var sourceContainer = Get("source_container");
        var sourceBlob = Get("source_blob_name");
        var destContainer = Get("destination_container");
        var destBlob = Get("destination_blob_name");
        if (string.IsNullOrEmpty(sourceContainer) || string.IsNullOrEmpty(sourceBlob) ||
            string.IsNullOrEmpty(destContainer) || string.IsNullOrEmpty(destBlob))
        {
            return Task.FromResult("""{"error":"Missing one or more required parameters: source_container, source_blob_name, destination_container, destination_blob_name."}""");
        }

        var actionId = Guid.NewGuid().ToString("N");
        var summary = $"Copy '{sourceContainer}/{sourceBlob}' to '{destContainer}/{destBlob}'";
        var action = new PendingAgentAction
        {
            Id = actionId,
            Type = AgentActionType.CopyBlob,
            Summary = summary,
            Target = $"Blob '{destContainer}/{destBlob}'",
            Risk = AgentActionRisk.Low,
            Preview = $"From: {sourceContainer}/{sourceBlob}\nTo: {destContainer}/{destBlob}",
            ExpectedFingerprint = null,
            Payload = arguments.Clone(),
        };
        _coordinator.RegisterAction(action);

        return Task.FromResult(JsonSerializer.Serialize(new
        {
            action_id = actionId,
            status = "pending_confirmation",
            summary = action.Summary,
            preview = action.Preview,
            risk = "Low",
            expires_at = action.ExpiresAt.ToString("yyyy-MM-dd HH:mm UTC"),
            message = "Copy proposed. User must confirm before it happens.",
        }));
    }
}
