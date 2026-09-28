using System.Text.Json;
using SwebKit.Agents;
using SwebKit.Agents.Tools;
using SwebKit.Core.Security;
using Xunit;

namespace SwebKit.Agents.Tests;

public class ScreenStateStoreTests
{
    private static ScreenStateSnapshot Snapshot(string route = "/aks") => new()
    {
        Route = route,
        FeatureArea = "Aks",
        CapturedAt = DateTimeOffset.UtcNow,
        Snapshot = JsonDocument.Parse("""{"pod":"api-7c9f"}""").RootElement,
    };

    [Fact]
    public void Current_NothingPublished_ReturnsNull()
    {
        Assert.Null(new ScreenStateStore().Current);
    }

    [Fact]
    public void Publish_ThenCurrent_ReturnsLatestSnapshot_LatestWins()
    {
        var store = new ScreenStateStore();
        store.Publish(Snapshot("/aks"));
        store.Publish(Snapshot("/redis"));

        Assert.Equal("/redis", store.Current?.Route);
    }

    [Fact]
    public void Current_SnapshotOlderThanTtl_ReturnsNull()
    {
        var store = new ScreenStateStore();
        var stale = Snapshot();
        stale.ReceivedAt = DateTimeOffset.UtcNow - ScreenStateStore.Ttl - TimeSpan.FromSeconds(1);
        store.Publish(stale);

        Assert.Null(store.Current);
    }
}

public class GetScreenStateToolTests
{
    private static readonly JsonElement EmptyArgs = JsonDocument.Parse("{}").RootElement;

    [Fact]
    public async Task Execute_NothingPublished_ReturnsAvailableFalse()
    {
        var tool = new GetScreenStateTool(new ScreenStateStore());

        using var doc = JsonDocument.Parse(await tool.ExecuteAsync(EmptyArgs, CancellationToken.None));

        Assert.False(doc.RootElement.GetProperty("available").GetBoolean());
    }

    [Fact]
    public async Task Execute_PublishedSnapshot_ReturnsRouteAreaAndSnapshot_WithAge()
    {
        var store = new ScreenStateStore();
        store.Publish(new ScreenStateSnapshot
        {
            Route = "/service-bus",
            FeatureArea = "ServiceBus",
            CapturedAt = DateTimeOffset.UtcNow.AddSeconds(-5),
            Snapshot = JsonDocument.Parse("""{"entity":"orders"}""").RootElement,
        });
        var tool = new GetScreenStateTool(store);

        using var doc = JsonDocument.Parse(await tool.ExecuteAsync(EmptyArgs, CancellationToken.None));

        var root = doc.RootElement;
        Assert.True(root.GetProperty("available").GetBoolean());
        Assert.Equal("/service-bus", root.GetProperty("route").GetString());
        Assert.Equal("ServiceBus", root.GetProperty("featureArea").GetString());
        Assert.Equal("orders", root.GetProperty("snapshot").GetProperty("entity").GetString());
        Assert.True(root.GetProperty("ageSeconds").GetInt32() >= 5);
    }

    [Fact]
    public async Task Execute_ExpiredSnapshot_ReturnsAvailableFalse()
    {
        var store = new ScreenStateStore();
        store.Publish(new ScreenStateSnapshot
        {
            Route = "/aks",
            CapturedAt = DateTimeOffset.UtcNow,
            Snapshot = JsonDocument.Parse("{}").RootElement,
            ReceivedAt = DateTimeOffset.UtcNow - ScreenStateStore.Ttl - TimeSpan.FromSeconds(1),
        });
        var tool = new GetScreenStateTool(store);

        using var doc = JsonDocument.Parse(await tool.ExecuteAsync(EmptyArgs, CancellationToken.None));

        Assert.False(doc.RootElement.GetProperty("available").GetBoolean());
    }

    [Fact]
    public async Task Execute_PublishedEntities_ListsEntityIdsInOverview()
    {
        var store = new ScreenStateStore();
        store.Publish(new ScreenStateSnapshot
        {
            Route = "/sql",
            FeatureArea = "Sql",
            CapturedAt = DateTimeOffset.UtcNow,
            Snapshot = JsonDocument.Parse("""{"connection":"prod"}""").RootElement,
            Entities = new Dictionary<string, JsonElement>
            {
                // Deliberately unsorted — the overview output orders them.
                ["sql.table.dbo.orders"] = JsonDocument.Parse("""{"columns":["id"]}""").RootElement,
                ["sql.connection.c1"] = JsonDocument.Parse("""{"displayName":"Prod"}""").RootElement,
            },
        });
        var tool = new GetScreenStateTool(store);

        using var doc = JsonDocument.Parse(await tool.ExecuteAsync(EmptyArgs, CancellationToken.None));

        var ids = doc.RootElement.GetProperty("entities").EnumerateArray()
            .Select(e => e.GetString()!).ToList();
        Assert.Equal(new[] { "sql.connection.c1", "sql.table.dbo.orders" }, ids);
        // The overview carries ids only — entity detail payloads stay out of it.
        Assert.DoesNotContain("displayName", doc.RootElement.GetRawText());
    }
}

