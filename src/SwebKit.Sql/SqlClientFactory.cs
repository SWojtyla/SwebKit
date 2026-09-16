using SwebKit.Core.Abstractions;
using SwebKit.Core.Domain;

namespace SwebKit.Sql;

/// <summary>Builds <see cref="ISqlClient"/> instances for configured connections.</summary>
public sealed class SqlClientFactory : ISqlClientFactory
{
    public Task<ISqlClient> CreateAsync(SqlConnectionEntry connection, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(connection);
        if (string.IsNullOrWhiteSpace(connection.Server))
            throw new InvalidOperationException($"{nameof(SqlConnectionEntry.Server)} is required for connection '{connection.DisplayName}'.");
        return Task.FromResult<ISqlClient>(new SqlDatabaseClient(connection));
    }
}
