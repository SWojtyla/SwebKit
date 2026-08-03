using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using SwebKit.Core.Abstractions;
using SwebKit.Core.Configuration;
using SwebKit.Core.Domain;

namespace SwebKit.Agents;

/// <summary>
/// Result of a capability test for a provider profile.
/// </summary>
public sealed class CapabilityTestResult
{
    public bool ServerReachable { get; init; }
    public bool ModelAvailable { get; init; }
    public bool ChatValid { get; init; }
    public bool ToolCallingValid { get; init; }
    public AgentCapability Capability { get; init; } = AgentCapability.Unknown;
    public string? Diagnostic { get; init; }
    public IReadOnlyList<string>? AvailableModels { get; init; }
}

/// <summary>
/// Tests whether an LLM provider endpoint is reachable and supports tool calling.
/// Used by the settings UI to classify a profile as ChatOnly or ToolCalling.
/// </summary>
public sealed class AgentCapabilityTester
{
    private readonly HttpClient _httpClient;
    private readonly ICredentialStore _credentialStore;

    public AgentCapabilityTester(HttpClient httpClient, ICredentialStore credentialStore)
    {
        _httpClient = httpClient;
        _credentialStore = credentialStore;
    }

    /// <summary>
    /// Runs a full capability test against the specified profile:
    /// 1. GET /models (if available)
    /// 2. Mini chat call
    /// 3. Mini tool call
    /// </summary>
    public async Task<CapabilityTestResult> TestAsync(
        AgentProfile profile,
        CancellationToken ct = default)
    {
        var baseUrl = OpenAiCompatibleAgentClient.NormalizeBaseUrl(profile.BaseUrl);
        var apiKey = ResolveApiKey(profile);

        if (profile.RequiresApiKey && string.IsNullOrEmpty(apiKey))
        {
            return new CapabilityTestResult
            {
                ServerReachable = false,
                Diagnostic = $"API key not found. Expected credential store key: '{profile.CredentialKey}'."
            };
        }

        // Step 1: Check server reachability via GET /models
        List<string>? models = null;
        bool serverReachable;
        try
        {
            models = await GetModelsAsync(baseUrl, apiKey, ct);
            serverReachable = true;
        }
        catch (HttpRequestException ex)
        {
            return new CapabilityTestResult
            {
                ServerReachable = false,
                Diagnostic = $"Server unreachable: {ex.Message}"
            };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            // Some servers don't support /models — treat as reachable but model list unavailable
            serverReachable = true;
            models = null;
        }

        // Check if the configured model is available
        var modelAvailable = string.IsNullOrEmpty(profile.Model) ||
            (models is null) || // Can't verify, assume available
            models.Contains(profile.Model, StringComparer.OrdinalIgnoreCase);

        // Step 2: Mini chat call
        bool chatValid;
        try
        {
            var chatResponse = await SendMiniChatAsync(baseUrl, apiKey, profile.Model, ct);
            chatValid = chatResponse;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new CapabilityTestResult
            {
                ServerReachable = serverReachable,
                ModelAvailable = modelAvailable,
                ChatValid = false,
                Diagnostic = $"Chat test failed: {ex.Message}"
            };
        }

        if (!chatValid)
        {
            return new CapabilityTestResult
            {
                ServerReachable = serverReachable,
                ModelAvailable = modelAvailable,
                ChatValid = false,
                Capability = AgentCapability.Unknown,
                Diagnostic = "Chat returned empty response."
            };
        }

        // Step 3: Mini tool call
        bool toolCallingValid;
        try
        {
            toolCallingValid = await SendMiniToolCallAsync(baseUrl, apiKey, profile.Model, ct);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            toolCallingValid = false;
        }

        var capability = toolCallingValid ? AgentCapability.ToolCalling : AgentCapability.ChatOnly;
        var diagnostic = toolCallingValid
            ? "Tool calling supported."
            : "Tool calling not supported. Chat-only mode active.";

        return new CapabilityTestResult
        {
            ServerReachable = serverReachable,
            ModelAvailable = modelAvailable,
            ChatValid = chatValid,
            ToolCallingValid = toolCallingValid,
            Capability = capability,
            Diagnostic = diagnostic,
            AvailableModels = models
        };
    }

