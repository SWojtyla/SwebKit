using System.Text.Json;

namespace SwebKit.Core.Models;

/// <summary>
/// One persisted thumbs-down record (agent-colleague item 5), stored in
/// <c>agent-feedback.json</c> via <c>AgentFeedbackRepository</c> and listed/exported from
/// Settings → AI Agent for prompt-tuning regression cases.
///
/// Everything on here is post-redaction: string fields are truncated to
/// <c>AgentFeedbackRedactor.MaxFieldChars</c>, <see cref="ScreenStateDigest"/> has been through
/// the shared sensitive-key denylist (<c>Security.SensitiveDataKeys</c>), and pending-action
/// payloads are structurally absent — the exchange buffer never captures them, so there is
/// nothing here to redact away. Lives in Core (not SwebKit.Agents) because the repository
/// pattern there can't depend on Agents; <see cref="AgentFeedbackStep"/> is a Core-local,
/// flattened copy of the chat-step shape for that reason.
/// </summary>
public sealed class AgentFeedbackEntry
{
    /// <summary>This entry's own id (the feedback record), distinct from
    /// <see cref="ExchangeId"/> (the chat turn it rates).</summary>
    public required string Id { get; set; }

    /// <summary>The <c>exchangeId</c> from the terminal Done stream event the user reacted to.</summary>
    public required string ExchangeId { get; set; }

    /// <summary>"down" today; the field exists so a future thumbs-up needs no schema change.</summary>
    public string Sentiment { get; set; } = "down";

    public string? Comment { get; set; }
    public List<string> Tags { get; set; } = [];
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>False when the exchange ring buffer had already evicted (or never saw) the id —
    /// the thumbs-down is still recorded, just without the exchange context below.</summary>
    public bool ExchangeFound { get; set; }

    // ── Exchange context (null/empty when ExchangeFound is false) ──

    public string? SessionId { get; set; }
    public string? FeatureArea { get; set; }
    public string? Mode { get; set; }
    public string? Scope { get; set; }
    public string? UserMessage { get; set; }
    public string? AssistantText { get; set; }
    public List<AgentFeedbackStep> Steps { get; set; } = [];
    public List<string> ToolsUsed { get; set; } = [];

    /// <summary>Redacted screen-state digest captured when the reply landed
    /// (<c>{route, featureArea, capturedAt, entityIds, snapshot}</c>) — the "what the user was
    /// looking at" half of the regression case.</summary>
    public JsonElement? ScreenStateDigest { get; set; }
}

/// <summary>Flattened copy of one <c>AgentChatStep</c> for persistence — type, tool name, and
/// the curated non-sensitive summary only (no step output, no elapsed-precision worth keeping).</summary>
public sealed class AgentFeedbackStep
{
    public string? Type { get; set; }
    public string? ToolName { get; set; }
    public string? Summary { get; set; }
    public bool IsFailure { get; set; }
}
