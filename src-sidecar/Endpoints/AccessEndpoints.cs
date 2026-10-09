using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using SwebKit.Core.Abstractions;
using SwebKit.Core.Configuration;
using SwebKit.Core.Security;
using SwebKit.Sidecar.Services;

namespace SwebKit.Sidecar.Endpoints;

/// <summary>
/// Access-awareness endpoints (Phases 2–4 of access-awareness-pipeline.md): the
/// per-environment access report, a single-entry refresh, the copyable access-request
/// artifact a denied row produces, the request-webhook configuration, and the webhook send.
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
        app.MapGet("/api/access/webhook", GetWebhookConfig);
        app.MapPut("/api/access/webhook", SaveWebhookConfigAsync);
        app.MapPost("/api/access/request/send", SendRequestAsync);
    }

    /// <summary>Full report. <c>?refresh=true</c> re-probes every capability; otherwise the
    /// service's 5-minute TTL cache answers.</summary>
    internal static async Task<IResult> GetReportAsync(
        IAccessReportService accessReports,
        bool? refresh,
        CancellationToken ct)
    {
        var report = await accessReports.GetReportAsync(refresh ?? false, ct);
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
        ICredentialStore credentials,
        CancellationToken ct)
    {
        var (artifact, error) = await BuildArtifactAsync(request, profile, demo, services, ct)
            .ConfigureAwait(false);
        if (error is not null)
        {
            return error;
        }

        return Results.Ok(artifact! with
        {
            WebhookConfigured = IsWebhookConfigured(profile, credentials, demo),
        });
    }

    /// <summary>
    /// <c>GET /api/access/webhook</c> — the webhook config view for Settings → Access. Never
    /// returns the trigger URL itself (it sits in the credential store); <c>hasUrl</c> is the
    /// honest "a URL is actually stored" signal the UI needs to explain whether Send can work.
    /// </summary>
    internal static IResult GetWebhookConfig(
        ProfileRepository profile,
        ICredentialStore credentials) =>
        Results.Ok(WebhookViewOf(profile.Config.AccessRequest, credentials));

    /// <summary>
    /// <c>PUT /api/access/webhook</c> — saves the webhook config for the current profile.
    /// A non-empty <c>url</c> is written to the OS credential store (never profiles.json) under
    /// the stored key; <c>clearUrl</c> removes it. Omitting both keeps the existing URL, so a
    /// template/enabled edit never touches the secret.
    /// </summary>
    internal static async Task<IResult> SaveWebhookConfigAsync(
        SaveAccessRequestWebhookRequest request,
        ProfileRepository profile,
        ICredentialStore credentials)
    {
        var credentialKey = profile.Config.AccessRequest?.UrlCredentialKey;

        if (request.ClearUrl)
        {
            if (credentialKey is not null)
            {
                credentials.Delete(credentialKey);
            }
            credentialKey = null;
        }

        if (!string.IsNullOrWhiteSpace(request.Url))
        {
            var url = request.Url.Trim();
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)
                || uri.Scheme != Uri.UriSchemeHttps)
            {
                return ApiErrors.BadRequest(
                    "The webhook URL must be an absolute https:// URL — a Power Automate " +
                    "trigger URL carries its SAS signature, so it only ever goes over TLS.");
            }

            credentialKey ??= AccessRequestConfig.DefaultUrlCredentialKey;
            credentials.Save(credentialKey, url);
        }

        profile.Config.AccessRequest = new AccessRequestConfig
        {
            Enabled = request.Enabled,
            UrlCredentialKey = credentialKey,
            BodyTemplate = string.IsNullOrWhiteSpace(request.BodyTemplate)
                ? null
                : request.BodyTemplate,
        };
        await profile.SaveAsync().ConfigureAwait(false);
        return Results.Ok(WebhookViewOf(profile.Config.AccessRequest, credentials));
    }

    /// <summary>
    /// <c>POST /api/access/request/send</c> — renders the configured body template for the
    /// denied row and POSTs it to the webhook (Phase 4). The result is deliberately honest:
    /// <c>sent</c> means "the trigger accepted the call", never "access was granted".
    ///
    /// <c>dryRun</c> renders the body and stops — the settings test path, which also works
    /// for a connection triple that doesn't resolve (a sample artifact is rendered instead
    /// of 404ing, since the point is to preview the payload shape).
    ///
    /// Demo mode never fires a real POST: it answers a labeled simulated 403 so the demo
    /// exercises the same failure/permission path a locked-down flow produces.
    /// </summary>
    internal static async Task<IResult> SendRequestAsync(
        AccessRequestSendRequest request,
        ProfileRepository profile,
        DemoModeService demo,
        IServiceProvider services,
        ICredentialStore credentials,
        AccessRequestSender sender,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.FeatureArea)
            || string.IsNullOrWhiteSpace(request.ConnectionKey)
            || string.IsNullOrWhiteSpace(request.Capability))
        {
            return ApiErrors.BadRequest("featureArea, connectionKey and capability are required.");
        }

        var (resolved, error) = await BuildArtifactAsync(
            new AccessRequestArtifactRequest(
                request.FeatureArea, request.ConnectionKey, request.Capability),
            profile, demo, services, ct).ConfigureAwait(false);

        AccessRequestArtifact artifact;
        if (error is not null)
        {
            if (!request.DryRun)
            {
                return error;
            }
            // Dry-run "test send" doesn't need a real connection — render sample values so
            // the user sees exactly what the template produces before trusting it. Demo
            // mode never touches the real credential — the sample principal stands in.
            var principalContext =
                services.GetService<IAzurePrincipalContext>() ?? SharedPrincipalContext.Value;
            artifact = SampleArtifact(
                demo.IsDemoMode
                    ? null
                    : await principalContext.GetPrincipalAsync(ct).ConfigureAwait(false));
        }
        else
        {
            artifact = resolved!;
        }

        var config = profile.Config.AccessRequest;
        var template = string.IsNullOrWhiteSpace(config?.BodyTemplate)
            ? AccessRequestTemplate.DefaultBodyTemplate
            : config!.BodyTemplate!;
        var body = AccessRequestTemplate.Render(
            template,
            AccessRequestTemplate.BuildValues(
                artifact, request.FeatureArea, request.Capability,
                request.Justification, DateTimeOffset.UtcNow));

        // A template that doesn't render to JSON would fail at the trigger anyway — catch it
        // here where the rendered body can be shown, rather than surfacing a remote 400.
        var validJson = false;
        try
        {
            validJson = JsonNode.Parse(body) is not null;
        }
        catch (JsonException)
        {
            // falls through to the honest failure below
        }

        if (!validJson)
        {
            return Results.Ok(new AccessRequestSendResult(
                Sent: false, DryRun: request.DryRun, Demo: demo.IsDemoMode, StatusCode: null,
                Error: "The body template didn't render to valid JSON — check the braces " +
                       "and placeholder quoting in Settings → Access.",
                RenderedBody: body));
        }

        if (request.DryRun)
        {
            return Results.Ok(new AccessRequestSendResult(
                Sent: false, DryRun: true, Demo: demo.IsDemoMode, StatusCode: null,
                Error: null, RenderedBody: body));
        }

        if (demo.IsDemoMode)
        {
            // Honest demo: no request leaves the machine. The simulated 403 exercises the
            // same failure/permission path a real locked-down trigger produces, labeled as
            // a simulation end to end.
            return Results.Ok(new AccessRequestSendResult(
                Sent: false, DryRun: false, Demo: true, StatusCode: 403,
                Error: "Demo mode — nothing was sent. The demo webhook answers HTTP 403 " +
                       "(Forbidden) so the failure path is visible; with a real trigger " +
                       "URL this is what a rejected request looks like.",
                RenderedBody: body));
        }

        if (config is not { Enabled: true })
        {
            return Results.Ok(new AccessRequestSendResult(
                Sent: false, DryRun: false, Demo: false, StatusCode: null,
                Error: "No access-request webhook is enabled for this profile — copy the " +
                       "request text instead.",
                RenderedBody: body));
        }

        var url = config.UrlCredentialKey is { } key ? credentials.Get(key) : null;
        if (url is null)
        {
            return Results.Ok(new AccessRequestSendResult(
                Sent: false, DryRun: false, Demo: false, StatusCode: null,
                Error: "No webhook URL is stored — save the trigger URL in Settings → Access.",
                RenderedBody: body));
        }

        var result = await sender.SendAsync(url, body, ct).ConfigureAwait(false);
        return Results.Ok(result);
    }

    /// <summary>The artifact a dry-run test renders when no real connection is named —
    /// clearly a sample, so a rendered preview can't be mistaken for a real request.</summary>
    private static AccessRequestArtifact SampleArtifact(ResolvedPrincipal? principal) =>
        new(
            Role: "Azure Service Bus Data Receiver",
            Scope: "/subscriptions/00000000-0000-0000-0000-000000000000/resourceGroups/rg-sample" +
                   "/providers/Microsoft.ServiceBus/namespaces/sample-ns",
            Principal: principal ?? new ResolvedPrincipal(
                "00000000-0000-0000-0000-000000000001", "sample.user@example.com",
                "00000000-0000-0000-0000-000000000002", null, "Sample User"),
            Resource: "sample-namespace",
            SummaryText: "Sample access request — rendered for a webhook test, not a real row.",
            AzCommand: null,
            GrantStatement: null,
            RemedyKind: AccessRemedyKind.ArmRole,
            AssignmentKind: "permanent",
            WebhookConfigured: true);

    /// <summary>
    /// Whether a send would actually fire: the config exists, is enabled, and its URL
    /// credential resolves. Demo mode reports configured — the send endpoint answers the
    /// labeled simulated failure instead of POSTing, which keeps the demo's Send path
    /// honest and visible.
    /// </summary>
    private static bool IsWebhookConfigured(
        ProfileRepository profile,
        ICredentialStore credentials,
        DemoModeService demo) =>
        demo.IsDemoMode
        || (profile.Config.AccessRequest is { Enabled: true, UrlCredentialKey: { } key }
            && credentials.Get(key) is not null);

    /// <summary>The settings-facing view: no URL, only whether one is stored. Includes the
    /// effective template so the editor shows what would actually be sent.</summary>
    private static AccessRequestWebhookView WebhookViewOf(
        AccessRequestConfig? config,
        ICredentialStore credentials) =>
        new(
            Enabled: config?.Enabled ?? false,
            HasUrl: config?.UrlCredentialKey is { } key && credentials.Get(key) is not null,
            UrlCredentialKey: config?.UrlCredentialKey,
            BodyTemplate: config?.BodyTemplate,
            EffectiveBodyTemplate: string.IsNullOrWhiteSpace(config?.BodyTemplate)
                ? AccessRequestTemplate.DefaultBodyTemplate
                : config!.BodyTemplate!);

    /// <summary>
    /// The artifact-building core shared by <c>POST /api/access/request</c> and the send
    /// endpoint: resolves the row's connection + remedy, the signed-in principal (skipped
    /// for connection-string rows), the az command / grant statement, and the summary.
    /// Returns either the artifact or the error result — never both.
    /// </summary>
    private static async Task<(AccessRequestArtifact? Artifact, IResult? Error)> BuildArtifactAsync(
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
            return (null, ApiErrors.BadRequest(
                "featureArea, connectionKey and capability are required."));
        }

        var resolver = new AccessScopeResolver(profile, demo);
        var resolved = resolver.Resolve(request.FeatureArea, request.ConnectionKey);
        if (resolved is null)
        {
            return (null, ApiErrors.NotFound(
                $"No configured connection matches {request.FeatureArea}/{request.ConnectionKey}."));
        }

        var remedy = AccessScopeResolver.RemedyFor(request.Capability);

        // Connection-string connections never authenticate as the signed-in identity — there is
        // no Entra principal to grant anything to, so no ARM remedy applies regardless of the
        // capability's usual kind.
        var connectionStringAuth = string.Equals(
            resolved.AuthMode, "connectionString", StringComparison.OrdinalIgnoreCase);

        // Resolve the signed-in principal only when it can actually be granted something —
        // the token lookup can shell out to `az`, so don't pay it for connection-string rows.
        // Demo mode skips it entirely: the demo must never touch a real credential (the
        // DefaultAzureCredential chain probing az/IMDS can take seconds on a clean machine).
        ResolvedPrincipal? principal = null;
        if (!connectionStringAuth && !demo.IsDemoMode)
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
            // PIM assignment kinds stay reserved; the caller fills WebhookConfigured.
            AssignmentKind: "permanent",
            WebhookConfigured: false);

        return (artifact, null);
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

/// <summary>Body of <c>PUT /api/access/webhook</c>. <paramref name="Url"/> is the raw trigger
/// URL — accepted here once and written straight to the credential store; it is never persisted
/// or echoed back. <paramref name="ClearUrl"/> removes the stored URL. When neither is set the
/// existing URL is untouched.</summary>
internal sealed record SaveAccessRequestWebhookRequest(
    bool Enabled,
    string? BodyTemplate,
    string? Url,
    bool ClearUrl);

/// <summary>The webhook config view the settings UI renders — <see cref="HasUrl"/> is the
/// credential store's answer, so it stays true even if the OS store lost the entry.</summary>
internal sealed record AccessRequestWebhookView(
    bool Enabled,
    bool HasUrl,
    string? UrlCredentialKey,
    string? BodyTemplate,
    string EffectiveBodyTemplate);
