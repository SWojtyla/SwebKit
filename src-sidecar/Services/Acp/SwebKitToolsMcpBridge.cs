using System.Text.Json;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using SwebKit.Agents;
using SwebKit.Agents.Tools;

namespace SwebKit.Sidecar.Services.Acp;

/// <summary>
/// Exposes SwebKit's domain tools (<see cref="IAgentToolRegistry"/>) to external ACP agents over
/// MCP (streamable HTTP, stateless). The sidecar passes this endpoint to the agent via ACP
/// <c>session/new → mcpServers</c>, so Claude/Codex/etc. see the same domain tools a local model
/// would — filtered by mode/area/scope through the <c>?tools=</c> allowlist baked into the URL.
///
/// Safety: mutating tools keep their propose/pending-approval semantics because they dispatch to
/// the same registry; nothing here grants fs/terminal access (those are ACP client capabilities,
/// which are negotiated off in AcpAgentHost).
/// </summary>
public sealed class SwebKitToolsMcpBridge
{
    public const string EndpointPath = "/mcp/swebkit-tools";

    private readonly IAgentToolRegistry _toolRegistry;
    private readonly IHttpContextAccessor _http;
    private readonly OutOfScopeCallTracker _outOfScopeCalls;

    public SwebKitToolsMcpBridge(IAgentToolRegistry toolRegistry, IHttpContextAccessor http, OutOfScopeCallTracker outOfScopeCalls)
    {
        _toolRegistry = toolRegistry;
        _http = http;
        _outOfScopeCalls = outOfScopeCalls;
    }

    /// <summary>Builds the per-session MCP URL handed to the ACP agent, with the mode/area/scope
    /// allowlist baked in as a query param (stateless transport → every call carries it).</summary>
    public static string BuildUrl(
        string baseUrl,
        IEnumerable<string>? allowedTools,
        IReadOnlyDictionary<string, string>? selection = null)
    {
        var parameters = new List<string>();
        var list = allowedTools is null ? null : string.Join(',', allowedTools);
        if (!string.IsNullOrEmpty(list))
            parameters.Add($"tools={Uri.EscapeDataString(list)}");
        if (selection is not null)
            parameters.AddRange(selection
                .Where(pair => !string.IsNullOrWhiteSpace(pair.Key))
                .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .Select(pair => $"sel={Uri.EscapeDataString($"{pair.Key}={pair.Value}")}"));
        var query = parameters.Count == 0 ? string.Empty : "?" + string.Join('&', parameters);
        return $"{baseUrl.TrimEnd('/')}{EndpointPath}{query}";
    }

    private string? AllowlistKey() => _http.HttpContext?.Request.Query["tools"].FirstOrDefault();

    private HashSet<string>? AllowedSet() => ParseAllowedSet(AllowlistKey());

    /// <summary><c>?mode=full</c> exposes the whole registry — including <c>propose_*</c> mutations —
    /// to a standalone client. Deliberately opt-in: internal ACP sessions always carry an explicit
    /// <c>?tools=</c> allowlist, so an absent one means an unmanaged client attached directly, and
    /// those get read tools only by default.</summary>
    private bool FullAccess() =>
        string.Equals(_http.HttpContext?.Request.Query["mode"].FirstOrDefault(), "full", StringComparison.OrdinalIgnoreCase);

    private IReadOnlyDictionary<string, string>? Selection() =>
        ParseSelection(_http.HttpContext?.Request.Query["sel"] ?? []);

    public ValueTask<ListToolsResult> ListToolsAsync(RequestContext<ListToolsRequestParams> request, CancellationToken ct)
        => ValueTask.FromResult(new ListToolsResult { Tools = ListTools(AllowedSet(), FullAccess()) });

    /// <summary>Tools visible under this request's allowlist, mapped to MCP <see cref="Tool"/>s.
    /// <paramref name="allowed"/>: explicit <c>?tools=</c> set; null means "no allowlist" — the
    /// standalone surface, where <paramref name="fullAccess"/> decides between read-only (default)
    /// and everything (<c>?mode=full</c>).</summary>
    internal List<Tool> ListTools(HashSet<string>? allowed, bool fullAccess = false) =>
        _toolRegistry.GetDefinitions()
            .Where(t => allowed is not null
                ? allowed.Contains(t.Name)
                : fullAccess || t.Kind != ToolKind.Mutate)
            .Select(t => new Tool
            {
                Name = t.Name,
                Description = BuildDescription(t),
                InputSchema = t.ParametersSchema.Clone(),
            })
            .ToList();

