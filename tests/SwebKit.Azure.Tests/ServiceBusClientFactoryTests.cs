using SwebKit.Azure.ServiceBus;
using SwebKit.Core.Abstractions;

namespace SwebKit.Azure.Tests;

public sealed class ServiceBusClientFactoryTests
{
    private readonly ServiceBusClientFactory _factory = new();

    [Theory]
    [InlineData("Endpoint=sb://my-namespace.servicebus.windows.net/;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=abc=",
        "my-namespace.servicebus.windows.net")]
    [InlineData("Endpoint=sb://orders-dev.servicebus.windows.net/;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=xyz=",
        "orders-dev.servicebus.windows.net")]
    public void ParseFullyQualifiedNamespace_ReturnsNamespaceHostname(string connectionString, string expectedFqns)
    {
        var result = _factory.ParseFullyQualifiedNamespace(connectionString);

        Assert.Equal(expectedFqns, result);
    }

    [Fact]
    public void Create_ReturnsNonNullClient()
    {
        // We can't connect to a real SB endpoint in tests, but we can verify
        // the factory returns an IServiceBusClient without throwing on construction.
        var connStr = "Endpoint=sb://my-namespace.servicebus.windows.net/;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=abc=";

        var client = _factory.Create(connStr);

        Assert.NotNull(client);
        Assert.IsAssignableFrom<IServiceBusClient>(client);
    }

    [Fact]
    public void Create_CreatesDisposableClient()
    {
        var connStr = "Endpoint=sb://my-namespace.servicebus.windows.net/;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=abc=";

        var client = _factory.Create(connStr);

        Assert.IsAssignableFrom<IAsyncDisposable>(client);
    }
}
