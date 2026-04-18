using SwebKit.Core.Domain;
using SwebKit.Core.Services;

namespace SwebKit.Core.Tests;

public class StorageMutationContractTests
{
    private static DemoStorageClient CreateClient() => new();

    // --- Wave 1 ---

    [Fact]
    public async Task UploadBlobAsync_DemoStub_ReturnsSuccess()
    {
        var client = CreateClient();
        var options = new BlobUploadOptions("exports", "test-upload.json", Overwrite: true, ContentType: "application/json");
        using var stream = new MemoryStream([1, 2, 3]);

        var result = await client.UploadBlobAsync(options, stream);

        Assert.True(result.Success);
        Assert.Equal("exports/test-upload.json", result.ResultBlobPath);
    }

    [Fact]
    public async Task CopyBlobAsync_DemoStub_ReturnsSuccess()
    {
        var client = CreateClient();
        var options = new BlobCopyOptions("exports", "2026-03-21-report.csv", "fixtures", "copy-of-report.csv");

        var result = await client.CopyBlobAsync(options);

        Assert.True(result.Success);
        Assert.Equal("fixtures/copy-of-report.csv", result.ResultBlobPath);
    }

    [Fact]
    public async Task GetStorageCapabilitiesAsync_DemoStub_ReturnsVersioningEnabled()
    {
        var client = CreateClient();

        var caps = await client.GetStorageCapabilitiesAsync();

        Assert.True(caps.VersioningEnabled);
        Assert.True(caps.SoftDeleteEnabled);
        Assert.True(caps.CanUpload);
        Assert.True(caps.CanRestore);
    }

    // --- Wave 2 ---

    [Fact]
    public void BlobMetadataDiff_Compute_IdentifiesAddedRemovedChanged()
    {
        var before = new Dictionary<string, string>
        {
            ["author"] = "alice",
            ["env"] = "staging",
            ["to-remove"] = "old-value"
        };
        var after = new Dictionary<string, string>
        {
            ["author"] = "bob",       // changed
            ["env"] = "staging",      // unchanged
            ["added-key"] = "new"     // added
            // "to-remove" is absent → removed
        };

        var diff = BlobMetadataDiff.Compute(before, after);

        Assert.Contains("added-key", diff.AddedKeys);
        Assert.Contains("to-remove", diff.RemovedKeys);
        Assert.Contains("author", diff.ChangedKeys);
        Assert.DoesNotContain("env", diff.ChangedKeys);
        Assert.DoesNotContain("env", diff.AddedKeys);
        Assert.DoesNotContain("env", diff.RemovedKeys);
    }

    [Fact]
    public async Task GetVersionComparisonAsync_DemoStub_ReturnsContentComparePossibleWithTextDiff()
    {
        var client = CreateClient();

        var comparison = await client.GetVersionComparisonAsync(
            "configs", "app-settings.json", "2026-03-15T08:30:00Z");

        Assert.True(comparison.ContentComparePossible);
        Assert.NotNull(comparison.TextDiff);
        Assert.Contains("version", comparison.MetadataDiff.ChangedKeys);
    }

    [Fact]
    public async Task SetBlobMetadataAsync_DemoStub_ReturnsSuccess()
    {
        var client = CreateClient();
        var metadata = new Dictionary<string, string> { ["env"] = "test", ["author"] = "ci" };

        var result = await client.SetBlobMetadataAsync("configs", "app-settings.json", metadata);

        Assert.True(result.Success);
        Assert.Equal("configs/app-settings.json", result.ResultBlobPath);
    }

    // --- Wave 3 ---

    [Fact]
    public async Task RestoreBlobVersionAsync_DemoStub_ReturnsRestored()
    {
        var client = CreateClient();

        var result = await client.RestoreBlobVersionAsync("configs", "app-settings.json", "2026-03-15T08:30:00Z");

        Assert.Equal(BlobRecoveryState.Restored, result.State);
        Assert.Equal("configs/app-settings.json", result.ResultBlobPath);
    }

    [Fact]
    public async Task UndeleteBlobAsync_DemoStub_ReturnsUndeleted()
    {
        var client = CreateClient();

        var result = await client.UndeleteBlobAsync("configs", "app-settings.json");

        Assert.Equal(BlobRecoveryState.Undeleted, result.State);
        Assert.Equal("configs/app-settings.json", result.ResultBlobPath);
    }
}
