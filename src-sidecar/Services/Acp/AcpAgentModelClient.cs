using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using SwebKit.Agents;
using SwebKit.Core.Configuration;
using SwebKit.Core.Domain;

namespace SwebKit.Sidecar.Services.Acp;

/// <summary>
/// <see cref="IAgentModelClient"/> implementation that delegates each turn to an external ACP
/// agent process via <see cref="AcpAgentHost"/>. Unlike <c>OpenAiCompatibleAgentClient</c>, the
/// tool loop runs inside the agent: <paramref name="toolExecutor"/> and <see cref="AgentModelRequest.History"/>
/// are ignored (SwebKit tools reach the agent through the MCP bridge in <c>session/new</c>, and
/// the agent owns its transcript), while <see cref="AgentModelRequest.Tools"/> still drives which
/// SwebKit tools the session's MCP endpoint exposes.
/// </summary>
public sealed class AcpAgentModelClient : IAgentModelClient
{
    /// <summary>Same cap <c>OpenAiCompatibleAgentClient</c> applies to tool results — applied
    /// MCP-side too so an oversized result can't blow the agent's context in one call.</summary>
    internal const int MaxToolResultChars = 8_000;

    private readonly UserSettingsRepository _settings;
    private readonly AcpAgentHost _host;
    private readonly IServer _server;
    private readonly OutOfScopeCallTracker _outOfScopeCalls;

    /// <summary>Last system prompt actually sent per ACP session id — the agent owns its
    /// transcript, so context is injected by prompt-stuffing: on a new session, and again
    /// whenever the rebuilt prompt changed (e.g. the "current focus" selection moved).</summary>
    private readonly ConcurrentDictionary<string, string> _lastSentSystemPrompt = new();

    public AcpAgentModelClient(
        UserSettingsRepository settings,
        AcpAgentHost host,
        IServer server,
        OutOfScopeCallTracker outOfScopeCalls)
    {
        _settings = settings;
        _host = host;
        _server = server;
        _outOfScopeCalls = outOfScopeCalls;
    }

    public async Task<AgentChatResult> ChatAsync(
        AgentModelRequest request,
        Func<string, JsonElement, CancellationToken, Task<string>>? toolExecutor,
        CancellationToken ct)
    {
        AgentChatResult? result = null;
        string? error = null;
        await foreach (var evt in ChatStreamAsync(request, toolExecutor, ct))
        {
            if (evt.Kind == AgentStreamEventKind.Done)
                result = evt.Result;
            else if (evt.Kind == AgentStreamEventKind.Error)
                error = evt.ErrorMessage;
        }

        if (result is not null)
            return result;

        throw new InvalidOperationException(error ?? "ACP agent ended the turn without a result.");
    }

    /// <summary>Runs a throwaway ACP session for a single completion — used by
    /// <c>ProactiveInsightService</c>'s background summaries and nothing else (rolling
    /// summarization is skipped for ACP profiles; the agent manages its own context).</summary>
    public async Task<AgentModelResponse> CompleteAsync(AgentModelRequest request, CancellationToken ct)
    {
        var oneshot = new AgentModelRequest
        {
            SystemPrompt = request.SystemPrompt,
            UserMessage = request.UserMessage,
            Tools = [],
            SessionKey = $"__oneshot_{Guid.NewGuid():N}",
        };

        var result = await ChatAsync(oneshot, null, ct);
        return new AgentModelResponse
        {
            FinishReason = AgentFinishReason.Stop,
            Content = result.Text,
            AssistantMessage = new AgentMessage { Role = "assistant", Content = result.Text },
        };
    }

