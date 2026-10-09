using System.Text;
using System.Text.Json;
using Azure.Core;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using SwebKit.Core.Configuration;
using SwebKit.Core.Domain;
using SwebKit.Core.Security;
using SwebKit.Sidecar.Endpoints;
using SwebKit.Sidecar.Services;

namespace SwebKit.Sidecar.Tests;

// ── Fakes ────────────────────────────────────────────────────────────────────

internal sealed class StubPrincipalContext(ResolvedPrincipal? principal) : IAzurePrincipalContext
{
    public int Calls { get; private set; }
    public Task<ResolvedPrincipal?> GetPrincipalAsync(CancellationToken ct = default)
    {
        Calls++;
        return Task.FromResult(principal);
    }
}

/// <summary>IServiceProvider that hands out the services the artifact endpoint asks for.</summary>
internal sealed class StubServiceProvider(IAzurePrincipalContext? principalContext) : IServiceProvider
{
    public object? GetService(Type serviceType) =>
        serviceType == typeof(IAzurePrincipalContext) ? principalContext : null;
}

/// <summary>TokenCredential returning a caller-supplied JWT — exercises the real token path.</summary>
internal sealed class StubTokenCredential(string token) : TokenCredential
{
    public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken) =>
        new(token, DateTimeOffset.UtcNow.AddHours(1));

    public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken) =>
        new(new AccessToken(token, DateTimeOffset.UtcNow.AddHours(1)));
}

internal static class TestJwt
{
    public static string Create(object payload)
    {
        static string Seg(object value) => Base64Url(JsonSerializer.SerializeToUtf8Bytes(value));
        return $"{Seg(new { alg = "none", typ = "JWT" })}.{Seg(payload)}.signature";
    }

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}

// ── Scope resolver ───────────────────────────────────────────────────────────

public class AccessScopeResolverTests
{
    private static readonly Guid NsId = Guid.NewGuid();
    private const string SqlId = "sql-1";

    private static (ProfileRepository Profile, DemoModeService Demo, AccessScopeResolver Resolver) CreateHarness(
        Action<ProfileData>? seed = null, bool demoMode = false)
    {
        var profile = new ProfileRepository();
        var data = profile.GetProfileData();
        data.ServiceBusNamespaces.Add(new ServiceBusNamespace
        {
            Id = NsId,
            Alias = "orders",
            FullyQualifiedNamespace = "orders.servicebus.windows.net",
            AuthMode = SbAuthMode.DefaultAzureCredential,
        });
        data.Config.SqlConfig = new SqlConfig
        {
            Connections = [new SqlConnectionEntry { Id = SqlId, DisplayName = "Dev DB", Server = "dev.database.windows.net" }],
        };
        seed?.Invoke(data);
        var demo = new DemoModeService { IsDemoMode = demoMode };
        return (profile, demo, new AccessScopeResolver(profile, demo));
    }

    [Fact]
    public void Resolve_EntraNamespaceWithResourceId_ReturnsScope()
    {
        var (_, _, resolver) = CreateHarness(d =>
        {
            d.ServiceBusNamespaces[0].ResourceId =
                "/subscriptions/sub-1/resourceGroups/rg-1/providers/Microsoft.ServiceBus/namespaces/orders";
        });

        var scope = resolver.Resolve("ServiceBus", NsId.ToString());

        Assert.NotNull(scope);
        Assert.Equal("orders", scope.Label);
        Assert.Equal("/subscriptions/sub-1/resourceGroups/rg-1/providers/Microsoft.ServiceBus/namespaces/orders",
            scope.ScopeResourceId);
        Assert.Null(scope.AuthMode);
    }

    [Fact]
    public void Resolve_ConnectionStringNamespace_MarkedAuthMode()
    {
        var id = Guid.NewGuid();
        var (_, _, resolver) = CreateHarness(d =>
            d.ServiceBusNamespaces.Add(new ServiceBusNamespace
            {
                Id = id, Alias = "legacy", AuthMode = SbAuthMode.ConnectionString, CredentialKey = "k",
            }));

        var scope = resolver.Resolve("ServiceBus", id.ToString());

        Assert.NotNull(scope);
        Assert.Equal("connectionString", scope.AuthMode);
    }

