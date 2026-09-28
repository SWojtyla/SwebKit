using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using SwebKit.Agents;
using SwebKit.Core.Configuration;
using SwebKit.Core.Domain;
using SwebKit.Core.Models;
using SwebKit.Sidecar.Endpoints;
using SwebKit.Sidecar.Services;
using Xunit;

namespace SwebKit.Sidecar.Tests;

/// <summary>Publish-path enforcement of the agent-colleague item-4 entity contract:
/// <c>&lt;area&gt;.&lt;kind&gt;.&lt;id&gt;</c> keys, per-entity/count/total byte caps, and the
/// sensitive-key denylist dropping an over-sharing entity instead of the whole publish.</summary>
public class ScreenStatePublishEntityTests
{
    private static ScreenStatePublishRequest Request(Dictionary<string, JsonElement>? entities = null) => new()
    {
        Route = "/sql",
        FeatureArea = "Sql",
        CapturedAt = DateTimeOffset.UtcNow,
        Snapshot = JsonDocument.Parse("""{"ok":true}""").RootElement,
        Entities = entities,
    };

    private static int StatusOf(IResult result) =>
        Assert.IsAssignableFrom<IStatusCodeHttpResult>(result).StatusCode ?? 0;

    [Fact]
    public void Publish_ValidEntities_StoredAndFetchable()
    {
        var store = new ScreenStateStore();
        var entities = new Dictionary<string, JsonElement>
        {
            ["sql.table.dbo.orders"] = JsonDocument.Parse("""{"columns":3}""").RootElement,
        };

        var result = AgentEndpoints.PublishScreenState(store, Request(entities));

        Assert.Equal(204, StatusOf(result));
        Assert.Equal(3, store.GetEntity("sql.table.dbo.orders")!.Detail.GetProperty("columns").GetInt32());
    }

    [Fact]
    public void Publish_NoEntities_StillAccepted()
    {
        var store = new ScreenStateStore();
        Assert.Equal(204, StatusOf(AgentEndpoints.PublishScreenState(store, Request())));
        Assert.Null(store.GetEntity("sql.table.x.y"));
    }

    [Fact]
    public void Publish_MalformedEntityId_BadRequest()
    {
        // Two segments only — the frozen convention requires <area>.<kind>.<id>.
        var entities = new Dictionary<string, JsonElement>
        {
            ["not.namespaced"] = JsonDocument.Parse("{}").RootElement,
        };
        Assert.Equal(400, StatusOf(AgentEndpoints.PublishScreenState(new ScreenStateStore(), Request(entities))));
    }

    [Fact]
    public void Publish_TooManyEntities_BadRequest()
    {
        var entities = Enumerable.Range(0, ScreenStateStore.MaxEntities + 1)
            .ToDictionary(i => $"sql.table.t{i}", _ => JsonDocument.Parse("{}").RootElement);

        Assert.Equal(400, StatusOf(AgentEndpoints.PublishScreenState(new ScreenStateStore(), Request(entities))));
    }

    [Fact]
    public void Publish_OversizedEntity_BadRequest()
    {
        var entities = new Dictionary<string, JsonElement>
        {
            ["sql.table.dbo.big"] = JsonDocument.Parse(
                $$"""{"data":"{{new string('x', ScreenStateStore.MaxEntityBytes)}}"}""").RootElement,
        };

        Assert.Equal(400, StatusOf(AgentEndpoints.PublishScreenState(new ScreenStateStore(), Request(entities))));
    }

    [Fact]
    public void Publish_TotalEntitiesOverCap_BadRequest()
    {
        // Each entity is under the per-entity cap but together they exceed the total budget.
        var chunk = new string('x', ScreenStateStore.MaxEntityBytes - 200);
        var entities = Enumerable.Range(0, 5)
            .ToDictionary(
                i => $"sql.table.t{i}",
                _ => JsonDocument.Parse($$"""{"data":"{{chunk}}"}""").RootElement);

        Assert.Equal(400, StatusOf(AgentEndpoints.PublishScreenState(new ScreenStateStore(), Request(entities))));
    }

