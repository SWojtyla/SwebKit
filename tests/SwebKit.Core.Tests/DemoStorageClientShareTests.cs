using SwebKit.Core.Services;

namespace SwebKit.Core.Tests;

/// <summary>
/// Demo-mode Azure Files coverage: the seeded shares, per-directory listing semantics,
/// and the file detail/content/SAS reads the Storage page's share panes depend on.
/// </summary>
public class DemoStorageClientShareTests
{
    private static DemoStorageClient CreateClient() => new();

    [Fact]
    public async Task ListFileSharesAsync_ReturnsSeededShares()
    {
        var client = CreateClient();

        var shares = await client.ListFileSharesAsync();

        Assert.NotEmpty(shares);
        Assert.Contains(shares, s => s.Name == "team-shared");
        Assert.All(shares, s =>
        {
            Assert.False(string.IsNullOrWhiteSpace(s.Name));
            Assert.True(s.QuotaGiB > 0);
        });
    }

    [Fact]
    public async Task ListShareEntriesAsync_Root_ReturnsTopLevelOnly()
    {
        var client = CreateClient();

        var page = await client.ListShareEntriesAsync("team-shared", "");

        Assert.Contains(page.Items, e => e.Name == "docs" && e.IsDirectory);
        Assert.Contains(page.Items, e => e.Name == "readme.md" && !e.IsDirectory);
        // Nested paths must not leak into the root listing.
        Assert.DoesNotContain(page.Items, e => e.Name.Contains('/'));
    }

    [Fact]
    public async Task ListShareEntriesAsync_Subdirectory_ReturnsItsChildren()
    {
        var client = CreateClient();

        var page = await client.ListShareEntriesAsync("team-shared", "docs");

        Assert.NotEmpty(page.Items);
        Assert.All(page.Items, e => Assert.StartsWith("docs/", e.Name));
        Assert.DoesNotContain(page.Items, e => e.Name == "docs");
        Assert.Contains(page.Items, e => e.Name == "docs/onboarding.md" && !e.IsDirectory);
    }

    [Fact]
    public async Task ListShareEntriesAsync_UnknownShare_ReturnsEmptyPage()
    {
        var client = CreateClient();

        var page = await client.ListShareEntriesAsync("no-such-share", "");

        Assert.Empty(page.Items);
        Assert.Null(page.ContinuationToken);
    }

    [Fact]
    public async Task GetShareFilePropertiesAsync_ReturnsFileMetadata()
    {
        var client = CreateClient();

        var props = await client.GetShareFilePropertiesAsync("team-shared", "readme.md");

        Assert.Equal("readme.md", props.Name);
        Assert.True(props.SizeBytes > 0);
        Assert.False(string.IsNullOrWhiteSpace(props.ContentType));
    }

    [Fact]
    public async Task GetShareFilePropertiesAsync_UnknownFile_Throws()
    {
        var client = CreateClient();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => client.GetShareFilePropertiesAsync("team-shared", "missing.txt"));
    }

    [Fact]
    public async Task GetShareFileContentAsync_TextFile_ReturnsPreviewText()
    {
        var client = CreateClient();

        var content = await client.GetShareFileContentAsync("team-shared", "readme.md");

        Assert.False(content.IsBinary);
        Assert.False(string.IsNullOrWhiteSpace(content.Content));
        Assert.Equal("team-shared", content.ShareName);
        Assert.Equal("readme.md", content.Path);
    }

    [Fact]
    public async Task GetShareFileContentAsync_BinaryFile_MarksBinaryWithoutContent()
    {
        var client = CreateClient();

        var content = await client.GetShareFileContentAsync("team-shared", "media/hero.png");

        Assert.True(content.IsBinary);
        Assert.Equal(string.Empty, content.Content);
    }

    [Fact]
    public async Task GetShareFileSasUrlAsync_ReturnsReadOnlyFileUrl()
    {
        var client = CreateClient();

        var url = await client.GetShareFileSasUrlAsync("team-shared", "readme.md", TimeSpan.FromMinutes(30));

        Assert.Contains("team-shared", url);
        Assert.Contains("readme.md", url);
        Assert.Contains("sp=r", url);
    }
}
