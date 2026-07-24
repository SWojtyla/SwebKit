using System.Text.Json;
using SwebKit.Agents.Tools;

namespace SwebKit.Agents;

/// <summary>
/// Default implementation of <see cref="IAgentToolRegistry"/>.
/// Tools are injected via <c>IEnumerable&lt;IAgentTool&gt;</c> (open-type DI registration).
/// </summary>
public sealed class AgentToolRegistry : IAgentToolRegistry
{
    private readonly Dictionary<string, IAgentTool> _tools;

    public AgentToolRegistry(IEnumerable<IAgentTool> tools)
    {
        _tools = tools.ToDictionary(t => t.Name, StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyList<ToolDefinition> GetDefinitions()
    {
        return _tools.Values
            .Select(t => new ToolDefinition
            {
                Name = t.Name,
                Description = t.Description,
                ParametersSchema = t.ParametersSchema,
                Kind = t.Kind,
                Risk = t.Risk,
                RequiredCapability = t.RequiredCapability
            })
            .ToList();
    }

    public async Task<string> ExecuteAsync(string toolName, JsonElement arguments, CancellationToken ct)
    {
        if (!_tools.TryGetValue(toolName, out var tool))
            return $"{{\"error\": \"Unknown tool '{toolName}'\"}}";

        try
        {
            return await tool.ExecuteAsync(arguments, ct);
        }
        catch (Exception ex)
        {
            return $"{{\"error\": \"{ex.Message.Replace("\"", "\\\"")}\"}}";
        }
    }
}
