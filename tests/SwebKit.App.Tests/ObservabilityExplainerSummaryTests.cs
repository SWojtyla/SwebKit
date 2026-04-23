using Bunit;
using Bunit.JSInterop;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.FluentUI.AspNetCore.Components;
using SwebKit.Core.Models;
using Xunit;

// Alias to resolve ambiguity: the component and model share the same name.
using ExplainerSummaryComponent = SwebKit.App.Components.Observability.ObservabilityExplainerSummary;

namespace SwebKit.App.Tests;

public sealed class ObservabilityExplainerSummaryTests : TestContext
{
    public ObservabilityExplainerSummaryTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;

        var libConfigType = Type.GetType(
            "Microsoft.FluentUI.AspNetCore.Components.LibraryConfiguration, Microsoft.FluentUI.AspNetCore.Components");
        if (libConfigType is not null)
        {
            Services.AddSingleton(libConfigType, Activator.CreateInstance(libConfigType)!);
        }

        Services.AddFluentUIComponents();
    }

    [Fact]
    public void Renders_AnomaliesDetected_WhenHasAnomaliesTrue()
    {
        var summary = MakeSummary(hasAnomalies: true);

        var cut = RenderComponent<ExplainerSummaryComponent>(ps => ps
            .Add(p => p.Summary, summary));

        Assert.Contains("Anomalies detected", cut.Markup);
    }

    [Fact]
    public void Renders_AllClear_WhenHasAnomaliesFalse()
    {
        var summary = MakeSummary(hasAnomalies: false);

        var cut = RenderComponent<ExplainerSummaryComponent>(ps => ps
            .Add(p => p.Summary, summary));

        Assert.Contains("All clear", cut.Markup);
    }

    [Fact]
    public void Renders_TopDependencyName_WhenSet()
    {
        var summary = MakeSummary(hasAnomalies: true, topDependency: "checkout-api");

        var cut = RenderComponent<ExplainerSummaryComponent>(ps => ps
            .Add(p => p.Summary, summary));

        Assert.Contains("checkout-api", cut.Markup);
        Assert.Contains("Top concern", cut.Markup);
    }

    [Fact]
    public void DoesNotRender_InvestigateButton_WhenNoDelegateSet()
    {
        var summary = MakeSummary(hasAnomalies: true);

        var cut = RenderComponent<ExplainerSummaryComponent>(ps => ps
            .Add(p => p.Summary, summary));
        // OnInvestigate not set → HasDelegate is false → button must not appear.
        Assert.DoesNotContain("Investigate", cut.Markup);
    }

    [Fact]
    public void Renders_InvestigateButton_AndInvokesDelegate_WhenDelegateSetAndHasAnomalies()
    {
        var summary = MakeSummary(hasAnomalies: true);
        var invoked = false;

        var cut = RenderComponent<ExplainerSummaryComponent>(ps => ps
            .Add(p => p.Summary, summary)
            .Add(p => p.OnInvestigate, EventCallback.Factory.Create(this, () => { invoked = true; })));

        cut.WaitForAssertion(() => Assert.Contains("Investigate", cut.Markup));

        cut.Find("button").Click();

        Assert.True(invoked);
    }

    private static ObservabilityExplainerSummary MakeSummary(
        bool hasAnomalies,
        string? topDependency = null,
        string? topDimension = null) =>
        new(
            DependencyHealth: new DependencyHealthSummary([], false, 20),
            DimensionPivots: [],
            TopDependencyName: topDependency,
            TopDimensionKey: topDimension,
            HasAnomalies: hasAnomalies);
}
