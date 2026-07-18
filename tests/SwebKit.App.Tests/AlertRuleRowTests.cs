using Bunit;
using SwebKit.App.Components.Monitoring;
using SwebKit.Core.Models;

namespace SwebKit.App.Tests;

/// <summary>
/// Tests for <see cref="AlertRuleRow"/> — a single rule row in the Monitoring page's rule list.
/// Pure presentational component: no injected services.
/// </summary>
public sealed class AlertRuleRowTests : TestContext
{
    private static MonitoringAlertRule MakeRule(
        string name = "Prod pod health",
        bool enabled = true,
        AlertRuleSource source = AlertRuleSource.AksPodHealth,
        AlertSeverity severity = AlertSeverity.Warning,
        int intervalSeconds = 60) => new()
        {
            Id = "rule-1",
            Name = name,
            Enabled = enabled,
            Source = source,
            Severity = severity,
            IntervalSeconds = intervalSeconds,
        };

    [Fact]
    public void RendersRuleNameSourceAndSeverity()
    {
        var rule = MakeRule(name: "DLQ depth", source: AlertRuleSource.ServiceBusDlqDepth, severity: AlertSeverity.Critical);

        var cut = RenderComponent<AlertRuleRow>(parameters => parameters
            .Add(p => p.Rule, rule));

        Assert.Contains("DLQ depth", cut.Markup);
        Assert.Contains("Service Bus", cut.Markup);
        Assert.Contains("Critical", cut.Markup);
        Assert.Contains("60 s", cut.Markup);
    }

    [Fact]
    public void DisabledRule_AppliesDisabledClass_AndShowsDisabledMeta()
    {
        var rule = MakeRule(enabled: false);

        var cut = RenderComponent<AlertRuleRow>(parameters => parameters
            .Add(p => p.Rule, rule));

        Assert.Contains("alert-rule-row--disabled", cut.Find(".alert-rule-row").ClassName);
        Assert.Contains("Disabled", cut.Markup);
    }

    [Fact]
    public void TogglingCheckbox_RaisesOnToggle_WithRule()
    {
        var rule = MakeRule();
        MonitoringAlertRule? toggled = null;

        var cut = RenderComponent<AlertRuleRow>(parameters => parameters
            .Add(p => p.Rule, rule)
            .Add(p => p.OnToggle, r => toggled = r));

        cut.Find("input[type=checkbox]").Change(false);

        Assert.Same(rule, toggled);
    }

    [Fact]
    public void ClickingEdit_RaisesOnEdit_WithRule()
    {
        var rule = MakeRule();
        MonitoringAlertRule? edited = null;

        var cut = RenderComponent<AlertRuleRow>(parameters => parameters
            .Add(p => p.Rule, rule)
            .Add(p => p.OnEdit, r => edited = r));

        cut.Find("button.alert-rule-row__action-btn").Click();

        Assert.Same(rule, edited);
    }

    [Fact]
    public void Delete_RequiresConfirmation_BeforeRaisingOnDelete()
    {
        var rule = MakeRule();
        var deleteCount = 0;

        var cut = RenderComponent<AlertRuleRow>(parameters => parameters
            .Add(p => p.Rule, rule)
            .Add(p => p.OnDelete, _ => deleteCount++));

        cut.Find("button.alert-rule-row__action-btn--danger").Click();

        Assert.Contains("Delete?", cut.Markup);
        Assert.Equal(0, deleteCount);

        cut.Find("button.alert-rule-row__action-btn--danger").Click();

        Assert.Equal(1, deleteCount);
    }

    [Fact]
    public void DeleteConfirm_CanBeCancelled_WithoutRaisingOnDelete()
    {
        var rule = MakeRule();
        var deleteCount = 0;

        var cut = RenderComponent<AlertRuleRow>(parameters => parameters
            .Add(p => p.Rule, rule)
            .Add(p => p.OnDelete, _ => deleteCount++));

        cut.Find("button.alert-rule-row__action-btn--danger").Click();
        cut.FindAll("span.alert-rule-row__confirm-delete button")
            .First(b => b.TextContent.Contains("No", StringComparison.Ordinal))
            .Click();

        Assert.DoesNotContain("Delete?", cut.Markup);
        Assert.Equal(0, deleteCount);
    }

    [Theory]
    [InlineData(AlertRuleUiStateKind.Firing, "firing")]
    [InlineData(AlertRuleUiStateKind.Ok, "ok")]
    [InlineData(AlertRuleUiStateKind.Cooldown, "cooldown")]
    [InlineData(AlertRuleUiStateKind.Skipped, "skipped")]
    [InlineData(AlertRuleUiStateKind.Error, "error")]
    public void UiState_RendersMatchingStatusDotClass(AlertRuleUiStateKind kind, string expectedDotClass)
    {
        var rule = MakeRule();
        var uiState = new AlertRuleUiState(kind, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);

        var cut = RenderComponent<AlertRuleRow>(parameters => parameters
            .Add(p => p.Rule, rule)
            .Add(p => p.UiState, uiState));

        Assert.Contains($"alert-rule-row--status-{expectedDotClass}", cut.Find(".alert-rule-row").ClassName);
    }
}
