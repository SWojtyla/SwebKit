using SwebKit.Azure.Storage;

namespace SwebKit.Azure.Tests;

/// <summary>
/// Covers the pure decision logic behind blob preview: whether a blob is rendered as text
/// or reported as binary, and the line diff used by the version comparison view.
/// Getting these wrong silently shows an operator garbage — or hides a readable log file.
/// </summary>
public sealed class AzureStorageClientContentTests
{
    // ── IsTextContentType: explicit text types ──

    [Theory]
    [InlineData("text/plain")]
    [InlineData("text/csv")]
    [InlineData("text/html")]
    [InlineData("application/json")]
    [InlineData("application/xml")]
    [InlineData("application/javascript")]
    [InlineData("application/x-javascript")]
    [InlineData("application/yaml")]
    [InlineData("application/x-yaml")]
    [InlineData("application/x-www-form-urlencoded")]
    public void IsTextContentType_KnownTextTypes_AreText(string contentType)
        => Assert.True(AzureStorageClient.IsTextContentType(contentType));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void IsTextContentType_MissingContentType_DefaultsToTextPreview(string? contentType)
        => Assert.True(AzureStorageClient.IsTextContentType(contentType));

    [Theory]
    [InlineData("TEXT/PLAIN")]
    [InlineData("Application/Json")]
    public void IsTextContentType_IsCaseInsensitive(string contentType)
        => Assert.True(AzureStorageClient.IsTextContentType(contentType));

    [Theory]
    [InlineData("text/plain; charset=utf-8")]
    [InlineData("application/json;charset=UTF-8")]
    public void IsTextContentType_IgnoresCharsetParameter(string contentType)
        => Assert.True(AzureStorageClient.IsTextContentType(contentType));

    [Fact]
    public void IsTextContentType_UnknownType_FallsBackToTextPreview()
        => Assert.True(AzureStorageClient.IsTextContentType("application/vnd.acme.thing"));

    // ── IsTextContentType: binary types ──

    [Theory]
    [InlineData("image/png")]
    [InlineData("image/jpeg")]
    [InlineData("video/mp4")]
    [InlineData("audio/mpeg")]
    [InlineData("application/zip")]
    [InlineData("application/gzip")]
    [InlineData("application/x-zip-compressed")]
    [InlineData("application/x-tar")]
    [InlineData("application/x-gzip")]
    [InlineData("application/pdf")]
    [InlineData("application/vnd.ms-excel")]
    [InlineData("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet")]
    [InlineData("application/msword")]
    [InlineData("application/vnd.openxmlformats-officedocument.wordprocessingml.document")]
    public void IsTextContentType_KnownBinaryTypes_AreBinary(string contentType)
        => Assert.False(AzureStorageClient.IsTextContentType(contentType));

    // ── IsTextContentType: the octet-stream extension fallback ──

    [Theory]
    [InlineData("app.log")]
    [InlineData("data.json")]
    [InlineData("notes.md")]
    [InlineData("script.ps1")]
    [InlineData("logs/2024/06/trace.txt")]
    public void IsTextContentType_OctetStream_WithTextExtension_IsText(string blobName)
        => Assert.True(AzureStorageClient.IsTextContentType("application/octet-stream", blobName));

    [Theory]
    [InlineData("backup.bin")]
    [InlineData("archive.7z")]
    [InlineData("noextension")]
    public void IsTextContentType_OctetStream_WithoutTextExtension_IsBinary(string blobName)
        => Assert.False(AzureStorageClient.IsTextContentType("application/octet-stream", blobName));

    [Fact]
    public void IsTextContentType_OctetStream_WithoutBlobName_IsBinary()
    {
        // GetVersionComparisonAsync calls the single-argument overload, so an
        // octet-stream blob is never text-compared even when it is named *.json.
        Assert.False(AzureStorageClient.IsTextContentType("application/octet-stream"));
    }

    [Fact]
    public void IsTextContentType_OctetStream_ExtensionMatchIsCaseInsensitive()
        => Assert.True(AzureStorageClient.IsTextContentType("application/octet-stream", "REPORT.JSON"));

    // ── HasTextExtension ──

    [Theory]
    [InlineData("a.txt")]
    [InlineData("a.log")]
    [InlineData("a.json")]
    [InlineData("a.xml")]
    [InlineData("a.csv")]
    [InlineData("a.yaml")]
    [InlineData("a.yml")]
    [InlineData("a.md")]
    [InlineData("a.html")]
    [InlineData("a.cs")]
    [InlineData("a.tsx")]
    [InlineData("a.svelte")]
    public void HasTextExtension_KnownTextExtensions_AreRecognised(string blobName)
        => Assert.True(AzureStorageClient.HasTextExtension(blobName));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("README")]
    [InlineData("image.png")]
    [InlineData("archive.tar.gz")]
    public void HasTextExtension_OtherNames_AreNotRecognised(string? blobName)
        => Assert.False(AzureStorageClient.HasTextExtension(blobName));

    [Fact]
    public void HasTextExtension_UsesTheLastExtensionOnly()
        => Assert.True(AzureStorageClient.HasTextExtension("archive.tar.log"));

    // ── ProduceSimpleLineDiff ──

    [Fact]
    public void ProduceSimpleLineDiff_IdenticalText_ReportsNoDifferences()
        => Assert.Equal("(no content differences)", AzureStorageClient.ProduceSimpleLineDiff("a\nb\nc", "a\nb\nc"));

    [Fact]
    public void ProduceSimpleLineDiff_EmptyOnBothSides_ReportsNoDifferences()
        => Assert.Equal("(no content differences)", AzureStorageClient.ProduceSimpleLineDiff(string.Empty, string.Empty));

    [Fact]
    public void ProduceSimpleLineDiff_AddedTrailingLine_IsMarkedAsAdded()
        => Assert.Equal("+ c", AzureStorageClient.ProduceSimpleLineDiff("a\nb", "a\nb\nc"));

    [Fact]
    public void ProduceSimpleLineDiff_RemovedTrailingLine_IsMarkedAsRemoved()
        => Assert.Equal("- c", AzureStorageClient.ProduceSimpleLineDiff("a\nb\nc", "a\nb"));

    [Fact]
    public void ProduceSimpleLineDiff_ChangedLine_EmitsBothSides()
        => Assert.Equal("- b\n+ B", AzureStorageClient.ProduceSimpleLineDiff("a\nb\nc", "a\nB\nc"));

    [Fact]
    public void ProduceSimpleLineDiff_OnlyChangedLinesAreEmitted()
    {
        var diff = AzureStorageClient.ProduceSimpleLineDiff("keep\nold\nkeep2", "keep\nnew\nkeep2");

        Assert.DoesNotContain("keep", diff, StringComparison.Ordinal);
    }

    [Fact]
    public void ProduceSimpleLineDiff_IsPositional_NotAnAlignedDiff()
    {
        // Documents current behaviour: inserting a line at the top shifts every
        // subsequent line, so the whole file is reported as changed.
        var diff = AzureStorageClient.ProduceSimpleLineDiff("a\nb", "x\na\nb");

        Assert.Equal("- a\n+ x\n- b\n+ a\n+ b", diff);
    }
}
