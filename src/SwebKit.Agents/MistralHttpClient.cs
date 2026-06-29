using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using SwebKit.Core.Abstractions;

namespace SwebKit.Agents;

public sealed class MistralHttpClient : IMistralClient
{
    private readonly HttpClient _httpClient;
    private readonly MistralConfig _config;
    private readonly ICredentialStore _credentialStore;

    // Guard against runaway agentic loops
    private const int MaxToolRounds = 5;

    public MistralHttpClient(HttpClient httpClient, MistralConfig config, ICredentialStore credentialStore)
    {
        _httpClient = httpClient;
        _config = config;
        _credentialStore = credentialStore;
    }

    public async Task<string> ChatAsync(
        string systemPrompt,
        string userMessage,
        IReadOnlyList<ToolDefinition> tools,
        Func<string, JsonElement, CancellationToken, Task<string>>? toolExecutor,
        CancellationToken ct)
    {
        var apiKey = !string.IsNullOrEmpty(_config.ApiKey)
            ? _config.ApiKey
            : _credentialStore.Get("SwebKit-Agent:Mistral-ApiKey") ?? string.Empty;

        if (string.IsNullOrEmpty(apiKey))
            throw new InvalidOperationException(
                "Mistral API key not configured. Set MISTRAL_API_KEY env var or store in credential store under 'SwebKit-Agent:Mistral-ApiKey'.");

        var toolDefs = tools.Select(t => new
        {
            type = "function",
            function = new
            {
                name = t.Name,
                description = t.Description,
                parameters = t.ParametersSchema
            }
        }).ToArray();

        var messages = new List<object>
        {
            new { role = "system", content = systemPrompt },
            new { role = "user",   content = userMessage }
        };

        // Agentic loop: Mistral may ask us to call tools, and we send results back
        // until it produces a final text response (or we hit the round cap).
        for (var round = 0; round < MaxToolRounds; round++)
        {
            var payload = JsonSerializer.Serialize(new
            {
                model = _config.Model,
                messages = messages.ToArray(),
                tools = toolDefs,
                max_tokens = _config.MaxTokens,
                temperature = _config.Temperature
            });

            var responseJson = await PostAsync(payload, apiKey, ct);

            // Extract finish_reason and any tool calls from the response.
            // All JsonDocument reads are scoped inside the using block so the
            // document is never accessed after disposal.
            string finishReason;
            string? textContent;
            var pendingCalls = new List<(string Id, string ToolName, string ArgsJson)>();

            using (var doc = JsonDocument.Parse(responseJson))
            {
                var choice = doc.RootElement.GetProperty("choices")[0];
                var message = choice.GetProperty("message");
                finishReason = choice.GetProperty("finish_reason").GetString()!;
                textContent = message.TryGetProperty("content", out var ce) ? ce.GetString() : null;

                if (finishReason == "tool_calls" && message.TryGetProperty("tool_calls", out var tcs))
                {
                    foreach (var tc in tcs.EnumerateArray())
                    {
                        var callId = tc.GetProperty("id").GetString()!;
                        var fn = tc.GetProperty("function");
                        var name = fn.GetProperty("name").GetString()!;
                        var argsStr = fn.GetProperty("arguments").GetString()!;
                        pendingCalls.Add((callId, name, argsStr));
                    }

                    // Preserve assistant message (with tool_calls) in history
                    messages.Add(new
                    {
                        role = "assistant",
                        content = textContent ?? string.Empty,
                        tool_calls = pendingCalls.Select(c => new
                        {
                            id = c.Id,
                            type = "function",
                            function = new { name = c.ToolName, arguments = c.ArgsJson }
                        }).ToArray()
                    });
                }
            }

            // Normal final response — return text
            if (finishReason != "tool_calls" || toolExecutor is null || pendingCalls.Count == 0)
                return textContent ?? string.Empty;

            // Execute tools and feed results back to Mistral
            foreach (var (callId, toolName, argsJson) in pendingCalls)
            {
                string toolResult;
                try
                {
                    // argsDoc stays alive for the entirety of ExecuteAsync
                    using var argsDoc = JsonDocument.Parse(argsJson);
                    toolResult = await toolExecutor(toolName, argsDoc.RootElement, ct);
                }
                catch (OperationCanceledException)
                {
                    throw; // CS-2: never swallow cancellation
                }
                catch (Exception ex)
                {
                    toolResult = JsonSerializer.Serialize(new { error = ex.Message });
                }

                messages.Add(new { role = "tool", tool_call_id = callId, content = toolResult });
            }
        }

        return "Agent did not produce a final response after reaching the maximum tool-call rounds.";
    }

    private async Task<string> PostAsync(string payload, string apiKey, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, _config.ApiEndpoint + "/chat/completions");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Content = new StringContent(payload, Encoding.UTF8, "application/json");

        using var response = await _httpClient.SendAsync(request, ct);

        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync(ct);
            throw new HttpRequestException($"Mistral API error {(int)response.StatusCode}: {error}");
        }

        return await response.Content.ReadAsStringAsync(ct);
    }
}