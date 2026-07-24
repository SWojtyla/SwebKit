using Bunit;
using SwebKit.App.Components.Shared;

namespace SwebKit.App.Tests;

public sealed class LazyPanelTests : TestContext
{
    [Fact]
    public void PreviouslyRenderedPanel_UpdatesHiddenState_WhenActivationChanges()
    {
        var cut = RenderComponent<LazyPanel>(parameters => parameters
            .Add(component => component.IsActive, true)
            .AddChildContent("<span>resource grid</span>"));

        Assert.False(cut.Find(".lazy-panel").HasAttribute("hidden"));

        cut.SetParametersAndRender(parameters => parameters
            .Add(component => component.IsActive, false)
            .AddChildContent("<span>resource grid</span>"));

        Assert.True(cut.Find(".lazy-panel").HasAttribute("hidden"));
        Assert.Contains("resource grid", cut.Markup, StringComparison.Ordinal);

        cut.SetParametersAndRender(parameters => parameters
            .Add(component => component.IsActive, true)
            .AddChildContent("<span>resource grid</span>"));

        Assert.False(cut.Find(".lazy-panel").HasAttribute("hidden"));
    }
}
