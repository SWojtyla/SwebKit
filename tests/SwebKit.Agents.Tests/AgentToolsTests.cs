using System.Text.Json;
using SwebKit.Agents.Tools;
using Xunit;

namespace SwebKit.Agents.Tests;

/// <summary>
/// Placeholder test class for Phase 1 agent tools.
/// Comprehensive unit tests will be added in subsequent iterations.
/// </summary>
public class AgentToolsTests
{
    [Fact]
    public void AgentToolSchema_Parse_ValidJson_ReturnsJsonElement()
    {
        // Arrange
        var validJson = "{ \"type\": \"object\", \"properties\": { \"name\": { \"type\": \"string\" } } }";

        // Act
        var result = AgentToolSchema.Parse(validJson);

        // Assert
        Assert.Equal(JsonValueKind.Object, result.ValueKind);
        Assert.Equal("object", result.GetProperty("type").GetString());
    }

    [Fact]
    public void AgentToolSchema_Parse_WithProperties_ReturnsCompleteSchema()
    {
        // Arrange
        var jsonWithProps = "{ \"type\": \"object\", \"properties\": { \"param1\": { \"type\": \"string\" }, \"param2\": { \"type\": \"integer\" } } }";

        // Act
        var result = AgentToolSchema.Parse(jsonWithProps);

        // Assert
        var props = result.GetProperty("properties");
        Assert.True(props.TryGetProperty("param1", out _));
        Assert.True(props.TryGetProperty("param2", out _));
    }
}
