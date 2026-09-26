using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using ModelContextProtocol.Protocol;
using SwebKit.Agents.Tools;
using SwebKit.Core.Domain;
using SwebKit.Sidecar.Services;
using Xunit;

namespace SwebKit.Sidecar.Tests;

/// <summary>
/// <see cref="ExternalMcpToolSource"/> — the in-process MCP client adapter for non-ACP profiles.
/// Transport details are faked via <see cref="IMcpServerConnection"/>; what matters here is the
/// read-only filter, exposed-name sanitization/dedup, result mapping, and connection caching.
/// </summary>
public sealed class ExternalMcpToolSourceTests
{
    private sealed class FakeConnection : IMcpServerConnection
    {
        public List<RemoteMcpTool> Tools { get; } = [];
        public Exception? ListError { get; set; }
        public CallToolResult CallResult { get; set; } = new()
        {
            Content = [new TextContentBlock { Text = "{\"ok\":true}" }],
            IsError = false,
        };

        public string? LastCalledTool { get; private set; }
        public IReadOnlyDictionary<string, object?>? LastArguments { get; private set; }
        public bool Disposed { get; private set; }

        public Task<IReadOnlyList<RemoteMcpTool>> ListToolsAsync(CancellationToken ct) =>
            ListError is not null
                ? Task.FromException<IReadOnlyList<RemoteMcpTool>>(ListError)
                : Task.FromResult<IReadOnlyList<RemoteMcpTool>>(Tools);

        public Task<CallToolResult> CallToolAsync(
            string remoteName, IReadOnlyDictionary<string, object?> arguments, CancellationToken ct)
        {
            LastCalledTool = remoteName;
            LastArguments = arguments;
            return Task.FromResult(CallResult);
        }

        public ValueTask DisposeAsync()
        {
            Disposed = true;
            return ValueTask.CompletedTask;
        }
    }

    private static AgentProfile ProfileWith(params AgentMcpServer[] servers) => new()
    {
        DisplayName = "local",
        ExtraMcpServers = servers.ToList(),
    };

    private static AgentMcpServer HttpServer(string name = "azure") => new()
    {
        Name = name,
        Enabled = true,
        Transport = "http",
        Url = "http://localhost:9999/mcp",
    };

    private static RemoteMcpTool Tool(string name, bool readOnly = true, string? description = null) =>
        new(name, description ?? $"Does {name}", JsonDocument.Parse("{\"type\":\"object\"}").RootElement, readOnly);

    [Fact]
    public async Task GetTools_exposes_readOnly_tools_with_prefixed_sanitized_names()
    {
        var connection = new FakeConnection();
        var source = new ExternalMcpToolSource(
            NullLogger<ExternalMcpToolSource>.Instance,
            (_, _) => Task.FromResult<IMcpServerConnection>(connection));
        connection.Tools.Add(Tool("list-subscriptions", description: "Lists subs"));
        connection.Tools.Add(Tool("delete resource!")); // unsafe name → sanitized

        var bindings = await source.GetToolsAsync(ProfileWith(HttpServer("Azure MCP")), CancellationToken.None);

        Assert.Equal(2, bindings.Count);
        Assert.Equal("mcp_azure_mcp_list_subscriptions", bindings[0].Definition.Name);
        Assert.Equal("mcp_azure_mcp_delete_resource", bindings[1].Definition.Name);
        Assert.StartsWith("[external:Azure MCP]", bindings[0].Definition.Description);
        Assert.Equal(ToolKind.Read, bindings[0].Definition.Kind);
        Assert.Equal(FeatureArea.External, bindings[0].Definition.FeatureArea);
    }

    [Fact]
    public async Task GetTools_skips_tools_without_readOnly_annotation()
    {
        var connection = new FakeConnection();
        var source = new ExternalMcpToolSource(
            NullLogger<ExternalMcpToolSource>.Instance,
            (_, _) => Task.FromResult<IMcpServerConnection>(connection));
        connection.Tools.Add(Tool("list_things", readOnly: true));
        connection.Tools.Add(Tool("apply_manifest", readOnly: false));

        var bindings = await source.GetToolsAsync(ProfileWith(HttpServer()), CancellationToken.None);

        Assert.Single(bindings);
        Assert.Equal("mcp_azure_list_things", bindings[0].Definition.Name);
    }

    [Fact]
    public async Task GetTools_skips_disabled_and_unreachable_servers()
    {
        var connectCalls = 0;
        var source = new ExternalMcpToolSource(
            NullLogger<ExternalMcpToolSource>.Instance,
            (_, _) =>
            {
                connectCalls++;
                throw new InvalidOperationException("connection refused");
            });
        var profile = ProfileWith(
            HttpServer("dead"),
            new AgentMcpServer { Name = "off", Enabled = false, Transport = "http", Url = "http://localhost:9998/" });

        var bindings = await source.GetToolsAsync(profile, CancellationToken.None);

        Assert.Empty(bindings);
        Assert.Equal(1, connectCalls); // disabled entry never attempted
    }