    public async IAsyncEnumerable<AgentStreamEvent> ChatStreamAsync(
        AgentModelRequest request,
        Func<string, JsonElement, CancellationToken, Task<string>>? toolExecutor,
        [EnumeratorCancellation] CancellationToken ct)
    {
        var profile = _settings.Settings.Agent.GetActiveProfile()
            ?? throw new InvalidOperationException(
                "No active agent profile configured. Create a profile in Agent Settings.");
        if (profile.Provider != ProviderKind.Acp)
            throw new InvalidOperationException(
                $"Active profile '{profile.DisplayName}' is not an ACP agent.");

        var caps = await _host.EnsureStartedAsync(profile, ct);

        var sessionKey = request.SessionKey ?? "global";
        // No resolved tools → no MCP server at all. An empty ?tools= allowlist would mean
        // "unfiltered" to the bridge (all tools), which would bypass the capability/mode gates
        // that decided this turn gets zero tools.
        var allowlist = request.Tools.Select(t => t.Name).OrderBy(n => n, StringComparer.Ordinal).ToList();
        var mcpUrl = caps.McpHttp && allowlist.Count > 0
            ? SwebKitToolsMcpBridge.BuildUrl($"http://127.0.0.1:{BoundPort()}", allowlist, request.Selection)
            : null;
        // The decoded ?tools= value — the same string the bridge reads back off the request query,
        // so its OutOfScopeCallTracker key matches ours.
        var allowlistKey = mcpUrl is null ? null : string.Join(',', allowlist);
        var outOfScopeBefore = _outOfScopeCalls.CountFor(allowlistKey);
        string? acpSessionId = null;
        string? sessionError = null;
        try
        {
            acpSessionId = await _host.EnsureSessionAsync(profile, sessionKey, mcpUrl, ct);
        }
        catch (InvalidOperationException ex)
        {
            sessionError = ex.Message;
        }
        if (acpSessionId is null)
        {
            yield return new AgentStreamEvent { Kind = AgentStreamEventKind.Error, ErrorMessage = sessionError ?? "Failed to create ACP session." };
            yield break;
        }

        var promptText = ComposePrompt(acpSessionId, request.SystemPrompt, request.UserMessage);

        var sw = Stopwatch.StartNew();
        var text = new StringBuilder();
        var toolsUsed = new List<string>();
        var steps = new List<AgentChatStep>();
        var openToolCalls = new Dictionary<string, (string Title, Stopwatch Elapsed)>();
        double? usagePercent = null;

        await foreach (var evt in _host.PromptAsync(acpSessionId, promptText, ct))
        {
            if (evt.Permission is not null)
            {
                yield return new AgentStreamEvent { Kind = AgentStreamEventKind.PermissionRequired };
                continue;
            }

            if (evt.StopReason is not null)
            {
                sw.Stop();
                foreach (var open in openToolCalls)
                    steps.Add(new AgentChatStep { Type = "tool_result", ToolName = open.Value.Title, Summary = "Cancelled", IsFailure = true, Elapsed = open.Value.Elapsed.Elapsed });

                if (evt.StopReason == "refusal")
                {
                    yield return new AgentStreamEvent
                    {
                        Kind = AgentStreamEventKind.Error,
                        ErrorMessage = "The agent refused to continue this turn.",
                        Steps = steps,
                        ContextUsagePercent = usagePercent,
                    };
                    yield break;
                }

                var finalText = text.ToString();
                if (evt.StopReason == "cancelled")
                    finalText += string.IsNullOrEmpty(finalText) ? "(cancelled)" : "\n\n(cancelled)";

                yield return new AgentStreamEvent
                {
                    Kind = AgentStreamEventKind.Done,
                    Result = new AgentChatResult
                    {
                        Text = finalText,
                        ToolsUsed = toolsUsed,
                        Elapsed = sw.Elapsed,
                        HitMaxRounds = false,
                        Steps = steps,
                        // agent-correlation Module 3: the agent reached for a tool this turn's scope
                        // fence hid — the UI turns this into a "retry with workspace scope" chip.
                        SuggestedScope = _outOfScopeCalls.CountFor(allowlistKey) > outOfScopeBefore
                            ? "workspace"
                            : null,
                    },
                    ContextUsagePercent = usagePercent,
                };
                yield break;
            }

            var update = evt.Update;
            if (update.ValueKind != JsonValueKind.Object ||
                !update.TryGetProperty("sessionUpdate", out var kindEl))
            {
                continue;
            }

            switch (kindEl.GetString())
            {
                case "agent_message_chunk":
                    var chunk = ExtractText(update);
                    if (chunk is not null)
                    {
                        text.Append(chunk);
                        yield return new AgentStreamEvent { Kind = AgentStreamEventKind.Token, Token = chunk };
                    }
                    break;

                case "agent_thought_chunk":
                    var thought = ExtractText(update);
                    if (thought is not null)
                        yield return new AgentStreamEvent { Kind = AgentStreamEventKind.Thought, Token = thought };
                    break;

                case "tool_call":
                    var toolCallId = update.TryGetProperty("toolCallId", out var tcid) ? tcid.GetString() ?? "" : "";
                    var title = update.TryGetProperty("title", out var tt) ? tt.GetString() ?? "tool call" : "tool call";
                    toolsUsed.Add(title);
                    openToolCalls[toolCallId] = (title, Stopwatch.StartNew());
                    steps.Add(new AgentChatStep { Type = "tool_call", ToolName = title, Summary = $"Calling {title}" });
                    yield return new AgentStreamEvent { Kind = AgentStreamEventKind.ToolCallStarted, ToolName = title };
                    break;

                case "tool_call_update":
                    var status = update.TryGetProperty("status", out var st) ? st.GetString() : null;
                    if (status is "completed" or "failed" or "cancelled")
                    {
                        var id = update.TryGetProperty("toolCallId", out var tid) ? tid.GetString() ?? "" : "";
                        if (openToolCalls.Remove(id, out var open))
                        {
                            open.Elapsed.Stop();
                            steps.Add(new AgentChatStep
                            {
                                Type = "tool_result",
                                ToolName = open.Title,
                                Summary = status == "completed" ? "Completed" : $"Tool call {status}",
                                IsFailure = status != "completed",
                                Elapsed = open.Elapsed.Elapsed,
                            });
                            yield return new AgentStreamEvent { Kind = AgentStreamEventKind.ToolCallResult, ToolName = open.Title };
                        }
                    }
                    break;

                case "usage_update":
                    if (update.TryGetProperty("used", out var used) && update.TryGetProperty("size", out var size) &&
                        used.ValueKind == JsonValueKind.Number && size.ValueKind == JsonValueKind.Number &&
                        size.GetDouble() > 0)
                    {
                        usagePercent = Math.Round(100.0 * used.GetDouble() / size.GetDouble(), 1);
                    }
                    break;

                    // "plan", "available_commands_update", "current_mode_update", "user_message_chunk":
                    // nothing in the current UI consumes them — ignored deliberately.
            }
        }
    }

