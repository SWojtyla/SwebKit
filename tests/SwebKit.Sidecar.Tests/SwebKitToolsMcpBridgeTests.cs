using System.Text.Json;
using Microsoft.AspNetCore.Http;
using ModelContextProtocol.Protocol;
using SwebKit.Agents;
using SwebKit.Agents.Tools;
using SwebKit.Sidecar.Services.Acp;

namespace SwebKit.Sidecar.Tests;

/// <summary>Covers the allowlist filtering and dispatch in <see cref="SwebKitToolsMcpBridge"/> —
/// the piece that decides which SwebKit tools an external ACP agent can see and call.</summary>
public class SwebKitToolsMcpBridgeTests
{
    private sealed class FakeToolRegistry : IAgentToolRegistry
    {
        private readonly List<ToolDefinition> _defs;

        public FakeToolRegistry(params ToolDefinition[] defs) => _defs = defs.ToList();

        public string? LastCalledName { get; private set; }
        public JsonElement LastCalledArgs { get; private set; }
        public IReadOnlyDictionary<string, string>? CapturedSelection { get; private set; }
        public string Result { get; set; } = "{\"ok\":true}";

        public IReadOnlyList<ToolDefinition> GetDefinitions() => _defs;

        public Task<string> ExecuteAsync(string toolName, JsonElement arguments, CancellationToken ct)
        {
            LastCalledName = toolName;
            LastCalledArgs = arguments;
            CapturedSelection = AgentExecutionContext.Selection;
            return Task.FromResult(Result);
        }
    }

    private static ToolDefinition Def(string name, FeatureArea area = FeatureArea.Aks, ToolKind kind = ToolKind.Read) =>
        new()
        {
            Name = name,
            Description = $"Does {name}",
            ParametersSchema = AgentToolSchema.Parse("{\"type\":\"object\",\"properties\":{}}"),
            Kind = kind,
            FeatureArea = area,
        };

    private static SwebKitToolsMcpBridge Bridge(FakeToolRegistry registry, OutOfScopeCallTracker? tracker = null) =>
        new(registry, new HttpContextAccessor(), tracker ?? new OutOfScopeCallTracker());

    [Fact]
    public void BuildUrl_appends_the_allowlist_as_a_query_param()
    {
        var url = SwebKitToolsMcpBridge.BuildUrl("http://127.0.0.1:5199", ["list_pods", "get_pod_logs"]);

        Assert.Equal("http://127.0.0.1:5199/mcp/swebkit-tools?tools=list_pods%2Cget_pod_logs", url);
    }

    [Fact]
    public void BuildUrl_appends_selection_without_colliding_with_tool_arguments()
    {
        var url = SwebKitToolsMcpBridge.BuildUrl(
            "http://127.0.0.1:5199",
            ["list_pods"],
            new Dictionary<string, string> { ["namespace"] = "team a/dev" });

        Assert.Equal("http://127.0.0.1:5199/mcp/swebkit-tools?tools=list_pods&sel=namespace%3Dteam%20a%2Fdev", url);
    }

    [Fact]
    public void BuildUrl_omits_the_query_param_for_an_empty_or_missing_allowlist()
    {
        Assert.Equal("http://127.0.0.1:5199/mcp/swebkit-tools",
            SwebKitToolsMcpBridge.BuildUrl("http://127.0.0.1:5199/", null));
        Assert.Equal("http://127.0.0.1:5199/mcp/swebkit-tools",
            SwebKitToolsMcpBridge.BuildUrl("http://127.0.0.1:5199", []));
    }

    [Fact]
    public void ParseAllowedSet_returns_null_for_absent_or_blank_values()
    {
        Assert.Null(SwebKitToolsMcpBridge.ParseAllowedSet(null));
        Assert.Null(SwebKitToolsMcpBridge.ParseAllowedSet("  "));
    }

    [Fact]
    public void ParseAllowedSet_splits_trims_and_is_case_insensitive()
    {
        var set = SwebKitToolsMcpBridge.ParseAllowedSet("list_pods, get_pod_logs ,");

        Assert.NotNull(set);
        Assert.Equal(2, set!.Count);
        Assert.Contains("LIST_PODS", set);
    }

    [Fact]
    public void ParseSelection_reconstructs_key_value_pairs()
    {
        var selection = SwebKitToolsMcpBridge.ParseSelection(["namespace=team a/dev", "pod=worker-0"]);

        Assert.Equal("team a/dev", selection!["namespace"]);
        Assert.Equal("worker-0", selection["pod"]);
    }

    [Fact]
    public void ListTools_returns_everything_when_no_allowlist_applies()
    {
        var bridge = Bridge(new FakeToolRegistry(Def("list_pods"), Def("list_namespaces")));

        var tools = bridge.ListTools(null);

        Assert.Equal(["list_pods", "list_namespaces"], tools.Select(t => t.Name));
        Assert.Equal(JsonValueKind.Object, tools[0].InputSchema.ValueKind);
    }