    [Fact]
    public async Task GetTools_caches_the_connection_per_server_config()
    {
        var connection = new FakeConnection();
        var connectCalls = 0;
        var source = new ExternalMcpToolSource(
            NullLogger<ExternalMcpToolSource>.Instance,
            (_, _) =>
            {
                connectCalls++;
                return Task.FromResult<IMcpServerConnection>(connection);
            });
        var profile = ProfileWith(HttpServer());

        await source.GetToolsAsync(profile, CancellationToken.None);
        await source.GetToolsAsync(profile, CancellationToken.None);

        Assert.Equal(1, connectCalls);
    }

    [Fact]
    public async Task Execute_proxies_arguments_and_returns_text_content()
    {
        var connection = new FakeConnection();
        var source = new ExternalMcpToolSource(
            NullLogger<ExternalMcpToolSource>.Instance,
            (_, _) => Task.FromResult<IMcpServerConnection>(connection));
        connection.Tools.Add(Tool("list_things"));
        var bindings = await source.GetToolsAsync(ProfileWith(HttpServer()), CancellationToken.None);

        var args = JsonDocument.Parse("{\"ns\":\"default\",\"limit\":3}").RootElement;
        var result = await bindings[0].Execute(args, CancellationToken.None);

        Assert.Equal("{\"ok\":true}", result);
        Assert.Equal("list_things", connection.LastCalledTool);
        Assert.Equal("default", Assert.IsType<JsonElement>(connection.LastArguments!["ns"]).GetString());
        Assert.Equal(3, Assert.IsType<JsonElement>(connection.LastArguments["limit"]).GetInt32());
    }

    [Fact]
    public async Task Execute_wraps_remote_errors_in_the_error_convention()
    {
        var connection = new FakeConnection
        {
            CallResult = new CallToolResult
            {
                Content = [new TextContentBlock { Text = "quota exceeded" }],
                IsError = true,
            },
        };
        var source = new ExternalMcpToolSource(
            NullLogger<ExternalMcpToolSource>.Instance,
            (_, _) => Task.FromResult<IMcpServerConnection>(connection));
        connection.Tools.Add(Tool("list_things"));
        var bindings = await source.GetToolsAsync(ProfileWith(HttpServer()), CancellationToken.None);

        var result = await bindings[0].Execute(JsonDocument.Parse("{}").RootElement, CancellationToken.None);

        var payload = JsonDocument.Parse(result).RootElement;
        Assert.Equal("quota exceeded", payload.GetProperty("error").GetString());
        Assert.True(AgentToolCallOrchestrator.IsErrorResult(result));
    }

    [Fact]
    public async Task Execute_prefers_structured_content_over_text()
    {
        var connection = new FakeConnection
        {
            CallResult = new CallToolResult
            {
                StructuredContent = JsonDocument.Parse("{\"rows\":[1,2]}").RootElement,
                Content = [new TextContentBlock { Text = "fallback" }],
                IsError = false,
            },
        };
        var source = new ExternalMcpToolSource(
            NullLogger<ExternalMcpToolSource>.Instance,
            (_, _) => Task.FromResult<IMcpServerConnection>(connection));
        connection.Tools.Add(Tool("list_things"));
        var bindings = await source.GetToolsAsync(ProfileWith(HttpServer()), CancellationToken.None);

        var result = await bindings[0].Execute(JsonDocument.Parse("{}").RootElement, CancellationToken.None);

        Assert.Equal("{\"rows\":[1,2]}", result);
    }

    [Theory]
    [InlineData("My Server!!  Name", "my_server_name")]
    [InlineData("---leading", "leading")]
    [InlineData("", "x")]
    [InlineData("a__b", "a_b")]
    public void Slug_normalizes_to_the_function_name_charset(string input, string expected) =>
        Assert.Equal(expected, ExternalMcpToolSource.Slug(input));

    [Fact]
    public void ExposedName_truncates_to_64_and_deduplicates()
    {
        var used = new HashSet<string>();

        var first = ExternalMcpToolSource.ExposedName("srv", new string('t', 100), used);
        var second = ExternalMcpToolSource.ExposedName("srv", new string('t', 100), used);

        Assert.True(first.Length <= 64);
        Assert.True(second.Length <= 64);
        Assert.NotEqual(first, second);
        Assert.Equal(2, used.Count);
    }
}
