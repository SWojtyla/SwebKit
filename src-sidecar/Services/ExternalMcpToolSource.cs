using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using SwebKit.Agents;
using SwebKit.Agents.Tools;
using SwebKit.Core.Domain;

namespace SwebKit.Sidecar.Services;

/// <summary>
/// MCP <em>client</em> adapter — the counterpart to <see cref="Acp.SwebKitToolsMcpBridge"/> (which
/// serves our tools <em>to</em> agents). For non-ACP profiles (LM Studio, Mistral, other
/// OpenAI-compatible providers) this resolves the profile's <see cref="AgentProfile.ExtraMcpServers"/>
/// into <see cref="ToolDefinition"/>s the in-process tool loop can call: each remote tool is proxied
/// through an <see cref="McpClient"/> held for the lifetime of the server config, so stdio
/// subprocesses are spawned once, not per turn. ACP profiles never come through here — their
/// external servers attach directly in <c>session/new</c> (<see cref="Acp.AcpAgentHost"/>).
/// </summary>
/// <remarks>
/// Safety posture: <c>readOnlyHint</c> tools execute directly as <see cref="ToolKind.Read"/> (they
/// flow through the ask-mode filter and per-turn memoization for free). Everything else — absent
/// annotation included, since an absent hint is not a promise — becomes a <see cref="ToolKind.Mutate"/>
/// tool whose execution does NOT touch the remote server: it registers a
/// <see cref="PendingAgentAction"/> and returns the same <c>pending_confirmation</c> payload the
/// native <c>propose_*</c> tools produce. Confirm goes through the existing
/// <c>/api/agent/pending-approvals/{id}/confirm</c> endpoint, dispatched by
/// <see cref="AgentActionApplier"/> to <see cref="ExternalMcpActionExecutor"/>, which is the only
/// place a mutating external call actually fires. So the in-process path gets the same
/// confirm-before-execute gate ACP users get from <c>session/request_permission</c> — and
/// <c>ask</c> mode never sees these tools at all (Mutate is filtered out upstream).
/// </remarks>
public sealed class ExternalMcpToolSource : IAsyncDisposable
{
    /// <summary>Prefix for exposed names — keeps external tools out of the native namespace and
    /// makes provenance obvious in step traces. Sanitized to the OpenAI function-name charset.</summary>
    internal const string NamePrefix = "mcp";

    private const int MaxExposedNameLength = 64;

    private readonly ILogger<ExternalMcpToolSource> _logger;
    private readonly IAgentActionCoordinator _coordinator;
    private readonly Func<AgentMcpServer, CancellationToken, Task<IMcpServerConnection>> _connect;
    private readonly ConcurrentDictionary<string, Lazy<Task<IMcpServerConnection?>>> _connections = new();

    public ExternalMcpToolSource(ILogger<ExternalMcpToolSource> logger, IAgentActionCoordinator coordinator)
        : this(logger, coordinator, null)
    {
    }

    /// <summary>Test seam — <paramref name="connect"/> replaces real transport construction.</summary>
    internal ExternalMcpToolSource(
        ILogger<ExternalMcpToolSource> logger,
        IAgentActionCoordinator coordinator,
        Func<AgentMcpServer, CancellationToken, Task<IMcpServerConnection>>? connect)
    {
        _logger = logger;
        _coordinator = coordinator;
        _connect = connect ?? CreateConnectionAsync;
    }

