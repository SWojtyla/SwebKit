using System.Text.Json;
using Moq;
using SwebKit.Agents.Tools;
using Xunit;

namespace SwebKit.Agents.Tests;

public class ConversationSessionTests
{
    [Fact]
    public void Add_SingleMessage_CountIsOne()
    {
        var session = new ConversationSession(20);
        session.Add(new AgentMessage { Role = "user", Content = "hello" });
        Assert.Equal(1, session.Count);
    }

    [Fact]
    public void Clear_AfterAddingMessages_CountIsZero()
    {
        var session = new ConversationSession(20);
        session.Add(new AgentMessage { Role = "user", Content = "hello" });
        session.Add(new AgentMessage { Role = "assistant", Content = "hi" });
        session.Clear();
        Assert.Equal(0, session.Count);
    }

    [Fact]
    public void Add_ExceedsMaxMessages_TrimsOldestPair()
    {
        var session = new ConversationSession(maxMessages: 4);
        for (var i = 0; i < 5; i++)
            session.Add(new AgentMessage { Role = "user", Content = $"msg {i}" });

        // After 5 adds with max=4, one trim pass removes 2 → leaves 3
        Assert.Equal(3, session.Count);
    }

    [Fact]
    public void Add_AtExactLimit_DoesNotTrim()
    {
        var session = new ConversationSession(maxMessages: 4);
        for (var i = 0; i < 4; i++)
            session.Add(new AgentMessage { Role = "user", Content = $"msg {i}" });

        Assert.Equal(4, session.Count);
    }

    [Fact]
    public void IsNearLimit_BelowThreshold_ReturnsFalse()
    {
        var session = new ConversationSession(maxMessages: 20);
        session.Add(new AgentMessage { Role = "user", Content = "a" });
        Assert.False(session.IsNearLimit);
    }

    [Fact]
    public void IsNearLimit_AtSeventyFivePercent_ReturnsTrue()
    {
        var session = new ConversationSession(maxMessages: 4);
        // 75% of 4 = 3
        session.Add(new AgentMessage { Role = "user", Content = "1" });
        session.Add(new AgentMessage { Role = "user", Content = "2" });
        session.Add(new AgentMessage { Role = "user", Content = "3" });
        Assert.True(session.IsNearLimit);
    }

    [Fact]
    public void MaxMessages_SetToZero_DefaultsToTwenty()
    {
        var session = new ConversationSession(maxMessages: 10);
        session.MaxMessages = 0;
        Assert.Equal(20, session.MaxMessages);
    }

    [Fact]
    public void Constructor_ZeroOrNegativeMax_DefaultsToTwenty()
    {
        var s1 = new ConversationSession(0);
        var s2 = new ConversationSession(-5);
        Assert.Equal(20, s1.MaxMessages);
        Assert.Equal(20, s2.MaxMessages);
    }

    [Fact]
    public void Messages_ReturnsReadOnlyView()
    {
        var session = new ConversationSession(20);
        session.Add(new AgentMessage { Role = "user", Content = "test" });
        Assert.IsAssignableFrom<IReadOnlyList<AgentMessage>>(session.Messages);
        Assert.Single(session.Messages);
    }
}

public class AgentToolRegistryTests
{
    private static Mock<IAgentTool> MakeTool(string name, string result = "{\"ok\":true}")
    {
        var mock = new Mock<IAgentTool>();
        mock.Setup(t => t.Name).Returns(name);
        mock.Setup(t => t.Description).Returns($"Description for {name}");
        mock.Setup(t => t.ParametersSchema).Returns(AgentToolSchema.Parse("{\"type\":\"object\",\"properties\":{}}"));
        mock.Setup(t => t.ExecuteAsync(It.IsAny<JsonElement>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(result);
        return mock;
    }

    [Fact]
    public void GetDefinitions_WithTwoTools_ReturnsBothDefinitions()
    {
        var registry = new AgentToolRegistry([MakeTool("toolA").Object, MakeTool("toolB").Object]);
        var defs = registry.GetDefinitions();
        Assert.Equal(2, defs.Count);
        Assert.Contains(defs, d => d.Name == "toolA");
        Assert.Contains(defs, d => d.Name == "toolB");
    }

    [Fact]
    public void GetDefinitions_EmptyRegistry_ReturnsEmptyList()
    {
        var registry = new AgentToolRegistry([]);
        Assert.Empty(registry.GetDefinitions());
    }

    [Fact]
    public async Task ExecuteAsync_KnownTool_ReturnsToolResult()
    {
        var mock = MakeTool("myTool", "{\"data\":42}");
        var registry = new AgentToolRegistry([mock.Object]);

        var args = JsonDocument.Parse("{}").RootElement;
        var result = await registry.ExecuteAsync("myTool", args, CancellationToken.None);

        Assert.Equal("{\"data\":42}", result);
    }

    [Fact]
    public async Task ExecuteAsync_UnknownTool_ReturnsErrorJson()
    {
        var registry = new AgentToolRegistry([MakeTool("knownTool").Object]);

        var args = JsonDocument.Parse("{}").RootElement;
        var result = await registry.ExecuteAsync("ghost", args, CancellationToken.None);

        Assert.Contains("Unknown tool", result);
        Assert.Contains("ghost", result);
    }

    [Fact]
    public async Task ExecuteAsync_ToolNameIsCaseInsensitive_ReturnsResult()
    {
        var registry = new AgentToolRegistry([MakeTool("GetPodStatus").Object]);
        var args = JsonDocument.Parse("{}").RootElement;
        var result = await registry.ExecuteAsync("getpodstatus", args, CancellationToken.None);
        Assert.DoesNotContain("Unknown tool", result);
    }

    [Fact]
    public async Task ExecuteAsync_ToolThrows_ReturnsErrorJson()
    {
        var mock = new Mock<IAgentTool>();
        mock.Setup(t => t.Name).Returns("badTool");
        mock.Setup(t => t.Description).Returns("bad");
        mock.Setup(t => t.ParametersSchema).Returns(AgentToolSchema.Parse("{\"type\":\"object\",\"properties\":{}}"));
        mock.Setup(t => t.ExecuteAsync(It.IsAny<JsonElement>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("something went wrong"));

        var registry = new AgentToolRegistry([mock.Object]);
        var args = JsonDocument.Parse("{}").RootElement;
        var result = await registry.ExecuteAsync("badTool", args, CancellationToken.None);

        Assert.Contains("error", result);
        Assert.Contains("something went wrong", result);
    }

    [Fact]
    public void GetDefinitions_IncludesDescriptionAndSchema()
    {
        var registry = new AgentToolRegistry([MakeTool("aTool").Object]);
        var def = registry.GetDefinitions().Single();
        Assert.Equal("Description for aTool", def.Description);
        Assert.NotEqual(default, def.ParametersSchema);
    }
}
