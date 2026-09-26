using System.Text.Json;
using SwebKit.Agents.Tools;
using SwebKit.Core.Security;
using SwebKit.Core.Serialization;

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
                RequiredCapability = t.RequiredCapability,
                FeatureArea = t.FeatureArea,
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
            // Authorization failures get a structured result the model can aggregate into an
            // access-gap report — in locked-down environments (typical PRD) the identity often
            // has rights on only some resources, and "request X on Y" is the actionable answer.
            if (AccessAdvisor.TryCreateDenial(ex, tool.FeatureArea.ToString(), out var denial))
            {
                return JsonSerializer.Serialize(new
                {
                    status = "access_denied",
                    capability = denial.Capability,
                    featureArea = denial.FeatureArea,
                    requiredAccess = denial.RequiredAccess,
                    guidance = denial.Guidance,
                    detail = denial.Detail,
                }, SwebKitJsonOptions.Default);
            }
            return JsonSerializer.Serialize(new { error = ex.Message }, SwebKitJsonOptions.Default);
        }
    }
}