    /// <summary>External tools for this profile's enabled servers. Resolves (and caches) a
    /// connection per enabled entry, lists its tools, and maps each: <c>readOnlyHint</c> tools
    /// become direct <see cref="ToolKind.Read"/> calls; everything else becomes a
    /// <see cref="ToolKind.Mutate"/> whose "execution" is a pending-action proposal — the remote
    /// call only fires after the user confirms it in the UI. Unreachable/misconfigured servers are
    /// skipped with a warning — a dead server must never take the whole chat turn down.</summary>
    public async Task<IReadOnlyList<ExternalMcpToolBinding>> GetToolsAsync(
        AgentProfile profile, CancellationToken ct)
    {
        var servers = profile.ExtraMcpServers.Where(s => s.Enabled).ToList();
        if (servers.Count == 0)
            return [];

        var bindings = new List<ExternalMcpToolBinding>();
        var usedNames = new HashSet<string>(StringComparer.Ordinal);

        foreach (var server in servers)
        {
            var serverConfigJson = JsonSerializer.Serialize(server);
            var connection = await ConnectionForAsync(server, serverConfigJson, ct);
            if (connection is null)
                continue;

            IReadOnlyList<RemoteMcpTool> remoteTools;
            try
            {
                remoteTools = await connection.ListToolsAsync(ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "External MCP server '{Name}' failed to list tools — skipped", server.Name);
                continue;
            }

            foreach (var remote in remoteTools)
            {
                var exposedName = ExposedName(server.Name, remote.Name, usedNames);
                var risk = remote.Destructive ? ToolRisk.High : ToolRisk.Low;

                if (remote.ReadOnly)
                {
                    bindings.Add(new ExternalMcpToolBinding(
                        Definition: new ToolDefinition
                        {
                            Name = exposedName,
                            Description = $"[external:{server.Name}] {remote.Description ?? remote.Name}",
                            ParametersSchema = remote.InputSchema,
                            Kind = ToolKind.Read,
                            FeatureArea = FeatureArea.External,
                        },
                        Execute: (args, toolCt) => CallRemoteAsync(connection, server.Name, remote.Name, args, toolCt)));
                }
                else
                {
                    // Not annotated read-only — could mutate. Expose as a proposal tool: "calling"
                    // it registers a pending action; the real remote call happens in
                    // ExecuteConfirmedAsync after the user confirms in the UI.
                    bindings.Add(new ExternalMcpToolBinding(
                        Definition: new ToolDefinition
                        {
                            Name = exposedName,
                            Description = $"[external:{server.Name}] {remote.Description ?? remote.Name} " +
                                "Calling this tool proposes the action — the user must confirm before the external server is invoked.",
                            ParametersSchema = remote.InputSchema,
                            Kind = ToolKind.Mutate,
                            Risk = risk,
                            FeatureArea = FeatureArea.External,
                        },
                        Execute: (args, _) => Task.FromResult(
                            ProposeRemoteAsync(server, serverConfigJson, remote, args, risk))));
                }
            }
        }

        return bindings;
    }

    /// <summary>Registers the pending action for a mutating external call — mirrors the
    /// <c>propose_*</c> payload shape so the model reads it the same way, and so the confirm card
    /// carries everything <see cref="ExecuteConfirmedAsync"/> needs to re-dial and fire.</summary>
    private string ProposeRemoteAsync(
        AgentMcpServer server, string serverConfigJson, RemoteMcpTool remote, JsonElement args, ToolRisk risk)
    {
        var actionId = Guid.NewGuid().ToString("N");
        var argsJson = args.ValueKind == JsonValueKind.Object ? args.GetRawText() : "{}";
        var action = new PendingAgentAction
        {
            Id = actionId,
            Type = AgentActionType.ExternalMcpCall,
            Summary = $"Run '{remote.Name}' on external MCP server '{server.Name}'",
            Target = $"{server.Name}/{remote.Name}",
            Risk = risk == ToolRisk.High ? AgentActionRisk.High : AgentActionRisk.Low,
            Preview = argsJson.Length > 4000 ? argsJson[..4000] + "\n… (truncated)" : argsJson,
            ExpectedFingerprint = null,
            Payload = JsonSerializer.SerializeToElement(new
            {
                serverConfigJson,
                tool = remote.Name,
                args = argsJson,
            }),
        };
        _coordinator.RegisterAction(action);
        return JsonSerializer.Serialize(new
        {
            action_id = actionId,
            status = "pending_confirmation",
            summary = action.Summary,
            preview = action.Preview,
            risk = action.Risk.ToString(),
            expires_at = action.ExpiresAt.ToString("yyyy-MM-dd HH:mm UTC"),
            message = "External tool call proposed. User must confirm before the remote server is invoked.",
        });
    }

