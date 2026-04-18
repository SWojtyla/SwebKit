using Bunit;
using Bunit.JSInterop;
using Microsoft.Extensions.DependencyInjection;
using SwebKit.App.Components.Storage;
using SwebKit.Core.Domain;

namespace SwebKit.App.Tests;

public class BlobRecoveryPanelTests : TestContext
{
    public BlobRecoveryPanelTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;

        var libConfigType = Type.GetType(
            "Microsoft.FluentUI.AspNetCore.Components.LibraryConfiguration, Microsoft.FluentUI.AspNetCore.Components");
        if (libConfigType is not null)
            Services.AddSingleton(libConfigType, Activator.CreateInstance(libConfigType)!);
    }

    [Fact]
    public void BlobRecoveryPanel_CanRestoreFalse_ShowsNotAvailable()
    {
        var capabilities = new StorageCapabilities(
            VersioningEnabled: false,
            SoftDeleteEnabled: false,
            CanUpload: false,
            CanCopy: false,
            CanSetMetadata: false,
            CanRestore: false);

        var cut = RenderComponent<BlobRecoveryPanel>(ps => ps
            .Add(p => p.Capabilities, capabilities)
            .Add(p => p.SelectedVersionId, "v1"));

        Assert.Contains("not available", cut.Markup);
        Assert.DoesNotContain("Restore this version", cut.Markup);
    }

    [Fact]
    public void BlobRecoveryPanel_CanRestoreTrue_WithVersionSelected_ShowsRestoreButton()
    {
        var capabilities = new StorageCapabilities(
            VersioningEnabled: true,
            SoftDeleteEnabled: false,
            CanUpload: false,
            CanCopy: false,
            CanSetMetadata: false,
            CanRestore: true);

        var cut = RenderComponent<BlobRecoveryPanel>(ps => ps
            .Add(p => p.Capabilities, capabilities)
            .Add(p => p.SelectedVersionId, "v1"));

        Assert.Contains("Restore this version", cut.Markup);
    }

    [Fact]
    public void BlobRecoveryPanel_SoftDeleteEnabled_ShowsRecoverButton()
    {
        var capabilities = new StorageCapabilities(
            VersioningEnabled: false,
            SoftDeleteEnabled: true,
            CanUpload: false,
            CanCopy: false,
            CanSetMetadata: false,
            CanRestore: false);

        var cut = RenderComponent<BlobRecoveryPanel>(ps => ps
            .Add(p => p.Capabilities, capabilities)
            .Add(p => p.SelectedVersionId, null));

        Assert.Contains("Recover deleted blob", cut.Markup);
    }
}
