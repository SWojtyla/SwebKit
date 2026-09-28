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
    private readonly IAccessReportService? _accessReport;

    /// <param name="accessReport">
    /// The per-environment access report (access-awareness Phase 2) — optional so hosts without
    /// the report service degrade gracefully: the short-circuit and observed-denial feed simply
    /// don't run, and classification still produces the structured <c>access_denied</c> result.
    /// </param>
    public AgentToolRegistry(IEnumerable<IAgentTool> tools, IAccessReportService? accessReport = null)
    {
        _tools = tools.ToDictionary(t => t.Name, StringComparer.OrdinalIgnoreCase);
        _accessReport = accessReport;
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

        var featureArea = tool.FeatureArea.ToString();
        var aware = tool as IAccessAwareTool;
        // Resolve the report's connection key once — the same key drives the pre-execution
        // short-circuit and the post-failure observed-denial feed.
        var connectionKey = ResolveConnectionKey(aware, arguments);

        // Known-denial short-circuit: a fresh probed/observed denial for this
        // (area, connection, capability) means the call would fail the same way — answer with the
        // denial JSON instead of hitting the SDK again. Unknown results never pre-empt
        // (TryGetKnownDenial only returns fresh Denied rows).
        if (aware is not null && connectionKey is not null && _accessReport is not null &&
            _accessReport.TryGetKnownDenial(featureArea, connectionKey, aware.Capability, out var known))
        {
            return AccessDeniedJson(known, cached: true);
        }

        try
        {
            return await tool.ExecuteAsync(arguments, ct);
        }
        catch (Exception ex)
        {
            // Authorization failures get a structured result the model can aggregate into an
            // access-gap report — in locked-down environments (typical PRD) the identity often
            // has rights on only some resources, and "request X on Y" is the actionable answer.
            if (AccessAdvisor.TryCreateDenial(ex, featureArea, out var denial))
            {
                if (aware is not null)
                {
                    // Refine the remedy table's coarse area capability (e.g. "service-bus.data")
                    // to the exact capability this call exercised — the same key the report rows
                    // use, so recording it flips the matching row red immediately.
                    denial = denial with { Capability = aware.Capability };
                    if (_accessReport is not null && connectionKey is not null)
                    {
                        try
                        {
                            _accessReport.RecordObservedDenial(denial, connectionKey);
                        }
                        catch
                        {
                            // A report-sink failure must never mask the denial result itself.
                        }
                    }
                }
                return AccessDeniedJson(denial, cached: false);
            }
            return JsonSerializer.Serialize(new { error = ex.Message }, SwebKitJsonOptions.Default);
        }
    }

    private static string? ResolveConnectionKey(IAccessAwareTool? aware, JsonElement arguments)
    {
        if (aware is null)
            return null;
        try
        {
            return aware.GetConnectionKey(arguments);
        }
        catch
        {
            // Malformed arguments — let the tool itself produce its validation error.
            return null;
        }
    }

    private static string AccessDeniedJson(AccessDenial denial, bool cached)
    {
        // The thrown-denial shape stays exactly as phase 1 shipped it; the short-circuit adds
        // "cached": true so the model knows the call was skipped rather than re-denied live.
        return cached
            ? JsonSerializer.Serialize(new
            {
                status = "access_denied",
                capability = denial.Capability,
                featureArea = denial.FeatureArea,
                requiredAccess = denial.RequiredAccess,
                guidance = denial.Guidance,
                detail = denial.Detail,
                cached = true,
            }, SwebKitJsonOptions.Default)
            : JsonSerializer.Serialize(new
            {
                status = "access_denied",
                capability = denial.Capability,
                featureArea = denial.FeatureArea,
                requiredAccess = denial.RequiredAccess,
                guidance = denial.Guidance,
                detail = denial.Detail,
            }, SwebKitJsonOptions.Default);
    }
}
