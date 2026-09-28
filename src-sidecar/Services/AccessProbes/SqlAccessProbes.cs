using SwebKit.Core.Abstractions;
using SwebKit.Core.Domain;
using SwebKit.Core.Security;

namespace SwebKit.Sidecar.Services.AccessProbes;

/// <summary>
/// SQL access probes. <c>sql.query</c> verifies the connection can run a self-permission probe
/// (which any login can) — a successful non-empty answer means data-plane queries work.
/// <c>sql.metadata</c> applies the SqlSchemaModel.MetadataHidden
/// heuristic: empty catalog + data-plane grants but no VIEW DEFINITION is a *denial* (sqlGrant
/// remedy — a DBA grant, not an ARM role), while a merely empty/unanswered probe is Unknown.
/// </summary>
internal static class SqlAccessProbes
{
    public const string Area = "Sql";
    public const string CapabilityQuery = "sql.query";
    public const string CapabilityMetadata = "sql.metadata";

    /// <summary>The remedy text for the metadata-hidden state — a database grant, not an ARM role.</summary>
    public static AccessDenial MetadataHiddenDenial(string detail) =>
        new(Area, CapabilityMetadata,
            "VIEW DEFINITION",
            "Ask a DBA for VIEW DEFINITION (or db_datareader) on this database so schema browsing works.",
            detail,
            Remedy: AccessScopeResolver.RemedyFor(CapabilityMetadata));

    public static IEnumerable<AccessProbeSpec> Build(
        IReadOnlyList<SqlConnectionEntry> connections,
        ISqlConnectionPool pool)
    {
        foreach (var connection in connections)
        {
            var label = string.IsNullOrWhiteSpace(connection.DisplayName) ? connection.Server : connection.DisplayName;

            yield return new AccessProbeSpec(Area, connection.Id, CapabilityQuery, label, AccessScopeResolver.SqlServerScope(connection), null,
                async ct =>
                {
                    var client = await pool.GetOrCreateAsync(connection, ct).ConfigureAwait(false);
                    var permissions = await client.GetMyPermissionsAsync(null, ct).ConfigureAwait(false);
                    // sys.fn_my_permissions returns [] both when the login genuinely has no
                    // granted permissions AND when the client swallowed a probe failure — an
                    // empty answer can't assert Ok, so it stays Unknown.
                    return permissions.Count > 0
                        ? ProbeOutcome.Ok
                        : ProbeOutcome.Unknown("Permission probe returned no data.");
                });

            yield return new AccessProbeSpec(Area, connection.Id, CapabilityMetadata, label, AccessScopeResolver.SqlServerScope(connection), null,
                async ct =>
                {
                    var client = await pool.GetOrCreateAsync(connection, ct).ConfigureAwait(false);
                    var schema = await client.GetSchemaAsync(null, ct).ConfigureAwait(false);
                    if (schema.Schemas.Count > 0)
                        return ProbeOutcome.Ok;

                    IReadOnlyList<string> permissions;
                    try
                    {
                        permissions = await client.GetMyPermissionsAsync(null, ct).ConfigureAwait(false);
                    }
                    catch
                    {
                        // Same contract as SqlEndpoints.GetSchemaAsync — a failed self-permission
                        // probe leaves the flag off.
                        permissions = [];
                    }

                    schema.ApplyPermissionAnalysis(permissions);
                    return schema.MetadataHidden
                        ? ProbeOutcome.Denied(MetadataHiddenDenial(
                            "The identity holds data-plane grants (SELECT/EXECUTE) but the schema " +
                            "catalog reads empty — metadata is hidden without VIEW DEFINITION."))
                        : ProbeOutcome.Unknown("Schema returned no objects.");
                });
        }
    }
}
