using System.Reflection;
using Bunit;
using Bunit.JSInterop;
using Microsoft.Extensions.DependencyInjection;
using SwebKit.App.Components.Storage;
using SwebKit.Core.Domain;

namespace SwebKit.App.Tests;

file static class StorageMutationTestHelpers
{
    public static void InvokePrivateAsync<TComponent>(IRenderedComponent<TComponent> cut, string methodName, params object?[] args)
        where TComponent : Microsoft.AspNetCore.Components.IComponent
    {
        var method = typeof(TComponent).GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        cut.InvokeAsync(() =>
        {
            _ = method!.Invoke(cut.Instance, args);
            return Task.CompletedTask;
        }).GetAwaiter().GetResult();
    }
}

public class StorageMutationBannerTests : TestContext
{
    public StorageMutationBannerTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;

        var libConfigType = Type.GetType(
            "Microsoft.FluentUI.AspNetCore.Components.LibraryConfiguration, Microsoft.FluentUI.AspNetCore.Components");
        if (libConfigType is not null)
            Services.AddSingleton(libConfigType, Activator.CreateInstance(libConfigType)!);
    }

    [Fact]
    public void StorageMutationBanner_ReadOnly_ShowsInfoText()
    {
        var cut = RenderComponent<StorageMutationBanner>(ps => ps
            .Add(p => p.MutationsEnabled, false)
            .Add(p => p.AccountName, "myaccount"));

        Assert.Contains("Read-only", cut.Markup);
        Assert.DoesNotContain("Mutation mode is active", cut.Markup);
    }

    [Fact]
    public void StorageMutationBanner_MutationsEnabled_ShowsWarningWithAccountName()
    {
        var cut = RenderComponent<StorageMutationBanner>(ps => ps
            .Add(p => p.MutationsEnabled, true)
            .Add(p => p.AccountName, "prodaccount"));

        Assert.Contains("Mutation mode is active", cut.Markup);
        Assert.Contains("prodaccount", cut.Markup);
        Assert.DoesNotContain("Read-only", cut.Markup);
    }
}

public class BlobUploadDialogTests : TestContext
{
    public BlobUploadDialogTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;

        var libConfigType = Type.GetType(
            "Microsoft.FluentUI.AspNetCore.Components.LibraryConfiguration, Microsoft.FluentUI.AspNetCore.Components");
        if (libConfigType is not null)
            Services.AddSingleton(libConfigType, Activator.CreateInstance(libConfigType)!);
    }

    [Fact]
    public void BlobUploadDialog_ConfirmNotInvoked_WhenNoFileSelected()
    {
        bool callbackInvoked = false;

        var cut = RenderComponent<BlobUploadDialog>(ps => ps
            .Add(p => p.Visible, true)
            .Add(p => p.ContainerName, "mycontainer")
            .Add(p => p.OnConfirm, ((BlobUploadOptions Options, Stream FileStream) _) => callbackInvoked = true));

        // ConfirmAsync should no-op because _selectedFile is null
        StorageMutationTestHelpers.InvokePrivateAsync(cut, "ConfirmAsync");

        Assert.False(callbackInvoked);
    }

    [Fact]
    public void BlobUploadDialog_OverwriteWarning_AppearsWhenOverwriteEnabled()
    {
        var cut = RenderComponent<BlobUploadDialog>(ps => ps
            .Add(p => p.Visible, true)
            .Add(p => p.ContainerName, "mycontainer"));

        Assert.DoesNotContain("Warning", cut.Markup);

        var checkbox = cut.Find("input[type=checkbox]");
        checkbox.Change(true);

        cut.WaitForAssertion(() => Assert.Contains("Warning", cut.Markup));
    }
}

public class BlobCopyDialogTests : TestContext
{
    public BlobCopyDialogTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;

        var libConfigType = Type.GetType(
            "Microsoft.FluentUI.AspNetCore.Components.LibraryConfiguration, Microsoft.FluentUI.AspNetCore.Components");
        if (libConfigType is not null)
            Services.AddSingleton(libConfigType, Activator.CreateInstance(libConfigType)!);
    }

    [Fact]
    public void BlobCopyDialog_SourceLabel_ShowsContainerAndBlobName()
    {
        var cut = RenderComponent<BlobCopyDialog>(ps => ps
            .Add(p => p.Visible, true)
            .Add(p => p.SourceContainer, "src-container")
            .Add(p => p.SourceBlobName, "reports/data.csv")
            .Add(p => p.AvailableContainers, ["dest-container"]));

        Assert.Contains("src-container/reports/data.csv", cut.Markup);
    }

    [Fact]
    public void BlobCopyDialog_Confirm_FiresWithCorrectOptions_WhenDestinationFilled()
    {
        BlobCopyOptions? received = null;

        var cut = RenderComponent<BlobCopyDialog>(ps => ps
            .Add(p => p.Visible, true)
            .Add(p => p.SourceContainer, "src")
            .Add(p => p.SourceBlobName, "blob.txt")
            .Add(p => p.AvailableContainers, ["dest"])
            .Add(p => p.OnConfirm, (BlobCopyOptions o) => received = o));

        // _destinationBlobName is initialized to SourceBlobName in OnInitialized.
        // Set the destination container via the select element.
        var select = cut.Find("select");
        select.Change("dest");

        StorageMutationTestHelpers.InvokePrivateAsync(cut, "ConfirmAsync");

        Assert.NotNull(received);
        Assert.Equal("dest", received.DestinationContainer);
        Assert.Equal("blob.txt", received.DestinationBlobName);
        Assert.Equal("src", received.SourceContainer);
        Assert.Equal("blob.txt", received.SourceBlobName);
    }
}
