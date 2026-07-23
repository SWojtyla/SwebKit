using System.Net.Http.Headers;
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
        var (baseUrl, apiKey, model, temperature, maxTokens, timeout) = ResolveActiveProfile();

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        if (timeout > TimeSpan.Zero)
            cts.CancelAfter(timeout);

        var toolDefs = BuildToolDefs(request.Tools);
        var messages = BuildMessages(request.SystemPrompt, request.History, request.UserMessage);
        var maxRounds = request.MaxToolRounds > 0 ? request.MaxToolRounds : 5;
        var seenToolCalls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (var round = 0; round < maxRounds; round++)
        {
            var payload = JsonSerializer.Serialize(new
            {
                model,
                messages = messages.ToArray(),
                tools = toolDefs,
                max_tokens = maxTokens,
                temperature
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
                    Content = toolResult
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
        var (baseUrl, apiKey, model, temperature, maxTokens, timeout) = ResolveActiveProfile();

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        if (timeout > TimeSpan.Zero)
            cts.CancelAfter(timeout);

        var toolDefs = BuildToolDefs(request.Tools);
        var messages = BuildMessages(request.SystemPrompt, request.History, request.UserMessage);

        var payload = JsonSerializer.Serialize(new
        {
            model,
            messages = messages.ToArray(),
            tools = toolDefs,
            max_tokens = maxTokens,
            temperature
        });

        var responseJson = await PostChatCompletionsAsync(payload, apiKey, baseUrl, cts.Token);
        return ParseResponse(responseJson);
    }

    // ── Helpers ──

    private (string baseUrl, string? apiKey, string model, double temp, int maxTokens, TimeSpan timeout) ResolveActiveProfile()
    {
        var profile = _settings.Settings.Agent.GetActiveProfile()
            ?? throw new InvalidOperationException(
                "No active agent profile configured. Create a profile in Agent Settings.");

        var apiKey = !string.IsNullOrEmpty(profile.CredentialKey)
            ? _credentialStore.Get(profile.CredentialKey)
            : null;

        if (profile.RequiresApiKey && string.IsNullOrEmpty(apiKey))
            throw new InvalidOperationException(
                $"API key not found for profile '{profile.DisplayName}'. " +
                $"Expected credential store key: '{profile.CredentialKey}'.");

        var baseUrl = NormalizeBaseUrl(profile.BaseUrl);
        var timeout = profile.TimeoutSeconds > 0
            ? TimeSpan.FromSeconds(profile.TimeoutSeconds)
            : TimeSpan.Zero;

        return (baseUrl, apiKey, profile.Model, profile.Temperature, profile.MaxTokens, timeout);
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
        // Only append /v1 if it's missing and the URL doesn't already contain it.
        if (!url.EndsWith("/v1", StringComparison.OrdinalIgnoreCase) &&
            !url.Contains("/v1/", StringComparison.OrdinalIgnoreCase))
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
        var choice = doc.RootElement.GetProperty("choices")[0];
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
}