    [Fact]
    public void Publish_EntityWithDeniedKey_DroppedButPublishAccepted()
    {
        var store = new ScreenStateStore();
        var entities = new Dictionary<string, JsonElement>
        {
            ["sql.connection.c1"] = JsonDocument.Parse(
                """{"displayName":"Prod","connectionString":"Server=…;Password=x"}""").RootElement,
            ["sql.table.dbo.orders"] = JsonDocument.Parse("""{"columns":3}""").RootElement,
        };

        var result = AgentEndpoints.PublishScreenState(store, Request(entities));

        Assert.Equal(204, StatusOf(result));
        Assert.Null(store.GetEntity("sql.connection.c1"));      // denied key → dropped
        Assert.NotNull(store.GetEntity("sql.table.dbo.orders")); // clean sibling kept
    }
}

/// <summary>agent-colleague item 5: the terminal Done event carries an exchangeId, and the
/// ring buffer retains the exchange under it for a later thumbs-down.</summary>
public class AgentExchangeBufferTests
{
    private static SidecarAgentChatService CreateService(
        Func<IAsyncEnumerable<AgentStreamEvent>> streamFactory,
        AgentExchangeBuffer buffer,
        ScreenStateStore? screenState = null)
    {
        var registry = new AgentToolRegistry([]);
        var profiles = new ProfileRepository();
        var settings = new UserSettingsRepository();
        settings.Settings.Agent.Profiles.Add(new AgentProfile
        {
            Id = "p1",
            DisplayName = "Test",
            Capability = AgentCapability.ChatOnly,
        });
        settings.Settings.Agent.ActiveProfileId = "p1";
        return new SidecarAgentChatService(
            new ScriptedStreamingModelClient(streamFactory),
            registry, profiles, settings, new DemoModeService(),
            exchangeBuffer: buffer);
    }

    private static async Task<List<AgentStreamEvent>> Drain(IAsyncEnumerable<AgentStreamEvent> stream)
    {
        var events = new List<AgentStreamEvent>();
        await foreach (var evt in stream) events.Add(evt);
        return events;
    }

    [Fact]
    public async Task SendStreamAsync_DoneEvent_CarriesExchangeId_AndBufferRetainsExchange()
    {
        var buffer = new AgentExchangeBuffer();
        var service = CreateService(() => ScriptedStreamingModelClient.TokensThenDone("hi"), buffer);

        var events = await Drain(service.SendStreamAsync("sess-1", "hello"));

        var done = Assert.IsType<AgentStreamEvent>(events[^1]);
        Assert.Equal(AgentStreamEventKind.Done, done.Kind);
        Assert.False(string.IsNullOrWhiteSpace(done.ExchangeId));
        // Only the terminal event carries it — intermediate tokens don't.
        Assert.All(events[..^1], e => Assert.Null(e.ExchangeId));

        Assert.True(buffer.TryGet(done.ExchangeId!, out var exchange));
        Assert.Equal("hello", exchange.UserMessage);
        Assert.Equal("hi", exchange.AssistantText);
        Assert.Equal("sess-1", exchange.SessionId);
    }

    [Fact]
    public async Task SendStreamAsync_DistinctTurns_GetDistinctExchangeIds()
    {
        var buffer = new AgentExchangeBuffer();
        var service = CreateService(() => ScriptedStreamingModelClient.TokensThenDone("ok"), buffer);

        var first = await Drain(service.SendStreamAsync(null, "one"));
        var second = await Drain(service.SendStreamAsync(null, "two"));

        Assert.NotEqual(first[^1].ExchangeId, second[^1].ExchangeId);
        Assert.True(buffer.TryGet(first[^1].ExchangeId!, out _));
        Assert.True(buffer.TryGet(second[^1].ExchangeId!, out _));
    }

    [Fact]
    public async Task SendStreamAsync_ErrorTurn_EmitsNoExchangeId()
    {
        var buffer = new AgentExchangeBuffer();
        var service = CreateService(
            () => ScriptedStreamingModelClient.TokenThenThrow("partial", "boom"), buffer);

        var events = await Drain(service.SendStreamAsync(null, "hello"));

        Assert.All(events, e => Assert.Null(e.ExchangeId));
    }