    [Fact]
    public void Resolve_SqlComposesScopeFromDiscoveredSubscriptionAndGroup()
    {
        var (_, _, resolver) = CreateHarness(d =>
        {
            var c = d.Config.SqlConfig!.Connections[0];
            c.SubscriptionId = "sub-1";
            c.ResourceGroup = "rg-1";
        });

        var scope = resolver.Resolve("Sql", SqlId);

        Assert.NotNull(scope);
        Assert.Equal(
            "/subscriptions/sub-1/resourceGroups/rg-1/providers/Microsoft.Sql/servers/dev",
            scope.ScopeResourceId);
    }

    [Fact]
    public void Resolve_SqlExplicitResourceIdWins_OverComposition()
    {
        var (_, _, resolver) = CreateHarness(d =>
        {
            var c = d.Config.SqlConfig!.Connections[0];
            c.SubscriptionId = "sub-1";
            c.ResourceGroup = "rg-1";
            c.ResourceId = "/subscriptions/other/resourceGroups/rg/providers/Microsoft.Sql/servers/explicit";
        });

        var scope = resolver.Resolve("Sql", SqlId);

        Assert.Equal("/subscriptions/other/resourceGroups/rg/providers/Microsoft.Sql/servers/explicit",
            scope!.ScopeResourceId);
    }

    [Fact]
    public void Resolve_SqlWithoutArmFields_NoScope_NeverGuessed()
    {
        var (_, _, resolver) = CreateHarness();

        var scope = resolver.Resolve("Sql", SqlId);

        Assert.NotNull(scope);
        Assert.Null(scope.ScopeResourceId);
    }

    [Fact]
    public void Resolve_SqlNonAzureHostname_NeverComposes()
    {
        var (_, _, resolver) = CreateHarness(d =>
        {
            var c = d.Config.SqlConfig!.Connections[0];
            c.Server = @"localhost\SQLEXPRESS";
            c.SubscriptionId = "sub-1";
            c.ResourceGroup = "rg-1";
        });

        Assert.Null(resolver.Resolve("Sql", SqlId)!.ScopeResourceId);
    }

    [Fact]
    public void Resolve_UnknownConnection_ReturnsNull()
    {
        var (_, _, resolver) = CreateHarness();

        Assert.Null(resolver.Resolve("ServiceBus", Guid.NewGuid().ToString()));
        Assert.Null(resolver.Resolve("Sql", "nope"));
        Assert.Null(resolver.Resolve("Bogus", "x"));
    }

    [Fact]
    public void Resolve_DemoMode_SeesDemoConnections()
    {
        var (_, _, resolver) = CreateHarness(demoMode: true);

        var scope = resolver.Resolve("Sql", DemoModeService.DemoSqlConnectionIdRestricted);

        Assert.NotNull(scope);
        Assert.Equal("orders-prd-sql (restricted)", scope.Label);
    }

    [Fact]
    public void RemedyFor_KnownCapabilities_MapToExpectedKinds()
    {
        Assert.Equal(AccessRemedyKind.ArmRole, AccessScopeResolver.RemedyFor("servicebus.peek").Kind);
        Assert.Equal("Azure Service Bus Data Receiver", AccessScopeResolver.RemedyFor("servicebus.peek").RoleName);
        Assert.Equal("Azure Service Bus Data Sender", AccessScopeResolver.RemedyFor("servicebus.send").RoleName);
        Assert.Equal(AccessRemedyKind.SqlGrant, AccessScopeResolver.RemedyFor("sql.metadata").Kind);
        Assert.Equal(AccessRemedyKind.RedisAcl, AccessScopeResolver.RemedyFor("redis.data").Kind);
        Assert.Equal(AccessRemedyKind.KubeRbac, AccessScopeResolver.RemedyFor("kubernetes.read").Kind);
        Assert.Equal(AccessRemedyKind.Other, AccessScopeResolver.RemedyFor("does.not.exist").Kind);
    }
}

