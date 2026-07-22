namespace SwebKit.Core.Domain;

/// <summary>
/// Per-user configuration for the AI agent feature.
/// Stored in <see cref="SwebKit.Core.Configuration.UserSettings"/> (user-scoped, not per-workspace).
/// The Mistral API key is NOT stored here — use <c>ICredentialStore</c> key
/// <c>"SwebKit-Agent:Mistral-ApiKey"</c>.
/// </summary>
public sealed class AgentConfig
{
    /// <summary>Show the AI Agent section in the navigation. Default false until the user configures an API key.</summary>
    public bool IsEnabled { get; set; }

    /// <summary>
    /// Override the Mistral model for this user.
    /// Empty string means "use the default from <c>MistralConfig.Model</c>".
    /// </summary>
    public string ModelOverride { get; set; } = string.Empty;

    /// <summary>
    /// Maximum number of messages kept in the conversation history before the oldest
    /// user/assistant pairs are dropped.  Each exchange counts as 2 messages (user + assistant).
    /// Default 20 = 10 back-and-forth exchanges.
    /// </summary>
    /// <remarks>
    /// Phase 1 uses a simple message count. Phase 2 will replace this with a token budget
    /// so large tool payloads are accounted for properly.
    /// </remarks>
    public int MaxHistoryMessages { get; set; } = 20;

    /// <summary>
    /// Percentage of <see cref="MaxHistoryMessages"/> at which the UI shows a
    /// "history almost full" warning.  Value between 0 and 100.
    /// </summary>
    public int HistoryWarningThresholdPercent { get; set; } = 75;
}
