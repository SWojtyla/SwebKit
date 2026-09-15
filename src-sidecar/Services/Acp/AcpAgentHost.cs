using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading.Channels;
using SwebKit.Agents;
using SwebKit.Core.Abstractions;
using SwebKit.Core.Configuration;
using SwebKit.Core.Domain;

namespace SwebKit.Sidecar.Services.Acp;

public sealed record AcpAuthMethod(string Id, string Name, string? Description);

/// <summary>What an ACP agent reported in its <c>initialize</c> response.</summary>
public sealed class AcpAgentCapabilities
{
    public int ProtocolVersion { get; set; }
    public string? AgentName { get; set; }
    public string? AgentTitle { get; set; }
    public string? AgentVersion { get; set; }
    public bool LoadSession { get; set; }
    public bool McpHttp { get; set; }
    public bool McpSse { get; set; }
    public IReadOnlyList<AcpAuthMethod> AuthMethods { get; set; } = [];
}

/// <summary>One item surfaced while a prompt turn is in flight: either a <c>session/update</c>
/// payload, a parked permission request, or the terminal stop-reason marker.</summary>
public sealed class AcpSessionEvent
{
    public JsonElement Update { get; private init; }
    public AcpPendingPermission? Permission { get; private init; }
    public string? StopReason { get; private init; }

    private AcpSessionEvent() { }

    public static AcpSessionEvent FromUpdate(JsonElement update) => new() { Update = update };
    public static AcpSessionEvent FromPermission(AcpPendingPermission p) => new() { Permission = p };
    public static AcpSessionEvent Terminal(string stopReason) => new() { StopReason = stopReason };
}

/// <summary>
/// Owns the ACP agent child process and every ACP session multiplexed over it (one process per
/// active ACP profile; one <c>session/new</c> per SwebKit chat session). The process is
/// respawned lazily when the profile's spawn configuration changes or it dies, and killed when
/// the sidecar shuts down. Inbound <c>session/update</c> notifications are routed to the
/// per-session event channel that <see cref="PromptAsync"/> streams out.
/// </summary>
public sealed class AcpAgentHost : IAsyncDisposable
{
    private const int ProtocolVersion = 1;

    private readonly UserSettingsRepository _settings;
    private readonly ICredentialStore _credentials;
    private readonly AcpPermissionStore _permissions;
    private readonly ILogger<AcpAgentHost> _logger;
    private readonly SemaphoreSlim _lifecycleLock = new(1, 1);

    private Process? _process;
    private AcpJsonRpcPeer? _peer;
    private string? _fingerprint;
    private AcpAgentCapabilities? _capabilities;

    private readonly ConcurrentDictionary<string, SessionEntry> _sessionsByKey = new();
    private readonly ConcurrentDictionary<string, SessionEntry> _sessionsByAcpId = new();
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _promptLocks = new();

    private sealed record SessionEntry(
        string Key,
        string AcpSessionId,
        string Spec,
        Channel<AcpSessionEvent> Events);

    public AcpAgentHost(
        UserSettingsRepository settings,
        ICredentialStore credentials,
        AcpPermissionStore permissions,
        ILogger<AcpAgentHost> logger)
    {
        _settings = settings;
        _credentials = credentials;
        _permissions = permissions;
        _logger = logger;
    }

    public AcpAgentCapabilities? CurrentCapabilities => _capabilities;

