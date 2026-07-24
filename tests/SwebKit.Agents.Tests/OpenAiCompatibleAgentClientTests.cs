using System.Text.Json;
using Xunit;

namespace SwebKit.Agents.Tests;

public class OpenAiCompatibleAgentClientTests
{
    // ── URL normalization ──

    [Theory]
    [InlineData("http://localhost:1234", "http://localhost:1234/v1")]
    [InlineData("http://localhost:1234/", "http://localhost:1234/v1")]
    [InlineData("http://localhost:1234/v1", "http://localhost:1234/v1")]
    [InlineData("http://localhost:1234/v1/", "http://localhost:1234/v1")]
    [InlineData("https://api.mistral.ai/v1", "https://api.mistral.ai/v1")]
    [InlineData("https://api.mistral.ai/v1/", "https://api.mistral.ai/v1")]
    [InlineData("https://api.example.com", "https://api.example.com/v1")]
    [InlineData("https://api.example.com/", "https://api.example.com/v1")]
    public void NormalizeBaseUrl_HandlesTrailingSlashAndV1(string input, string expected)
    {
        Assert.Equal(expected, OpenAiCompatibleAgentClient.NormalizeBaseUrl(input));
    }

    [Theory]
    [InlineData("http://localhost:1234/v1/chat/completions", "http://localhost:1234/v1/chat/completions")]
    [InlineData("https://api.example.com/v1/chat/completions", "https://api.example.com/v1/chat/completions")]
    public void NormalizeBaseUrl_AlreadyHasV1_DoesNotDoubleAppend(string input, string expected)
    {
        Assert.Equal(expected, OpenAiCompatibleAgentClient.NormalizeBaseUrl(input));
    }

    [Fact]
    public void NormalizeBaseUrl_EmptyString_ReturnsEmpty()
    {
        Assert.Equal("", OpenAiCompatibleAgentClient.NormalizeBaseUrl(""));
    }

    [Fact]
    public void NormalizeBaseUrl_Whitespace_ReturnsWhitespace()
    {
        Assert.Equal("  ", OpenAiCompatibleAgentClient.NormalizeBaseUrl("  "));
    }

    // ── Response parsing ──

    [Fact]
    public void ParseResponse_SimpleChat_ReturnsContent()
    {
        var json = """
        {
            "choices": [
                {
                    "finish_reason": "stop",
                    "message": {
                        "role": "assistant",
                        "content": "Hello, world!"
                    }
                }
            ]
        }
        """;

        var response = OpenAiCompatibleAgentClient.ParseResponse(json);

        Assert.Equal(AgentFinishReason.Stop, response.FinishReason);
        Assert.Equal("Hello, world!", response.Content);
        Assert.Null(response.ToolCalls);
    }

    [Fact]
    public void ParseResponse_NullContent_ReturnsNullContent()
    {
        var json = """
        {
            "choices": [
                {
                    "finish_reason": "stop",
                    "message": {
                        "role": "assistant",
                        "content": null
                    }
                }
            ]
        }
        """;

        var response = OpenAiCompatibleAgentClient.ParseResponse(json);

        Assert.Equal(AgentFinishReason.Stop, response.FinishReason);
        Assert.Null(response.Content);
    }

    [Fact]
    public void ParseResponse_ToolCalls_ReturnsToolCalls()
    {
        var json = """
        {
            "choices": [
                {
                    "finish_reason": "tool_calls",
                    "message": {
                        "role": "assistant",
                        "content": null,
                        "tool_calls": [
                            {
                                "id": "call_001",
                                "type": "function",
                                "function": {
                                    "name": "get_pod_status",
                                    "arguments": "{\"pod_name\":\"nginx\",\"namespace\":\"default\"}"
                                }
                            }
                        ]
                    }
                }
            ]
        }
        """;

        var response = OpenAiCompatibleAgentClient.ParseResponse(json);

        Assert.Equal(AgentFinishReason.ToolCalls, response.FinishReason);
        Assert.NotNull(response.ToolCalls);
        Assert.Single(response.ToolCalls);
        Assert.Equal("call_001", response.ToolCalls[0].Id);
        Assert.Equal("get_pod_status", response.ToolCalls[0].Name);
        Assert.Contains("nginx", response.ToolCalls[0].ArgumentsJson);
    }