    [Fact]
    public void ListTools_filters_to_the_allowlist()
    {
        var bridge = Bridge(new FakeToolRegistry(Def("list_pods"), Def("list_namespaces")));
        var allowed = SwebKitToolsMcpBridge.ParseAllowedSet("list_pods");

        var tools = bridge.ListTools(allowed);

        Assert.Single(tools);
        Assert.Equal("list_pods", tools[0].Name);
    }

    [Fact]
    public async Task CallTool_dispatches_to_the_registry_and_wraps_the_result_as_text()
    {
        var registry = new FakeToolRegistry(Def("list_pods"));
        var bridge = Bridge(registry);
        var args = JsonDocument.Parse("{\"namespace\":\"default\"}").RootElement;

        var result = await bridge.CallToolAsync("list_pods", args, null, null, CancellationToken.None);

        Assert.Equal("list_pods", registry.LastCalledName);
        Assert.Equal("default", registry.LastCalledArgs.GetProperty("namespace").GetString());
        Assert.False(result.IsError);
        Assert.Equal("{\"ok\":true}", Assert.IsType<TextContentBlock>(result.Content[0]).Text);
    }

    [Fact]
    public async Task CallTool_scopes_selection_to_the_registry_call()
    {
        var registry = new FakeToolRegistry(Def("list_pods"));
        var bridge = Bridge(registry);
        var selection = new Dictionary<string, string> { ["namespace"] = "dev" };

        await bridge.CallToolAsync("list_pods", default, null, null, CancellationToken.None, selection);

        Assert.Equal("dev", registry.CapturedSelection!["namespace"]);
        Assert.Null(AgentExecutionContext.Selection);
    }

    [Fact]
    public async Task CallTool_marks_registry_error_payloads_as_errors()
    {
        var registry = new FakeToolRegistry(Def("list_pods")) { Result = "{\"error\":\"boom\"}" };
        var bridge = Bridge(registry);

        var result = await bridge.CallToolAsync("list_pods", default, null, null, CancellationToken.None);

        Assert.True(result.IsError);
    }

    [Fact]
    public async Task CallTool_refuses_tools_outside_the_allowlist_without_calling_the_registry()
    {
        var registry = new FakeToolRegistry(Def("list_pods"), Def("propose_delete", kind: ToolKind.Mutate));
        var bridge = Bridge(registry);
        var allowed = SwebKitToolsMcpBridge.ParseAllowedSet("list_pods");

        var result = await bridge.CallToolAsync("propose_delete", default, allowed, "list_pods", CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Null(registry.LastCalledName);
        var payload = JsonDocument.Parse(Assert.IsType<TextContentBlock>(result.Content[0]).Text).RootElement;
        Assert.Equal("tool_out_of_scope", payload.GetProperty("error").GetString());
        Assert.Equal("propose_delete", payload.GetProperty("tool").GetString());
        Assert.Contains("Search across my whole workspace", payload.GetProperty("message").GetString());
    }

    [Fact]
    public async Task CallTool_out_of_scope_call_increments_the_tracker_under_the_allowlist_key()
    {
        var tracker = new OutOfScopeCallTracker();
        var registry = new FakeToolRegistry(Def("list_pods"), Def("analyze_queue_health", FeatureArea.ServiceBus));
        var bridge = Bridge(registry, tracker);
        var allowed = SwebKitToolsMcpBridge.ParseAllowedSet("list_pods");

        await bridge.CallToolAsync("analyze_queue_health", default, allowed, "list_pods", CancellationToken.None);
        await bridge.CallToolAsync("analyze_queue_health", default, allowed, "list_pods", CancellationToken.None);
        // A different allowlist (a different turn's scope) counts separately.
        await bridge.CallToolAsync("analyze_queue_health", default, allowed, "list_pods,get_pod_logs", CancellationToken.None);

        Assert.Equal(2, tracker.CountFor("list_pods"));
        Assert.Equal(1, tracker.CountFor("list_pods,get_pod_logs"));
        Assert.Equal(0, tracker.CountFor("something-else"));
    }

    [Fact]
    public async Task CallTool_refuses_unknown_tools()
    {
        var tracker = new OutOfScopeCallTracker();
        var registry = new FakeToolRegistry(Def("list_pods"));
        var bridge = Bridge(registry, tracker);

        var result = await bridge.CallToolAsync("nope", default, null, "list_pods", CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Null(registry.LastCalledName);
        // Unknown tools are not scope-fence hits — they keep the generic message and don't tick the tracker.
        Assert.Contains("not available", Assert.IsType<TextContentBlock>(result.Content[0]).Text);
        Assert.Equal(0, tracker.CountFor("list_pods"));
    }
}
