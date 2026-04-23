using Bunit;
using Bunit.JSInterop;
using Microsoft.Extensions.DependencyInjection;
using SwebKit.App.Components.Storage;
using SwebKit.Core.Domain;

namespace SwebKit.App.Tests;

public class BlobVersionDiffPaneTests : TestContext
{
    public BlobVersionDiffPaneTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;

        var libConfigType = Type.GetType(
            "Microsoft.FluentUI.AspNetCore.Components.LibraryConfiguration, Microsoft.FluentUI.AspNetCore.Components");
        if (libConfigType is not null)
            Services.AddSingleton(libConfigType, Activator.CreateInstance(libConfigType)!);
    }

    [Fact]
    public void BlobVersionDiffPane_NullComparison_ShowsEmptyState()
    {
        var cut = RenderComponent<BlobVersionDiffPane>(ps => ps
            .Add(p => p.Comparison, null));

        Assert.Contains("Select a version", cut.Markup);
    }

    [Fact]
    public void BlobVersionDiffPane_ContentCompareNotPossible_ShowsNotAvailable()
    {
        var comparison = new BlobVersionComparison(
            BaseVersionId: "v1",
            CompareVersionId: null,
            MetadataDiff: BlobMetadataDiff.Compute(
                new Dictionary<string, string>(),
                new Dictionary<string, string>()),
            ContentComparePossible: false,
            BaseSizeBytes: 1024,
            CompareSizeBytes: 2048,
            TextDiff: null);

        var cut = RenderComponent<BlobVersionDiffPane>(ps => ps
            .Add(p => p.Comparison, comparison));

        Assert.Contains("not available", cut.Markup);
    }

    [Fact]
    public void BlobVersionDiffPane_WithMetadataDiff_ShowsAddedAndRemovedLabels()
    {
        var before = new Dictionary<string, string> { { "key1", "val1" } };
        var after = new Dictionary<string, string> { { "key2", "val2" } };
        var diff = BlobMetadataDiff.Compute(before, after);

        var comparison = new BlobVersionComparison(
            BaseVersionId: "v1",
            CompareVersionId: null,
            MetadataDiff: diff,
            ContentComparePossible: false,
            BaseSizeBytes: 1024,
            CompareSizeBytes: 2048,
            TextDiff: null);

        var cut = RenderComponent<BlobVersionDiffPane>(ps => ps
            .Add(p => p.Comparison, comparison));

        Assert.Contains("Added", cut.Markup);
        Assert.Contains("Removed", cut.Markup);
    }
}
