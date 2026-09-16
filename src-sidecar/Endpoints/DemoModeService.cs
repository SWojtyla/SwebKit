using SwebKit.Core.Abstractions;
using SwebKit.Core.Domain;
using SwebKit.Core.Services;

namespace SwebKit.Sidecar.Endpoints;

/// <summary>
/// Provides demo Service Bus namespaces and clients when demo mode is enabled.
/// Mirrors the old MAUI app's ServiceBusNamespaceBootstrapper.BuildDemoStates().
/// </summary>
public sealed class DemoModeService : IDisposable
{
    public static readonly Guid DemoNamespaceId1 = new("00000000-0000-0000-0000-000000000001");
    public static readonly Guid DemoNamespaceId2 = new("00000000-0000-0000-0000-000000000002");

    public static readonly string DemoRedisCacheId = "demo-cache";
    public static readonly string DemoStorageId = "demo-storage";
    public const string DemoSqlConnectionId = "demo-sql";
    public const string DemoSqlConnectionId2 = "demo-sql-2";

    private readonly DemoServiceBusClient _ordersClient = DemoServiceBusClient.OrdersDev();
    private readonly DemoServiceBusClient _paymentsClient = DemoServiceBusClient.PaymentsDev();
    private readonly DemoAksClient _aksClient = new();
    private readonly DemoRedisClient _redisClient = new(0);
    private readonly DemoStorageClient _storageClient = new();

    // Two demo connections with slightly different catalogs/data so the data & schema compare
    // panels show a real diff in demo mode (see DemoSqlClient's variant docs).
    private readonly DemoSqlClient _sqlClient = new(new SqlConnectionEntry
    {
        Id = DemoSqlConnectionId,
        DisplayName = "orders-dev-sql",
        Server = "orders-dev-sql.database.windows.net",
        Database = "orders",
    }, variant: 0);
    private readonly DemoSqlClient _sqlClient2 = new(new SqlConnectionEntry
    {
        Id = DemoSqlConnectionId2,
        DisplayName = "orders-prod-sql",
        Server = "orders-prod-sql.database.windows.net",
        Database = "orders",
    }, variant: 1);

    public bool IsDemoMode { get; set; }

    public IReadOnlyList<ServiceBusNamespace> GetDemoNamespaces() =>
    [
        new ServiceBusNamespace
        {
            Id = DemoNamespaceId1,
            Alias = "orders-dev",
            FullyQualifiedNamespace = "orders-dev.servicebus.windows.net",
            CredentialKey = string.Empty,
        },
        new ServiceBusNamespace
        {
            Id = DemoNamespaceId2,
            Alias = "payments-dev",
            FullyQualifiedNamespace = "payments-dev.servicebus.windows.net",
            CredentialKey = string.Empty,
        },
    ];

    public IServiceBusClient GetSbClient(ServiceBusNamespace ns)
    {
        if (ns.Id == DemoNamespaceId1) return _ordersClient;
        if (ns.Id == DemoNamespaceId2) return _paymentsClient;
        throw new InvalidOperationException($"Unknown demo namespace: {ns.Alias}");
    }

    public IAksClient GetAksClient() => _aksClient;

    public RedisCacheEntry? GetDemoRedisCache(string cacheId)
    {
        if (cacheId != DemoRedisCacheId)
            return null;

        return new RedisCacheEntry
        {
            Id = DemoRedisCacheId,
            DisplayName = "Demo Cache",
            ConnectionString = "localhost:6379",
            Database = 0,
        };
    }

    public IRedisClient GetRedisClient(RedisCacheEntry cache) => _redisClient;

    public StorageConfig? GetDemoStorageConfig() =>
        new()
        {
            Id = DemoStorageId,
            DisplayName = "Demo Storage",
            AccountName = "devstore",
            UseAad = false,
            AllowMutations = true,
        };

    public IStorageClient GetStorageClient() => _storageClient;

    public SqlConnectionEntry? GetDemoSqlConnection(string connectionId) => connectionId switch
    {
        DemoSqlConnectionId => _sqlClient.Connection,
        DemoSqlConnectionId2 => _sqlClient2.Connection,
        _ => null,
    };

    public IReadOnlyList<SqlConnectionEntry> GetDemoSqlConnections() =>
        [_sqlClient.Connection, _sqlClient2.Connection];

    public ISqlClient GetSqlClient(SqlConnectionEntry connection) => connection.Id switch
    {
        DemoSqlConnectionId => _sqlClient,
        DemoSqlConnectionId2 => _sqlClient2,
        _ => _sqlClient,
    };

    public void Dispose() => _redisClient.Dispose();
}