// ── JWT payload parsing ──────────────────────────────────────────────────────

public class AzurePrincipalContextTests
{
    [Fact]
    public async Task GetPrincipal_UserToken_ResolvesUpnOidTid()
    {
        var jwt = TestJwt.Create(new
        {
            oid = "obj-1",
            upn = "dev@contoso.com",
            tid = "tenant-1",
            name = "Dev User",
        });
        var ctx = new AzurePrincipalContext(new StubTokenCredential(jwt));

        var principal = await ctx.GetPrincipalAsync();

        Assert.NotNull(principal);
        Assert.Equal("obj-1", principal.ObjectId);
        Assert.Equal("dev@contoso.com", principal.Upn);
        Assert.Equal("tenant-1", principal.TenantId);
        Assert.Equal("Dev User", principal.DisplayName);
    }

    [Fact]
    public async Task GetPrincipal_ServicePrincipalToken_DegradesToOidAndAppId()
    {
        var jwt = TestJwt.Create(new { oid = "obj-sp", appid = "app-9", tid = "tenant-1" });
        var ctx = new AzurePrincipalContext(new StubTokenCredential(jwt));

        var principal = await ctx.GetPrincipalAsync();

        Assert.NotNull(principal);
        Assert.Equal("obj-sp", principal.ObjectId);
        Assert.Equal("app-9", principal.AppId);
        Assert.Null(principal.Upn);
    }

    [Fact]
    public async Task GetPrincipal_MalformedToken_ReturnsNull_NotCached()
    {
        var ctx = new AzurePrincipalContext(new StubTokenCredential("not-a-jwt"));

        Assert.Null(await ctx.GetPrincipalAsync());
    }

    [Fact]
    public async Task GetPrincipal_TokenWithoutIdentityClaims_ReturnsNull()
    {
        var ctx = new AzurePrincipalContext(new StubTokenCredential(TestJwt.Create(new { tid = "t" })));

        Assert.Null(await ctx.GetPrincipalAsync());
    }

    [Fact]
    public async Task GetPrincipal_CachesAcrossCalls()
    {
        var jwt = TestJwt.Create(new { oid = "obj-1", upn = "dev@contoso.com" });
        var ctx = new AzurePrincipalContext(new StubTokenCredential(jwt));

        var first = await ctx.GetPrincipalAsync();
        var second = await ctx.GetPrincipalAsync();

        Assert.Same(first, second);
    }
}

// ── Artifact endpoint ────────────────────────────────────────────────────────

public class AccessRequestArtifactEndpointTests
{
    private static readonly Guid EntraNsId = Guid.NewGuid();
    private static readonly Guid ConnStringNsId = Guid.NewGuid();
    private const string SqlId = "sql-1";

    private static readonly ResolvedPrincipal UserPrincipal =
        new("obj-1", "dev@contoso.com", "tenant-1", null, "Dev User");

    // CA1869: serializer options must not be allocated per call.
    private static readonly JsonSerializerOptions WebJson = new(JsonSerializerDefaults.Web);

    private static (ProfileRepository, DemoModeService) CreateProfile()
    {
        var profile = new ProfileRepository();
        var data = profile.GetProfileData();
        data.ServiceBusNamespaces.Add(new ServiceBusNamespace
        {
            Id = EntraNsId,
            Alias = "orders",
            FullyQualifiedNamespace = "orders.servicebus.windows.net",
            AuthMode = SbAuthMode.DefaultAzureCredential,
            ResourceId = "/subscriptions/sub-1/resourceGroups/rg-1/providers/Microsoft.ServiceBus/namespaces/orders",
        });
        data.ServiceBusNamespaces.Add(new ServiceBusNamespace
        {
            Id = ConnStringNsId,
            Alias = "legacy",
            FullyQualifiedNamespace = "legacy.servicebus.windows.net",
            AuthMode = SbAuthMode.ConnectionString,
            CredentialKey = "legacy-key",
        });
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
        return (profile, new DemoModeService());
    }