    [Fact]
    public void Record_PastCapacity_EvictsOldest()
    {
        var buffer = new AgentExchangeBuffer();
        for (var i = 0; i < AgentExchangeBuffer.Capacity + 10; i++)
            buffer.Record($"ex-{i}", null, null, null, null, $"u{i}", $"a{i}", [], []);

        Assert.False(buffer.TryGet("ex-0", out _));       // evicted
        Assert.True(buffer.TryGet($"ex-{AgentExchangeBuffer.Capacity + 9}", out _)); // newest kept
    }

    [Fact]
    public void Record_CapturesScreenStateDigest()
    {
        var store = new ScreenStateStore();
        store.Publish(new ScreenStateSnapshot
        {
            Route = "/sql",
            FeatureArea = "Sql",
            CapturedAt = DateTimeOffset.UtcNow,
            Snapshot = JsonDocument.Parse("""{"tab":"browse"}""").RootElement,
            Entities = new Dictionary<string, JsonElement>
            {
                ["sql.table.dbo.orders"] = JsonDocument.Parse("{}").RootElement,
            },
        });
        var buffer = new AgentExchangeBuffer(store);

        buffer.Record("ex-1", null, null, null, null, "q", "a", [], []);

        Assert.True(buffer.TryGet("ex-1", out var exchange));
        var digest = exchange.ScreenStateDigest!.Value;
        Assert.Equal("/sql", digest.GetProperty("route").GetString());
        Assert.Equal("sql.table.dbo.orders", digest.GetProperty("entityIds")[0].GetString());
        Assert.Equal("browse", digest.GetProperty("snapshot").GetProperty("tab").GetString());
    }
}

/// <summary>agent-colleague item 5 redaction + persistence: POST /api/agent/feedback resolves
/// the exchangeId through the ring buffer and flushes a redacted entry to
/// agent-feedback.json.</summary>
public class AgentFeedbackEndpointTests
{
    private static AgentExchange Exchange(string id = "ex-1") => new(
        id, "sess", "Sql", "ask", "feature",
        "why is this slow?", "because reasons", [], [],
        JsonDocument.Parse("""{"route":"/sql","snapshot":{"apiKey":"leak","name":"orders"}}""").RootElement,
        DateTimeOffset.UtcNow);

    [Fact]
    public async Task Submit_MissingExchangeId_BadRequest()
    {
        using var sandbox = new AppDataSandbox();
        var result = await AgentEndpoints.SubmitFeedbackAsync(
            new AgentExchangeBuffer(), new AgentFeedbackRepository(),
            new AgentFeedbackRequest { ExchangeId = "  " }, CancellationToken.None);

        Assert.Equal(400, Assert.IsAssignableFrom<IStatusCodeHttpResult>(result).StatusCode);
    }

    [Fact]
    public async Task Submit_UnknownExchange_StillRecorded_WithoutContext()
    {
        using var sandbox = new AppDataSandbox();
        var repo = new AgentFeedbackRepository();

        var result = await AgentEndpoints.SubmitFeedbackAsync(
            new AgentExchangeBuffer(), repo,
            new AgentFeedbackRequest { ExchangeId = "evicted-id" }, CancellationToken.None);

        Assert.Equal(200, Assert.IsAssignableFrom<IStatusCodeHttpResult>(result).StatusCode);
        var all = await repo.GetAllAsync();
        var entry = Assert.Single(all);
        Assert.False(entry.ExchangeFound);
        Assert.Equal("evicted-id", entry.ExchangeId);
        Assert.Equal("down", entry.Sentiment);
        Assert.Null(entry.UserMessage);
    }

