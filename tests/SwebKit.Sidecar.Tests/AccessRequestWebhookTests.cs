using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using SwebKit.Core.Configuration;
using SwebKit.Core.Domain;
using SwebKit.Core.Security;
using SwebKit.Sidecar.Endpoints;
using SwebKit.Sidecar.Services;

namespace SwebKit.Sidecar.Tests;

// ── Fakes ────────────────────────────────────────────────────────────────────
// FakeCredentialStore lives in SidecarAuthHeaderBuilderTests.cs (shared in this assembly).

/// <summary><see cref="HttpMessageHandler"/> stub — captures the request and answers with a
/// canned response (or throws), so sender tests never touch the network.</summary>
internal sealed class StubHttpHandler(Func<HttpRequestMessage, HttpResponseMessage> respond)
    : HttpMessageHandler
{
    public HttpRequestMessage? LastRequest { get; private set; }
    public string? LastBody { get; private set; }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        LastRequest = request;
        LastBody = request.Content is null
            ? null
            : await request.Content.ReadAsStringAsync(cancellationToken);
        return respond(request);
    }
}

// ── Body template rendering ──────────────────────────────────────────────────

public class AccessRequestTemplateTests
{
    private static readonly ResolvedPrincipal UserPrincipal =
        new("obj-1", "dev@contoso.com", "tenant-1", null, "Dev User");

    private static AccessRequestArtifact Artifact(
        string role = "Azure Service Bus Data Receiver",
        string? scope = "/subscriptions/sub-1/resourceGroups/rg-1/providers/Microsoft.ServiceBus/namespaces/orders",
        ResolvedPrincipal? principal = null,
        string resource = "orders",
        string summary = "Access request: …") =>
        new(role, scope, principal ?? UserPrincipal, resource, summary,
            null, null, AccessRemedyKind.ArmRole, "permanent", true);

