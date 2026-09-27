using Microsoft.Extensions.DependencyInjection;
using SwebKit.Core.Configuration;
using SwebKit.Core.Security;
using SwebKit.Sidecar.Services;

namespace SwebKit.Sidecar.Endpoints;

/// <summary>
/// Access-awareness endpoints (Phases 2 and 3a of access-awareness-pipeline.md): the
/// per-environment access report, a single-entry refresh, and the copyable access-request
/// artifact a denied row produces.
/// </summary>
public static class AccessEndpoints
{
    /// <summary>
    /// The shared principal context used until/unless DI registers
    /// <see cref="IAzurePrincipalContext"/> — <see cref="GetService{T}"/> picks up a
    /// registration automatically, and this fallback keeps the endpoint working meanwhile.
    /// A single lazy instance so the ARM-token lookup stays session-cached across requests.
    /// </summary>
    private static readonly Lazy<IAzurePrincipalContext> SharedPrincipalContext =
        new(() => new AzurePrincipalContext());

    public static void MapAccessEndpoints(this WebApplication app)
    {
        app.MapGet("/api/access/report", GetReportAsync);
        app.MapGet("/api/access/report/{featureArea}/{connectionKey}", RefreshEntryAsync);
        app.MapPost("/api/access/request", CreateRequestArtifactAsync);
    }

    /// <summary>Full report. <c>?refresh=true</c> re-probes every capability; otherwise the
    /// service's 5-minute TTL cache answers.</summary>
    internal static async Task<IResult> GetReportAsync(
        IAccessReportService accessReports,
        bool refresh,
        CancellationToken ct)
    {
        var report = await accessReports.GetReportAsync(refresh, ct);
        return Results.Ok(report);
    }

    /// <summary>
    /// Re-probes one connection's rows and returns just that entry. Other entries keep their
    /// cached results, so this stays fast on a large report.
    /// </summary>
    internal static async Task<IResult> RefreshEntryAsync(
        string featureArea,
        string connectionKey,
        IAccessReportService accessReports,
        CancellationToken ct)
    {
        accessReports.Invalidate(featureArea, connectionKey);
        var report = await accessReports.GetReportAsync(forceRefresh: false, ct);
        var entry = report.Entries.FirstOrDefault(e =>
            string.Equals(e.FeatureArea, featureArea, StringComparison.OrdinalIgnoreCase)
            && string.Equals(e.ConnectionKey, connectionKey, StringComparison.OrdinalIgnoreCase));
        return entry is null
            ? ApiErrors.NotFound("Access report entry not found.")
            : Results.Ok(entry);
    }

    /// <summary>
    /// Builds the copyable access-request artifact for one denied row (Phase 3a). Every piece
    /// degrades honestly rather than fabricating: unknown scope → <c>scope: null</c> (the UI
    /// says "ask your admin for the scope"), unresolvable principal → <c>principal: null</c>,
    /// and <c>azCommand</c> exists only for an ARM-role remedy with both scope and object id.
    /// </summary>
    internal static async Task<IResult> CreateRequestArtifactAsync(
        AccessRequestArtifactRequest request,
        ProfileRepository profile,
        DemoModeService demo,
        IServiceProvider services,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.FeatureArea)
            || string.IsNullOrWhiteSpace(request.ConnectionKey)
            || string.IsNullOrWhiteSpace(request.Capability))
        {
            return ApiErrors.BadRequest("featureArea, connectionKey and capability are required.");
        }

        var resolver = new AccessScopeResolver(profile, demo);
        var resolved = resolver.Resolve(request.FeatureArea, request.ConnectionKey);
        if (resolved is null)
        {
            return ApiErrors.NotFound(
                $"No configured connection matches {request.FeatureArea}/{request.ConnectionKey}.");
        }

        var remedy = AccessScopeResolver.RemedyFor(request.Capability);

        // Connection-string connections never authenticate as the signed-in identity — there is
        // no Entra principal to grant anything to, so no ARM remedy applies regardless of the
        // capability's usual kind.
        var connectionStringAuth = string.Equals(
            resolved.AuthMode, "connectionString", StringComparison.OrdinalIgnoreCase);

        // Resolve the signed-in principal only when it can actually be granted something —
        // the token lookup can shell out to `az`, so don't pay it for connection-string rows.
        ResolvedPrincipal? principal = null;
        if (!connectionStringAuth)
        {
            var principalContext =
                services.GetService<IAzurePrincipalContext>() ?? SharedPrincipalContext.Value;
            principal = await principalContext.GetPrincipalAsync(ct).ConfigureAwait(false);
        }

        var scope = resolved.ScopeResourceId;
        string? azCommand = null;
        if (!connectionStringAuth
            && remedy.Kind == AccessRemedyKind.ArmRole
            && scope is not null
            && principal?.ObjectId is { } objectId)
        {
            var principalType = principal.Upn is not null ? "User" : "ServicePrincipal";
            azCommand = $"az role assignment create --assignee-object-id {objectId} " +
                        $"--assignee-principal-type {principalType} " +
                        $"--role \"{remedy.RoleName}\" --scope \"{scope}\"";
        }

        // SQL grants substitute the resolved UPN; without one the placeholder stays explicit
        // rather than pretending to know the login name.
        var grantStatement = remedy.GrantStatement?.Replace(
            "{principal}", principal?.Upn ?? "<your-login>", StringComparison.Ordinal);

        var artifact = new AccessRequestArtifact(
            Role: remedy.RoleName,
            Scope: scope,
            Principal: principal,
            Resource: resolved.Label,
            SummaryText: BuildSummary(request, resolved, remedy, principal, connectionStringAuth),
            AzCommand: azCommand,
            GrantStatement: grantStatement,
            RemedyKind: connectionStringAuth ? AccessRemedyKind.Other : remedy.Kind,
            // Phase 4 hook points: PIM assignment kinds and the request webhook don't exist yet.
            AssignmentKind: "permanent",
            WebhookConfigured: false);

        return Results.Ok(artifact);
    }

    /// <summary>
    /// The plain-language summary the dialog shows and the copy button puts on the clipboard —
    /// complete enough to paste into a Teams message or ticket on its own.
    /// </summary>
    private static string BuildSummary(
        AccessRequestArtifactRequest request,
        ResolvedAccessScope resolved,
        AccessRemedy remedy,
        ResolvedPrincipal? principal,
        bool connectionStringAuth)
    {
        if (connectionStringAuth)
        {
            return $"Access request: {request.Capability} on {resolved.Label} " +
                   "— this connection uses a connection string / access key, not your signed-in " +
                   "identity, so access is managed by whoever owns the credential (no Azure RBAC " +
                   "role to assign).";
        }

        var who = principal switch
        {
            { Upn: { } upn } => upn,
            { ObjectId: { } oid } => $"object id {oid}" +
                (principal.AppId is { } appId ? $" (app {appId})" : string.Empty),
            _ => "the signed-in identity (run `az ad signed-in-user show` to get its object id)",
        };
        var where = resolved.ScopeResourceId ?? "scope unknown — ask your admin for the resource's ARM id";
        return $"Access request: {remedy.RoleName} for {who} — {request.Capability} on " +
               $"{resolved.Label} ({where}). {remedy.Instructions}";
    }
}
