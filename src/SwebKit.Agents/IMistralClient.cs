using System.Text.Json;

namespace SwebKit.Agents;

public interface IMistralClient
{
    /// <summary>
    /// Sends a user message and runs the full agentic loop: if Mistral requests
    /// tool calls, <paramref name="toolExecutor"/> is invoked for each, and the
    /// results are sent back to Mistral before returning the final text response.
    /// </summary>
    /// <param name="history">
    /// Optional mutable list of prior conversation messages (role/content pairs).
    /// When provided, prior turns are included in the request and the new user +
    /// assistant messages are appended to it so the next call carries full context.
    /// </param>
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