    private int BoundPort()
    {
        var addresses = _server.Features.Get<IServerAddressesFeature>()?.Addresses;
        var first = addresses?.FirstOrDefault();
        if (first is not null && Uri.TryCreate(first, UriKind.Absolute, out var uri))
            return uri.Port;
        return 5199;
    }

    /// <summary>Prepends the system prompt when this ACP session hasn't seen it yet or it changed
    /// since last turn — ACP has no system-prompt channel, so context travels inside the user
    /// message.</summary>
    private string ComposePrompt(string acpSessionId, string systemPrompt, string userMessage)
    {
        if (_lastSentSystemPrompt.TryGetValue(acpSessionId, out var last) && last == systemPrompt)
            return userMessage;

        _lastSentSystemPrompt[acpSessionId] = systemPrompt;
        return $"{systemPrompt}\n\n---\n\n{userMessage}";
    }

    /// <summary>Extracts the text of a <c>content</c> block on a message-chunk update
    /// (<c>{"type":"text","text":"…"}</c>). Non-text content (images, resources) is ignored —
    /// nothing in SwebKit renders it.</summary>
    private static string? ExtractText(JsonElement update) =>
        update.TryGetProperty("content", out var content) &&
        content.TryGetProperty("text", out var t) &&
        t.ValueKind == JsonValueKind.String
            ? t.GetString()
            : null;
}
