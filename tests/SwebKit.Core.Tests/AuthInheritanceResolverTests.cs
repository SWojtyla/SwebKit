using SwebKit.Core.Domain;
using SwebKit.Core.Services;

namespace SwebKit.Core.Tests;

public sealed class AuthInheritanceResolverTests
{
    private static readonly AuthInheritanceResolver Sut = new();

    // ── helpers ────────────────────────────────────────────────────────────────

    private static HttpRequestEntry Request(string id, AuthType? type = null) => new()
    {
        Id = id,
        Auth = type is null ? null : new AuthConfig { Type = type.Value },
    };

    private static ApiCollectionNode RequestNode(HttpRequestEntry req) => new()
    {
        Id = req.Id,
        Type = ApiCollectionNodeType.Request,
        Name = req.Name,
        Request = req,
    };

    private static ApiCollectionNode FolderNode(string name, AuthConfig? auth = null, params ApiCollectionNode[] children) => new()
    {
        Id = Guid.NewGuid().ToString(),
        Type = ApiCollectionNodeType.Folder,
        Name = name,
        DefaultAuth = auth,
        Children = [.. children],
    };

    private static AuthConfig BearerAuth(string key = "key1") => new() { Type = AuthType.BearerToken, CredentialKey = key };
    private static AuthConfig BasicAuth() => new() { Type = AuthType.Basic, BasicUsername = "alice" };
    private static AuthConfig ApiKeyAuth() => new() { Type = AuthType.ApiKey, ApiKeyParamName = "x-key" };

    // ── request has own explicit auth ──────────────────────────────────────────

    [Fact]
    public void Resolve_RequestHasBearer_ReturnsRequestAuthNoInheritedName()
    {
        var req = Request("r1", AuthType.BearerToken);
        req.Auth = BearerAuth("my-token");

        var col = new ApiCollection
        {
            Id = "c1",
            Name = "My Collection",
            Nodes = [RequestNode(req)],
        };

        var (auth, from) = Sut.Resolve(req, col);

        Assert.Equal(AuthType.BearerToken, auth.Type);
        Assert.Equal("my-token", auth.CredentialKey);
        Assert.Null(from);
    }

    [Fact]
    public void Resolve_RequestHasNoneExplicit_ReturnsNoneNoInheritedName()
    {
        var req = Request("r1", AuthType.None);

        var col = new ApiCollection
        {
            Id = "c1",
            Name = "My Collection",
            DefaultAuth = BearerAuth(),
            Nodes = [RequestNode(req)],
        };

        var (auth, from) = Sut.Resolve(req, col);

        Assert.Equal(AuthType.None, auth.Type);
        Assert.Null(from);
    }

    // ── request inherits from folder ───────────────────────────────────────────

    [Fact]
    public void Resolve_RequestNullAuth_FolderHasBearer_ReturnsFolderAuthAndFolderName()
    {
        var req = Request("r1");
        var folder = FolderNode("Auth Folder", BearerAuth("folder-token"), RequestNode(req));

        var col = new ApiCollection
        {
            Id = "c1",
            Name = "My Collection",
            Nodes = [folder],
        };

        var (auth, from) = Sut.Resolve(req, col);

        Assert.Equal(AuthType.BearerToken, auth.Type);
        Assert.Equal("folder-token", auth.CredentialKey);
        Assert.Equal("Auth Folder", from);
    }

    [Fact]
    public void Resolve_RequestInherited_FolderHasBasic_ReturnsFolderAuthAndFolderName()
    {
        var req = Request("r1", AuthType.Inherited);
        var folder = FolderNode("Secured", BasicAuth(), RequestNode(req));

        var col = new ApiCollection
        {
            Id = "c1",
            Name = "My Collection",
            Nodes = [folder],
        };

        var (auth, from) = Sut.Resolve(req, col);

        Assert.Equal(AuthType.Basic, auth.Type);
        Assert.Equal("Secured", from);
    }