    private static Dictionary<string, string?> Values(AccessRequestArtifact? artifact = null) =>
        AccessRequestTemplate.BuildValues(
            artifact ?? Artifact(), "ServiceBus", "servicebus.peek",
            justification: "need it for the migration",
            timestamp: new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero));

    [Fact]
    public void Render_QuotedPlaceholder_SubstitutesContent()
    {
        var rendered = AccessRequestTemplate.Render(
            """{"role": "{role}", "upn": "{upn}"}""", Values());

        var doc = JsonDocument.Parse(rendered);
        Assert.Equal("Azure Service Bus Data Receiver", doc.RootElement.GetProperty("role").GetString());
        Assert.Equal("dev@contoso.com", doc.RootElement.GetProperty("upn").GetString());
    }

    [Fact]
    public void Render_UnquotedPlaceholder_EmitsJsonLiteral()
    {
        var rendered = AccessRequestTemplate.Render(
            """{"scope": {scope}}""", Values());

        var doc = JsonDocument.Parse(rendered);
        Assert.Equal(
            "/subscriptions/sub-1/resourceGroups/rg-1/providers/Microsoft.ServiceBus/namespaces/orders",
            doc.RootElement.GetProperty("scope").GetString());
    }

    [Fact]
    public void Render_MissingValue_NullUnquoted_EmptyInsideString()
    {
        var rendered = AccessRequestTemplate.Render(
            """{"scope": {scope}, "label": "scope is {scope}!"}""",
            Values(Artifact(scope: null)));

        var doc = JsonDocument.Parse(rendered);
        Assert.Equal(JsonValueKind.Null, doc.RootElement.GetProperty("scope").ValueKind);
        Assert.Equal("scope is !", doc.RootElement.GetProperty("label").GetString());
    }

    [Fact]
    public void Render_ValueEscaping_QuoteBackslashNewline_StayInsideTheString()
    {
        var artifact = Artifact(resource: """
            say "hi" \n real
            newline
            """);
        var rendered = AccessRequestTemplate.Render(
            """{"resource": "{resource}"}""",
            Values(artifact));

        var doc = JsonDocument.Parse(rendered);
        Assert.Equal(artifact.Resource, doc.RootElement.GetProperty("resource").GetString());
    }

    [Fact]
    public void Render_InterpolatesInsideLongerStrings()
    {
        var rendered = AccessRequestTemplate.Render(
            """{"text": "Please grant {role} to {upn} on {resource}."}""", Values());

        var doc = JsonDocument.Parse(rendered);
        Assert.Equal(
            "Please grant Azure Service Bus Data Receiver to dev@contoso.com on orders.",
            doc.RootElement.GetProperty("text").GetString());
    }

    [Fact]
    public void Render_UnknownOrBracelessTokens_PassThroughLiterally()
    {
        var rendered = AccessRequestTemplate.Render(
            """{"a": "{bogus}", "b": {}, "c": "{ role }", "d": "{role}"}""", Values());

        var doc = JsonDocument.Parse(rendered);
        Assert.Equal("{bogus}", doc.RootElement.GetProperty("a").GetString());
        Assert.Equal("{ role }", doc.RootElement.GetProperty("c").GetString()); // literal, untouched
        Assert.Equal("Azure Service Bus Data Receiver", doc.RootElement.GetProperty("d").GetString());
    }

    [Fact]
    public void Render_TemplateEscapedQuote_DoesNotEndTheStringContext()
    {
        // \" inside the template's own JSON must not be mistaken for a string end —
        // otherwise {role} would render as a bare literal mid-string.
        var rendered = AccessRequestTemplate.Render(
            """{"note": "she said \"hi\" to {upn}"}""", Values());

        var doc = JsonDocument.Parse(rendered);
        Assert.Equal("she said \"hi\" to dev@contoso.com",
            doc.RootElement.GetProperty("note").GetString());
    }

    [Fact]
    public void Render_AllDocumentedPlaceholders_Substitute()
    {
        const string template = """
            {
              "role": "{role}", "scope": "{scope}", "principal": "{principal}",
              "upn": "{upn}", "objectId": "{objectId}", "principalId": "{principalId}",
              "resource": "{resource}", "featureArea": "{featureArea}",
              "capability": "{capability}", "justification": "{justification}",
              "summary": "{summary}", "timestamp": "{timestamp}"
            }
            """;

        var rendered = AccessRequestTemplate.Render(template, Values());
        var doc = JsonDocument.Parse(rendered).RootElement;

        Assert.Equal("Azure Service Bus Data Receiver", doc.GetProperty("role").GetString());
        Assert.Equal("dev@contoso.com", doc.GetProperty("principal").GetString());
        Assert.Equal("dev@contoso.com", doc.GetProperty("upn").GetString());
        Assert.Equal("obj-1", doc.GetProperty("objectId").GetString());
        Assert.Equal("obj-1", doc.GetProperty("principalId").GetString());
        Assert.Equal("orders", doc.GetProperty("resource").GetString());
        Assert.Equal("ServiceBus", doc.GetProperty("featureArea").GetString());
        Assert.Equal("servicebus.peek", doc.GetProperty("capability").GetString());
        Assert.Equal("need it for the migration", doc.GetProperty("justification").GetString());
        Assert.Equal("Access request: …", doc.GetProperty("summary").GetString());
        Assert.Equal("2026-01-02T03:04:05.0000000+00:00", doc.GetProperty("timestamp").GetString());
    }

    [Fact]
    public void Render_NoPrincipal_PrincipalFieldsDegrade()
    {
        var artifact = new AccessRequestArtifact(
            "role-x", null, null, "res", "sum", null, null,
            AccessRemedyKind.Other, "permanent", true);
        var rendered = AccessRequestTemplate.Render(
            """{"principal": {principal}, "upn": "{upn}"}""",
            Values(artifact));

        var doc = JsonDocument.Parse(rendered);
        Assert.Equal(JsonValueKind.Null, doc.RootElement.GetProperty("principal").ValueKind);
        Assert.Equal(string.Empty, doc.RootElement.GetProperty("upn").GetString());
    }

    [Fact]
    public void Render_EmptyJustification_RendersEmpty()
    {
        var values = AccessRequestTemplate.BuildValues(
            Artifact(), "Sql", "sql.query", justification: null,
            timestamp: DateTimeOffset.UtcNow);
        var rendered = AccessRequestTemplate.Render("""{"justification": "{justification}"}""", values);

        Assert.Equal(string.Empty,
            JsonDocument.Parse(rendered).RootElement.GetProperty("justification").GetString());
    }

    [Fact]
    public void DefaultTemplate_RendersToValidJson()
    {
        var rendered = AccessRequestTemplate.Render(
            AccessRequestTemplate.DefaultBodyTemplate, Values());
        Assert.NotNull(JsonDocument.Parse(rendered));
    }
}

// ── Sender ───────────────────────────────────────────────────────────────────

