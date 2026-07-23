using System.Text.Json;

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

public sealed class ToolDefinition
{
    public required string Name { get; set; }
    public required string Description { get; set; }
    public required JsonElement ParametersSchema { get; set; }
}