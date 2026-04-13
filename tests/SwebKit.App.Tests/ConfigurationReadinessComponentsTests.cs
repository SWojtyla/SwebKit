using Bunit;
using SwebKit.App.Components.Shared;
using SwebKit.Core.Models;

namespace SwebKit.App.Tests;

public sealed class ConfigurationReadinessComponentsTests : TestContext
{
    [Fact]
    public void DashboardComponent_HidesConfiguredOnlyReport()
    {
        var report = new ConfigurationHealthReport(
            ConfigurationCheckStatus.Configured,
            "Everything is configured.",
            false,
            [],
            [
                new ConfigurationAreaHealth("servicebus", "Service Bus", "servicebus", ConfigurationCheckStatus.Configured, "Configured.", null, [], [], true, null)
            ]);

        var cut = RenderComponent<ConfigurationReadinessDashboard>(ps => ps
            .Add(component => component.Report, report));

        Assert.DoesNotContain("dashboard-readiness", cut.Markup);
    }

    [Fact]
    public void DashboardComponent_WarningAreaStillLinksToSettings()
    {
        Func<string, string> settingsHrefBuilder = section => $"/settings?section={section}";

        var report = new ConfigurationHealthReport(
            ConfigurationCheckStatus.Warning,
            "One area needs attention.",
            false,
            [],
            [
                new ConfigurationAreaHealth("servicebus", "Service Bus", "servicebus", ConfigurationCheckStatus.Warning, "Service Bus live access failed.", "Access denied.", [], [], true,
                    new ConfigurationAreaProbeResult("servicebus", ConfigurationCheckStatus.Warning, "Service Bus live access failed.", "Access denied.", DateTimeOffset.UtcNow, TimeSpan.FromMilliseconds(120)))
            ]);

        var cut = RenderComponent<ConfigurationReadinessDashboard>(ps => ps
            .Add(component => component.Report, report)
            .Add(component => component.SettingsHrefBuilder, settingsHrefBuilder));

        Assert.Equal("/settings?section=servicebus", cut.Find("a.dashboard-readiness-area__link").GetAttribute("href"));
    }

    [Fact]
    public void AreaCard_ShowsLiveCheckPrompt_WhenProbeHasNotRun()
    {
        var report = new ConfigurationHealthReport(
            ConfigurationCheckStatus.Configured,
            "Readiness is configured.",
            false,
            [],
            [
                new ConfigurationAreaHealth("aks", "AKS", "aks", ConfigurationCheckStatus.Configured, "AKS defaults are configured.", "Run live checks to verify runtime access.", [], [], true, null)
            ]);

        var cut = RenderComponent<ConfigurationReadinessAreaCard>(ps => ps
            .Add(component => component.Report, report)
            .Add(component => component.Area, report.Areas[0]));

        Assert.Contains("Run live checks", cut.Markup);
        Assert.Contains("Live verification has not been run yet", cut.Markup);
    }

    [Fact]
    public void AreaCard_ShowsProductionCue_AndProbeMetadata()
    {
        var probe = new ConfigurationAreaProbeResult(
            "devops",
            ConfigurationCheckStatus.Ready,
            "Azure DevOps live access succeeded.",
            "The current PAT completed a read-only projects query.",
            DateTimeOffset.UtcNow,
            TimeSpan.FromMilliseconds(215));

        var report = new ConfigurationHealthReport(
            ConfigurationCheckStatus.Ready,
            "Everything is ready.",
            false,
            [],
            [
                new ConfigurationAreaHealth("devops", "Azure DevOps", "devops", ConfigurationCheckStatus.Ready, "Azure DevOps live access succeeded.", "The PAT reference is available.", [], [], true, probe)
            ],
            new ConfigurationProbeSnapshot(DateTimeOffset.UtcNow.AddSeconds(-1), DateTimeOffset.UtcNow, new Dictionary<string, ConfigurationAreaProbeResult> { ["devops"] = probe }));

        var cut = RenderComponent<ConfigurationReadinessAreaCard>(ps => ps
            .Add(component => component.Report, report)
            .Add(component => component.Area, report.Areas[0])
            .Add(component => component.IsProductionConfiguration, true));

        Assert.Contains("settings-readiness-production", cut.Markup);
        Assert.Contains("Checked", cut.Markup);
        Assert.Contains("Azure DevOps live access succeeded.", cut.Markup);
    }
}