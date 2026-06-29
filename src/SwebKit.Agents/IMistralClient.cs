using System.Text.Json;

namespace SwebKit.Agents;

public interface IMistralClient
{
    /// <summary>
    /// Sends a user message and runs the full agentic loop: if Mistral requests
    /// tool calls, <paramref name="toolExecutor"/> is invoked for each, and the
    /// results are sent back to Mistral before returning the final text response.
    /// </summary>
    Task<string> ChatAsync(
        string systemPrompt,
        string userMessage,
        IReadOnlyList<ToolDefinition> tools,
        Func<string, JsonElement, CancellationToken, Task<string>>? toolExecutor,
        CancellationToken ct);
}

public sealed class ToolDefinition
{
    public required string Name { get; set; }
    public required string Description { get; set; }
    public required JsonElement ParametersSchema { get; set; }
}