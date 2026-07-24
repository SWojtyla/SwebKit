namespace SwebKit.Core.Domain;

/// <summary>
/// Factory methods for creating default <see cref="AgentProfile"/> instances per provider.
/// </summary>
public static class AgentProfilePresets
{
    /// <summary>Logical credential key for the Mistral API key in the credential store.</summary>
    public const string MistralCredentialKey = "SwebKit-Agent:Mistral-ApiKey";

    /// <summary>Default LM Studio base URL.</summary>
    public const string LmStudioBaseUrl = "http://localhost:1234/v1";

    /// <summary>Default Mistral API base URL.</summary>
    public const string MistralBaseUrl = "https://api.mistral.ai/v1";

    /// <summary>Default Mistral model.</summary>
    public const string MistralDefaultModel = "mistral-medium-latest";

    /// <summary>Creates a default LM Studio profile (local, no API key required).</summary>
    public static AgentProfile LmStudio(string? model = null) => new()
    {
        DisplayName = "LM Studio Local",
        Provider = ProviderKind.LmStudio,
        BaseUrl = LmStudioBaseUrl,
        Model = model ?? string.Empty,
        CredentialKey = null,
        Temperature = 0.7,
        MaxTokens = 2048,
        TimeoutSeconds = 120,
    };

    /// <summary>Creates a default Mistral profile (cloud, API key required).</summary>
    public static AgentProfile Mistral(string? model = null) => new()
    {
        DisplayName = "Mistral Cloud",
        Provider = ProviderKind.Mistral,
        BaseUrl = MistralBaseUrl,
        Model = model ?? MistralDefaultModel,
        CredentialKey = MistralCredentialKey,
        Temperature = 0.7,
        MaxTokens = 2048,
        TimeoutSeconds = 60,
    };

    /// <summary>Creates a generic OpenAI-compatible profile template.</summary>
    public static AgentProfile OpenAiCompatible(string baseUrl, string model, string? credentialKey = null) => new()
    {
        DisplayName = "OpenAI-Compatible",
        Provider = ProviderKind.OpenAiCompatible,
        BaseUrl = baseUrl,
        Model = model,
        CredentialKey = credentialKey,
        Temperature = 0.7,
        MaxTokens = 2048,
        TimeoutSeconds = 60,
    };
}
