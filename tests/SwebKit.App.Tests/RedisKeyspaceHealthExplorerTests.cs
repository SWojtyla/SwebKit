using Bunit;
using Bunit.JSInterop;
using Microsoft.Extensions.DependencyInjection;
using SwebKit.App.Components.Redis;
using SwebKit.Core.Models;

namespace SwebKit.App.Tests;

public class RedisKeyspaceHealthExplorerTests : TestContext
{
    public RedisKeyspaceHealthExplorerTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;

        var libConfigType = Type.GetType(
            "Microsoft.FluentUI.AspNetCore.Components.LibraryConfiguration, Microsoft.FluentUI.AspNetCore.Components");
        if (libConfigType is not null)
            Services.AddSingleton(libConfigType, Activator.CreateInstance(libConfigType)!);
    }

    [Fact]
    public void RendersEmptyState_WhenNoReport()
    {
        var cut = RenderComponent<RedisKeyspaceHealthExplorer>(parameters => parameters
            .Add(component => component.Report, null)
            .Add(component => component.IsLoading, false));

        Assert.Contains("Run Analyze", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void OpenKeyButton_InvokesFindingSelection()
    {
        string? selectedKey = null;
        var report = BuildReport(
        [
            new RedisHealthFinding
            {
                EntityType = RedisHealthEntityType.Key,
                RiskType = RedisHealthRiskType.NoTtl,
                Severity = RedisHealthSeverity.Warning,
                Target = "user:1001",
                DrillKey = "user:1001",
                Reason = "Key has no TTL.",
            },
        ]);

        var cut = RenderComponent<RedisKeyspaceHealthExplorer>(parameters => parameters
            .Add(component => component.Report, report)
            .Add(component => component.OnFindingSelected, (string key) => selectedKey = key));

        var openButton = cut.FindAll("fluent-button")
            .First(button => button.TextContent.Contains("Open key", StringComparison.Ordinal));

        openButton.Click();

        Assert.Equal("user:1001", selectedKey);
    }

    [Fact]
    public void SeverityCardFilter_UpdatesVisibleRows()
    {
        var report = BuildReport(
        [
            new RedisHealthFinding
            {
                EntityType = RedisHealthEntityType.Key,
                RiskType = RedisHealthRiskType.NoTtl,
                Severity = RedisHealthSeverity.Critical,
                Target = "critical:key",
                DrillKey = "critical:key",
                Reason = "critical",
            },
            new RedisHealthFinding
            {
                EntityType = RedisHealthEntityType.Key,
                RiskType = RedisHealthRiskType.OversizedValue,
                Severity = RedisHealthSeverity.Info,
                Target = "info:key",
                DrillKey = "info:key",
                Reason = "info",
            },
        ]);

        var cut = RenderComponent<RedisKeyspaceHealthExplorer>(parameters => parameters
            .Add(component => component.Report, report));

        Assert.Equal(2, cut.FindAll("tbody tr").Count);

        cut.Find("button.summary-card.info").Click();

        Assert.Single(cut.FindAll("tbody tr"));
        Assert.Contains("info:key", cut.Markup, StringComparison.Ordinal);
    }

    private static RedisKeyspaceHealthReport BuildReport(IReadOnlyList<RedisHealthFinding> findings)
    {
        return new RedisKeyspaceHealthReport
        {
            LoadedKeyCount = 10,
            EstimatedKeyCount = 12,
            CoveragePercent = 83.3,
            IsPartialCoverage = true,
            ConfidenceLabel = "Medium",
            HotKeySignalsAvailable = true,
            KeysWithHotKeySignal = 4,
            KeysWithoutHotKeySignal = 6,
            CriticalCount = findings.Count(finding => finding.Severity == RedisHealthSeverity.Critical),
            WarningCount = findings.Count(finding => finding.Severity == RedisHealthSeverity.Warning),
            InfoCount = findings.Count(finding => finding.Severity == RedisHealthSeverity.Info),
            Findings = findings,
        };
    }
}
