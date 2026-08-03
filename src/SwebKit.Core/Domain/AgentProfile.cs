namespace SwebKit.Core.Domain;

/// <summary>
/// Result of the last capability test for a profile.
/// </summary>
public enum AgentCapability
{
    /// <summary>Not yet tested.</summary>
    Unknown,

    /// <summary>Server reachable but model does not support tool calling.</summary>
    ChatOnly,

    /// <summary>Model supports native tool calling.</summary>
    ToolCalling,
}

/// <summary>
/// A named LLM provider profile with endpoint, model, credential reference and runtime parameters.
/// Stored in <see cref="AgentConfig.Profiles"/>; the active profile is selected by
/// <see cref="AgentConfig.ActiveProfileId"/>.
/// </summary>
public sealed class AgentProfile
{
    /// <summary>Stable unique identifier for this profile (GUID string).</summary>
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    /// <summary>Human-readable name shown in the UI (e.g. "LM Studio Local", "Mistral Cloud").</summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>Provider type determining presets and behaviour.</summary>
    public ProviderKind Provider { get; set; } = ProviderKind.LmStudio;

    /// <summary>
    /// Base URL of the OpenAI-compatible API endpoint (e.g. <c>http://localhost:1234/v1</c>).
    /// Must not include a trailing slash.
    /// </summary>
    public string BaseUrl { get; set; } = string.Empty;

    /// <summary>Model identifier to use for chat completions (e.g. <c>mistral-medium-latest</c>).</summary>
    public string Model { get; set; } = string.Empty;

    /// <summary>
    /// Logical credential key for the API key (e.g. <c>SwebKit-Agent:Mistral-ApiKey</c>).
    /// The actual secret is resolved via <c>ICredentialStore</c> at runtime.
    /// Empty or null for providers that don't require a key (e.g. LM Studio).
    /// </summary>
    public string? CredentialKey { get; set; }

    /// <summary>Request timeout in seconds. 0 means use the HttpClient default.</summary>
    public int TimeoutSeconds { get; set; } = 60;

    /// <summary>Capability result from the last connection/capability test.</summary>
    public AgentCapability Capability { get; set; } = AgentCapability.Unknown;

    /// <summary>Human-readable diagnostic from the last test (null if no test was run).</summary>
    public string? LastTestDiagnostic { get; set; }

    /// <summary>Whether this profile requires an API key to function.</summary>
    public bool RequiresApiKey => Provider != ProviderKind.LmStudio;
}
