using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using SwebKit.Core.Abstractions;
using SwebKit.Core.Configuration;
using SwebKit.Core.Domain;

namespace SwebKit.Agents;

/// <summary>
/// OpenAI-compatible LLM client that works with LM Studio, Mistral, and any
/// endpoint implementing the <c>/chat/completions</c> protocol.
/// Replaces the former <c>MistralHttpClient</c>.
/// </summary>
public sealed class OpenAiCompatibleAgentClient : IAgentModelClient
{
    /// <summary>Cap on a single tool result's length before it's fed back into the conversation —
    /// workspace-intelligence Module 5. One tool call (e.g. <c>GetPodLogsTool</c>) can return far
    /// more text than twenty ordinary chat turns combined; capping here, not just trimming overall
    /// history later, matters because an oversized result would otherwise blow the context budget
    /// within a single turn, before <c>SidecarAgentChatService</c>'s history-level summarization
    /// ever gets a chance to run. ~8,000 chars (~2,000 tokens) leaves room for several capped
    /// results across a multi-tool-call turn without either number needing to be exact.</summary>
    private const int MaxToolResultChars = 8_000;

    private readonly HttpClient _httpClient;
    private readonly UserSettingsRepository _settings;
    private readonly ICredentialStore _credentialStore;

    public OpenAiCompatibleAgentClient(
        HttpClient httpClient,
        UserSettingsRepository settings,
        ICredentialStore credentialStore)
    {
        _httpClient = httpClient;
        _settings = settings;
        _credentialStore = credentialStore;
    }

    public async Task<AgentChatResult> ChatAsync(
        AgentModelRequest request,
        Func<string, JsonElement, CancellationToken, Task<string>>? toolExecutor,
        CancellationToken ct)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var toolsUsed = new List<string>();
        var (baseUrl, apiKey, model, timeout) = ResolveActiveProfile();

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        if (timeout > TimeSpan.Zero)
            cts.CancelAfter(timeout);

        var toolDefs = BuildToolDefs(request.Tools);
        var messages = BuildMessages(request.SystemPrompt, request.History, request.UserMessage);
        var maxRounds = request.MaxToolRounds > 0 ? request.MaxToolRounds : 5;

        for (var round = 0; round < maxRounds; round++)
        {
            var seenToolCalls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var payload = JsonSerializer.Serialize(new
            {
                model,
                messages = messages.Select(m => m.ToWireFormat()).ToArray(),
                tools = toolDefs
            });

            var responseJson = await PostChatCompletionsAsync(payload, apiKey, baseUrl, cts.Token);
            var response = ParseResponse(responseJson);

            messages.Add(response.AssistantMessage);

            if (response.FinishReason != AgentFinishReason.ToolCalls ||
                toolExecutor is null ||
                response.ToolCalls is null ||
                response.ToolCalls.Count == 0)
            {
                sw.Stop();
                return new AgentChatResult
                {
                    Text = response.Content ?? string.Empty,
                    ToolsUsed = toolsUsed,
                    Elapsed = sw.Elapsed,
                    HitMaxRounds = false
                };
            }

            // Execute tools and feed results back
            foreach (var tc in response.ToolCalls)
            {
                // Guard against duplicate calls in the same round
                if (!seenToolCalls.Add(tc.Id))
                    continue;

                string toolResult;
                try
                {
                    using var argsDoc = JsonDocument.Parse(tc.ArgumentsJson);
                    toolResult = await toolExecutor(tc.Name, argsDoc.RootElement, cts.Token);
                    toolsUsed.Add(tc.Name);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    toolResult = JsonSerializer.Serialize(new { error = ex.Message });
                }

                messages.Add(new AgentMessage
                {
                    Role = "tool",
                    ToolCallId = tc.Id,
                    Content = CapToolResult(toolResult)
                });
            }
        }

