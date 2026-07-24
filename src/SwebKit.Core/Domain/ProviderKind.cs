namespace SwebKit.Core.Domain;

/// <summary>
/// Identifies the LLM provider type for an <see cref="AgentProfile"/>.
/// </summary>
public enum ProviderKind
{
    /// <summary>LM Studio local server (OpenAI-compatible, http://localhost:1234/v1).</summary>
    LmStudio,

    /// <summary>Generic OpenAI-compatible endpoint (user-supplied base URL).</summary>
    OpenAiCompatible,

    /// <summary>Mistral AI cloud API (https://api.mistral.ai/v1).</summary>
    Mistral,
}
