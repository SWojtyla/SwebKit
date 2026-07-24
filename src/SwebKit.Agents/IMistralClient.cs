using System.Text.Json;
using SwebKit.Agents.Tools;
using SwebKit.Core.Domain;

namespace SwebKit.Agents;

/// <summary>
/// Legacy interface kept for backward compatibility. Use <see cref="IAgentModelClient"/> instead.
/// </summary>
[Obsolete("Replaced by IAgentModelClient. Will be removed in a future version.")]
public interface IMistralClient
{
    Task<string> ChatAsync(
        string systemPrompt,
        string userMessage,
        IReadOnlyList<ToolDefinition> tools,
        List<object>? history,
        Func<string, JsonElement, CancellationToken, Task<string>>? toolExecutor,
        CancellationToken ct);
}

/// <summary>
/// Tool definition enriched with metadata for the agent loop and UI.
/// </summary>
public sealed class ToolDefinition
{
    public required string Name { get; set; }
    public required string Description { get; set; }
    public required JsonElement ParametersSchema { get; set; }

    /// <summary>Whether this tool reads data or mutates state.</summary>
    public ToolKind Kind { get; set; } = ToolKind.Read;

    /// <summary>Risk level for confirmation UI.</summary>
    public ToolRisk Risk { get; set; } = ToolRisk.None;

    /// <summary>Minimum capability the provider must support.</summary>
    public AgentCapability RequiredCapability { get; set; } = AgentCapability.ToolCalling;
}