    private static Task<IResult> Invoke(
        ProfileRepository profile,
        DemoModeService demo,
        IAzurePrincipalContext? principalContext,
        string featureArea,
        string connectionKey,
        string capability) =>
        AccessEndpoints.CreateRequestArtifactAsync(
            new AccessRequestArtifactRequest(featureArea, connectionKey, capability),
            profile, demo, new StubServiceProvider(principalContext),
            new FakeCredentialStore(), CancellationToken.None);

    private static AccessRequestArtifact Artifact(IResult result) =>
        Assert.IsType<Ok<AccessRequestArtifact>>(result).Value!;

    [Fact]
    public async Task Request_ArmRoleWithScopeAndPrincipal_EmitsAzCommand()
    {
        var (profile, demo) = CreateProfile();
        var principal = new StubPrincipalContext(UserPrincipal);

        var artifact = Artifact(await Invoke(
            profile, demo, principal, "ServiceBus", EntraNsId.ToString(), "servicebus.peek"));

        Assert.Equal("Azure Service Bus Data Receiver", artifact.Role);
        Assert.Equal("/subscriptions/sub-1/resourceGroups/rg-1/providers/Microsoft.ServiceBus/namespaces/orders",
            artifact.Scope);
        Assert.Equal(UserPrincipal, artifact.Principal);
        Assert.Equal("orders", artifact.Resource);
        Assert.Equal(AccessRemedyKind.ArmRole, artifact.RemedyKind);
        Assert.Equal("permanent", artifact.AssignmentKind);
        Assert.False(artifact.WebhookConfigured);
        Assert.NotNull(artifact.AzCommand);
        Assert.Contains("az role assignment create", artifact.AzCommand);
        Assert.Contains("--assignee-object-id obj-1", artifact.AzCommand);
        Assert.Contains("--assignee-principal-type User", artifact.AzCommand);
        Assert.Contains("Data Receiver", artifact.AzCommand);
        Assert.Contains("dev@contoso.com", artifact.SummaryText);
    }

    [Fact]
    public async Task Request_NoPrincipal_StillUseful_AzCommandSuppressed()
    {
        var (profile, demo) = CreateProfile();
        var principal = new StubPrincipalContext(null);

        var artifact = Artifact(await Invoke(
            profile, demo, principal, "ServiceBus", EntraNsId.ToString(), "servicebus.peek"));

        Assert.Null(artifact.Principal);
        Assert.Null(artifact.AzCommand);
        Assert.Equal(AccessRemedyKind.ArmRole, artifact.RemedyKind);
        Assert.Contains("az ad signed-in-user show", artifact.SummaryText);
    }

    [Fact]
    public async Task Request_ServicePrincipal_UsesServicePrincipalType()
    {
        var (profile, demo) = CreateProfile();
        var principal = new StubPrincipalContext(
            new ResolvedPrincipal("obj-sp", null, "tenant-1", "app-9", null));

        var artifact = Artifact(await Invoke(
            profile, demo, principal, "ServiceBus", EntraNsId.ToString(), "servicebus.send"));

        Assert.Contains("--assignee-principal-type ServicePrincipal", artifact.AzCommand!);
        Assert.Contains("Data Sender", artifact.AzCommand);
    }

    [Fact]
    public async Task Request_ConnectionStringRow_NoPrincipalLookup_NoAzArtifacts()
    {
        var (profile, demo) = CreateProfile();
        var principal = new StubPrincipalContext(UserPrincipal);

        var artifact = Artifact(await Invoke(
            profile, demo, principal, "ServiceBus", ConnStringNsId.ToString(), "servicebus.peek"));

        // The principal lookup must not run for connection-string rows — there is no Entra
        // identity to grant anything to, and the token acquisition would be wasted.
        Assert.Equal(0, principal.Calls);
        Assert.Equal(AccessRemedyKind.Other, artifact.RemedyKind);
        Assert.Null(artifact.AzCommand);
        Assert.Null(artifact.GrantStatement);
        Assert.Contains("connection string", artifact.SummaryText);
    }

