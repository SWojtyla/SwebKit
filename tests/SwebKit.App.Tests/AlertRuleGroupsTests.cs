using Bunit;
using SwebKit.App.Components.Monitoring;
using SwebKit.Core.Models;

namespace SwebKit.App.Tests;

/// <summary>
/// Tests for <see cref="AlertRuleGroups"/> — groups monitoring rules by source (AKS/Service
/// Bus/Redis/Storage) with collapsible headers. Pure presentational component: no injected
/// services.
/// </summary>
public sealed class AlertRuleGroupsTests : TestContext
{
    private static MonitoringAlertRule MakeRule(string id, string name, AlertRuleSource source) => new()
    {
        Id = id,
        Name = name,
        Source = source,
        Enabled = true,
    };

    [Fact]
    public void NoRules_RendersEmptyState()
    {
        var cut = RenderComponent<AlertRuleGroups>(parameters => parameters
            .Add(p => p.Rules, new List<MonitoringAlertRule>()));

        Assert.Contains("No alert rules", cut.Markup);
    }

    [Fact]
    public void RendersAllFourGroupHeaders_WithCorrectRuleCounts()
    {
        var rules = new List<MonitoringAlertRule>
        {
            MakeRule("r1", "AKS rule", AlertRuleSource.AksPodHealth),
            MakeRule("r2", "SB rule 1", AlertRuleSource.ServiceBusDlqDepth),
            MakeRule("r3", "SB rule 2", AlertRuleSource.ServiceBusActiveDepth),
        };

        var cut = RenderComponent<AlertRuleGroups>(parameters => parameters
            .Add(p => p.Rules, rules));

        Assert.Contains("AKS", cut.Markup);
        Assert.Contains("Service Bus", cut.Markup);
        Assert.Contains("Redis", cut.Markup);
        Assert.Contains("Storage", cut.Markup);

        // Groups start expanded by default, so rows should be visible immediately.
        Assert.Contains("AKS rule", cut.Markup);
        Assert.Contains("SB rule 1", cut.Markup);
        Assert.Contains("SB rule 2", cut.Markup);
    }

    [Fact]
    public void CollapsingGroup_HidesItsRows_AndTogglingBackShowsThemAgain()
    {
        var rules = new List<MonitoringAlertRule> { MakeRule("r1", "AKS rule", AlertRuleSource.AksPodHealth) };

        var cut = RenderComponent<AlertRuleGroups>(parameters => parameters
            .Add(p => p.Rules, rules));

        Assert.Contains("AKS rule", cut.Markup);

        cut.Find(".alert-rule-group__header").Click();
        Assert.DoesNotContain("AKS rule", cut.Markup);

        cut.Find(".alert-rule-group__header").Click();
        Assert.Contains("AKS rule", cut.Markup);
    }

    [Fact]
    public void FiringRuleInGroup_ShowsFiringBadge_InsteadOfRuleCount()
    {
        var rules = new List<MonitoringAlertRule> { MakeRule("r1", "AKS rule", AlertRuleSource.AksPodHealth) };
        var uiStates = new Dictionary<string, AlertRuleUiState>
        {
            ["r1"] = new AlertRuleUiState(AlertRuleUiStateKind.Firing, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow),
        };

        var cut = RenderComponent<AlertRuleGroups>(parameters => parameters
            .Add(p => p.Rules, rules)
            .Add(p => p.UiStates, uiStates));

        Assert.Contains("1 firing", cut.Markup);
        Assert.Contains("alert-rule-group__badge--firing", cut.Markup);
    }

    [Fact]
    public void ToggleCallback_PropagatesFromNestedAlertRuleRow()
    {
        var rule = MakeRule("r1", "AKS rule", AlertRuleSource.AksPodHealth);
        MonitoringAlertRule? toggled = null;

        var cut = RenderComponent<AlertRuleGroups>(parameters => parameters
            .Add(p => p.Rules, [rule])
            .Add(p => p.OnToggle, r => toggled = r));

        cut.Find("input[type=checkbox]").Change(false);

        Assert.Same(rule, toggled);
    }
}
