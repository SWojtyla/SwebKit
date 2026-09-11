using System.Globalization;
using SwebKit.Azure.Storage;
using SwebKit.Core.Abstractions;
using SwebKit.Core.Domain;

namespace SwebKit.Azure.Tests;

/// <summary>
/// SAS generation is one of the few <see cref="AzureStorageClient"/> behaviours that runs
/// entirely client-side: the shared key signs the URL locally, so expiry, permissions and
/// path encoding are all verifiable without a live storage account.
/// </summary>
public sealed class AzureStorageClientSasTests
{
    private const string AccountName = "myaccount";

    // ── Blob SAS ──

    [Fact]
    public async Task GetBlobSasUrlAsync_PointsAtTheRequestedContainerAndBlob()
    {
        var url = await SharedKeyClient().GetBlobSasUrlAsync("mycontainer", "report.json", TimeSpan.FromHours(1));

        var uri = new Uri(url);
        Assert.Equal($"{AccountName}.blob.core.windows.net", uri.Host);
        Assert.Equal("/mycontainer/report.json", uri.AbsolutePath);
    }

    [Fact]
    public async Task GetBlobSasUrlAsync_IsSigned()
    {
        var url = await SharedKeyClient().GetBlobSasUrlAsync("mycontainer", "report.json", TimeSpan.FromHours(1));

        var query = ParseQuery(new Uri(url));
        Assert.True(query.TryGetValue("sig", out var signature));
        Assert.False(string.IsNullOrWhiteSpace(signature));
    }

    [Fact]
    public async Task GetBlobSasUrlAsync_GrantsReadOnlyOnASingleBlob()
    {
        var url = await SharedKeyClient().GetBlobSasUrlAsync("mycontainer", "report.json", TimeSpan.FromHours(1));

        var query = ParseQuery(new Uri(url));
        Assert.Equal("r", query["sp"]);
        Assert.Equal("b", query["sr"]);   // resource = blob, not container
    }

    [Fact]
    public async Task GetBlobSasUrlAsync_ExpiryIsRelativeToNow()
    {
        var before = DateTimeOffset.UtcNow;

        var url = await SharedKeyClient().GetBlobSasUrlAsync("mycontainer", "report.json", TimeSpan.FromMinutes(30));

        var expiry = ParseSasTime(ParseQuery(new Uri(url))["se"]);
        Assert.InRange(expiry, before.AddMinutes(30).AddSeconds(-30), DateTimeOffset.UtcNow.AddMinutes(30).AddSeconds(30));
    }

    [Fact]
    public async Task GetBlobSasUrlAsync_LongExpiryIsHonoured()
    {
        var url = await SharedKeyClient().GetBlobSasUrlAsync("mycontainer", "report.json", TimeSpan.FromDays(7));

        var expiry = ParseSasTime(ParseQuery(new Uri(url))["se"]);
        Assert.InRange(expiry, DateTimeOffset.UtcNow.AddDays(7).AddMinutes(-2), DateTimeOffset.UtcNow.AddDays(7).AddMinutes(2));
    }

    [Fact]
    public async Task GetBlobSasUrlAsync_NegativeExpiry_ProducesAnAlreadyExpiredUrl()
    {
        // Documents current behaviour: the client does not guard against a non-positive
        // expiry, it simply hands back a SAS that is dead on arrival.
        var url = await SharedKeyClient().GetBlobSasUrlAsync("mycontainer", "report.json", TimeSpan.FromHours(-1));

        var expiry = ParseSasTime(ParseQuery(new Uri(url))["se"]);
        Assert.True(expiry < DateTimeOffset.UtcNow);
    }

    [Fact]
    public async Task GetBlobSasUrlAsync_VirtualFolderSeparatorsSurviveAsPathSegments()
    {
        // A blob name is a flat string containing '/'; it must stay a path, not become %2F,
        // otherwise the SAS points at a blob that does not exist.
        var url = await SharedKeyClient().GetBlobSasUrlAsync("mycontainer", "logs/2024/06/app.log", TimeSpan.FromHours(1));

        Assert.Equal("/mycontainer/logs/2024/06/app.log", new Uri(url).AbsolutePath);
    }

    [Fact]
    public async Task GetBlobSasUrlAsync_SpacesAndSpecialCharactersAreEscaped()
    {
        var url = await SharedKeyClient().GetBlobSasUrlAsync("mycontainer", "my folder/a+b c.txt", TimeSpan.FromHours(1));

        var uri = new Uri(url);
        Assert.DoesNotContain(' ', uri.AbsolutePath);
        Assert.Equal("/mycontainer/my folder/a+b c.txt", Uri.UnescapeDataString(uri.AbsolutePath));
    }

    [Fact]
    public async Task GetBlobSasUrlAsync_AadMode_Throws()
    {
        // AAD-authenticated clients have no shared key, so a user-delegation SAS would be
        // required. The client does not implement that, and does not guard against it either:
        // the SDK's raw ArgumentNullException("sharedKeyCredential") escapes to the caller.
        // Asserting current behaviour — see the report note about the unhelpful message.
        var client = AadClient();

        var ex = await Assert.ThrowsAsync<ArgumentNullException>(
            () => client.GetBlobSasUrlAsync("mycontainer", "report.json", TimeSpan.FromHours(1)));
        Assert.Equal("sharedKeyCredential", ex.ParamName);
    }