    /// <summary>The confirm-side half of a mutating external call — invoked by
    /// <see cref="ExternalMcpActionExecutor"/> with the payload stashed at proposal time. Reuses
    /// the connection cache: editing the server config between proposal and confirm produces a
    /// fresh connection (the serialized config is the key), so confirmed calls always go to the
    /// server as configured NOW.</summary>
    internal async Task<string> ExecuteConfirmedAsync(
        string serverConfigJson, string remoteName, JsonElement args, CancellationToken ct)
    {
        var server = JsonSerializer.Deserialize<AgentMcpServer>(serverConfigJson)
            ?? throw new InvalidOperationException("Stored MCP server config is unreadable.");

        var connection = await ConnectionForAsync(server, serverConfigJson, ct)
            ?? throw new InvalidOperationException(
                $"External MCP server '{server.Name}' is not reachable — cannot execute the confirmed call.");

        return await CallRemoteAsync(connection, server.Name, remoteName, args, ct);
    }

    /// <summary>Connection cache keyed by the serialized server config — editing an entry produces
    /// a new key, so reconfiguration naturally spawns a fresh connection and the stale one is
    /// evicted. Failures are removed so the next turn retries rather than caching a dead server.</summary>
    private async Task<IMcpServerConnection?> ConnectionForAsync(
        AgentMcpServer server, string serverConfigJson, CancellationToken ct)
    {
        var key = serverConfigJson;
        var lazy = _connections.GetOrAdd(key, _ => new Lazy<Task<IMcpServerConnection?>>(
            () => ConnectOrNullAsync(server), LazyThreadSafetyMode.ExecutionAndPublication));

        var connection = await lazy.Value;
        if (connection is null)
            _connections.TryRemove(key, out _); // dead server — next turn retries instead of caching failure

        return connection;
    }

    private async Task<IMcpServerConnection?> ConnectOrNullAsync(AgentMcpServer server)
    {
        try
        {
            return await _connect(server, CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "External MCP server '{Name}' ({Transport}) failed to connect — skipped",
                server.Name, server.Transport);
            return null;
        }
    }

    private async Task<IMcpServerConnection> CreateConnectionAsync(AgentMcpServer server, CancellationToken ct)
    {
        IClientTransport transport = server.Transport switch
        {
            "stdio" => new StdioClientTransport(new StdioClientTransportOptions
            {
                Name = server.Name,
                Command = server.Command
                    ?? throw new InvalidOperationException($"stdio MCP server '{server.Name}' has no command"),
                Arguments = Acp.AcpProcessLauncher.SplitArguments(server.Arguments),
                EnvironmentVariables = server.EnvironmentVariables
                    .ToDictionary(kv => kv.Key, kv => (string?)kv.Value),
            }),
            "http" => new HttpClientTransport(new HttpClientTransportOptions
            {
                Name = server.Name,
                Endpoint = new Uri(server.Url
                    ?? throw new InvalidOperationException($"http MCP server '{server.Name}' has no URL")),
                AdditionalHeaders = server.Headers,
            }),
            _ => throw new InvalidOperationException(
                $"External MCP server '{server.Name}' has unknown transport '{server.Transport}'"),
        };

        var client = await McpClient.CreateAsync(transport, clientOptions: null, loggerFactory: null, ct);
        return new McpClientConnection(client);
    }

    /// <summary>Exposed name: <c>mcp_{server}_{tool}</c> in the OpenAI charset, ≤64 chars, deduplicated
    /// with a numeric suffix when sanitization collides.</summary>
    internal static string ExposedName(string serverName, string toolName, HashSet<string> usedNames)
    {
        var candidate = $"{NamePrefix}_{Slug(serverName)}_{Slug(toolName)}";
        if (candidate.Length > MaxExposedNameLength)
            candidate = candidate[..MaxExposedNameLength].TrimEnd('_');

        var name = candidate;
        for (var i = 2; !usedNames.Add(name); i++)
        {
            var suffix = $"_{i}";
            name = candidate.Length + suffix.Length > MaxExposedNameLength
                ? candidate[..(MaxExposedNameLength - suffix.Length)] + suffix
                : candidate + suffix;
        }

        return name;
    }

    /// <summary>Lowercase, non-alnum runs → <c>_</c>, edges trimmed — a superset-legal slug for the
    /// <c>^[a-zA-Z0-9_-]+$</c> function-name charset (digits/underscore preserved).</summary>
    internal static string Slug(string value)
    {
        var sb = new StringBuilder(value.Length);
        var lastWasUnderscore = true; // strips a leading separator
        foreach (var c in value.ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(c))
            {
                sb.Append(c);
                lastWasUnderscore = false;
            }
            else if (!lastWasUnderscore)
            {
                sb.Append('_');
                lastWasUnderscore = true;
            }
        }