public class AccessRequestSenderTests
{
    private const string Url = "https://flow.example.com/triggers/manual/paths/invoke?sig=SECRET";

    private static AccessRequestSender Sender(Func<HttpRequestMessage, HttpResponseMessage> respond,
        out StubHttpHandler handler)
    {
        handler = new StubHttpHandler(respond);
        return new AccessRequestSender(new HttpClient(handler));
    }

    [Fact]
    public async Task Send_2xx_ReportsSentWithStatus_AndPostsTheRenderedBody()
    {
        var sender = Sender(_ => new HttpResponseMessage(HttpStatusCode.Accepted), out var handler);

        var result = await sender.SendAsync(Url, """{"role":"x"}""", CancellationToken.None);

        Assert.True(result.Sent);
        Assert.Equal(202, result.StatusCode);
        Assert.Null(result.Error);
        Assert.Equal("""{"role":"x"}""", result.RenderedBody);
        Assert.Equal("""{"role":"x"}""", handler.LastBody);
        Assert.Equal("application/json", handler.LastRequest!.Content!.Headers.ContentType!.MediaType);
    }

    [Fact]
    public async Task Send_Non2xx_ReportsStatusAndBodySnippet()
    {
        var sender = Sender(_ => new HttpResponseMessage(HttpStatusCode.Forbidden)
        {
            Content = new StringContent("""{"error":{"message":"trigger disabled"}}"""),
        }, out _);

        var result = await sender.SendAsync(Url, "{}", CancellationToken.None);

        Assert.False(result.Sent);
        Assert.Equal(403, result.StatusCode);
        Assert.Contains("HTTP 403", result.Error);
        Assert.Contains("trigger disabled", result.Error);
    }

    [Fact]
    public async Task Send_Timeout_ReportsTimeout_NotCrash()
    {
        var sender = Sender(_ => throw new TaskCanceledException("timeout"), out _);

        var result = await sender.SendAsync(Url, "{}", CancellationToken.None);

        Assert.False(result.Sent);
        Assert.Null(result.StatusCode);
        Assert.Contains("didn't answer", result.Error);
    }

    [Fact]
    public async Task Send_HttpRequestException_NeverLeaksTheSasUrl()
    {
        // HttpRequestException messages embed the request URI — the URL carries the SAS sig,
        // so the surfaced error must be built from the error category, not the message.
        var sender = Sender(_ => throw new HttpRequestException(
            $"No such host: {Url}", null, HttpStatusCode.NotFound), out _);

        var result = await sender.SendAsync(Url, "{}", CancellationToken.None);

        Assert.False(result.Sent);
        Assert.DoesNotContain("sig=SECRET", result.Error);
        Assert.DoesNotContain("flow.example.com", result.Error);
    }

    [Fact]
    public async Task Send_NonHttpsUrl_RefusedBeforeAnyHttp()
    {
        var sender = Sender(_ => new HttpResponseMessage(HttpStatusCode.OK), out var handler);

        var result = await sender.SendAsync("http://insecure.example.com/hook", "{}", CancellationToken.None);

        Assert.False(result.Sent);
        Assert.Null(handler.LastRequest); // never attempted
        Assert.Contains("https://", result.Error);
    }
}

// ── Webhook config + send endpoints ─────────────────────────────────────────

public class AccessRequestWebhookEndpointTests
{
    private const string SqlId = "sql-1";

    private static readonly ResolvedPrincipal UserPrincipal =
        new("obj-1", "dev@contoso.com", "tenant-1", null, "Dev User");

    private static (ProfileRepository Profile, DemoModeService Demo, FakeCredentialStore Store)
        CreateHarness(bool demoMode = false)
    {
        var profile = new ProfileRepository();
        var data = profile.GetProfileData();
        data.Config.SqlConfig = new SqlConfig
        {
            Connections =
            [
                new SqlConnectionEntry
                {
                    Id = SqlId,
                    DisplayName = "Dev DB",
                    Server = "dev.database.windows.net",
                    SubscriptionId = "sub-1",
                    ResourceGroup = "rg-1",
                },
            ],
        };
        return (profile, new DemoModeService { IsDemoMode = demoMode }, new FakeCredentialStore());
    }

