namespace SwebKit.App.Components.Monitoring;

public enum AlertRuleUiStateKind
{
    Unknown,
    Ok,
    Cooldown,
    Firing,
    Skipped,
    Error,
}

public readonly record struct AlertRuleUiState(
    AlertRuleUiStateKind Kind,
    DateTimeOffset? LastFiredAt,
    DateTimeOffset? LastEvaluatedAt);
