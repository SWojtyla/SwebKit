using SwebKit.Core.Abstractions;
using SwebKit.Core.Domain;
using SwebKit.Core.Services;

namespace SwebKit.Sidecar.Services;

/// <summary>
/// Provides demo resources and clients when demo mode is enabled.
/// </summary>
public sealed class DemoModeService : IDisposable
{
    public static readonly Guid DemoNamespaceId1 = new("00000000-0000-0000-0000-000000000001");
    public static readonly Guid DemoNamespaceId2 = new("00000000-0000-0000-0000-000000000002");

    public static readonly string DemoRedisCacheId = "demo-cache";
    public static readonly string DemoStorageId = "demo-storage";
    public const string DemoSqlConnectionId = "demo-sql";
    public const string DemoSqlConnectionId2 = "demo-sql-2";
    /// <summary>A locked-down "prd" connection — SELECT/EXECUTE but no VIEW DEFINITION, so the
    /// schema tree is empty while queries still work. Demonstrates the metadata-hidden state.</summary>
    public const string DemoSqlConnectionIdRestricted = "demo-sql-prd";

    private DemoServiceBusClient _ordersClient = DemoServiceBusClient.OrdersDev();
    private DemoServiceBusClient _paymentsClient = DemoServiceBusClient.PaymentsDev();
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
    private readonly DemoSqlClient _sqlClientRestricted = new(new SqlConnectionEntry
    {
        Id = DemoSqlConnectionIdRestricted,
        DisplayName = "orders-prd-sql (restricted)",
        Server = "orders-prd-sql.database.windows.net",
        Database = "orders",
    }, variant: 2);

    private bool _isDemoMode;

    public bool IsDemoMode
    {
        get => _isDemoMode;
        set
        {
            // Re-entering demo mode re-seeds the Service Bus data: the demo clients are
            // stateful in-memory stores (send/complete/resend mutate them), and consumers —
            // including the Playwright suite, which toggles demo mode around every test —
            // assume each demo session starts from the pristine seed data.
            if (value && !_isDemoMode)
            {
                _ordersClient = DemoServiceBusClient.OrdersDev();
                _paymentsClient = DemoServiceBusClient.PaymentsDev();
            }

            _isDemoMode = value;
        }
    }

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
        DemoSqlConnectionIdRestricted => _sqlClientRestricted.Connection,
        _ => null,
    };

    public IReadOnlyList<SqlConnectionEntry> GetDemoSqlConnections() =>
        [_sqlClient.Connection, _sqlClient2.Connection, _sqlClientRestricted.Connection];

    public ISqlClient GetSqlClient(SqlConnectionEntry connection) => connection.Id switch
    {
        DemoSqlConnectionId => _sqlClient,
        DemoSqlConnectionId2 => _sqlClient2,
        DemoSqlConnectionIdRestricted => _sqlClientRestricted,
        _ => _sqlClient,
    };

    public void Dispose() => _redisClient.Dispose();
}
