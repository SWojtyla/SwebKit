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

    /// <summary>Documented context window for <see cref="MistralDefaultModel"/> (32K tokens, per
    /// Mistral's published model card at the time this was written) — a reasonable starting default
    /// for a known cloud model; <see cref="AgentCapabilityTester"/> doesn't probe this for Mistral
    /// specifically (its <c>/v1/models</c> response doesn't advertise it), unlike the best-effort
    /// LM Studio detection.</summary>
    public const int MistralDefaultContextWindowTokens = 32_000;

    /// <summary>Creates a default LM Studio profile (local, no API key required).</summary>
    public static AgentProfile LmStudio(string? model = null) => new()
    {
        DisplayName = "LM Studio Local",
        Provider = ProviderKind.LmStudio,
        BaseUrl = LmStudioBaseUrl,
        Model = model ?? string.Empty,
        CredentialKey = null,
        TimeoutSeconds = 120,
        // Left null on purpose: unlike a cloud model, a local model's actual context window
        // depends entirely on what the user loaded into LM Studio — there's no single documented
        // default to fall back to, and AgentCapabilityTester's best-effort probe is the real source
        // of truth for this field once a test has run.
        ContextWindowTokens = null,
    };

    /// <summary>Creates a default Mistral profile (cloud, API key required).</summary>
    public static AgentProfile Mistral(string? model = null) => new()
    {
        DisplayName = "Mistral Cloud",
        Provider = ProviderKind.Mistral,
        BaseUrl = MistralBaseUrl,
        Model = model ?? MistralDefaultModel,
        CredentialKey = MistralCredentialKey,
        TimeoutSeconds = 60,
        ContextWindowTokens = MistralDefaultContextWindowTokens,
    };

    /// <summary>Creates a generic OpenAI-compatible profile template.</summary>
    public static AgentProfile OpenAiCompatible(string baseUrl, string model, string? credentialKey = null) => new()
    {
        DisplayName = "OpenAI-Compatible",
        Provider = ProviderKind.OpenAiCompatible,
        BaseUrl = baseUrl,
        Model = model,
        CredentialKey = credentialKey,
        TimeoutSeconds = 60,
    };
}
