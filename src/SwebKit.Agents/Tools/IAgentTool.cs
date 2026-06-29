using System.Text.Json;

namespace SwebKit.Agents.Tools;

public interface IAgentTool
{
    string Name { get; }
    string Description { get; }
    Task<string> ExecuteAsync(JsonElement arguments, CancellationToken ct);
}