    private string? ResolveApiKey(AgentProfile profile)
    {
        if (string.IsNullOrEmpty(profile.CredentialKey))
            return null;
        return _credentialStore.Get(profile.CredentialKey);
    }

    private async Task<List<string>?> GetModelsAsync(string baseUrl, string? apiKey, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, baseUrl + "/models");
        if (!string.IsNullOrEmpty(apiKey))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        using var response = await _httpClient.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
            return null;

        var json = await response.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(json);

        if (!doc.RootElement.TryGetProperty("data", out var data))
            return null;

        var models = new List<string>();
        foreach (var item in data.EnumerateArray())
        {
            if (item.TryGetProperty("id", out var id))
            {
                var idStr = id.GetString();
                if (!string.IsNullOrEmpty(idStr))
                    models.Add(idStr);
            }
        }
        return models;
    }

    private async Task<bool> SendMiniChatAsync(string baseUrl, string? apiKey, string model, CancellationToken ct)
    {
        var payload = JsonSerializer.Serialize(new
        {
            model,
            messages = new[]
            {
                new { role = "user", content = "Reply with exactly: OK" }
            },
            // Was 10 — reasoning-capable local models (observed with a Gemma QAT model in LM
            // Studio) can spend the entire budget on hidden reasoning tokens before emitting any
            // visible content, so a tiny cap made this probe report "empty response" for models
            // that work perfectly fine in real conversation (which uses no cap at all — see
            // OpenAiCompatibleAgentClient, which omits max_tokens entirely since Module 9). 64
            // gives reasoning room without turning this into a slow test.
            max_tokens = 64,
            temperature = 0
        });

        var responseJson = await PostChatAsync(baseUrl, apiKey, payload, ct);
        using var doc = JsonDocument.Parse(responseJson);
        var choice = doc.RootElement.GetProperty("choices")[0];
        var message = choice.GetProperty("message");
        var content = message.TryGetProperty("content", out var ce) && ce.ValueKind != JsonValueKind.Null
            ? ce.GetString()
            : null;
        return !string.IsNullOrEmpty(content);
    }

    private async Task<bool> SendMiniToolCallAsync(string baseUrl, string? apiKey, string model, CancellationToken ct)
    {
        var toolSchema = JsonSerializer.SerializeToDocument(new
        {
            type = "object",
            properties = new
            {
                echo = new { type = "string", description = "Echo this value back." }
            },
            required = Array.Empty<string>()
        }).RootElement;

        var payload = JsonSerializer.Serialize(new
        {
            model,
            messages = new[]
            {
                new { role = "user", content = "Call the echo_test tool with echo='hello'." }
            },
            tools = new[]
            {
                new
                {
                    type = "function",
                    function = new { name = "echo_test", description = "Echo test tool.", parameters = toolSchema }
                }
            },
            max_tokens = 100,
            temperature = 0
        });

        var responseJson = await PostChatAsync(baseUrl, apiKey, payload, ct);
        using var doc = JsonDocument.Parse(responseJson);
        var choice = doc.RootElement.GetProperty("choices")[0];
        var finishReason = choice.TryGetProperty("finish_reason", out var fr) ? fr.GetString() : null;
        if (finishReason != "tool_calls")
            return false;

        var message = choice.GetProperty("message");
        if (!message.TryGetProperty("tool_calls", out var tcs))
            return false;

        return tcs.GetArrayLength() > 0;
    }

    private async Task<string> PostChatAsync(string baseUrl, string? apiKey, string payload, CancellationToken ct)
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
            var snippet = errorBody.Length > 200 ? errorBody[..200] + "…" : errorBody;
            throw new HttpRequestException($"LLM API error {statusCode}: {snippet}");
        }

        return await response.Content.ReadAsStringAsync(ct);
    }
}