    /// <summary>Spawns the agent process (or reuses the live one) and performs the ACP
    /// <c>initialize</c> handshake. Respawns when the profile's spawn-relevant fields changed or
    /// the process died.</summary>
    public async Task<AcpAgentCapabilities> EnsureStartedAsync(AgentProfile profile, CancellationToken ct)
    {
        var fingerprint = Fingerprint(profile);
        if (_peer is not null && _process is { HasExited: false } && _fingerprint == fingerprint)
            return _capabilities!;

        await _lifecycleLock.WaitAsync(ct);
        try
        {
            if (_peer is not null && _process is { HasExited: false } && _fingerprint == fingerprint)
                return _capabilities!;

            await KillLockedAsync();

            var secret = ResolveSecret(profile);
            _process = AcpProcessLauncher.Start(profile, secret, _logger);
            _peer = new AcpJsonRpcPeer(
                _process.StandardOutput.BaseStream,
                _process.StandardInput.BaseStream)
            {
                OnNotification = HandleNotificationAsync,
                OnRequest = HandleInboundRequestAsync,
            };

            var init = await _peer.SendRequestAsync("initialize", new
            {
                protocolVersion = ProtocolVersion,
                // Deliberately empty: fs/*, terminal/* and elicitation are all off, so the agent
                // may only chat and use the MCP tools it's handed in session/new.
                clientCapabilities = new { },
                clientInfo = new { name = "swebkit", title = "SwebKit", version = "0.2.0" },
            }, ct);

            _capabilities = ParseCapabilities(init);
            _fingerprint = fingerprint;
            _logger.LogInformation(
                "ACP agent started: {Agent} v{Version} (protocol v{Protocol}, mcp http: {McpHttp})",
                _capabilities.AgentTitle ?? _capabilities.AgentName ?? profile.Command,
                _capabilities.AgentVersion ?? "?",
                _capabilities.ProtocolVersion,
                _capabilities.McpHttp);
            return _capabilities;
        }
        finally
        {
            _lifecycleLock.Release();
        }
    }

    /// <summary>Returns the ACP session id for <paramref name="sessionKey"/>, creating it via
    /// <c>session/new</c> on first use. If the tool-visibility spec (encoded in
    /// <paramref name="mcpUrl"/>) changed since this key's session was created, the old ACP
    /// session is dropped and a fresh one is created — MCP servers are fixed at session creation,
    /// so there is no cheaper way to change what the agent can see.</summary>
    public async Task<string> EnsureSessionAsync(
        AgentProfile profile, string sessionKey, string? mcpUrl, CancellationToken ct)
    {
        var caps = await EnsureStartedAsync(profile, ct);
        var spec = mcpUrl ?? string.Empty;

        if (_sessionsByKey.TryGetValue(sessionKey, out var existing) &&
            existing.Spec == spec &&
            _sessionsByAcpId.ContainsKey(existing.AcpSessionId))
        {
            return existing.AcpSessionId;
        }

        var cwd = !string.IsNullOrWhiteSpace(profile.WorkingDirectory)
            ? profile.WorkingDirectory
            : Directory.GetCurrentDirectory();

        // headers must be present even when empty: the ACP schema marks it required for http/sse
        // servers, and adapters (verified against claude-agent-acp) silently drop entries missing
        // it — the session then comes up with no SwebKit tools at all.
        object[] mcpServers = caps.McpHttp && mcpUrl is not null
            ? [new { type = "http", name = "swebkit", url = mcpUrl, headers = Array.Empty<object>() }]
            : [];
        if (mcpUrl is not null && !caps.McpHttp)
            _logger.LogWarning(
                "ACP agent does not advertise http MCP capability — SwebKit tools unavailable this session.");

        JsonElement result;
        try
        {
            result = await _peer!.SendRequestAsync("session/new", new { cwd, mcpServers }, ct);
        }
        catch (AcpRpcException ex) when (ex.Code == AcpRpcException.AuthRequired)
        {
            throw new InvalidOperationException(AuthRequiredMessage(caps));
        }

        var acpSessionId = result.GetProperty("sessionId").GetString()!;

        if (existing is not null)
        {
            _sessionsByKey.TryRemove(sessionKey, out _);
            _sessionsByAcpId.TryRemove(existing.AcpSessionId, out _);
            existing.Events.Writer.TryComplete();
        }

        var entry = new SessionEntry(
            sessionKey, acpSessionId, spec, Channel.CreateUnbounded<AcpSessionEvent>());
        _sessionsByKey[sessionKey] = entry;
        _sessionsByAcpId[acpSessionId] = entry;
        return acpSessionId;
    }

