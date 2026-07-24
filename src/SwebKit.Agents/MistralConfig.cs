namespace SwebKit.Agents;

/// <summary>
/// Legacy configuration for Mistral AI API integration.
/// Replaced by <see cref="SwebKit.Core.Domain.AgentProfile"/> with <see cref="SwebKit.Core.Domain.ProviderKind.Mistral"/>.
/// </summary>
[Obsolete("Replaced by AgentProfile. Will be removed in a future version.")]
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