public class ScreenStateEntityIdTests
{
    [Theory]
    [InlineData("sql.table.dbo.orders")]
    [InlineData("aks.pod.api-7c9f")]
    [InlineData("service-bus.queue.orders-dlq")]
    [InlineData("a.b.c.d.e")] // extra dots land inside the id segment — fine
    public void IsEntityId_ValidConventions_Accepted(string id) =>
        Assert.True(ScreenStateStore.IsEntityId(id));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("no-dots")]
    [InlineData("two.segments")]
    [InlineData("sql..table")] // empty segment
    [InlineData(".sql.table.x")]
    [InlineData("sql.table.has space")]
    [InlineData("sql.table.has/slash")]
    public void IsEntityId_Malformed_Rejected(string? id) =>
        Assert.False(ScreenStateStore.IsEntityId(id));

    [Fact]
    public void IsEntityId_OverlyLong_Rejected()
    {
        var id = $"sql.table.{new string('x', ScreenStateStore.MaxEntityIdLength)}";
        Assert.False(ScreenStateStore.IsEntityId(id));
    }
}

public class ScreenStateEntityStoreTests
{
    private static ScreenStateStore StoreWithEntities()
    {
        var store = new ScreenStateStore();
        store.Publish(new ScreenStateSnapshot
        {
            Route = "/sql",
            FeatureArea = "Sql",
            CapturedAt = DateTimeOffset.UtcNow,
            Snapshot = JsonDocument.Parse("{}").RootElement,
            Entities = new Dictionary<string, JsonElement>
            {
                ["sql.table.dbo.orders"] = JsonDocument.Parse("""{"rowEstimate":42}""").RootElement,
            },
        });
        return store;
    }

    [Fact]
    public void GetEntity_KnownId_ReturnsDetailWithContext()
    {
        var entity = StoreWithEntities().GetEntity("sql.table.dbo.orders");

        Assert.NotNull(entity);
        Assert.Equal("/sql", entity.Route);
        Assert.Equal("Sql", entity.FeatureArea);
        Assert.Equal(42, entity.Detail.GetProperty("rowEstimate").GetInt32());
    }

    [Fact]
    public void GetEntity_UnknownId_ReturnsNull() =>
        Assert.Null(StoreWithEntities().GetEntity("sql.table.dbo.missing"));

    [Fact]
    public void GetEntity_NothingPublished_ReturnsNull() =>
        Assert.Null(new ScreenStateStore().GetEntity("sql.table.dbo.orders"));

    [Fact]
    public void GetEntity_ExpiredSnapshot_ReturnsNull()
    {
        var store = StoreWithEntities();
        // Push a stale snapshot in over the fresh one — expiry is on the snapshot, and
        // entities share it.
        store.Publish(new ScreenStateSnapshot
        {
            Route = "/sql",
            CapturedAt = DateTimeOffset.UtcNow,
            Snapshot = JsonDocument.Parse("{}").RootElement,
            Entities = new Dictionary<string, JsonElement>
            {
                ["sql.table.dbo.orders"] = JsonDocument.Parse("{}").RootElement,
            },
            ReceivedAt = DateTimeOffset.UtcNow - ScreenStateStore.Ttl - TimeSpan.FromSeconds(1),
        });

        Assert.Null(store.GetEntity("sql.table.dbo.orders"));
    }

    [Fact]
    public void Publish_SecondSnapshot_ReplacesEntities_LastWriteWins()
    {
        var store = StoreWithEntities();
        store.Publish(new ScreenStateSnapshot
        {
            Route = "/sql",
            CapturedAt = DateTimeOffset.UtcNow,
            Snapshot = JsonDocument.Parse("{}").RootElement,
            Entities = new Dictionary<string, JsonElement>
            {
                ["sql.connection.c2"] = JsonDocument.Parse("""{"displayName":"Stg"}""").RootElement,
            },
        });

        Assert.Null(store.GetEntity("sql.table.dbo.orders"));
        Assert.NotNull(store.GetEntity("sql.connection.c2"));
    }
}

public class GetScreenDetailToolTests
{
    private static readonly JsonElement EmptyArgs = JsonDocument.Parse("{}").RootElement;

    private static GetScreenDetailTool ToolWithEntity()
    {
        var store = new ScreenStateStore();
        store.Publish(new ScreenStateSnapshot
        {
            Route = "/sql",
            FeatureArea = "Sql",
            CapturedAt = DateTimeOffset.UtcNow,
            Snapshot = JsonDocument.Parse("{}").RootElement,
            Entities = new Dictionary<string, JsonElement>
            {
                ["sql.table.dbo.orders"] = JsonDocument.Parse(
                    """{"columns":[{"name":"id","dataType":"int"}]}""").RootElement,
            },
        });
        return new GetScreenDetailTool(store);
    }

