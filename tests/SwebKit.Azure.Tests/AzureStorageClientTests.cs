using SwebKit.Azure.Storage;
using SwebKit.Core.Abstractions;
using SwebKit.Core.Domain;

namespace SwebKit.Azure.Tests;

public class AzureStorageClientTests
{
    // UT-1 equivalent: connection string mode with a credential store that returns a valid connection string
    [Fact]
    public void Constructor_ConnectionStringMode_CredentialFound_DoesNotThrow()
    {
        var config = new StorageConfig
        {
            UseAad = false,
            ConnectionStringRef = "storage:myaccount"
        };

        var client = new AzureStorageClient(config, new FakeCredentialStore());
        Assert.NotNull(client);
        Assert.Same(config, client.Config);
    }

    // UT-3: UseAad = false and ConnectionStringRef = null → InvalidOperationException
    [Fact]
    public void Constructor_ConnectionStringMode_NullRef_ThrowsInvalidOperation()
    {
        var config = new StorageConfig
        {
            UseAad = false,
            ConnectionStringRef = null
        };

        Assert.Throws<InvalidOperationException>(() => new AzureStorageClient(config, new FakeCredentialStore()));
    }

    // Guard: UseAad = true with empty AccountName → InvalidOperationException
    [Fact]
    public void Constructor_AadMode_EmptyAccountName_ThrowsInvalidOperation()
    {
        var config = new StorageConfig
        {
            UseAad = true,
            AccountName = string.Empty
        };

        Assert.Throws<InvalidOperationException>(() => new AzureStorageClient(config, new FakeCredentialStore()));
    }

    // Guard: credential ref is present but credential store returns null → InvalidOperationException
    [Fact]
    public void Constructor_ConnectionStringMode_CredentialMissing_ThrowsInvalidOperation()
    {
        var config = new StorageConfig
        {
            UseAad = false,
            ConnectionStringRef = "storage:missing"
        };

        var ex = Assert.Throws<InvalidOperationException>(() =>
            new AzureStorageClient(config, new EmptyCredentialStore()));
        Assert.Contains("storage:missing", ex.Message);
    }

    [Fact]
    public void IsExactVersionMatch_RequiresAnExactBlobName()
    {
        Assert.True(AzureStorageClient.IsExactVersionMatch("app-settings.json", "app-settings.json"));
        Assert.False(AzureStorageClient.IsExactVersionMatch("app-settings.json", "app-settings.json.backup"));
        Assert.False(AzureStorageClient.IsExactVersionMatch("folder/app-settings.json", "folder/app-settings.json/child"));
    }

    [Fact]
    public void IsTextContentType_UsesBlobNameFallbackForOctetStream()
    {
        Assert.True(AzureStorageClient.IsTextContentType("application/octet-stream", "app-settings.json"));
        Assert.True(AzureStorageClient.IsTextContentType("application/octet-stream", "settings.yaml"));
        Assert.False(AzureStorageClient.IsTextContentType("application/octet-stream", "bundle.bin"));
    }

    /// <summary>
    /// Provides a valid-looking Azure Storage connection string so that
    /// BlobServiceClient construction succeeds without network access.
    /// Convert.ToBase64String(new byte[64]) produces the correct 88-char base64
    /// string for a 64-byte storage account key.
    /// </summary>
    private sealed class FakeCredentialStore : ICredentialStore
    {
        private static readonly string ValidConnectionString =
            "DefaultEndpointsProtocol=https;AccountName=myaccount;AccountKey=" +
            Convert.ToBase64String(new byte[64]) +
            ";EndpointSuffix=core.windows.net";

        public string? Get(string key) => ValidConnectionString;
        public void Save(string key, string secret) { }
        public void Delete(string key) { }
        public IReadOnlyList<string> ListKeys(string prefix = "") => [];
    }

    private sealed class EmptyCredentialStore : ICredentialStore
    {
        public string? Get(string key) => null;
        public void Save(string key, string secret) { }
        public void Delete(string key) { }
        public IReadOnlyList<string> ListKeys(string prefix = "") => [];
    }
}