    private static AccessRequestSender OkSender() =>
        new(new HttpClient(new StubHttpHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK))));

    private static AccessRequestSendResult Send(IResult result) =>
        Assert.IsType<Ok<AccessRequestSendResult>>(result).Value!;

    private static AccessRequestWebhookView View(IResult result) =>
        Assert.IsType<Ok<AccessRequestWebhookView>>(result).Value!;

    // ── Config round-trip ────────────────────────────────────────────────────

    [Fact]
    public async Task Webhook_SaveWithUrl_WritesCredentialStore_NeverProfile()
    {
        using var sandbox = new AppDataSandbox();
        var (profile, _, store) = CreateHarness();

        var view = View(await AccessEndpoints.SaveWebhookConfigAsync(
            new SaveAccessRequestWebhookRequest(
                Enabled: true,
                BodyTemplate: """{"role": "{role}"}""",
                Url: "https://flow.example.com/trigger?sig=SAS",
                ClearUrl: false),
            profile, store));

        Assert.True(view.Enabled);
        Assert.True(view.HasUrl);
        // The URL itself lives only in the credential store — the profile holds the key.
        Assert.Equal(AccessRequestConfig.DefaultUrlCredentialKey, view.UrlCredentialKey);
        Assert.Equal("https://flow.example.com/trigger?sig=SAS",
            store.Get(AccessRequestConfig.DefaultUrlCredentialKey));
        Assert.Equal(
            """{"role": "{role}"}""",
            profile.Config.AccessRequest!.BodyTemplate);
    }

    [Fact]
    public async Task Webhook_SaveWithoutUrl_KeepsStoredSecret()
    {
        using var sandbox = new AppDataSandbox();
        var (profile, _, store) = CreateHarness();
        store.Save(AccessRequestConfig.DefaultUrlCredentialKey, "https://keep.me/trigger");
        profile.Config.AccessRequest = new AccessRequestConfig
        {
            Enabled = true,
            UrlCredentialKey = AccessRequestConfig.DefaultUrlCredentialKey,
        };

        var view = View(await AccessEndpoints.SaveWebhookConfigAsync(
            new SaveAccessRequestWebhookRequest(false, null, null, false),
            profile, store));

        Assert.True(view.HasUrl);
        Assert.False(view.Enabled);
        Assert.Equal("https://keep.me/trigger",
            store.Get(AccessRequestConfig.DefaultUrlCredentialKey));
    }

    [Fact]
    public async Task Webhook_ClearUrl_DeletesCredential()
    {
        using var sandbox = new AppDataSandbox();
        var (profile, _, store) = CreateHarness();
        store.Save(AccessRequestConfig.DefaultUrlCredentialKey, "https://gone.example.com");
        profile.Config.AccessRequest = new AccessRequestConfig
        {
            Enabled = true,
            UrlCredentialKey = AccessRequestConfig.DefaultUrlCredentialKey,
        };

        var view = View(await AccessEndpoints.SaveWebhookConfigAsync(
            new SaveAccessRequestWebhookRequest(true, null, null, ClearUrl: true),
            profile, store));

        Assert.False(view.HasUrl);
        Assert.Null(view.UrlCredentialKey);
        Assert.Null(store.Get(AccessRequestConfig.DefaultUrlCredentialKey));
    }

    [Fact]
    public async Task Webhook_NonHttpsUrl_Rejected()
    {
        using var sandbox = new AppDataSandbox();
        var (profile, _, store) = CreateHarness();

        var result = await AccessEndpoints.SaveWebhookConfigAsync(
            new SaveAccessRequestWebhookRequest(true, null, "http://nope", false),
            profile, store);

        Assert.IsNotType<Ok<AccessRequestWebhookView>>(result);
        Assert.Null(profile.Config.AccessRequest);
    }

    [Fact]
    public void Webhook_Get_WithoutConfig_ReportsUnconfigured()
    {
        var (profile, _, store) = CreateHarness();

        var view = View(AccessEndpoints.GetWebhookConfig(profile, store));

        Assert.False(view.Enabled);
        Assert.False(view.HasUrl);
        Assert.Equal(AccessRequestTemplate.DefaultBodyTemplate, view.EffectiveBodyTemplate);
    }

    // ── webhookConfigured on the artifact ────────────────────────────────────

    [Fact]
    public async Task Artifact_WebhookConfiguredOnly_WhenEnabledAndUrlStored()
    {
        var (profile, demo, store) = CreateHarness();

        var notConfigured = Assert.IsType<Ok<AccessRequestArtifact>>(
            await AccessEndpoints.CreateRequestArtifactAsync(
                new AccessRequestArtifactRequest("Sql", SqlId, "sql.query"),
                profile, demo, new StubServiceProvider(new StubPrincipalContext(UserPrincipal)),
                store, CancellationToken.None));
        Assert.False(notConfigured.Value!.WebhookConfigured);

        store.Save(AccessRequestConfig.DefaultUrlCredentialKey, "https://flow.example.com/x");
        profile.Config.AccessRequest = new AccessRequestConfig
        {
            Enabled = false, // stored URL but switched off — still not sendable
            UrlCredentialKey = AccessRequestConfig.DefaultUrlCredentialKey,
        };
        var disabled = Assert.IsType<Ok<AccessRequestArtifact>>(
            await AccessEndpoints.CreateRequestArtifactAsync(
                new AccessRequestArtifactRequest("Sql", SqlId, "sql.query"),
                profile, demo, new StubServiceProvider(new StubPrincipalContext(UserPrincipal)),
                store, CancellationToken.None));
        Assert.False(disabled.Value!.WebhookConfigured);

        profile.Config.AccessRequest.Enabled = true;
        var configured = Assert.IsType<Ok<AccessRequestArtifact>>(
            await AccessEndpoints.CreateRequestArtifactAsync(
                new AccessRequestArtifactRequest("Sql", SqlId, "sql.query"),
                profile, demo, new StubServiceProvider(new StubPrincipalContext(UserPrincipal)),
                store, CancellationToken.None));
        Assert.True(configured.Value!.WebhookConfigured);
    }

    // ── Send ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Send_Configured_PostsRenderedBody_AndReports2xx()
    {
        var (profile, demo, store) = CreateHarness();
        store.Save(AccessRequestConfig.DefaultUrlCredentialKey, "https://flow.example.com/go");
        profile.Config.AccessRequest = new AccessRequestConfig
        {
            Enabled = true,
            UrlCredentialKey = AccessRequestConfig.DefaultUrlCredentialKey,
            BodyTemplate = """{"who": "{upn}", "needs": "{capability} on {resource}"}""",
        };
        var handler = new StubHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var sender = new AccessRequestSender(new HttpClient(handler));

        var result = Send(await AccessEndpoints.SendRequestAsync(
            new AccessRequestSendRequest("Sql", SqlId, "sql.query",
                Justification: "reading the docs"),
            profile, demo, new StubServiceProvider(new StubPrincipalContext(UserPrincipal)),
            store, sender, CancellationToken.None));

        Assert.True(result.Sent);
        Assert.Equal(200, result.StatusCode);
        var posted = JsonDocument.Parse(handler.LastBody!).RootElement;
        Assert.Equal("dev@contoso.com", posted.GetProperty("who").GetString());
        Assert.Equal("sql.query on Dev DB", posted.GetProperty("needs").GetString());
    }

    [Fact]
    public async Task Send_NotConfigured_FailsHonestly_WithoutSending()
    {
        var (profile, demo, store) = CreateHarness();
        var handler = new StubHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var sender = new AccessRequestSender(new HttpClient(handler));

        var result = Send(await AccessEndpoints.SendRequestAsync(
            new AccessRequestSendRequest("Sql", SqlId, "sql.query"),
            profile, demo, new StubServiceProvider(new StubPrincipalContext(UserPrincipal)),
            store, sender, CancellationToken.None));

        Assert.False(result.Sent);
        Assert.Null(result.StatusCode);
        Assert.Contains("webhook", result.Error);
        Assert.Null(handler.LastRequest);
    }

    [Fact]
    public async Task Send_EnabledButUrlMissingFromStore_FailsHonestly()
    {
        var (profile, demo, store) = CreateHarness();
        profile.Config.AccessRequest = new AccessRequestConfig
        {
            Enabled = true,
            UrlCredentialKey = AccessRequestConfig.DefaultUrlCredentialKey, // nothing stored under it
        };

        var result = Send(await AccessEndpoints.SendRequestAsync(
            new AccessRequestSendRequest("Sql", SqlId, "sql.query"),
            profile, demo, new StubServiceProvider(new StubPrincipalContext(UserPrincipal)),
            store, OkSender(), CancellationToken.None));

        Assert.False(result.Sent);
        Assert.Contains("URL", result.Error);
    }

    [Fact]
    public async Task Send_DryRun_RendersBody_NeverPosts()
    {
        var (profile, demo, store) = CreateHarness();
        store.Save(AccessRequestConfig.DefaultUrlCredentialKey, "https://flow.example.com/go");
        profile.Config.AccessRequest = new AccessRequestConfig
        {
            Enabled = true,
            UrlCredentialKey = AccessRequestConfig.DefaultUrlCredentialKey,
        };
        var handler = new StubHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var sender = new AccessRequestSender(new HttpClient(handler));

        var result = Send(await AccessEndpoints.SendRequestAsync(
            new AccessRequestSendRequest("Sql", SqlId, "sql.query", DryRun: true),
            profile, demo, new StubServiceProvider(new StubPrincipalContext(UserPrincipal)),
            store, sender, CancellationToken.None));

        Assert.True(result.DryRun);
        Assert.False(result.Sent);
        Assert.NotNull(result.RenderedBody);
        Assert.Equal("Dev DB",
            JsonDocument.Parse(result.RenderedBody).RootElement.GetProperty("resource").GetString());
        Assert.Null(handler.LastRequest);
    }

    [Fact]
    public async Task Send_DryRun_UnknownConnection_RendersSample()
    {
        var (profile, demo, store) = CreateHarness();

        var result = Send(await AccessEndpoints.SendRequestAsync(
            new AccessRequestSendRequest("test", "test", "test", DryRun: true),
            profile, demo, new StubServiceProvider(new StubPrincipalContext(UserPrincipal)),
            store, OkSender(), CancellationToken.None));

        Assert.True(result.DryRun);
        var body = JsonDocument.Parse(result.RenderedBody!).RootElement;
        Assert.Equal("dev@contoso.com", body.GetProperty("upn").GetString());
    }

    [Fact]
    public async Task Send_UnknownConnection_NotDryRun_404()
    {
        var (profile, demo, store) = CreateHarness();

        var result = await AccessEndpoints.SendRequestAsync(
            new AccessRequestSendRequest("Sql", "missing", "sql.query"),
            profile, demo, new StubServiceProvider(null), store, OkSender(),
            CancellationToken.None);

        Assert.IsNotType<Ok<AccessRequestSendResult>>(result);
    }

    [Fact]
    public async Task Send_DemoMode_ReturnsLabeledSimulated403_WithoutPosting()
    {
        var (profile, demo, store) = CreateHarness(demoMode: true);
        var handler = new StubHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var sender = new AccessRequestSender(new HttpClient(handler));

        var result = Send(await AccessEndpoints.SendRequestAsync(
            new AccessRequestSendRequest("Sql", DemoModeService.DemoSqlConnectionIdRestricted, "sql.metadata"),
            profile, demo, new StubServiceProvider(new StubPrincipalContext(UserPrincipal)),
            store, sender, CancellationToken.None));

        Assert.True(result.Demo);
        Assert.False(result.Sent);
        Assert.Equal(403, result.StatusCode);
        Assert.Contains("Demo", result.Error);
        Assert.Null(handler.LastRequest); // nothing left the machine
    }

    [Fact]
    public async Task Send_BrokenTemplate_ReportsInvalidJson_WithoutPosting()
    {
        var (profile, demo, store) = CreateHarness();
        store.Save(AccessRequestConfig.DefaultUrlCredentialKey, "https://flow.example.com/go");
        profile.Config.AccessRequest = new AccessRequestConfig
        {
            Enabled = true,
            UrlCredentialKey = AccessRequestConfig.DefaultUrlCredentialKey,
            BodyTemplate = """{"role": {role}""", // unbalanced
        };
        var handler = new StubHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var sender = new AccessRequestSender(new HttpClient(handler));

        var result = Send(await AccessEndpoints.SendRequestAsync(
            new AccessRequestSendRequest("Sql", SqlId, "sql.query"),
            profile, demo, new StubServiceProvider(new StubPrincipalContext(UserPrincipal)),
            store, sender, CancellationToken.None));

        Assert.False(result.Sent);
        Assert.Contains("valid JSON", result.Error);
        Assert.Null(handler.LastRequest);
    }

    [Fact]
    public async Task Send_MissingFields_400()
    {
        var (profile, demo, store) = CreateHarness();

        var result = await AccessEndpoints.SendRequestAsync(
            new AccessRequestSendRequest("", "x", "y"),
            profile, demo, new StubServiceProvider(null), store, OkSender(),
            CancellationToken.None);

        Assert.IsNotType<Ok<AccessRequestSendResult>>(result);
    }
}
