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

    /// <summary>A working dev vault — the canned secrets below resolve here.</summary>
    public const string DemoKeyVaultDevId = "demo-kv-dev";
    /// <summary>A locked-down prod vault — <see cref="DemoAwareKeyVaultResolver"/> denies every
    /// lookup against it (the <c>*prod*</c>/<c>*restricted*</c> name rule), the same way a real
    /// vault denies an identity without <c>secrets/get</c>.</summary>
    public const string DemoKeyVaultProdId = "demo-kv-prod";

    private const string DemoKeyVaultDevName = "dev-secrets";
    private const string DemoKeyVaultProdName = "prod-secrets";

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
        // Declared objects are the whole point of a locked-down connection: the catalog is
        // hidden but these names keep the tree alive. v_orders is granted (columns + browse
        // work), p_recalc demonstrates a runnable proc, and v_audit carries an object-level
        // DENY so the per-object failure path is exercisable too.
        DeclaredObjects = ["prd.v_orders", "exec:prd.p_recalc", "prd.v_audit"],
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

    /// <summary>The demo vault pair overlaid onto the profile while demo mode is on — a working
    /// dev vault and a prod vault every secret lookup is denied against.</summary>
    public IReadOnlyList<KeyVaultEntry> GetDemoKeyVaults() =>
    [
        new KeyVaultEntry
        {
            Id = DemoKeyVaultDevId,
            Name = DemoKeyVaultDevName,
            Url = "https://dev-secrets.vault.azure.net/",
        },
        new KeyVaultEntry
        {
            Id = DemoKeyVaultProdId,
            Name = DemoKeyVaultProdName,
            Url = "https://prod-secrets.vault.azure.net/",
        },
    ];

    /// <summary>
    /// The demo "no prod access" rule: any vault reference — the friendly name or the vault URL —
    /// containing <c>prod</c> or <c>restricted</c> is denied.
    /// </summary>
    public static bool IsDeniedVaultName(string? name) =>
        name?.Contains("prod", StringComparison.OrdinalIgnoreCase) == true
        || name?.Contains("restricted", StringComparison.OrdinalIgnoreCase) == true;

    /// <summary>Canned dev-vault secrets — enough for the demo API collection's
    /// Key Vault-sourced environment variables to resolve to plausible-looking values.</summary>
    private static readonly IReadOnlyDictionary<string, string> DemoSecrets =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["payments-api-key"] = "demo-payments-key-0000",
            ["orders-api-key"] = "demo-orders-key-0000",
        };

    /// <summary>
    /// Demo-mode secret resolution. A denied vault returns <c>null</c> — the same shape a real
    /// denied vault produces through <c>IKeyVaultSecretResolver</c> (the contract collapses
    /// denial and missing-secret to null), so callers exercise the real failure path. Unknown
    /// vaults and unknown secret names are likewise null.
    /// </summary>
    public string? GetDemoSecret(string secretName, string? vaultName)
    {
        if (IsDeniedVaultName(vaultName))
            return null;

        var isDevVault = vaultName is null
            || string.Equals(vaultName, DemoKeyVaultDevName, StringComparison.OrdinalIgnoreCase)
            || vaultName.Contains("dev-secrets", StringComparison.OrdinalIgnoreCase);
        if (!isDevVault)
            return null;

        return DemoSecrets.TryGetValue(secretName, out var value) ? value : null;
    }

    public void Dispose() => _redisClient.Dispose();
}
