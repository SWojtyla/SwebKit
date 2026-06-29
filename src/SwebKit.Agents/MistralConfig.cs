namespace SwebKit.Agents;

/// <summary>
/// Configuration for Mistral AI API integration.
/// For Phase 0 POC: Minimal configuration with API key loaded from ICredentialStore.
/// </summary>
public sealed class MistralConfig
{
    /// <summary>
    /// Mistral API key. Loaded from ICredentialStore using key "SwebKit-Agent:Mistral-ApiKey".
    /// </summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>
    /// Mistral API endpoint. Defaults to official Mistral API.
    /// </summary>
    public string ApiEndpoint { get; set; } = "https://api.mistral.ai/v1";

    /// <summary>
    /// Default model to use for chat completions.
    /// </summary>
    public string Model { get; set; } = "mistral-medium-latest";

    /// <summary>
    /// Maximum number of tokens to generate in the response.
    /// </summary>
    public int MaxTokens { get; set; } = 2048;

    /// <summary>
    /// Temperature for sampling. Lower values make responses more deterministic.
    /// </summary>
    public double Temperature { get; set; } = 0.7;
}