    [Fact]
    public void ParseResponse_MultipleToolCalls_ReturnsAll()
    {
        var json = """
        {
            "choices": [
                {
                    "finish_reason": "tool_calls",
                    "message": {
                        "role": "assistant",
                        "content": null,
                        "tool_calls": [
                            {
                                "id": "call_001",
                                "type": "function",
                                "function": { "name": "tool_a", "arguments": "{}" }
                            },
                            {
                                "id": "call_002",
                                "type": "function",
                                "function": { "name": "tool_b", "arguments": "{}" }
                            }
                        ]
                    }
                }
            ]
        }
        """;

        var response = OpenAiCompatibleAgentClient.ParseResponse(json);

        Assert.Equal(AgentFinishReason.ToolCalls, response.FinishReason);
        Assert.NotNull(response.ToolCalls);
        Assert.Equal(2, response.ToolCalls.Count);
        Assert.Equal("tool_a", response.ToolCalls[0].Name);
        Assert.Equal("tool_b", response.ToolCalls[1].Name);
    }

    [Fact]
    public void ParseResponse_FinishReasonLength_ReturnsLength()
    {
        var json = """
        {
            "choices": [
                {
                    "finish_reason": "length",
                    "message": { "role": "assistant", "content": "truncated" }
                }
            ]
        }
        """;

        var response = OpenAiCompatibleAgentClient.ParseResponse(json);

        Assert.Equal(AgentFinishReason.Length, response.FinishReason);
    }

    [Fact]
    public void ParseResponse_FinishReasonContentFilter_ReturnsContentFilter()
    {
        var json = """
        {
            "choices": [
                {
                    "finish_reason": "content_filter",
                    "message": { "role": "assistant", "content": "" }
                }
            ]
        }
        """;

        var response = OpenAiCompatibleAgentClient.ParseResponse(json);

        Assert.Equal(AgentFinishReason.ContentFilter, response.FinishReason);
    }

    [Fact]
    public void ParseResponse_UnknownFinishReason_ReturnsUnknown()
    {
        var json = """
        {
            "choices": [
                {
                    "finish_reason": "something_new",
                    "message": { "role": "assistant", "content": "hi" }
                }
            ]
        }
        """;

        var response = OpenAiCompatibleAgentClient.ParseResponse(json);

        Assert.Equal(AgentFinishReason.Unknown, response.FinishReason);
    }

    [Fact]
    public void ParseResponse_AssistantMessage_HasCorrectRole()
    {
        var json = """
        {
            "choices": [
                {
                    "finish_reason": "stop",
                    "message": { "role": "assistant", "content": "test" }
                }
            ]
        }
        """;

        var response = OpenAiCompatibleAgentClient.ParseResponse(json);

        Assert.Equal("assistant", response.AssistantMessage.Role);
        Assert.Equal("test", response.AssistantMessage.Content);
    }

    [Fact]
    public void ParseResponse_ToolCalls_AssistantMessageContainsToolCalls()
    {
        var json = """
        {
            "choices": [
                {
                    "finish_reason": "tool_calls",
                    "message": {
                        "role": "assistant",
                        "content": null,
                        "tool_calls": [
                            {
                                "id": "call_001",
                                "type": "function",
                                "function": { "name": "test_tool", "arguments": "{}" }
                            }
                        ]
                    }
                }
            ]
        }
        """;

        var response = OpenAiCompatibleAgentClient.ParseResponse(json);

        Assert.NotNull(response.AssistantMessage.ToolCalls);
        Assert.Single(response.AssistantMessage.ToolCalls);
    }

    // ── AgentMessage wire format ──

    [Fact]
    public void AgentMessage_ToWireFormat_UserMessage_HasRoleAndContent()
    {
        var msg = new AgentMessage { Role = "user", Content = "hello" };
        var wire = msg.ToWireFormat();

        Assert.Equal("user", wire["role"]);
        Assert.Equal("hello", wire["content"]);
        Assert.False(wire.ContainsKey("tool_calls"));
        Assert.False(wire.ContainsKey("tool_call_id"));
    }

    [Fact]
    public void AgentMessage_ToWireFormat_ToolMessage_HasToolCallId()
    {
        var msg = new AgentMessage { Role = "tool", Content = "result", ToolCallId = "call_001" };
        var wire = msg.ToWireFormat();

        Assert.Equal("tool", wire["role"]);
        Assert.Equal("call_001", wire["tool_call_id"]);
        Assert.Equal("result", wire["content"]);
    }

    [Fact]
    public void AgentMessage_ToWireFormat_AssistantWithToolCalls_HasToolCalls()
    {
        var msg = new AgentMessage
        {
            Role = "assistant",
            Content = null,
            ToolCalls = [new AgentToolCall { Id = "call_001", Name = "test_tool", ArgumentsJson = "{}" }]
        };
        var wire = msg.ToWireFormat();

        Assert.Equal("assistant", wire["role"]);
        Assert.True(wire.ContainsKey("tool_calls"));
    }
}
