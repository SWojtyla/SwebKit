using System.ComponentModel;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ModelContextProtocol.AspNetCore;
using ModelContextProtocol.Server;
using SwebKit.Core.Domain;
using SwebKit.Sidecar.Services;
using Xunit;

namespace SwebKit.Sidecar.Tests;

/// <summary>
/// Real-wire integration test for <see cref="ExternalMcpToolSource"/>: hosts an actual MCP server
/// on an ephemeral loopback port using the same <c>AddMcpServer/MapMcp</c> stack the sidecar's own
/// bridge uses, then exercises the un-faked path — <see cref="HttpClientTransport"/> construction,
/// the MCP handshake, <c>tools/list</c> annotation filtering, and <c>tools/call</c>. Unit tests use
/// <see cref="IMcpServerConnection"/> fakes; this proves the transport code itself.
/// </summary>
public sealed class ExternalMcpToolSourceWireTests : IAsyncLifetime
{
    private WebApplication _app = null!;
    private string _mcpUrl = null!;

    [McpServerToolType]
    public sealed class StubTools
    {
        [McpServerTool(Name = "list_regions", ReadOnly = true), Description("Lists Azure regions")]
        public static string ListRegions(string? filter = null) =>
            $"[\"westeurope\",\"eastus\"]|filter={filter}";

        // Deliberately unannotated — the adapter must surface it as a Mutate proposal, not a direct call.
        [McpServerTool(Name = "delete_region")]
        public static string DeleteRegion() => "should never be callable";
    }

    public async Task InitializeAsync()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddMcpServer()
            .WithHttpTransport(o => o.SessionMode = HttpServerSessionMode.Stateless)
            .WithTools<StubTools>();

        _app = builder.Build();
        _app.MapMcp("/mcp");
        await _app.StartAsync();

        var addresses = _app.Services.GetRequiredService<IServer>()
            .Features.Get<IServerAddressesFeature>()!.Addresses;
        _mcpUrl = addresses.Single() + "/mcp";
    }

    public async Task DisposeAsync() => await _app.DisposeAsync();

    [Fact]
    public async Task Real_http_wire_classifies_annotations_and_executes_read_tools()
    {
        await using var source = new ExternalMcpToolSource(NullLogger<ExternalMcpToolSource>.Instance, new SwebKit.Agents.AgentActionCoordinator());
        var profile = new AgentProfile
        {
            DisplayName = "local",
            ExtraMcpServers = [new AgentMcpServer
            {
                Name = "stub",
                Transport = "http",
                Url = _mcpUrl,
            }],
        };

        var bindings = await source.GetToolsAsync(profile, CancellationToken.None);

        // delete_region is unannotated → exposed as a Mutate proposal, never a direct call.
        Assert.Equal(2, bindings.Count);
        var readBinding = Assert.Single(bindings, b => b.Definition.Name == "mcp_stub_list_regions");
        Assert.Equal(SwebKit.Agents.Tools.ToolKind.Read, readBinding.Definition.Kind);
        var mutateBinding = Assert.Single(bindings, b => b.Definition.Name == "mcp_stub_delete_region");
        Assert.Equal(SwebKit.Agents.Tools.ToolKind.Mutate, mutateBinding.Definition.Kind);

        var args = JsonDocument.Parse("{\"filter\":\"eu\"}").RootElement;
        var result = await readBinding.Execute(args, CancellationToken.None);

        Assert.Contains("westeurope", result);
        Assert.Contains("filter=eu", result);
    }

    [Fact]
    public async Task Real_http_wire_unreachable_server_is_skipped_not_fatal()
    {
        await using var source = new ExternalMcpToolSource(NullLogger<ExternalMcpToolSource>.Instance, new SwebKit.Agents.AgentActionCoordinator());
        var profile = new AgentProfile
        {
            DisplayName = "local",
            ExtraMcpServers = [
                new AgentMcpServer { Name = "dead", Transport = "http", Url = "http://127.0.0.1:1/mcp" },
                new AgentMcpServer { Name = "stub", Transport = "http", Url = _mcpUrl },
            ],
        };

        var bindings = await source.GetToolsAsync(profile, CancellationToken.None);

        Assert.Equal(2, bindings.Count); // the live server still contributed its tools
        Assert.All(bindings, b => Assert.StartsWith("mcp_stub_", b.Definition.Name));
    }
}
