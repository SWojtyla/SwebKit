using System.Text.Json;
using SwebKit.Core.Domain;

namespace SwebKit.Agents.Tools;

/// <summary>Classifies a tool as read-only or mutating.</summary>
public enum ToolKind
{
    /// <summary>Read-only: fetches data, no side effects.</summary>
    Read,
    /// <summary>Mutating: creates, updates, deletes, or executes something. Requires confirmation.</summary>
    Mutate,
}

/// <summary>Risk level for tool execution, used in UI confirmation cards.</summary>
public enum ToolRisk
{
    /// <summary>Safe read or low-impact operation.</summary>
    None,
    /// <summary>Mutation of local data (reversible or low-impact).</summary>
    Low,
    /// <summary>Mutation that may affect external systems or is hard to reverse.</summary>
    High,
}

public interface IAgentTool
{
    string Name { get; }
    string Description { get; }

    /// <summary>
    /// JSON Schema object describing the tool's parameters.
    /// Use <see cref="AgentToolSchema"/> helpers to build a valid schema.
    /// </summary>
    JsonElement ParametersSchema { get; }

    /// <summary>Whether this tool reads data or mutates state. Defaults to <see cref="ToolKind.Read"/>.</summary>
    ToolKind Kind => ToolKind.Read;

    /// <summary>Risk level for confirmation UI. Defaults to <see cref="ToolRisk.None"/>.</summary>
    ToolRisk Risk => ToolRisk.None;

    /// <summary>Minimum capability the provider must support for this tool to be available.</summary>
    AgentCapability RequiredCapability => AgentCapability.ToolCalling;

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