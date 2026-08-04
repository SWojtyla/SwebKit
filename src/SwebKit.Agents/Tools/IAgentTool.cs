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

/// <summary>
/// Which feature area a tool belongs to. Used by <c>SidecarAgentChatService</c> (ai-augmented-app
/// technical-plan.md Module 5) to scope a contextual conversation's tools to the page it was opened
/// from by default — an "Ask AI" panel opened from the AKS pod view shouldn't also be handed Redis
/// or Storage tools it was never asked about. No default value on purpose: every tool must declare
/// its area explicitly rather than silently inheriting one that might be wrong.
/// </summary>
public enum FeatureArea
{
    Aks,
    ServiceBus,
    Redis,
    Storage,
    Observability,
    ApiClient,

    /// <summary>Cross-area tools (workspace-intelligence Module 3) — unlike every other area, these
    /// are NOT exempt from the per-area filter the way Observability is; they only become visible
    /// when a turn explicitly requests <c>scope: "workspace"</c> (or from the global <c>/agent</c>
    /// page, which has no area filter to begin with). See <c>SidecarAgentChatService.ResolveTools</c>.</summary>
    Workspace,
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

    /// <summary>Which feature area this tool belongs to (see <see cref="FeatureArea"/>).</summary>
    FeatureArea FeatureArea { get; }

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