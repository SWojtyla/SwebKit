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
        CancellationToken ct)
    {
        var apiKey = !string.IsNullOrEmpty(_config.ApiKey) 
            ? _config.ApiKey 
            : _credentialStore.Get("SwebKit-Agent:Mistral-ApiKey") ?? string.Empty;

        if (string.IsNullOrEmpty(apiKey))
        {
            throw new InvalidOperationException("Mistral API key not configured. Set via MistralConfig.ApiKey or ICredentialStore with key 'SwebKit-Agent:Mistral-ApiKey'.");
        }

        var requestPayload = BuildChatCompletionRequest(systemPrompt, userMessage, tools);

        var request = new HttpRequestMessage(HttpMethod.Post, _config.ApiEndpoint + "/chat/completions");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Content = new StringContent(requestPayload, Encoding.UTF8, "application/json");

        var response = await _httpClient.SendAsync(request, ct);
        
        if (!response.IsSuccessStatusCode)
        {
            var errorContent = await response.Content.ReadAsStringAsync(ct);
            throw new HttpRequestException("Mistral API request failed: " + response.StatusCode + " - " + errorContent);
        }

        var responseContent = await response.Content.ReadAsStringAsync(ct);
        return ExtractResponseText(responseContent);
    }

    private string BuildChatCompletionRequest(string systemPrompt, string userMessage, IReadOnlyList<ToolDefinition> tools)
    {
        var messages = new object[]
        {
            new { role = "system", content = systemPrompt },
            new { role = "user", content = userMessage }
        };

        var toolsArray = tools.Select(t => new
        {
            type = "function",
            function = new
            {
                name = t.Name,
                description = t.Description,
                parameters = t.ParametersSchema
            }
        }).ToArray();

        var payload = new
        {
            model = _config.Model,
            messages = messages,
            tools = toolsArray,
            max_tokens = _config.MaxTokens,
            temperature = _config.Temperature
        };

        return JsonSerializer.Serialize(payload);
    }

    private string ExtractResponseText(string responseJson)
    {
        using var doc = JsonDocument.Parse(responseJson);
        var root = doc.RootElement;
        var choices = root.GetProperty("choices");
        var firstChoice = choices[0];
        var message = firstChoice.GetProperty("message");
        return message.GetProperty("content").GetString() ?? string.Empty;
    }
}