    // ── Container SAS ──

    [Fact]
    public async Task GetContainerSasUrlAsync_PointsAtTheContainer()
    {
        var url = await SharedKeyClient().GetContainerSasUrlAsync("mycontainer", TimeSpan.FromHours(1));

        var uri = new Uri(url);
        Assert.Equal("/mycontainer", uri.AbsolutePath);
        Assert.Equal("c", ParseQuery(uri)["sr"]);
    }

    [Fact]
    public async Task GetContainerSasUrlAsync_GrantsReadAndList()
    {
        // Read alone is useless for a container: the recipient could not enumerate it.
        var url = await SharedKeyClient().GetContainerSasUrlAsync("mycontainer", TimeSpan.FromHours(1));

        var permissions = ParseQuery(new Uri(url))["sp"];
        Assert.Contains('r', permissions);
        Assert.Contains('l', permissions);
        Assert.DoesNotContain('w', permissions);
        Assert.DoesNotContain('d', permissions);
    }

    [Fact]
    public async Task GetContainerSasUrlAsync_ExpiryIsRelativeToNow()
    {
        var url = await SharedKeyClient().GetContainerSasUrlAsync("mycontainer", TimeSpan.FromMinutes(15));

        var expiry = ParseSasTime(ParseQuery(new Uri(url))["se"]);
        Assert.InRange(expiry, DateTimeOffset.UtcNow.AddMinutes(15).AddSeconds(-60), DateTimeOffset.UtcNow.AddMinutes(15).AddSeconds(60));
    }

    [Fact]
    public async Task GetContainerSasUrlAsync_AadMode_Throws()
    {
        // Same unguarded shared-key requirement as the blob SAS path.
        var client = AadClient();

        var ex = await Assert.ThrowsAsync<ArgumentNullException>(
            () => client.GetContainerSasUrlAsync("mycontainer", TimeSpan.FromHours(1)));
        Assert.Equal("sharedKeyCredential", ex.ParamName);
    }

    // ── Construction ──

    [Fact]
    public void Constructor_AadMode_WithAccountName_DoesNotThrow()
    {
        var ex = Record.Exception(() => AadClient());

        Assert.Null(ex);
    }

    [Fact]
    public void Constructor_UseAadWins_WhenBothAadAndConnectionStringRefAreSet()
    {
        // UseAad = true short-circuits the connection-string branch, so the credential
        // store is never consulted — proved here by a store that would throw if it were.
        var config = new StorageConfig
        {
            UseAad = true,
            AccountName = AccountName,
            ConnectionStringRef = "storage:ignored"
        };

        var ex = Record.Exception(() => new AzureStorageClient(config, new ThrowingCredentialStore()));

        Assert.Null(ex);
    }

    [Fact]
    public void StorageClientFactory_CreatesAnAzureStorageClientBoundToTheConfig()
    {
        var config = new StorageConfig { UseAad = false, ConnectionStringRef = "storage:myaccount" };

        var client = new StorageClientFactory(new SharedKeyCredentialStore()).Create(config);

        Assert.IsType<AzureStorageClient>(client);
        Assert.Same(config, ((AzureStorageClient)client).Config);
    }

    // ── Helpers ──

    private static AzureStorageClient SharedKeyClient() =>
        new(new StorageConfig { UseAad = false, ConnectionStringRef = "storage:myaccount" }, new SharedKeyCredentialStore());

    private static AzureStorageClient AadClient() =>
        new(new StorageConfig { UseAad = true, AccountName = AccountName }, new SharedKeyCredentialStore());

    private static Dictionary<string, string> ParseQuery(Uri uri) =>
        uri.Query.TrimStart('?')
            .Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(part => part.Split('=', 2))
            .ToDictionary(
                part => Uri.UnescapeDataString(part[0]),
                part => part.Length > 1 ? Uri.UnescapeDataString(part[1]) : string.Empty,
                StringComparer.OrdinalIgnoreCase);

    private static DateTimeOffset ParseSasTime(string value) =>
        DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);

    /// <summary>
    /// Supplies a syntactically valid shared-key connection string so the SDK can sign SAS
    /// tokens locally. A 64-byte all-zero key base64-encodes to a well-formed account key.
    /// </summary>
    private sealed class SharedKeyCredentialStore : ICredentialStore
    {
        private static readonly string ConnectionString =
            $"DefaultEndpointsProtocol=https;AccountName={AccountName};AccountKey=" +
            Convert.ToBase64String(new byte[64]) +
            ";EndpointSuffix=core.windows.net";

        public string? Get(string key) => ConnectionString;
        public void Save(string key, string secret) { }
        public void Delete(string key) { }
        public IReadOnlyList<string> ListKeys(string prefix = "") => [];
    }

    private sealed class ThrowingCredentialStore : ICredentialStore
    {
        public string? Get(string key) => throw new InvalidOperationException("Credential store must not be consulted in AAD mode.");
        public void Save(string key, string secret) { }
        public void Delete(string key) { }
        public IReadOnlyList<string> ListKeys(string prefix = "") => [];
    }
}
