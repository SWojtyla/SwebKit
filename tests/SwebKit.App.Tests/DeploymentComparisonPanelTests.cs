using Bunit;
using Bunit.JSInterop;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.FluentUI.AspNetCore.Components;
using SwebKit.Core.Models;
using Xunit;

using DeploymentComparisonPanelComponent = SwebKit.App.Components.Observability.DeploymentComparisonPanel;

namespace SwebKit.App.Tests;

public sealed class DeploymentComparisonPanelTests : TestContext
{
    public DeploymentComparisonPanelTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;

        var libConfigType = Type.GetType(
            "Microsoft.FluentUI.AspNetCore.Components.LibraryConfiguration, Microsoft.FluentUI.AspNetCore.Components");
        if (libConfigType is not null)
            Services.AddSingleton(libConfigType, Activator.CreateInstance(libConfigType)!);

        Services.AddFluentUIComponents();
    }

    [Fact]
    public void Shows_SelectAnchorPrompt_WhenNoAnchorSelected()
    {
        var cut = RenderComponent<DeploymentComparisonPanelComponent>(ps => ps
            .Add(p => p.AvailableAnchors, Array.Empty<DeploymentAnchor>()));

        Assert.Contains("Select a release anchor above to compare telemetry before and after deployment.", cut.Markup);
    }

    [Fact]
    public void Shows_AnchorOptions_WhenAnchorsProvided()
    {
        var anchors = new[]
        {
            new DeploymentAnchor(Guid.NewGuid(), "v1.2.3", DateTimeOffset.UtcNow.AddDays(-2)),
            new DeploymentAnchor(Guid.NewGuid(), "v1.2.4", DateTimeOffset.UtcNow.AddDays(-1)),
        };

        var cut = RenderComponent<DeploymentComparisonPanelComponent>(ps => ps
            .Add(p => p.AvailableAnchors, anchors));

        Assert.Contains("v1.2.3", cut.Markup);
        Assert.Contains("v1.2.4", cut.Markup);
    }

    [Fact]
    public void Shows_RegressionDetected_WhenHasRegressionTrue()
    {
        var summary = MakeSummary(hasRegression: true);

        var cut = RenderComponent<DeploymentComparisonPanelComponent>(ps => ps
            .Add(p => p.Summary, summary));

        Assert.Contains("Regression detected", cut.Markup);
    }

    [Fact]
    public void Shows_NoRegression_WhenHasRegressionFalse()
    {
        var summary = MakeSummary(hasRegression: false);

        var cut = RenderComponent<DeploymentComparisonPanelComponent>(ps => ps
            .Add(p => p.Summary, summary));

        Assert.Contains("No regression", cut.Markup);
    }

    [Fact]
    public void Shows_MetricTableRows_WhenSummaryProvided()
    {
        var deltas = new[]
        {
            new MetricDelta("Error rate", 0.01, 0.02, 100.0),
            new MetricDelta("P95 latency", 200, 250, 25.0),
        };
        var summary = MakeSummary(hasRegression: false, deltas: deltas);

        var cut = RenderComponent<DeploymentComparisonPanelComponent>(ps => ps
            .Add(p => p.Summary, summary));

        Assert.Contains("Error rate", cut.Markup);
        Assert.Contains("P95 latency", cut.Markup);
    }

    private static DeploymentComparisonSummary MakeSummary(
        bool hasRegression,
        IReadOnlyList<MetricDelta>? deltas = null)
    {
        var anchor = new DeploymentAnchor(Guid.NewGuid(), "test-release", DateTimeOffset.UtcNow.AddDays(-1));
        var before = new TimeRange(DateTimeOffset.UtcNow.AddHours(-8), DateTimeOffset.UtcNow.AddHours(-4));
        var after = new TimeRange(DateTimeOffset.UtcNow.AddHours(-4), DateTimeOffset.UtcNow);

        return new DeploymentComparisonSummary(
            Anchor: anchor,
            BeforeWindow: before,
            AfterWindow: after,
            Deltas: deltas ?? [],
            HasRegression: hasRegression);
    }
}
