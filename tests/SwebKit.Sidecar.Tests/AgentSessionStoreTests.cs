using SwebKit.Agents;
using SwebKit.Sidecar.Services;

namespace SwebKit.Sidecar.Tests;

/// <summary>Covers the session-storage seam extracted out of <see cref="SidecarAgentChatService"/>:
/// key normalization, the fixed history cap, and idle eviction with the global session exempt.</summary>
public class AgentSessionStoreTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Key_NullOrBlankSessionId_MapsToTheSingleGlobalKey(string? sessionId)
    {
        Assert.Equal(AgentSessionStore.GlobalSessionKey, AgentSessionStore.Key(sessionId));
    }

    [Fact]
    public void Key_ExplicitSessionId_IsUsedVerbatim()
    {
        Assert.Equal("panel-42", AgentSessionStore.Key("panel-42"));
    }

    [Fact]
    public void GetOrCreate_SameKeyTwice_ReturnsTheSameSession()
    {
        var store = new AgentSessionStore();

        var first = store.GetOrCreate("a");
        var second = store.GetOrCreate("a");

        Assert.Same(first, second);
        Assert.NotSame(first, store.GetOrCreate("b"));
    }

    [Fact]
    public void CreateIfAbsent_ExistingSession_ReturnsNullSoCallersDoNotReseedIt()
    {
        var store = new AgentSessionStore();

        Assert.NotNull(store.CreateIfAbsent("insight-1"));
        Assert.Null(store.CreateIfAbsent("insight-1"));
    }

    [Fact]
    public void CreateIfAbsent_UsesTheRawId_NotTheNormalizedKey()
    {
        var store = new AgentSessionStore();
        store.CreateIfAbsent("insight-1");

        // Looked up through the normal (normalized) path it's the same session, and the global
        // session is untouched by seeding.
        Assert.True(store.TryGet("insight-1", out _));
        Assert.Equal(0, store.GetHistoryCount(null));
    }

    [Fact]
    public void Append_BeyondTheHistoryCap_DropsOldestMessagesOnly()
    {
        var store = new AgentSessionStore();
        var session = store.GetOrCreate("a");

        for (var i = 0; i < 25; i++)
            store.Append(session, new AgentMessage { Role = "user", Content = $"m{i}" });

        Assert.Equal(20, store.GetHistoryCount("a"));
        var contents = session.History.Select(m => m.Content).ToList();
        Assert.DoesNotContain("m0", contents);
        Assert.Contains("m24", contents);
    }

    [Fact]
    public void GetEstimatedTokens_UsesTheFourCharsPerTokenHeuristic_AndIgnoresUnknownSessions()
    {
        var store = new AgentSessionStore();
        var session = store.GetOrCreate("a");
        store.Append(session, new AgentMessage { Role = "user", Content = new string('x', 10) });

        Assert.Equal(3, store.GetEstimatedTokens("a")); // ceil(10 / 4)
        Assert.Equal(0, store.GetEstimatedTokens("never-used"));
    }

    [Fact]
    public void ClearHistory_EmptiesThatSessionOnly()
    {
        var store = new AgentSessionStore();
        store.Append(store.GetOrCreate("a"), new AgentMessage { Role = "user", Content = "hi" });
        store.Append(store.GetOrCreate("b"), new AgentMessage { Role = "user", Content = "hi" });

        store.ClearHistory("a");

        Assert.Equal(0, store.GetHistoryCount("a"));
        Assert.Equal(1, store.GetHistoryCount("b"));
    }

    [Fact]
    public void EvictIdleSessions_RemovesStaleContextualSessions_ButNeverTheGlobalOne()
    {
        var store = new AgentSessionStore();
        var global = store.GetOrCreate(AgentSessionStore.Key(null));
        var stale = store.GetOrCreate("stale");
        var fresh = store.GetOrCreate("fresh");
        store.Append(global, new AgentMessage { Role = "user", Content = "hi" });
        store.Append(stale, new AgentMessage { Role = "user", Content = "hi" });
        store.Append(fresh, new AgentMessage { Role = "user", Content = "hi" });

        // The global session is exempt from eviction by design, so make it look just as idle as the
        // contextual one to prove the exemption rather than the timestamp is what keeps it.
        var longAgo = DateTimeOffset.UtcNow.AddHours(-2);
        global.LastActivity = longAgo;
        stale.LastActivity = longAgo;

        store.EvictIdleSessions();

        Assert.Equal(1, store.GetHistoryCount(null));
        Assert.Equal(0, store.GetHistoryCount("stale"));
        Assert.Equal(1, store.GetHistoryCount("fresh"));
    }
}