    [Fact]
    public async Task Execute_KnownEntity_ReturnsDetail()
    {
        var tool = ToolWithEntity();
        var args = JsonDocument.Parse("""{"entity_id":"sql.table.dbo.orders"}""").RootElement;

        using var doc = JsonDocument.Parse(await tool.ExecuteAsync(args, CancellationToken.None));

        var root = doc.RootElement;
        Assert.True(root.GetProperty("found").GetBoolean());
        Assert.Equal("sql.table.dbo.orders", root.GetProperty("entityId").GetString());
        Assert.Equal("/sql", root.GetProperty("route").GetString());
        Assert.Equal("id", root.GetProperty("detail").GetProperty("columns")[0].GetProperty("name").GetString());
    }

    [Fact]
    public async Task Execute_UnknownEntity_ReturnsFoundFalse()
    {
        var tool = ToolWithEntity();
        var args = JsonDocument.Parse("""{"entity_id":"sql.table.dbo.nope"}""").RootElement;

        using var doc = JsonDocument.Parse(await tool.ExecuteAsync(args, CancellationToken.None));

        Assert.False(doc.RootElement.GetProperty("found").GetBoolean());
        Assert.True(doc.RootElement.TryGetProperty("reason", out _));
    }

    [Fact]
    public async Task Execute_MalformedEntityId_ReturnsError()
    {
        var tool = ToolWithEntity();
        var args = JsonDocument.Parse("""{"entity_id":"not-an-entity-id"}""").RootElement;

        using var doc = JsonDocument.Parse(await tool.ExecuteAsync(args, CancellationToken.None));

        Assert.True(doc.RootElement.TryGetProperty("error", out _));
    }

    [Fact]
    public async Task Execute_MissingArgument_ReturnsError()
    {
        var tool = ToolWithEntity();

        using var doc = JsonDocument.Parse(await tool.ExecuteAsync(EmptyArgs, CancellationToken.None));

        Assert.True(doc.RootElement.TryGetProperty("error", out _));
    }
}

public class SensitiveDataKeysTests
{
    [Theory]
    [InlineData("password")]
    [InlineData("apiKey")]
    [InlineData("api_key")]
    [InlineData("Authorization")]
    [InlineData("connectionString")]
    [InlineData("accessToken")]
    [InlineData("body")]
    [InlineData("requestBody")]
    [InlineData("payload")]
    public void IsDenied_SensitiveKeys_Denied(string key) =>
        Assert.True(SensitiveDataKeys.IsDenied(key));

    [Theory]
    [InlineData("bodyPreview")]   // curated bounded preview suffix — whitelist-owned
    [InlineData("valuePreview")]
    [InlineData("summary")]
    [InlineData("name")]
    [InlineData("columns")]
    [InlineData("content")]       // substring match alone must not eat legitimate fields
    [InlineData("messageId")]
    [InlineData(null)]
    [InlineData("")]
    public void IsDenied_CuratedFields_Allowed(string? key) =>
        Assert.False(SensitiveDataKeys.IsDenied(key));

    [Fact]
    public void ContainsDeniedKey_NestedObject_Found()
    {
        using var doc = JsonDocument.Parse(
            """{"table":{"columns":[{"name":"id"}],"credentialKey":"kv-secret"}}""");
        Assert.True(SensitiveDataKeys.ContainsDeniedKey(doc.RootElement));
    }

    [Fact]
    public void ContainsDeniedKey_CleanPayload_NotFound()
    {
        using var doc = JsonDocument.Parse(
            """{"table":{"columns":[{"name":"id","dataType":"int"}],"indexes":["pk"]}}""");
        Assert.False(SensitiveDataKeys.ContainsDeniedKey(doc.RootElement));
    }

    [Fact]
    public void Redact_ReplacesDeniedValues_KeepsOthers()
    {
        using var doc = JsonDocument.Parse(
            """{"name":"orders","secrets":{"apiKey":"abc"},"rows":[{"body":"x","id":1}]}""");

        var redacted = SensitiveDataKeys.Redact(doc.RootElement);

        Assert.Equal("orders", redacted.GetProperty("name").GetString());
        // "secrets" itself is a denied key — the whole subtree collapses to the marker.
        Assert.Equal("[redacted]", redacted.GetProperty("secrets").GetString());
        Assert.Equal("[redacted]", redacted.GetProperty("rows")[0].GetProperty("body").GetString());
        Assert.Equal(1, redacted.GetProperty("rows")[0].GetProperty("id").GetInt32());
    }
}
