using Bunit;
using Bunit.JSInterop;
using Microsoft.Extensions.DependencyInjection;
using SwebKit.App.Components.Aks;

namespace SwebKit.App.Tests;

public class AksHelmPanelTests : TestContext
{
    public AksHelmPanelTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;

        var libConfigType = Type.GetType(
            "Microsoft.FluentUI.AspNetCore.Components.LibraryConfiguration, Microsoft.FluentUI.AspNetCore.Components");
        if (libConfigType is not null)
            Services.AddSingleton(libConfigType, Activator.CreateInstance(libConfigType)!);
    }

    [Fact]
    public void AksHelmPanel_NoTargets_RendersNothing()
    {
        var cut = RenderComponent<AksHelmPanel>();

        Assert.Empty(cut.FindAll(".aks-panel-pane"));
    }

    [Fact]
    public void AksHelmPanel_IsOpen_FalseByDefault()
    {
        var cut = RenderComponent<AksHelmPanel>();

        Assert.False(cut.Instance.IsOpen);
    }

    [Fact]
    public void AksHelmPanel_IsHistoryOpen_FalseByDefault()
    {
        var cut = RenderComponent<AksHelmPanel>();

        Assert.False(cut.Instance.IsHistoryOpen);
        Assert.False(cut.Instance.IsValuesOpen);
    }
}