    /// <summary>Sends <c>session/prompt</c> and streams everything the agent reports until the
    /// turn ends: <c>session/update</c> payloads first, then one terminal
    /// <see cref="AcpSessionEvent.StopReason"/> item. Cancelling <paramref name="ct"/> sends
    /// <c>session/cancel</c> rather than abandoning the JSON-RPC request (which the agent would
    /// otherwise keep working on invisibly). Prompts on one session are serialized.</summary>
    public async IAsyncEnumerable<AcpSessionEvent> PromptAsync(
        string acpSessionId,
        string promptText,
        [EnumeratorCancellation] CancellationToken ct)
    {
        if (!_sessionsByAcpId.TryGetValue(acpSessionId, out var session))
            throw new InvalidOperationException("ACP session no longer exists.");

        var promptLock = _promptLocks.GetOrAdd(acpSessionId, _ => new SemaphoreSlim(1, 1));
        await promptLock.WaitAsync(ct);

        Task<JsonElement> promptTask;
        try
        {
            promptTask = _peer!.SendRequestAsync("session/prompt", new
            {
                sessionId = acpSessionId,
                prompt = new object[] { new { type = "text", text = promptText } },
            }, CancellationToken.None);
        }
        catch
        {
            promptLock.Release();
            throw;
        }

        using var cancelRegistration = ct.Register(() => _ = SafeCancelAsync(acpSessionId));

        try
        {
            while (true)
            {
                var readTask = session.Events.Reader.ReadAsync(ct).AsTask();
                var completed = await Task.WhenAny(readTask, promptTask);
                if (completed == promptTask)
                {
                    // Drain updates the agent emitted just before the response so ordering holds.
                    while (session.Events.Reader.TryRead(out var leftover))
                        yield return leftover;

                    var result = await promptTask;
                    var stopReason = result.ValueKind == JsonValueKind.Object &&
                                     result.TryGetProperty("stopReason", out var sr) &&
                                     sr.ValueKind == JsonValueKind.String
                        ? sr.GetString()!
                        : "end_turn";
                    yield return AcpSessionEvent.Terminal(stopReason);
                    yield break;
                }

                yield return await readTask;
            }
        }
        finally
        {
            promptLock.Release();
        }
    }

    /// <summary>Drops a SwebKit session's ACP session mapping (chat "Clear" path). Sends
    /// <c>session/close</c> when the agent advertises the capability; the local state is dropped
    /// either way.</summary>
    public async Task DropSessionAsync(string sessionKey, CancellationToken ct = default)
    {
        if (!_sessionsByKey.TryRemove(sessionKey, out var entry))
            return;

        _sessionsByAcpId.TryRemove(entry.AcpSessionId, out _);
        entry.Events.Writer.TryComplete();

        if (_peer is not null && _capabilities is not null)
        {
            try
            {
                await _peer.SendRequestAsync("session/close", new { sessionId = entry.AcpSessionId }, ct);
            }
            catch (AcpRpcException)
            {
                // Agent doesn't support session/close — dropping the mapping is enough.
            }
        }
    }

    /// <summary>One-shot probe for the settings "Test connection" path: spawns a throwaway
    /// process, performs <c>initialize</c>, reports what the agent advertised, and kills it.
    /// Never touches the shared <see cref="_process"/> used for chat.</summary>
    public async Task<CapabilityTestResult> ProbeAsync(AgentProfile profile, CancellationToken ct)
    {
        Process? process = null;
        try
        {
            var secret = ResolveSecret(profile);
            process = AcpProcessLauncher.Start(profile, secret, _logger);
            await using var peer = new AcpJsonRpcPeer(
                process.StandardOutput.BaseStream,
                process.StandardInput.BaseStream);

            var init = await peer.SendRequestAsync("initialize", new
            {
                protocolVersion = ProtocolVersion,
                clientCapabilities = new { },
                clientInfo = new { name = "swebkit", title = "SwebKit", version = "0.2.0" },
            }, ct);

            var caps = ParseCapabilities(init);
            var agentLabel = caps.AgentTitle ?? caps.AgentName ?? "ACP agent";
            var authNote = caps.AuthMethods.Count > 0
                ? $"; sign-in methods: {string.Join(", ", caps.AuthMethods.Select(m => m.Name))}"
                : string.Empty;

            return new CapabilityTestResult
            {
                ServerReachable = true,
                ModelAvailable = true,
                ChatValid = true,
                ToolCallingValid = true,
                Capability = AgentCapability.ToolCalling,
                Diagnostic = $"{agentLabel} v{caps.AgentVersion ?? "?"}, protocol v{caps.ProtocolVersion}, " +
                    $"MCP over HTTP: {(caps.McpHttp ? "yes" : "no")}{authNote}.",
            };
        }
        catch (FileNotFoundException ex)
        {
            return new CapabilityTestResult { Diagnostic = ex.Message };
        }
        catch (AcpRpcException ex)
        {
            return new CapabilityTestResult
            {
                ServerReachable = true,
                Diagnostic = $"Agent rejected initialize: {ex.Message}",
            };
        }
        catch (Exception ex)
        {
            return new CapabilityTestResult { Diagnostic = $"ACP probe failed: {ex.Message}" };
        }
        finally
        {
            try { process?.Kill(entireProcessTree: true); } catch { }
        }
    }

