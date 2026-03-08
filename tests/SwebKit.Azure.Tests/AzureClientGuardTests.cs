using SwebKit.Azure.Observability;
using SwebKit.Azure.ServiceBus;
using SwebKit.Core.Abstractions;
using SwebKit.Core.Domain;
using SwebKit.Core.Models;

namespace SwebKit.Azure.Tests;

public class AzureClientGuardTests
{
    [Fact]
    public void AzureServiceBusClient_Ctor_ConnectionStringMissing_Throws()
    {
        var config = new ServiceBusConfig
        {
            NamespaceHostname = "swebkit",
            AuthMode = SbAuthMode.ConnectionString,
            CredentialRef = "sb:missing"
        };

        var ex = Assert.Throws<InvalidOperationException>(() => new AzureServiceBusClient(config, new FakeCredentialStore()));
        Assert.Contains("Credential 'sb:missing' not found", ex.Message);
    }

    [Fact]
    public async Task AppInsightsProvider_QueryLogsAsync_NoWorkspace_Throws()
    {
        var provider = new AppInsightsObservabilityProvider(new ObservabilityConfig(), new FakeCredentialStore());

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => provider.QueryLogsAsync(new LogQuery { TimeRange = "15m" }));
        Assert.Equal("WorkspaceId is not configured.", ex.Message);
    }

    [Fact]
    public void AzureServiceBusClient_ConnectionStringCtor_InvalidString_Throws()
    {
        Assert.ThrowsAny<Exception>(() => new AzureServiceBusClient("not-a-valid-connection-string"));
    }

    [Fact]
    public void AzureServiceBusClient_ConnectionStringCtor_ValidString_DoesNotThrow()
    {
        // A syntactically valid connection string (no real credentials — just parsing smoke test)
        const string conn = "Endpoint=sb://sb-test.servicebus.windows.net/;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=";
        var client = new AzureServiceBusClient(conn);
        Assert.NotNull(client);
    }

    private sealed class FakeCredentialStore : ICredentialStore
    {
        public void Save(string key, string secret) { }
        public string? Get(string key) => null;
        public void Delete(string key) { }
        public IReadOnlyList<string> ListKeys(string prefix = "") => [];
    }
}