    [Fact]
    public async Task Submit_KnownExchange_PersistsRedactedContext()
    {
        using var sandbox = new AppDataSandbox();
        var buffer = new AgentExchangeBuffer();
        buffer.Record("ex-1", "sess", "Sql", "ask", "feature",
            "why is this slow?", "because reasons",
            [new AgentChatStep { Type = "tool_call", ToolName = "get_screen_state", Summary = "Calling get_screen_state" }],
            ["get_screen_state"]);
        var repo = new AgentFeedbackRepository();

        var result = await AgentEndpoints.SubmitFeedbackAsync(
            buffer, repo, new AgentFeedbackRequest { ExchangeId = "ex-1", Comment = "wrong" },
            CancellationToken.None);

        Assert.Equal(200, Assert.IsAssignableFrom<IStatusCodeHttpResult>(result).StatusCode);
        var entry = Assert.Single(await repo.GetAllAsync());
        Assert.True(entry.ExchangeFound);
        Assert.Equal("why is this slow?", entry.UserMessage);
        Assert.Equal("because reasons", entry.AssistantText);
        Assert.Equal("wrong", entry.Comment);
        Assert.Equal(new[] { "get_screen_state" }, entry.ToolsUsed);
        Assert.Single(entry.Steps);
        Assert.Equal("get_screen_state", entry.Steps[0].ToolName);
        // No screen state was published → no digest.
        Assert.Null(entry.ScreenStateDigest);
    }

    [Fact]
    public void Redactor_TruncatesLongFields_At4KB()
    {
        var longText = new string('y', AgentFeedbackRedactor.MaxFieldChars + 500);
        var exchange = Exchange() with { UserMessage = longText };

        var entry = AgentFeedbackRedactor.CreateEntry(
            new AgentFeedbackRequest { ExchangeId = "ex-1" }, exchange);

        Assert.True(entry.UserMessage!.Length <= AgentFeedbackRedactor.MaxFieldChars + 20);
        Assert.EndsWith("[truncated]", entry.UserMessage);
    }

    [Fact]
    public void Redactor_DigestDenylistedKeys_AreRedacted()
    {
        var entry = AgentFeedbackRedactor.CreateEntry(
            new AgentFeedbackRequest { ExchangeId = "ex-1" }, Exchange());

        var digest = entry.ScreenStateDigest!.Value;
        Assert.Equal("[redacted]", digest.GetProperty("snapshot").GetProperty("apiKey").GetString());
        Assert.Equal("orders", digest.GetProperty("snapshot").GetProperty("name").GetString());
    }

    [Fact]
    public void Redactor_EntryShape_HasNoPendingActionPayloadField()
    {
        var entry = AgentFeedbackRedactor.CreateEntry(
            new AgentFeedbackRequest { ExchangeId = "ex-1" }, Exchange());

        var json = JsonSerializer.Serialize(entry);
        Assert.DoesNotContain("\"payload\"", json, StringComparison.OrdinalIgnoreCase);
    }
}

public class AgentFeedbackRepositoryTests
{
    private static AgentFeedbackEntry Entry(string id, DateTimeOffset? at = null) => new()
    {
        Id = id,
        ExchangeId = $"ex-{id}",
        CreatedAt = at ?? DateTimeOffset.UtcNow,
    };

    [Fact]
    public async Task Add_ThenGetAll_NewestFirst()
    {
        using var sandbox = new AppDataSandbox();
        var repo = new AgentFeedbackRepository();

        await repo.AddAsync(Entry("a", DateTimeOffset.UtcNow.AddMinutes(-5)));
        await repo.AddAsync(Entry("b"));

        var all = await repo.GetAllAsync();
        Assert.Equal(2, all.Count);
        Assert.Equal("b", all[0].Id);
    }

    [Fact]
    public async Task Add_BeyondCap_DropsOldest()
    {
        using var sandbox = new AppDataSandbox();
        var repo = new AgentFeedbackRepository();

        for (var i = 0; i < AgentFeedbackRepository.MaxEntries + 10; i++)
            await repo.AddAsync(Entry($"e{i}", DateTimeOffset.UnixEpoch.AddMinutes(i)));

        var all = await repo.GetAllAsync();
        Assert.Equal(AgentFeedbackRepository.MaxEntries, all.Count);
        Assert.DoesNotContain(all, e => e.Id == "e0");
        Assert.Equal($"e{AgentFeedbackRepository.MaxEntries + 9}", all[0].Id);
    }

    [Fact]
    public async Task GetAll_MissingFile_Empty()
    {
        using var sandbox = new AppDataSandbox();
        Assert.Empty(await new AgentFeedbackRepository().GetAllAsync());
    }
}
