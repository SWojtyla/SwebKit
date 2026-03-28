using Bunit;
using Bunit.JSInterop;
using Microsoft.Extensions.DependencyInjection;
using SwebKit.App.Components.Aks;

namespace SwebKit.App.Tests;

public class AksYamlViewerTests : TestContext
{
    public AksYamlViewerTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;

        var libConfigType = Type.GetType(
            "Microsoft.FluentUI.AspNetCore.Components.LibraryConfiguration, Microsoft.FluentUI.AspNetCore.Components");
        if (libConfigType is not null)
            Services.AddSingleton(libConfigType, Activator.CreateInstance(libConfigType)!);
    }

    [Fact]
    public void AksYamlViewer_NoTargetSet_RendersNothing()
    {
        var cut = RenderComponent<AksYamlViewer>();

        Assert.Empty(cut.FindAll(".aks-panel-pane"));
    }

    [Fact]
    public void AksYamlViewer_IsOpen_FalseByDefault()
    {
        var cut = RenderComponent<AksYamlViewer>();

        Assert.False(cut.Instance.IsOpen);
    }

    [Fact]
    public void AksYamlViewer_IsEditing_FalseByDefault()
    {
        var cut = RenderComponent<AksYamlViewer>();

        Assert.False(cut.Instance.IsEditing);
    }
}
