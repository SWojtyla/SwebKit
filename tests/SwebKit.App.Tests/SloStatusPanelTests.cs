using Bunit;
using Bunit.JSInterop;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.FluentUI.AspNetCore.Components;
using SwebKit.Core.Models;
using Xunit;

using SloStatusPanelComponent = SwebKit.App.Components.Observability.SloStatusPanel;

namespace SwebKit.App.Tests;

public sealed class SloStatusPanelTests : TestContext
{
    public SloStatusPanelTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;

        var libConfigType = Type.GetType(
            "Microsoft.FluentUI.AspNetCore.Components.LibraryConfiguration, Microsoft.FluentUI.AspNetCore.Components");
        if (libConfigType is not null)
            Services.AddSingleton(libConfigType, Activator.CreateInstance(libConfigType)!);

        Services.AddFluentUIComponents();
    }

    [Fact]
    public void Shows_NoSloDefinitions_WhenHasDefinitionsFalse()
    {
        var cut = RenderComponent<SloStatusPanelComponent>(ps => ps
            .Add(p => p.HasDefinitions, false));

        Assert.Contains("No SLO definitions configured", cut.Markup);
    }

    [Fact]
    public void Shows_AllSlosMet_WhenNoBreachOrRisk()
    {
        var summary = MakeSummary(anyBreached: false, anyAtRisk: false);

        var cut = RenderComponent<SloStatusPanelComponent>(ps => ps
            .Add(p => p.HasDefinitions, true)
            .Add(p => p.Summary, summary));

        Assert.Contains("All SLOs met", cut.Markup);
    }

    [Fact]
    public void Shows_SloBreached_WhenAnyBreachedTrue()
    {
        var summary = MakeSummary(anyBreached: true, anyAtRisk: false);

        var cut = RenderComponent<SloStatusPanelComponent>(ps => ps
            .Add(p => p.HasDefinitions, true)
            .Add(p => p.Summary, summary));

        Assert.Contains("SLO breached", cut.Markup);
    }

    [Fact]
    public void Shows_SloAtRisk_WhenAnyAtRiskTrue()
    {
        var summary = MakeSummary(anyBreached: false, anyAtRisk: true);

        var cut = RenderComponent<SloStatusPanelComponent>(ps => ps
            .Add(p => p.HasDefinitions, true)
            .Add(p => p.Summary, summary));

        Assert.Contains("SLO at risk", cut.Markup);
    }

    private static SloStatusSummary MakeSummary(bool anyBreached, bool anyAtRisk)
    {
        var def = new SloDefinition
        {
            Name = "Error Rate",
            Metric = SloMetric.FailureRate,
            Target = 0.01,
        };

        var state = anyBreached ? SloState.Breached : (anyAtRisk ? SloState.AtRisk : SloState.Met);
        var entries = new[] { new SloStatusEntry(def, 0.02, state) };

        return new SloStatusSummary(entries, anyBreached, anyAtRisk);
    }
}
