using Bunit;
using SwebKit.Core.Models;
using SwebKit.App.Components.Monitoring;

namespace SwebKit.App.Tests;

/// <summary>
/// Tests for <see cref="MonitoringAlertHistoryPanel"/> — the session-scoped fired-alert history
/// list on the Monitoring page. Pure presentational component: no injected services.
/// </summary>
public sealed class MonitoringAlertHistoryPanelTests : TestContext
{
    private static AlertFiredEvent MakeEvent(
        string ruleId = "r1",
        string ruleName = "Prod pod health",
        AlertRuleSource source = AlertRuleSource.AksPodHealth,
        AlertSeverity severity = AlertSeverity.Warning,
        string message = "3 pods unhealthy") => new(
            RuleId: ruleId,
            RuleName: ruleName,
            Source: source,
            Severity: severity,
            Message: message,
            Detail: "detail",
            FiredAt: DateTimeOffset.UtcNow,
            ProfileName: "default");

    [Fact]
    public void NoEvents_RendersEmptyState_AndNoClearButton()
    {
        var cut = RenderComponent<MonitoringAlertHistoryPanel>(parameters => parameters
            .Add(p => p.Events, new List<AlertFiredEvent>()));

        Assert.Contains("No alerts this session", cut.Markup);
        Assert.Empty(cut.FindAll("button.alert-history-panel__clear-btn"));
    }

    [Fact]
    public void RendersEventCount_RuleName_AndSourceBadge()
    {
        var events = new List<AlertFiredEvent> { MakeEvent(source: AlertRuleSource.RedisMemoryUsage) };

        var cut = RenderComponent<MonitoringAlertHistoryPanel>(parameters => parameters
            .Add(p => p.Events, events));

        Assert.Contains("Prod pod health", cut.Markup);
        Assert.Contains("Redis", cut.Markup);
        Assert.Contains("1", cut.Find(".alert-history-panel__count").TextContent);
    }

    [Fact]
    public void CriticalEvent_GetsCriticalCssClass()
    {
        var events = new List<AlertFiredEvent> { MakeEvent(severity: AlertSeverity.Critical) };

        var cut = RenderComponent<MonitoringAlertHistoryPanel>(parameters => parameters
            .Add(p => p.Events, events));

        Assert.Contains("alert-history-event--critical", cut.Find(".alert-history-event").ClassName);
    }

    [Fact]
    public void LongMessage_IsTruncatedWithEllipsis()
    {
        var longMessage = new string('a', 120);
        var events = new List<AlertFiredEvent> { MakeEvent(message: longMessage) };

        var cut = RenderComponent<MonitoringAlertHistoryPanel>(parameters => parameters
            .Add(p => p.Events, events));

        var messageText = cut.Find(".alert-history-event__message").TextContent;
        Assert.EndsWith("…", messageText, StringComparison.Ordinal);
        Assert.True(messageText.Length < longMessage.Length);
    }

    [Fact]
    public void ClickingClear_RaisesOnClear()
    {
        var events = new List<AlertFiredEvent> { MakeEvent() };
        var clearedCount = 0;

        var cut = RenderComponent<MonitoringAlertHistoryPanel>(parameters => parameters
            .Add(p => p.Events, events)
            .Add(p => p.OnClear, () => clearedCount++));

        cut.Find("button.alert-history-panel__clear-btn").Click();

        Assert.Equal(1, clearedCount);
    }

    [Fact]
    public void ClickingSnooze_RaisesOnMuteRule_WithRuleId()
    {
        var events = new List<AlertFiredEvent> { MakeEvent(ruleId: "rule-42") };
        string? mutedRuleId = null;

        var cut = RenderComponent<MonitoringAlertHistoryPanel>(parameters => parameters
            .Add(p => p.Events, events)
            .Add(p => p.OnMuteRule, id => mutedRuleId = id));

        cut.Find("button.alert-history-event__snooze-btn").Click();

        Assert.Equal("rule-42", mutedRuleId);
    }
}