    [Fact]
    public void Resolve_RequestNullAuth_NestedFolder_OuterFolderHasAuth_ReturnsNearestAncestor()
    {
        var req = Request("r1");
        var inner = FolderNode("Inner", null, RequestNode(req));
        var outer = FolderNode("Outer", ApiKeyAuth(), inner);

        var col = new ApiCollection
        {
            Id = "c1",
            Name = "My Collection",
            Nodes = [outer],
        };

        var (auth, from) = Sut.Resolve(req, col);

        Assert.Equal(AuthType.ApiKey, auth.Type);
        Assert.Equal("Outer", from); // inner has no auth → walks up to outer
    }

    [Fact]
    public void Resolve_RequestNullAuth_InnerFolderHasAuth_OuterFolderHasAuth_ReturnsInnerFolder()
    {
        var req = Request("r1");
        var inner = FolderNode("Inner Auth", BearerAuth("inner-token"), RequestNode(req));
        var outer = FolderNode("Outer Auth", BasicAuth(), inner);

        var col = new ApiCollection
        {
            Id = "c1",
            Name = "My Collection",
            Nodes = [outer],
        };

        var (auth, from) = Sut.Resolve(req, col);

        Assert.Equal(AuthType.BearerToken, auth.Type);
        Assert.Equal("inner-token", auth.CredentialKey);
        Assert.Equal("Inner Auth", from);
    }

    // ── request inherits from collection ──────────────────────────────────────

    [Fact]
    public void Resolve_RequestNullAuth_NoFolderAuth_CollectionHasBearer_ReturnsCollectionAuthAndCollectionName()
    {
        var req = Request("r1");
        var folder = FolderNode("No Auth Folder", null, RequestNode(req));

        var col = new ApiCollection
        {
            Id = "c1",
            Name = "My Collection",
            DefaultAuth = BearerAuth("col-token"),
            Nodes = [folder],
        };

        var (auth, from) = Sut.Resolve(req, col);

        Assert.Equal(AuthType.BearerToken, auth.Type);
        Assert.Equal("col-token", auth.CredentialKey);
        Assert.Equal("My Collection", from);
    }

    [Fact]
    public void Resolve_RequestNullAuth_RequestAtRootLevel_CollectionHasAuth_ReturnsCollectionAuth()
    {
        var req = Request("r1");

        var col = new ApiCollection
        {
            Id = "c1",
            Name = "Root Collection",
            DefaultAuth = BasicAuth(),
            Nodes = [RequestNode(req)],
        };

        var (auth, from) = Sut.Resolve(req, col);

        Assert.Equal(AuthType.Basic, auth.Type);
        Assert.Equal("Root Collection", from);
    }

    // ── nothing in the chain ──────────────────────────────────────────────────

    [Fact]
    public void Resolve_NothingConfigured_ReturnsNoneAndNullName()
    {
        var req = Request("r1");

        var col = new ApiCollection
        {
            Id = "c1",
            Name = "Empty Collection",
            Nodes = [RequestNode(req)],
        };

        var (auth, from) = Sut.Resolve(req, col);

        Assert.Equal(AuthType.None, auth.Type);
        Assert.Null(from);
    }

    [Fact]
    public void Resolve_CollectionAuthIsInherited_FallsBackToNone()
    {
        var req = Request("r1");

        var col = new ApiCollection
        {
            Id = "c1",
            Name = "My Collection",
            DefaultAuth = new AuthConfig { Type = AuthType.Inherited },
            Nodes = [RequestNode(req)],
        };

        var (auth, from) = Sut.Resolve(req, col);

        Assert.Equal(AuthType.None, auth.Type);
        Assert.Null(from);
    }

    // ── request not in collection ─────────────────────────────────────────────

    [Fact]
    public void Resolve_RequestNotInCollection_FallsBackToCollectionDefault()
    {
        var req = Request("orphan");

        var col = new ApiCollection
        {
            Id = "c1",
            Name = "My Collection",
            DefaultAuth = BearerAuth("col-token"),
            Nodes = [],
        };

        var (auth, from) = Sut.Resolve(req, col);

        // FindRequest returns false → falls through to collection default
        Assert.Equal(AuthType.BearerToken, auth.Type);
        Assert.Equal("My Collection", from);
    }
}
