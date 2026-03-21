using Microsoft.Extensions.Logging.Abstractions;
using SwebKit.Azure.ServiceBus;
using SwebKit.Core.Abstractions;
using SwebKit.Core.Domain;

namespace SwebKit.Azure.Tests.ServiceBus;

public sealed class AzureServiceBusClientParsingTests
{
    private static readonly NullLogger<AzureServiceBusClient> Logger = NullLogger<AzureServiceBusClient>.Instance;

    // ── Construction guard tests ──

    [Fact]
    public void Ctor_DefaultAzureCredentialMode_DoesNotThrowAtConstruction()
    {
        // AuthMode defaults to DefaultAzureCredential — construction should not fail
        // even without real credentials; failure would only happen on first network call.
        var config = new ServiceBusConfig { NamespaceHostname = "test-ns" };

        var ex = Record.Exception(() => new AzureServiceBusClient(config, new NullCredentialStore(), Logger));

        Assert.Null(ex);
    }

    [Fact]
    public void Ctor_ConnectionStringMode_CredentialFound_DoesNotThrow()
    {
        const string conn =
            "Endpoint=sb://test-ns.servicebus.windows.net/;" +
            "SharedAccessKeyName=RootManageSharedAccessKey;" +
            "SharedAccessKey=AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=";

        var config = new ServiceBusConfig
        {
            NamespaceHostname = "test-ns",
            AuthMode = SbAuthMode.ConnectionString,
            CredentialRef = "sb:test"
        };
        var store = new ValueCredentialStore(conn);

        var ex = Record.Exception(() => new AzureServiceBusClient(config, store, Logger));

        Assert.Null(ex);
    }

    [Fact]
    public void Ctor_ConnectionStringMode_CredentialMissing_ThrowsInvalidOperation()
    {
        var config = new ServiceBusConfig
        {
            NamespaceHostname = "test-ns",
            AuthMode = SbAuthMode.ConnectionString,
            CredentialRef = "sb:not-stored"
        };

        Assert.Throws<InvalidOperationException>(
            () => new AzureServiceBusClient(config, new NullCredentialStore(), Logger));
    }

    [Fact]
    public void Ctor_ConnectionStringScopedToQueue_DoesNotThrow()
    {
        // A connection string that includes an EntityPath (scoped to a specific queue)
        // should be accepted at construction time — entity path is stored for fallback listing.
        const string conn =
            "Endpoint=sb://test-ns.servicebus.windows.net/;" +
            "SharedAccessKeyName=RootManageSharedAccessKey;" +
            "SharedAccessKey=AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=;" +
            "EntityPath=my-queue";

        var ex = Record.Exception(() => new AzureServiceBusClient(conn));

        Assert.Null(ex);
    }

    [Fact]
    public void Ctor_ConnectionStringWithSlashEntityPath_TopicSubscription_DoesNotThrow()
    {
        // EntityPath of the form "topic/subscriptions/sub" is valid for topic-scoped connections.
        const string conn =
            "Endpoint=sb://test-ns.servicebus.windows.net/;" +
            "SharedAccessKeyName=RootManageSharedAccessKey;" +
            "SharedAccessKey=AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=;" +
            "EntityPath=my-topic/subscriptions/my-sub";

        var ex = Record.Exception(() => new AzureServiceBusClient(conn));

        Assert.Null(ex);
    }

    [Fact]
    public void Ctor_EmptyConnectionString_Throws()
    {
        Assert.ThrowsAny<Exception>(() => new AzureServiceBusClient(string.Empty));
    }

    [Fact]
    public void Ctor_MalformedConnectionString_Throws()
    {
        Assert.ThrowsAny<Exception>(() => new AzureServiceBusClient("not-a-connection-string"));
    }

    // ── FullyQualifiedNamespace helpers ──

    [Theory]
    [InlineData("mynamespace", "mynamespace.servicebus.windows.net")]
    [InlineData("mynamespace.servicebus.windows.net", "mynamespace.servicebus.windows.net")]
    [InlineData("ns.custom.domain.com", "ns.custom.domain.com")]
    public void ServiceBusConfig_FullyQualifiedNamespace_IsComputedCorrectly(string hostname, string expected)
    {
        var config = new ServiceBusConfig { NamespaceHostname = hostname };

        Assert.Equal(expected, config.FullyQualifiedNamespace);
    }

    // ── Helpers ──

    private sealed class NullCredentialStore : ICredentialStore
    {
        public string? Get(string key) => null;
        public void Save(string key, string secret) { }
        public void Delete(string key) { }
        public IReadOnlyList<string> ListKeys(string prefix = "") => [];
    }

    private sealed class ValueCredentialStore(string value) : ICredentialStore
    {
        public string? Get(string key) => value;
        public void Save(string key, string secret) { }
        public void Delete(string key) { }
        public IReadOnlyList<string> ListKeys(string prefix = "") => [];
    }
}