    public ValueTask<CallToolResult> CallToolAsync(RequestContext<CallToolRequestParams> request, CancellationToken ct)
    {
        var name = request.Params?.Name ?? string.Empty;
        JsonElement args = request.Params?.Arguments is { } a
            ? JsonSerializer.SerializeToElement(a)
            : JsonDocument.Parse("{}").RootElement.Clone();
        return CallToolAsync(name, args, AllowedSet(), AllowlistKey(), ct, Selection(), FullAccess());
    }

    /// <summary>Dispatch core, split from the MCP request shape for testability.</summary>
    internal async ValueTask<CallToolResult> CallToolAsync(
        string name,
        JsonElement args,
        HashSet<string>? allowed,
        string? allowlistKey,
        CancellationToken ct,
        IReadOnlyDictionary<string, string>? selection = null,
        bool fullAccess = false)
    {
        var tool = _toolRegistry.GetDefinitions()
            .FirstOrDefault(t => string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase));

        if (tool is null)
            return Error($"Tool '{name}' is not available in this context.");

        if (allowed is not null && !allowed.Contains(name))
        {
            // agent-correlation Modules 2/3: a tool that exists but was filtered out of this
            // turn's allowlist gets a distinguishable error — the agent can relay the scope hint
            // instead of guessing, and the tracker tick lets the turn result offer a
            // scope-widened retry.
            _outOfScopeCalls.RecordOutOfScopeCall(allowlistKey);
            return new CallToolResult
            {
                Content = [new TextContentBlock
                {
                    Text = JsonSerializer.Serialize(new
                    {
                        error = "tool_out_of_scope",
                        tool = name,
                        area = tool.FeatureArea.ToString(),
                        message = $"Tool '{name}' belongs to a different area than this turn's scope. " +
                            "Tell the user to enable \"Search across my whole workspace\" to reach it.",
                    }),
                }],
                IsError = true,
            };
        }

        // Standalone surface (no ?tools= allowlist): read tools only unless ?mode=full was
        // requested — a directly-attached MCP client must not be able to register pending
        // mutation proposals by default.
        if (allowed is null && !fullAccess && tool.Kind == ToolKind.Mutate)
        {
            return new CallToolResult
            {
                Content = [new TextContentBlock
                {
                    Text = JsonSerializer.Serialize(new
                    {
                        error = "tool_read_only",
                        tool = name,
                        message = $"Tool '{name}' proposes a mutation and is not exposed on the read-only endpoint. " +
                            "Connect with ?mode=full, or apply the change through the SwebKit UI.",
                    }),
                }],
                IsError = true,
            };
        }

        using var executionContext = AgentExecutionContext.Push(selection);
        var result = await _toolRegistry.ExecuteAsync(name, args, ct);
        return new CallToolResult
        {
            Content = [new TextContentBlock { Text = result }],
            IsError = IsErrorResult(result),
        };
    }

    /// <summary>Parses the <c>?tools=a,b,c</c> allowlist — null when absent (unfiltered).</summary>
    internal static HashSet<string>? ParseAllowedSet(string? raw) =>
        string.IsNullOrWhiteSpace(raw)
            ? null
            : new HashSet<string>(raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
                StringComparer.OrdinalIgnoreCase);

    internal static IReadOnlyDictionary<string, string>? ParseSelection(IEnumerable<string?> raw)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in raw)
        {
            var separator = item?.IndexOf('=') ?? -1;
            if (separator <= 0) continue;
            result[item![..separator]] = item[(separator + 1)..];
        }
        return result.Count == 0 ? null : result;
    }

    private static CallToolResult Error(string message) => new()
    {
        Content = [new TextContentBlock { Text = JsonSerializer.Serialize(new { error = message }) }],
        IsError = true,
    };

    private static bool IsErrorResult(string result)
    {
        try
        {
            using var doc = JsonDocument.Parse(result);
            return doc.RootElement.ValueKind == JsonValueKind.Object && doc.RootElement.TryGetProperty("error", out _);
        }
        catch (JsonException) { return false; }
    }

    private static string BuildDescription(ToolDefinition tool)
        => $"{tool.Description} [area: {tool.FeatureArea}; access: {(tool.Kind == ToolKind.Mutate ? "propose/confirm" : "read")}]";

    /// <summary>Wires the bridge into MCP server options (kept out of Program.cs for testability).</summary>
    public static void Configure(McpServerOptions options, SwebKitToolsMcpBridge bridge)
    {
        options.Handlers ??= new McpServerHandlers();
        options.Handlers.ListToolsHandler = bridge.ListToolsAsync;
        options.Handlers.CallToolHandler = bridge.CallToolAsync;
    }
}