        return sb.ToString().TrimEnd('_') is { Length: > 0 } slug ? slug : "x";
    }

    /// <summary>Result mapping: <c>structuredContent</c> wins when present, else text blocks are
    /// concatenated. Errors are wrapped in the <c>{"error": …}</c> convention so
    /// <see cref="AgentToolCallOrchestrator.IsErrorResult"/> and the memoizer treat them like any
    /// failed tool call (never cached).</summary>
    private static async Task<string> CallRemoteAsync(
        IMcpServerConnection connection, string serverName, string remoteName,
        JsonElement args, CancellationToken ct)
    {
        IReadOnlyDictionary<string, object?> arguments = args.ValueKind == JsonValueKind.Object
            ? JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(args.GetRawText())
                ?.ToDictionary(kv => kv.Key, kv => (object?)kv.Value) ?? new Dictionary<string, object?>()
            : new Dictionary<string, object?>();

        CallToolResult result;
        try
        {
            result = await connection.CallToolAsync(remoteName, arguments, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return JsonSerializer.Serialize(new
            {
                error = $"External tool '{remoteName}' on '{serverName}' failed: {ex.Message}",
            });
        }

        if (result.StructuredContent is { ValueKind: not JsonValueKind.Undefined and not JsonValueKind.Null } structured)
            return WrapIfError(result, structured.GetRawText());

        var text = string.Concat(result.Content.OfType<TextContentBlock>().Select(b => b.Text));
        if (text.Length == 0)
            text = result.Content.Count == 0
                ? "{}"
                : JsonSerializer.Serialize(result.Content); // non-text blocks (images, resources) — serialize the envelope

        return WrapIfError(result, text);
    }

    private static string WrapIfError(CallToolResult result, string payload) =>
        result.IsError == true ? JsonSerializer.Serialize(new { error = payload }) : payload;

    public async ValueTask DisposeAsync()
    {
        foreach (var lazy in _connections.Values)
        {
            if (lazy is { IsValueCreated: true, Value.IsCompletedSuccessfully: true }
                && lazy.Value.Result is { } connection)
                await connection.DisposeAsync();
        }

        _connections.Clear();
    }
}

/// <summary>One proxied external tool: the <see cref="ToolDefinition"/> the model sees plus the
/// delegate that executes the remote call.</summary>
public sealed record ExternalMcpToolBinding(
    ToolDefinition Definition,
    Func<JsonElement, CancellationToken, Task<string>> Execute);

/// <summary>A remote tool as the adapter consumes it — decoupled from SDK types for testability.</summary>
internal sealed record RemoteMcpTool(
    string Name, string? Description, JsonElement InputSchema, bool ReadOnly, bool Destructive);

/// <summary>Live connection to one external MCP server. Production implementation wraps
/// <see cref="McpClient"/>; tests substitute fakes via the internal ctor.</summary>
internal interface IMcpServerConnection : IAsyncDisposable
{
    Task<IReadOnlyList<RemoteMcpTool>> ListToolsAsync(CancellationToken ct);
    Task<CallToolResult> CallToolAsync(
        string remoteName, IReadOnlyDictionary<string, object?> arguments, CancellationToken ct);
}

/// <summary><see cref="McpClient"/>-backed connection — owns the client (and for stdio, the
/// spawned subprocess) for the lifetime of the server config.</summary>
internal sealed class McpClientConnection(McpClient client) : IMcpServerConnection
{
    public async Task<IReadOnlyList<RemoteMcpTool>> ListToolsAsync(CancellationToken ct)
    {
        var tools = await client.ListToolsAsync(cancellationToken: ct);
        return tools
            .Select(t => new RemoteMcpTool(
                t.Name,
                t.ProtocolTool.Description,
                t.ProtocolTool.InputSchema,
                t.ProtocolTool.Annotations?.ReadOnlyHint == true,
                t.ProtocolTool.Annotations?.DestructiveHint == true))
            .ToList();
    }

    public Task<CallToolResult> CallToolAsync(
        string remoteName, IReadOnlyDictionary<string, object?> arguments, CancellationToken ct) =>
        client.CallToolAsync(remoteName, arguments, cancellationToken: ct).AsTask();

    public ValueTask DisposeAsync() => client.DisposeAsync();
}
