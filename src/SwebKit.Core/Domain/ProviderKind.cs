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

    /// <summary>External agent subprocess speaking the Agent Client Protocol (JSON-RPC over
    /// stdio) — e.g. Claude via <c>claude-agent-acp</c>, Gemini CLI (<c>gemini --acp</c>). The
    /// profile's <c>Command</c>/<c>Arguments</c> fields, not <c>BaseUrl</c>/<c>Model</c>,
    /// configure this provider.</summary>
    Acp,
}
