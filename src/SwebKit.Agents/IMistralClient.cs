using System.Text.Json;

namespace SwebKit.Agents;

public interface IMistralClient
{
    Task<string> ChatAsync(
        string systemPrompt,
        string userMessage,
        IReadOnlyList<ToolDefinition> tools,
        CancellationToken ct);
}

public sealed class ToolDefinition
{
    public required string Name { get; set; }
    public required string Description { get; set; }
    public required JsonElement ParametersSchema { get; set; }
}