    // ── Inbound message handling ──

    private Task HandleNotificationAsync(string method, JsonElement prms)
    {
        if (method == "session/update" &&
            prms.TryGetProperty("sessionId", out var sidEl) &&
            sidEl.ValueKind == JsonValueKind.String &&
            _sessionsByAcpId.TryGetValue(sidEl.GetString()!, out var session))
        {
            var update = prms.TryGetProperty("update", out var u) ? u.Clone() : default;
            session.Events.Writer.TryWrite(AcpSessionEvent.FromUpdate(update));
        }
        return Task.CompletedTask;
    }

    private async Task<object?> HandleInboundRequestAsync(string method, JsonElement prms, CancellationToken ct)
    {
        if (method != "session/request_permission")
        {
            throw new AcpRpcException(
                AcpRpcException.MethodNotFound,
                $"Unsupported client method '{method}' — fs/terminal capabilities are disabled in this profile.");
        }

        var sessionId = prms.TryGetProperty("sessionId", out var sid) && sid.ValueKind == JsonValueKind.String
            ? sid.GetString()!
            : string.Empty;
        var title = prms.TryGetProperty("toolCall", out var toolCall) &&
                    toolCall.TryGetProperty("title", out var t) && t.ValueKind == JsonValueKind.String
            ? t.GetString()!
            : "tool call";
        var options = ParsePermissionOptions(prms);

        var profile = _settings.Settings.Agent.GetActiveProfile();
        if (profile?.RequireToolApproval != true)
        {
            var autoOptionId = PickAutoApproveOption(options);
            return autoOptionId is null
                ? CancelledOutcome()
                : SelectedOutcome(autoOptionId);
        }

        var entry = _permissions.Create(sessionId, title, options);
        if (_sessionsByAcpId.TryGetValue(sessionId, out var session))
            session.Events.Writer.TryWrite(AcpSessionEvent.FromPermission(entry));

        var chosen = await entry.Outcome.Task;
        return chosen is null ? CancelledOutcome() : SelectedOutcome(chosen);
    }

    private static object SelectedOutcome(string optionId) =>
        new { outcome = new { outcome = "selected", optionId } };

    private static object CancelledOutcome() =>
        new { outcome = new { outcome = "cancelled" } };

    /// <summary>Picks the least-permissive approval: <c>allow_once</c> if offered, else the first
    /// <c>allow_*</c> option, else the first option. Returns null when the agent offered no
    /// options (degenerate but possible).</summary>
    internal static string? PickAutoApproveOption(IReadOnlyList<AcpPermissionOption> options)
    {
        if (options.Count == 0)
            return null;

        return options.FirstOrDefault(o => o.Kind == "allow_once")?.OptionId
            ?? options.FirstOrDefault(o => o.Kind?.StartsWith("allow", StringComparison.Ordinal) == true)?.OptionId
            ?? options[0].OptionId;
    }

    private static List<AcpPermissionOption> ParsePermissionOptions(JsonElement prms)
    {
        var options = new List<AcpPermissionOption>();
        if (!prms.TryGetProperty("options", out var arr) || arr.ValueKind != JsonValueKind.Array)
            return options;

        foreach (var o in arr.EnumerateArray())
        {
            var optionId = o.TryGetProperty("optionId", out var oid) && oid.ValueKind == JsonValueKind.String
                ? oid.GetString()!
                : null;
            var name = o.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String
                ? n.GetString()!
                : optionId;
            var kind = o.TryGetProperty("kind", out var k) && k.ValueKind == JsonValueKind.String
                ? k.GetString()
                : null;
            if (optionId is not null && name is not null)
                options.Add(new AcpPermissionOption(optionId, name, kind));
        }
        return options;
    }

