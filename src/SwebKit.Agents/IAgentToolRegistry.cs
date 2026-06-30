using System.Text.Json;
using SwebKit.Agents.Tools;

namespace SwebKit.Agents;

/// <summary>
/// Maintains a collection of <see cref="IAgentTool"/> implementations and provides the
/// plumbing the Mistral client needs:
/// <list type="bullet">
///   <item><see cref="GetDefinitions"/> – the JSON schema descriptors Mistral uses to decide which tool to call.</item>
///   <item><see cref="ExecuteAsync"/> – dispatches an inbound tool call to the matching implementation.</item>
/// </list>
/// </summary>
public interface IAgentToolRegistry
{
    /// <summary>Returns the tool schema descriptors for all registered tools.</summary>
    IReadOnlyList<ToolDefinition> GetDefinitions();

    /// <summary>Dispatches a tool call from Mistral to the correct <see cref="IAgentTool"/>.</summary>
    /// <param name="toolName">Name as returned in the tool-call response (must match <see cref="IAgentTool.Name"/>).</param>
    /// <param name="arguments">Raw JSON arguments from Mistral.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A string result that will be sent back to Mistral as the tool message.</returns>
    Task<string> ExecuteAsync(string toolName, JsonElement arguments, CancellationToken ct);
}
