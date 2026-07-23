using System.Text.Json;

namespace SwebKit.Agents.Tools;

public interface IAgentTool
{
    string Name { get; }
    string Description { get; }

    /// <summary>
    /// JSON Schema object describing the tool's parameters.
    /// Use <see cref="AgentToolSchema"/> helpers to build a valid schema.
    /// </summary>
    JsonElement ParametersSchema { get; }

    Task<string> ExecuteAsync(JsonElement arguments, CancellationToken ct);
}

/// <summary>Helpers for building JSON parameter schemas passed to the LLM.</summary>
public static class AgentToolSchema
{
    /// <summary>
    /// Parses a raw JSON schema string into a <see cref="JsonElement"/>.
    /// The string must remain valid for the lifetime of the returned element
    /// (parse into a <c>static readonly</c> field).
    /// </summary>
    public static JsonElement Parse(string json)
    {
        // JsonDocument.RootElement is only valid while the document is alive.
        // We clone to detach from the document lifetime.
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.Clone();
    }
}