    // ── Helpers ──

    private async Task SafeCancelAsync(string acpSessionId)
    {
        try
        {
            if (_peer is not null)
                await _peer.SendNotificationAsync("session/cancel", new { sessionId = acpSessionId }, CancellationToken.None);
        }
        catch
        {
            // best-effort — a dead agent doesn't need its cancel delivered
        }
    }

    private string ResolveSecret(AgentProfile profile) =>
        !string.IsNullOrEmpty(profile.CredentialKey)
            ? _credentials.Get(profile.CredentialKey) ?? string.Empty
            : string.Empty;

    private static string Fingerprint(AgentProfile p) =>
        string.Join('\n',
            new[] { p.Command, p.Arguments, p.WorkingDirectory, p.CredentialEnvVar }
                .Concat(p.EnvironmentVariables.OrderBy(kv => kv.Key).Select(kv => $"{kv.Key}={kv.Value}")));

    private static string AuthRequiredMessage(AcpAgentCapabilities caps) =>
        "The ACP agent requires authentication" +
        (caps.AuthMethods.Count > 0
            ? $" ({string.Join(", ", caps.AuthMethods.Select(m => m.Name))})"
            : string.Empty) +
        ". Sign in through the agent's own CLI first and retry.";

    private static AcpAgentCapabilities ParseCapabilities(JsonElement init)
    {
        var caps = new AcpAgentCapabilities
        {
            ProtocolVersion = init.TryGetProperty("protocolVersion", out var pv) && pv.ValueKind == JsonValueKind.Number
                ? pv.GetInt32()
                : ProtocolVersion,
        };

        if (init.TryGetProperty("agentInfo", out var info) && info.ValueKind == JsonValueKind.Object)
        {
            caps.AgentName = info.TryGetProperty("name", out var n) ? n.GetString() : null;
            caps.AgentTitle = info.TryGetProperty("title", out var t) ? t.GetString() : null;
            caps.AgentVersion = info.TryGetProperty("version", out var v) ? v.GetString() : null;
        }

        if (init.TryGetProperty("agentCapabilities", out var ac) && ac.ValueKind == JsonValueKind.Object)
        {
            caps.LoadSession = ac.TryGetProperty("loadSession", out var ls) && ls.ValueKind == JsonValueKind.True;
            if (ac.TryGetProperty("mcpCapabilities", out var mcp) && mcp.ValueKind == JsonValueKind.Object)
            {
                caps.McpHttp = mcp.TryGetProperty("http", out var h) && h.ValueKind == JsonValueKind.True;
                caps.McpSse = mcp.TryGetProperty("sse", out var s) && s.ValueKind == JsonValueKind.True;
            }
        }

        if (init.TryGetProperty("authMethods", out var am) && am.ValueKind == JsonValueKind.Array)
        {
            caps.AuthMethods = am.EnumerateArray()
                .Select(m => new AcpAuthMethod(
                    m.TryGetProperty("id", out var id) ? id.GetString() ?? string.Empty : string.Empty,
                    m.TryGetProperty("name", out var n) ? n.GetString() ?? string.Empty : string.Empty,
                    m.TryGetProperty("description", out var d) ? d.GetString() : null))
                .ToList();
        }

        return caps;
    }

    private async Task KillLockedAsync()
    {
        foreach (var entry in _sessionsByAcpId.Values)
            entry.Events.Writer.TryComplete();
        _sessionsByKey.Clear();
        _sessionsByAcpId.Clear();

        if (_peer is not null)
            await _peer.DisposeAsync();
        _peer = null;

        if (_process is not null)
        {
            try { _process.Kill(entireProcessTree: true); } catch { }
            _process.Dispose();
            _process = null;
        }

        _capabilities = null;
        _fingerprint = null;
    }

    public async ValueTask DisposeAsync()
    {
        await _lifecycleLock.WaitAsync();
        try
        {
            await KillLockedAsync();
        }
        finally
        {
            _lifecycleLock.Release();
        }
    }
}
