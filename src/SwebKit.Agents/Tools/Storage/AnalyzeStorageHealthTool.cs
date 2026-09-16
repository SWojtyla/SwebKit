using System.Text.Json;
using SwebKit.Core.Abstractions;
using SwebKit.Core.Configuration;
using SwebKit.Core.Services;

namespace SwebKit.Agents.Tools.Storage;

/// <summary>
/// Composite tool that analyzes a Storage account's health: lists its containers and fetches the
/// capabilities snapshot (versioning, soft delete, mutation permissions) in parallel, then reports
/// a plain-English health_summary — the Storage analogue of <c>AnalyzeCacheHealthTool</c>. Added in
/// agent-correlation Module 5 so <c>InvestigateWorkspaceIssueTool</c> can produce a real report for
/// Storage-area topology nodes instead of an honest "skipped".
/// </summary>
public sealed class AnalyzeStorageHealthTool : IAgentTool
{
    private readonly AppStateService _appState;
    private readonly ProfileRepository _profiles;
    private readonly IStorageClientFactory _factory;

    public AnalyzeStorageHealthTool(AppStateService appState, ProfileRepository profiles, IStorageClientFactory factory)
    {
        _appState = appState;
        _profiles = profiles;
        _factory = factory;
    }

    public string Name => "analyze_storage_health";

    public string Description =>
        "Analyzes a Storage account's health by listing its containers and fetching the account's " +
        "capability snapshot in parallel. Returns the container count and names, versioning/" +
        "soft-delete/mutation capabilities, and a plain-English health_summary field (Healthy or Critical).";

    public FeatureArea FeatureArea => FeatureArea.Storage;

    public JsonElement ParametersSchema { get; } = AgentToolSchema.Parse("""
        {
          "type": "object",
          "properties": {
            "account": { "type": "string", "description": "Which configured storage account to use — its id, account name, or display name. If omitted, uses the first configured account." }
          },
          "required": []
        }
        """);

    public async Task<string> ExecuteAsync(JsonElement arguments, CancellationToken ct)
    {
        var accountKey = arguments.TryGetProperty("account", out var a) ? a.GetString() : null;
        var resolution = StorageToolContext.Resolve(_appState, _profiles, _factory, accountKey);
        if (resolution.Error is not null)
            return JsonSerializer.Serialize(new { error = resolution.Error });

        try
        {
            var client = resolution.Client!;
            var containersTask = client.ListContainersAsync(ct);
            var capabilitiesTask = client.GetStorageCapabilitiesAsync(ct);
            await Task.WhenAll(containersTask, capabilitiesTask);

            var containers = await containersTask;
            var capabilities = await capabilitiesTask;

            return JsonSerializer.Serialize(new
            {
                account = resolution.Account!.DisplayName,
                account_name = resolution.Account.AccountName,
                reachable = true,
                container_count = containers.Count,
                containers = containers.Take(25).Select(c => new
                {
                    name = c.Name,
                    last_modified = c.LastModified?.ToString("o"),
                    public_access = c.PublicAccess,
                }),
                capabilities = new
                {
                    versioning_enabled = capabilities.VersioningEnabled,
                    soft_delete_enabled = capabilities.SoftDeleteEnabled,
                    can_upload = capabilities.CanUpload,
                    can_copy = capabilities.CanCopy,
                    can_set_metadata = capabilities.CanSetMetadata,
                    can_restore = capabilities.CanRestore,
                },
                health_summary = "Healthy",
            });
        }
        catch (Exception ex)
        {
            return JsonSerializer.Serialize(new
            {
                account = resolution.Account!.DisplayName,
                account_name = resolution.Account.AccountName,
                error = ex.Message,
                health_summary = "Critical",
            });
        }
    }
}
