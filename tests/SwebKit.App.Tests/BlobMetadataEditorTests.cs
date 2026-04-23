using Bunit;
using Bunit.JSInterop;
using Microsoft.Extensions.DependencyInjection;
using SwebKit.App.Components.Storage;
using SwebKit.Core.Domain;

namespace SwebKit.App.Tests;

public class BlobMetadataEditorTests : TestContext
{
    public BlobMetadataEditorTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;

        var libConfigType = Type.GetType(
            "Microsoft.FluentUI.AspNetCore.Components.LibraryConfiguration, Microsoft.FluentUI.AspNetCore.Components");
        if (libConfigType is not null)
            Services.AddSingleton(libConfigType, Activator.CreateInstance(libConfigType)!);
    }

    [Fact]
    public void BlobMetadataEditor_ReadOnly_DoesNotShowSaveButton()
    {
        var cut = RenderComponent<BlobMetadataEditor>(ps => ps
            .Add(p => p.CurrentMetadata, new Dictionary<string, string> { { "key1", "val1" } })
            .Add(p => p.ReadOnly, true));

        Assert.DoesNotContain("Save", cut.Markup);
    }

    [Fact]
    public void BlobMetadataEditor_AddKey_ShowsAddedLabel()
    {
        var cut = RenderComponent<BlobMetadataEditor>(ps => ps
            .Add(p => p.CurrentMetadata, new Dictionary<string, string>())
            .Add(p => p.ReadOnly, false));

        cut.Find(".blob-metadata-editor__add-btn").Click();

        // Re-find after each Change to avoid stale element references (BL-2 / bUnit render cycle)
        cut.FindAll("input[type=text]")[0].Change("newkey");
        cut.FindAll("input[type=text]")[1].Change("newval");

        Assert.Contains("Added", cut.Markup);
    }

    [Fact]
    public void BlobMetadataEditor_RemoveKey_ShowsRemovedLabel()
    {
        var cut = RenderComponent<BlobMetadataEditor>(ps => ps
            .Add(p => p.CurrentMetadata, new Dictionary<string, string> { { "key1", "val1" } })
            .Add(p => p.ReadOnly, false));

        cut.Find(".blob-metadata-editor__remove-btn").Click();

        Assert.Contains("Removed", cut.Markup);
    }

    [Fact]
    public void BlobMetadataEditor_ReadOnly_DoesNotShowAddKeyButton()
    {
        var cut = RenderComponent<BlobMetadataEditor>(ps => ps
            .Add(p => p.CurrentMetadata, new Dictionary<string, string> { { "key1", "val1" } })
            .Add(p => p.ReadOnly, true));

        Assert.Empty(cut.FindAll(".blob-metadata-editor__add-btn"));
    }
}
