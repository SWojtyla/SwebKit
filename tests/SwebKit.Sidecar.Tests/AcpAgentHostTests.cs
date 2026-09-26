using System.Text.Json;
using SwebKit.Core.Domain;
using SwebKit.Sidecar.Services.Acp;

namespace SwebKit.Sidecar.Tests;

/// <summary>Covers the pure parts of <see cref="AcpAgentHost"/> that don't need a live agent
/// process — the auto-approve option selection (the process/session lifecycle is exercised
/// end-to-end by hand against a real agent, per the feature's test plan).</summary>
public class AcpAgentHostTests
{
    [Fact]
    public void PickAutoApproveOption_prefers_allow_once_over_other_options()
    {
        var options = new[]
        {
            new AcpPermissionOption("a1", "Allow always", "allow_always"),
            new AcpPermissionOption("a2", "Allow once", "allow_once"),
            new AcpPermissionOption("r1", "Reject once", "reject_once"),
        };

        Assert.Equal("a2", AcpAgentHost.PickAutoApproveOption(options));
    }

    [Fact]
    public void PickAutoApproveOption_falls_back_to_any_allow_option()
    {
        var options = new[]
        {
            new AcpPermissionOption("r1", "Reject once", "reject_once"),
            new AcpPermissionOption("a1", "Allow always", "allow_always"),
        };

        Assert.Equal("a1", AcpAgentHost.PickAutoApproveOption(options));
    }

    [Fact]
    public void PickAutoApproveOption_uses_the_first_option_when_nothing_allows()
    {
        var options = new[]
        {
            new AcpPermissionOption("r1", "Reject once", "reject_once"),
            new AcpPermissionOption("r2", "Reject always", "reject_always"),
        };

        Assert.Equal("r1", AcpAgentHost.PickAutoApproveOption(options));
    }

    [Fact]
    public void PickAutoApproveOption_returns_null_for_an_empty_option_list()
    {
        Assert.Null(AcpAgentHost.PickAutoApproveOption([]));
    }

    // ── BuildMcpServers ──

    private static JsonElement[] SerializeServers(AgentProfile profile, AcpAgentCapabilities caps, string? mcpUrl)
        => AcpAgentHost.BuildMcpServers(profile, caps, mcpUrl, out _)
            .Select(s => JsonSerializer.SerializeToElement(s))
            .ToArray();

    private static AcpAgentCapabilities HttpCaps() => new() { McpHttp = true };

    [Fact]
    public void BuildMcpServers_puts_the_swebkit_bridge_first()
    {
        var profile = new AgentProfile
        {
            ExtraMcpServers =
            [
                new AgentMcpServer { Name = "azure", Url = "https://mcp.example.com/mcp" },
            ],
        };

        var servers = SerializeServers(profile, HttpCaps(), "http://127.0.0.1:5199/mcp/swebkit-tools?tools=a");

        Assert.Equal(2, servers.Length);
        Assert.Equal("swebkit", servers[0].GetProperty("name").GetString());
        Assert.Equal("azure", servers[1].GetProperty("name").GetString());
        Assert.Equal("http", servers[1].GetProperty("type").GetString());
        Assert.Equal("https://mcp.example.com/mcp", servers[1].GetProperty("url").GetString());
        Assert.Equal(JsonValueKind.Array, servers[1].GetProperty("headers").ValueKind);
    }

    [Fact]
    public void BuildMcpServers_serializes_stdio_servers_with_split_args_and_env()
    {
        var profile = new AgentProfile
        {
            ExtraMcpServers =
            [
                new AgentMcpServer
                {
                    Name = "local",
                    Transport = "stdio",
                    Command = "npx",
                    Arguments = "-y \"@scope/server\" --flag",
                    EnvironmentVariables = new() { ["LOG_LEVEL"] = "debug" },
                },
            ],
        };

        var servers = SerializeServers(profile, HttpCaps(), null);

        var server = Assert.Single(servers);
        Assert.Equal("stdio", server.GetProperty("type").GetString());
        Assert.Equal("npx", server.GetProperty("command").GetString());
        Assert.Equal(
            new[] { "-y", "@scope/server", "--flag" },
            server.GetProperty("args").EnumerateArray().Select(a => a.GetString()).ToArray());
        var env = server.GetProperty("env").EnumerateArray().Single();
        Assert.Equal("LOG_LEVEL", env.GetProperty("name").GetString());
        Assert.Equal("debug", env.GetProperty("value").GetString());
    }

    [Fact]
    public void BuildMcpServers_skips_disabled_and_invalid_entries_with_warnings()
    {
        var profile = new AgentProfile
        {
            ExtraMcpServers =
            [
                new AgentMcpServer { Name = "off", Enabled = false, Url = "https://a.example.com" },
                new AgentMcpServer { Name = "", Url = "https://b.example.com" },
                new AgentMcpServer { Name = "badurl", Url = "not-a-url" },
                new AgentMcpServer { Name = "nocommand", Transport = "stdio", Command = "" },
                new AgentMcpServer { Name = "good", Url = "https://c.example.com" },
            ],
        };

        var servers = AcpAgentHost.BuildMcpServers(profile, HttpCaps(), null, out var warnings)
            .Select(s => JsonSerializer.SerializeToElement(s))
            .ToArray();

        var server = Assert.Single(servers);
        Assert.Equal("good", server.GetProperty("name").GetString());
        Assert.Equal(3, warnings.Count);
    }

    [Fact]
    public void BuildMcpServers_drops_http_extras_when_the_agent_lacks_http_mcp()
    {
        var profile = new AgentProfile
        {
            ExtraMcpServers =
            [
                new AgentMcpServer { Name = "azure", Url = "https://mcp.example.com/mcp" },
                new AgentMcpServer
                {
                    Name = "local",
                    Transport = "stdio",
                    Command = "srv",
                },
            ],
        };

        // stdio extras still go through — only http ones need the advertised capability.
        var servers = SerializeServers(profile, new AcpAgentCapabilities { McpHttp = false },
            "http://127.0.0.1:5199/mcp/swebkit-tools?tools=a");

        var server = Assert.Single(servers);
        Assert.Equal("local", server.GetProperty("name").GetString());
    }

    [Fact]
    public void BuildMcpServers_forwards_http_headers_as_name_value_pairs()
    {
        var profile = new AgentProfile
        {
            ExtraMcpServers =
            [
                new AgentMcpServer
                {
                    Name = "azure",
                    Url = "https://mcp.example.com/mcp",
                    Headers = new() { ["X-Env"] = "prod" },
                },
            ],
        };

        var servers = SerializeServers(profile, HttpCaps(), null);

        var header = servers[0].GetProperty("headers").EnumerateArray().Single();
        Assert.Equal("X-Env", header.GetProperty("name").GetString());
        Assert.Equal("prod", header.GetProperty("value").GetString());
    }
}