        sw.Stop();
        return new AgentChatResult
        {
            Text = "Agent did not produce a final response after reaching the maximum tool-call rounds.",
            ToolsUsed = toolsUsed,
            Elapsed = sw.Elapsed,
            HitMaxRounds = true
        };
    }

    public async Task<AgentModelResponse> CompleteAsync(
        AgentModelRequest request,
        CancellationToken ct)
    {
        var (baseUrl, apiKey, model, timeout) = ResolveActiveProfile();

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        if (timeout > TimeSpan.Zero)
            cts.CancelAfter(timeout);

        var toolDefs = BuildToolDefs(request.Tools);
        var messages = BuildMessages(request.SystemPrompt, request.History, request.UserMessage);

        var payload = JsonSerializer.Serialize(new
        {
            model,
            messages = messages.Select(m => m.ToWireFormat()).ToArray(),
            tools = toolDefs
        });

        var responseJson = await PostChatCompletionsAsync(payload, apiKey, baseUrl, cts.Token);
        return ParseResponse(responseJson);
    }

    public async IAsyncEnumerable<AgentStreamEvent> ChatStreamAsync(
        AgentModelRequest request,
        Func<string, JsonElement, CancellationToken, Task<string>>? toolExecutor,
        [EnumeratorCancellation] CancellationToken ct)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var toolsUsed = new List<string>();
        var (baseUrl, apiKey, model, timeout) = ResolveActiveProfile();

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        if (timeout > TimeSpan.Zero)
            cts.CancelAfter(timeout);

        var toolDefs = BuildToolDefs(request.Tools);
        var messages = BuildMessages(request.SystemPrompt, request.History, request.UserMessage);
        var maxRounds = request.MaxToolRounds > 0 ? request.MaxToolRounds : 5;

        for (var round = 0; round < maxRounds; round++)
        {
            var seenToolCalls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var payload = JsonSerializer.Serialize(new
            {
                model,
                messages = messages.Select(m => m.ToWireFormat()).ToArray(),
                tools = toolDefs,
                stream = true
            });

            var accumulator = new StreamingResponseAccumulator();
            await foreach (var chunkJson in PostChatCompletionsStreamAsync(payload, apiKey, baseUrl, cts.Token))
            {
                var delta = accumulator.Accept(chunkJson);
                if (!string.IsNullOrEmpty(delta))
                    yield return new AgentStreamEvent { Kind = AgentStreamEventKind.Token, Token = delta };
            }

            var response = accumulator.Build();
            messages.Add(response.AssistantMessage);

            if (response.FinishReason != AgentFinishReason.ToolCalls ||
                toolExecutor is null ||
                response.ToolCalls is null ||
                response.ToolCalls.Count == 0)
            {
                sw.Stop();
                yield return new AgentStreamEvent
                {
                    Kind = AgentStreamEventKind.Done,
                    Result = new AgentChatResult
                    {
                        Text = response.Content ?? string.Empty,
                        ToolsUsed = toolsUsed,
                        Elapsed = sw.Elapsed,
                        HitMaxRounds = false
                    }
                };
                yield break;
            }

            foreach (var tc in response.ToolCalls)
            {
                if (!seenToolCalls.Add(tc.Id))
                    continue;

                yield return new AgentStreamEvent { Kind = AgentStreamEventKind.ToolCallStarted, ToolName = tc.Name };

                string toolResult;
                try
                {
                    using var argsDoc = JsonDocument.Parse(tc.ArgumentsJson);
                    toolResult = await toolExecutor(tc.Name, argsDoc.RootElement, cts.Token);
                    toolsUsed.Add(tc.Name);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    toolResult = JsonSerializer.Serialize(new { error = ex.Message });
                }

                yield return new AgentStreamEvent { Kind = AgentStreamEventKind.ToolCallResult, ToolName = tc.Name };

                messages.Add(new AgentMessage
                {
                    Role = "tool",
                    ToolCallId = tc.Id,
                    Content = toolResult
                });
            }
        }

        sw.Stop();
        yield return new AgentStreamEvent
        {
            Kind = AgentStreamEventKind.Done,
            Result = new AgentChatResult
            {
                Text = "Agent did not produce a final response after reaching the maximum tool-call rounds.",
                ToolsUsed = toolsUsed,
                Elapsed = sw.Elapsed,
                HitMaxRounds = true
            }
        };
    }

    // ── Helpers ──

    private (string baseUrl, string? apiKey, string model, TimeSpan timeout) ResolveActiveProfile()
    {
        var profile = _settings.Settings.Agent.GetActiveProfile()
            ?? throw new InvalidOperationException(
                "No active agent profile configured. Create a profile in Agent Settings.");

        var apiKey = !string.IsNullOrEmpty(profile.CredentialKey)
            ? _credentialStore.Get(profile.CredentialKey)
            : null;

        if (profile.RequiresApiKey && string.IsNullOrEmpty(apiKey))
            throw new InvalidOperationException(
                $"API key not found for profile '{profile.DisplayName}'. Check the credential store configuration.");

        var baseUrl = NormalizeBaseUrl(profile.BaseUrl);
        var timeout = profile.TimeoutSeconds > 0
            ? TimeSpan.FromSeconds(profile.TimeoutSeconds)
            : TimeSpan.Zero;

        return (baseUrl, apiKey, profile.Model, timeout);
    }

    /// <summary>
    /// Normalizes the base URL: ensures no trailing slash, no double /v1.
    /// </summary>
    internal static string NormalizeBaseUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return url;

        url = url.TrimEnd('/');

        // Avoid double /v1 — if the URL already ends with /v1, keep it as-is.
        // Only append /v1 if it's missing and the URL doesn't already contain a version segment.
        if (!url.EndsWith("/v1", StringComparison.OrdinalIgnoreCase) &&
            !url.Contains("/v1/", StringComparison.OrdinalIgnoreCase) &&
            !url.Contains("/v2", StringComparison.OrdinalIgnoreCase))
        {
            url += "/v1";
        }

        return url;
    }

    private static List<AgentMessage> BuildMessages(
        string systemPrompt,
        IReadOnlyList<AgentMessage> history,
        string userMessage)
    {
        var messages = new List<AgentMessage>(capacity: (history?.Count ?? 0) + 2)
        {
            new() { Role = "system", Content = systemPrompt }
        };
        if (history is not null)
            messages.AddRange(history);
        messages.Add(new() { Role = "user", Content = userMessage });
        return messages;
    }

    /// <summary>Truncates an oversized tool result with an explicit marker rather than silently
    /// cutting it off — workspace-intelligence Module 5. A result already under the cap passes
    /// through untouched (also handles a null/empty result without throwing).</summary>
    internal static string CapToolResult(string? toolResult)
    {
        if (string.IsNullOrEmpty(toolResult) || toolResult.Length <= MaxToolResultChars)
            return toolResult ?? string.Empty;

        var truncatedCount = toolResult.Length - MaxToolResultChars;
        return toolResult[..MaxToolResultChars] + $"\n...truncated, {truncatedCount:N0} more characters available";
    }

    private static object[] BuildToolDefs(IReadOnlyList<ToolDefinition> tools)
    {
        return tools.Select(t => new
        {
            type = "function",
            function = new
            {
                name = t.Name,
                description = t.Description,
                parameters = t.ParametersSchema
            }
        }).ToArray();
    }

    internal static AgentModelResponse ParseResponse(string responseJson)
    {
        using var doc = JsonDocument.Parse(responseJson);
        var choices = doc.RootElement.GetProperty("choices");
        if (choices.GetArrayLength() == 0)
            throw new InvalidOperationException("LLM API returned an empty choices array. The provider may be rate-limiting or experiencing an internal error.");

        var choice = choices[0];
        var message = choice.GetProperty("message");

        var finishReasonStr = choice.TryGetProperty("finish_reason", out var fr) ? fr.GetString() : null;
        var finishReason = finishReasonStr switch
        {
            "stop" => AgentFinishReason.Stop,
            "tool_calls" => AgentFinishReason.ToolCalls,
            "length" => AgentFinishReason.Length,
            "content_filter" => AgentFinishReason.ContentFilter,
            _ => AgentFinishReason.Unknown
        };

        var content = message.TryGetProperty("content", out var ce) && ce.ValueKind != JsonValueKind.Null
            ? ce.GetString()
            : null;

        List<AgentToolCall>? toolCalls = null;
        if (finishReason == AgentFinishReason.ToolCalls && message.TryGetProperty("tool_calls", out var tcs))
        {
            toolCalls = [];
            foreach (var tc in tcs.EnumerateArray())
            {
                var callId = tc.GetProperty("id").GetString()!;
                var fn = tc.GetProperty("function");
                var name = fn.GetProperty("name").GetString()!;
                var argsStr = fn.GetProperty("arguments").GetString() ?? "{}";
                toolCalls.Add(new AgentToolCall { Id = callId, Name = name, ArgumentsJson = argsStr });
            }
        }

        var assistantMessage = new AgentMessage
        {
            Role = "assistant",
            Content = content,
            ToolCalls = toolCalls
        };

        return new AgentModelResponse
        {
            FinishReason = finishReason,
            Content = content,
            ToolCalls = toolCalls,
            AssistantMessage = assistantMessage
        };
    }

    private async Task<string> PostChatCompletionsAsync(
        string payload,
        string? apiKey,
        string baseUrl,
        CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, baseUrl + "/chat/completions");

        if (!string.IsNullOrEmpty(apiKey))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Content = new StringContent(payload, Encoding.UTF8, "application/json");

        using var response = await _httpClient.SendAsync(request, ct);

        if (!response.IsSuccessStatusCode)
        {
            var statusCode = (int)response.StatusCode;
            var errorBody = await response.Content.ReadAsStringAsync(ct);
            // Sanitize: don't leak full payload, but include status and a truncated error snippet.
            var snippet = errorBody.Length > 200 ? errorBody[..200] + "…" : errorBody;
            throw new HttpRequestException(
                $"LLM API error {statusCode} from {baseUrl}: {snippet}");
        }

        return await response.Content.ReadAsStringAsync(ct);
    }

    /// <summary>
    /// Posts with <c>stream: true</c> and yields each SSE <c>data:</c> payload's raw JSON as it
    /// arrives (skipping keep-alive blanks and stopping at the terminal <c>data: [DONE]</c> line —
    /// never yielding that sentinel itself).
    /// </summary>
    private async IAsyncEnumerable<string> PostChatCompletionsStreamAsync(
        string payload,
        string? apiKey,
        string baseUrl,
        [EnumeratorCancellation] CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, baseUrl + "/chat/completions");

        if (!string.IsNullOrEmpty(apiKey))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
        request.Content = new StringContent(payload, Encoding.UTF8, "application/json");

        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);

        if (!response.IsSuccessStatusCode)
        {
            var statusCode = (int)response.StatusCode;
            var errorBody = await response.Content.ReadAsStringAsync(ct);
            var snippet = errorBody.Length > 200 ? errorBody[..200] + "…" : errorBody;
            throw new HttpRequestException(
                $"LLM API error {statusCode} from {baseUrl}: {snippet}");
        }

        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var reader = new StreamReader(stream);

        while (true)
        {
            var line = await reader.ReadLineAsync(ct);
            if (line is null)
                yield break;
            if (string.IsNullOrEmpty(line) || !line.StartsWith("data:", StringComparison.Ordinal))
                continue;

            var data = line["data:".Length..].Trim();
            if (data.Length == 0)
                continue;
            if (data == "[DONE]")
                yield break;

            yield return data;
        }
    }

    /// <summary>
    /// Accumulates OpenAI-compatible SSE chunk deltas (<c>choices[0].delta</c>) across a single
    /// round into a complete <see cref="AgentModelResponse"/> — content arrives token by token;
    /// tool calls arrive as index-keyed fragments (id/name once, <c>arguments</c> across many
    /// chunks) that must be concatenated before the JSON they form can be parsed.
    /// </summary>
    private sealed class StreamingResponseAccumulator
    {
        private readonly StringBuilder _content = new();
        private readonly Dictionary<int, ToolCallBuilder> _toolCallsByIndex = new();
        private AgentFinishReason _finishReason = AgentFinishReason.Unknown;

        /// <summary>Feeds one SSE chunk's JSON payload; returns the content delta it carried, if any.</summary>
        public string? Accept(string chunkJson)
        {
            using var doc = JsonDocument.Parse(chunkJson);
            if (!doc.RootElement.TryGetProperty("choices", out var choices) || choices.GetArrayLength() == 0)
                return null;

            var choice = choices[0];

            if (choice.TryGetProperty("finish_reason", out var fr) && fr.ValueKind == JsonValueKind.String)
            {
                _finishReason = fr.GetString() switch
                {
                    "stop" => AgentFinishReason.Stop,
                    "tool_calls" => AgentFinishReason.ToolCalls,
                    "length" => AgentFinishReason.Length,
                    "content_filter" => AgentFinishReason.ContentFilter,
                    _ => AgentFinishReason.Unknown
                };
            }

            if (!choice.TryGetProperty("delta", out var delta))
                return null;

            string? contentDelta = null;
            if (delta.TryGetProperty("content", out var ce) && ce.ValueKind == JsonValueKind.String)
            {
                contentDelta = ce.GetString();
                if (!string.IsNullOrEmpty(contentDelta))
                    _content.Append(contentDelta);
            }

            if (delta.TryGetProperty("tool_calls", out var tcs) && tcs.ValueKind == JsonValueKind.Array)
            {
                foreach (var tc in tcs.EnumerateArray())
                {
                    var index = tc.TryGetProperty("index", out var idxEl) && idxEl.ValueKind == JsonValueKind.Number
                        ? idxEl.GetInt32()
                        : 0;

                    if (!_toolCallsByIndex.TryGetValue(index, out var builder))
                        _toolCallsByIndex[index] = builder = new ToolCallBuilder();

                    if (tc.TryGetProperty("id", out var idEl) && idEl.ValueKind == JsonValueKind.String)
                        builder.Id = idEl.GetString();

                    if (tc.TryGetProperty("function", out var fn))
                    {
                        if (fn.TryGetProperty("name", out var nameEl) && nameEl.ValueKind == JsonValueKind.String)
                            builder.Name = nameEl.GetString();
                        if (fn.TryGetProperty("arguments", out var argsEl) && argsEl.ValueKind == JsonValueKind.String)
                            builder.Arguments.Append(argsEl.GetString());
                    }
                }
            }

            return contentDelta;
        }

        public AgentModelResponse Build()
        {
            List<AgentToolCall>? toolCalls = null;
            if (_toolCallsByIndex.Count > 0)
            {
                toolCalls = _toolCallsByIndex
                    .OrderBy(kv => kv.Key)
                    .Select(kv => new AgentToolCall
                    {
                        Id = kv.Value.Id ?? $"call_{kv.Key}",
                        Name = kv.Value.Name ?? string.Empty,
                        ArgumentsJson = kv.Value.Arguments.Length > 0 ? kv.Value.Arguments.ToString() : "{}"
                    })
                    .ToList();
            }

            // Some providers omit finish_reason on the final chunk when tool_calls were streamed —
            // infer it from having accumulated any tool call fragments at all, same fallback the
            // non-streaming ParseResponse doesn't need since it always gets an explicit reason.
            var finishReason = _finishReason == AgentFinishReason.Unknown && toolCalls is { Count: > 0 }
                ? AgentFinishReason.ToolCalls
                : _finishReason;

            var content = _content.Length > 0 ? _content.ToString() : null;
            var effectiveToolCalls = finishReason == AgentFinishReason.ToolCalls ? toolCalls : null;

            var assistantMessage = new AgentMessage
            {
                Role = "assistant",
                Content = content,
                ToolCalls = effectiveToolCalls
            };

            return new AgentModelResponse
            {
                FinishReason = finishReason,
                Content = content,
                ToolCalls = effectiveToolCalls,
                AssistantMessage = assistantMessage
            };
        }

        private sealed class ToolCallBuilder
        {
            public string? Id { get; set; }
            public string? Name { get; set; }
            public StringBuilder Arguments { get; } = new();
        }
    }
}