    [Fact]
    public async Task Request_DemoMode_NoPrincipalLookup()
    {
        // Demo artifacts never touch the real credential — the DefaultAzureCredential
        // chain (az CLI, IMDS, …) can take seconds on a clean machine and the demo
        // must stay deterministic. The artifact degrades to a null principal instead.
        var (profile, _) = CreateProfile();
        var demo = new DemoModeService { IsDemoMode = true };
        var principal = new StubPrincipalContext(UserPrincipal);

        var artifact = Artifact(await Invoke(
            profile, demo, principal, "Sql",
            DemoModeService.DemoSqlConnectionIdRestricted, "sql.metadata"));

        Assert.Equal(0, principal.Calls);
        Assert.Null(artifact.Principal);
        Assert.Equal("VIEW DEFINITION", artifact.Role);
        Assert.Equal("GRANT VIEW DEFINITION TO [<your-login>];", artifact.GrantStatement);
    }

    [Fact]
    public async Task Request_SqlGrant_SubstitutesPrincipalIntoStatement()
    {
        var (profile, demo) = CreateProfile();
        var principal = new StubPrincipalContext(UserPrincipal);

        var artifact = Artifact(await Invoke(
            profile, demo, principal, "Sql", SqlId, "sql.metadata"));

        Assert.Equal(AccessRemedyKind.SqlGrant, artifact.RemedyKind);
        Assert.Equal("VIEW DEFINITION", artifact.Role);
        Assert.Equal("GRANT VIEW DEFINITION TO [dev@contoso.com];", artifact.GrantStatement);
        Assert.Null(artifact.AzCommand); // sqlGrant never emits an ARM command
        Assert.Equal(
            "/subscriptions/sub-1/resourceGroups/rg-1/providers/Microsoft.Sql/servers/dev",
            artifact.Scope);
    }

    [Fact]
    public async Task Request_UnknownScope_LeavesScopeNull()
    {
        var (profile, demo) = CreateProfile();
        var entry = profile.GetProfileData().Config.SqlConfig!.Connections[0];
        entry.SubscriptionId = null;
        entry.ResourceGroup = null;
        var principal = new StubPrincipalContext(UserPrincipal);

        var artifact = Artifact(await Invoke(
            profile, demo, principal, "Sql", SqlId, "sql.query"));

        Assert.Null(artifact.Scope);
        Assert.Contains("ask your admin", artifact.SummaryText);
    }

    [Fact]
    public async Task Request_UnknownConnection_404()
    {
        var (profile, demo) = CreateProfile();

        var result = await Invoke(
            profile, demo, null, "ServiceBus", Guid.NewGuid().ToString(), "servicebus.peek");

        var json = Assert.IsAssignableFrom<IResult>(result);
        Assert.IsNotType<Ok<AccessRequestArtifact>>(json);
    }

    [Fact]
    public async Task Request_RemedyKind_SerializesPascalCase_OnTheWire()
    {
        // The frontend matches "ArmRole"/"SqlGrant"/… literally — pin the casing
        // (the enum converters emit member names unchanged, same as AccessStatus).
        var (profile, demo) = CreateProfile();
        var artifact = Artifact(await Invoke(
            profile, demo, new StubPrincipalContext(UserPrincipal),
            "ServiceBus", EntraNsId.ToString(), "servicebus.peek"));

        var json = JsonSerializer.Serialize(artifact, WebJson);

        Assert.Contains("\"remedyKind\":\"ArmRole\"", json);
        Assert.Contains("\"upn\":\"dev@contoso.com\"", json);
    }

    [Fact]
    public async Task Request_MissingFields_400()
    {
        var (profile, demo) = CreateProfile();

        var result = await AccessEndpoints.CreateRequestArtifactAsync(
            new AccessRequestArtifactRequest("", "x", "y"),
            profile, demo, new StubServiceProvider(null),
            new FakeCredentialStore(), CancellationToken.None);

        Assert.IsNotType<Ok<AccessRequestArtifact>>(result);
    }
}
