using Azure.Messaging.ServiceBus;
using SwebKit.Azure.ServiceBus;
using SwebKit.Core.Abstractions;
using SwebKit.Core.Domain;

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

    [Theory]
    [InlineData(SbTransportType.Amqp, ServiceBusTransportType.AmqpTcp)]
    [InlineData(SbTransportType.AmqpWebSockets, ServiceBusTransportType.AmqpWebSockets)]
    public void MapTransportType_MapsDomainEnumToSdkEnum(SbTransportType input, ServiceBusTransportType expected)
    {
        var result = ServiceBusClientFactory.MapTransportType(input);

        Assert.Equal(expected, result);
    }

    [Fact]
    public void Create_WithAmqpWebSockets_ReturnsNonNullClient()
    {
        var connStr = "Endpoint=sb://my-namespace.servicebus.windows.net/;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=abc=";

        var client = _factory.Create(connStr, SbTransportType.AmqpWebSockets);

        Assert.NotNull(client);
        Assert.IsAssignableFrom<IServiceBusClient>(client);
    }

    // ── DEC-3: credential diagnostic exposes identifiers only, never secret material ──────────

    [Fact]
    public void BuildConnectionDiagnostic_ExposesKeyNameAndEndpoint_ButNotSecretValue()
    {
        const string keyName = "RootManageSharedAccessKey";
        const string secretValue = "S3cr3tKeyMaterial+AbcDef123/xyz==";
        const string credentialSource = "sb:ns:orders-live";
        var connectionString =
            $"Endpoint=sb://orders-live.servicebus.windows.net/;SharedAccessKeyName={keyName};SharedAccessKey={secretValue}";

        var diagnostic = _factory.BuildConnectionDiagnostic(connectionString, credentialSource);

        // Non-secret identifiers ARE exposed.
        Assert.Equal("orders-live.servicebus.windows.net", diagnostic.EndpointHost);
        Assert.Equal(keyName, diagnostic.SharedAccessKeyName);
        Assert.Equal("SAS key", diagnostic.AuthMethod);
        Assert.Equal(credentialSource, diagnostic.CredentialSource);

        // DEC-3 hard rule: the secret value / full connection string must NEVER appear anywhere in
        // the diagnostic — including the auto-generated record ToString() used by structured logging.
        var everyField = string.Join(
            '\n',
            diagnostic.EndpointHost,
            diagnostic.SharedAccessKeyName,
            diagnostic.AuthMethod,
            diagnostic.CredentialSource);

        Assert.DoesNotContain(secretValue, everyField, StringComparison.Ordinal);
        Assert.DoesNotContain(secretValue, diagnostic.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("SharedAccessKey=", diagnostic.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(connectionString, diagnostic.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void BuildConnectionDiagnostic_FallsBackToPlaceholder_WhenCredentialSourceMissing()
    {
        var connectionString =
            "Endpoint=sb://payments-dev.servicebus.windows.net/;SharedAccessKeyName=Listen;SharedAccessKey=secret==";

        var diagnostic = _factory.BuildConnectionDiagnostic(connectionString, credentialSource: "   ");

        Assert.Equal("(unnamed credential)", diagnostic.CredentialSource);
        Assert.DoesNotContain("secret==", diagnostic.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void BuildEntraConnectionDiagnostic_UsesTokenAuthAndOmitsKeyName()
    {
        var diagnostic = _factory.BuildEntraConnectionDiagnostic("orders-live.servicebus.windows.net");

        Assert.Equal("orders-live.servicebus.windows.net", diagnostic.EndpointHost);
        Assert.Null(diagnostic.SharedAccessKeyName);
        Assert.Equal("Microsoft Entra (DefaultAzureCredential)", diagnostic.AuthMethod);
        Assert.Equal("DefaultAzureCredential", diagnostic.CredentialSource);
    }
}
