using System.Text.Json;
using SwebKit.Core.Security;

namespace SwebKit.Core.Tests;

/// <summary>Parsing of <c>{"status":"access_denied"}</c> tool results into
/// <see cref="AccessGap"/>s (agent-colleague item 2): the wire shape
/// <c>AgentToolRegistry</c> emits, URI sanitization of SDK detail strings, and
/// best-effort resource extraction from the denied call's arguments.</summary>
public class AccessGapParserTests
{
    private static readonly JsonElement EmptyArgs = JsonDocument.Parse("{}").RootElement;

    private const string DenialJson = """
        {"status":"access_denied","capability":"service-bus.data",
         "featureArea":"ServiceBus","requiredAccess":"Azure Service Bus Data Receiver",
         "guidance":"Ask a resource owner to assign the role.",
         "detail":"The request failed with status 403."}
        """;

    [Fact]
    public void TryParse_AccessDeniedResult_ProducesAGap()
    {
        var ok = AccessGapParser.TryParse("get_queue", EmptyArgs, DenialJson, out var gap);

        Assert.True(ok);
        Assert.Equal("ServiceBus", gap.FeatureArea);
        Assert.Equal("service-bus.data", gap.Capability);
        Assert.Equal("Azure Service Bus Data Receiver", gap.RequiredAccess);
        Assert.Equal("Ask a resource owner to assign the role.", gap.Guidance);
        Assert.Contains("403", gap.Detail);
        Assert.Equal("get_queue", gap.Tool);
    }

    [Theory]
    [InlineData("""{"status":"ok"}""")]
    [InlineData("""{"error":"boom"}""")]
    [InlineData("""{"status":"access_denied-ish"}""")]
    [InlineData("not json at all")]
    [InlineData("")]
    [InlineData("[]")]
    [InlineData("""["access_denied"]""")]
    public void TryParse_NonDenialOutput_ReturnsFalse(string result)
    {
        Assert.False(AccessGapParser.TryParse("t", EmptyArgs, result, out _));
    }

    [Fact]
    public void TryParse_CachedDenialVariant_StillParses()
    {
        // AgentToolRegistry's known-denial short-circuit adds "cached": true.
        var cached = DenialJson.Replace("\"detail\":", "\"cached\":true,\"detail\":");
        Assert.True(AccessGapParser.TryParse("t", EmptyArgs, cached, out var gap));
        Assert.Equal("ServiceBus", gap.FeatureArea);
    }

    [Fact]
    public void TryParse_RecordEquality_TwoIdenticalDenials_AreEqualForDedup()
    {
        var args = JsonDocument.Parse("""{"namespace":"prod","entity_path":"orders"}""").RootElement;
        AccessGapParser.TryParse("t", args, DenialJson, out var first);
        AccessGapParser.TryParse("t", args, DenialJson, out var second);

        Assert.Equal(first, second); // record equality — the dedup the orchestrator relies on
        var list = new List<AccessGap>();
        if (!list.Contains(first)) list.Add(first);
        if (!list.Contains(second)) list.Add(second);
        Assert.Single(list);
    }

    [Fact]
    public void TryParse_UrisInDetail_ReducedToHost()
    {
        var json = """
            {"status":"access_denied","capability":"x","featureArea":"ServiceBus",
             "requiredAccess":"r","guidance":"g",
             "detail":"Request to https://prod-sb.servicebus.windows.net/orders/messages?sig=SECRET failed with 403."}
            """;

        AccessGapParser.TryParse("t", EmptyArgs, json, out var gap);

        Assert.Contains("prod-sb.servicebus.windows.net", gap.Detail);
        Assert.DoesNotContain("SECRET", gap.Detail);
        Assert.DoesNotContain("/orders/messages", gap.Detail);
    }

    [Theory]
    // k8s-style: namespace scopes the object
    [InlineData("""{"namespace":"prod","pod_name":"api-7c9f"}""", "prod/api-7c9f")]
    // Service Bus: namespace + entity path
    [InlineData("""{"namespace":"prod-sb","entity_path":"orders"}""", "prod-sb/orders")]
    // connection-scoped SQL: connection + table
    [InlineData("""{"connection_id":"sql-1","table":"dbo.Orders"}""", "sql-1/dbo.Orders")]
    // cache only
    [InlineData("""{"cache_id":"prod-cache"}""", "prod-cache")]
    // object without scope
    [InlineData("""{"entity_path":"orders"}""", "orders")]
    public void TryParse_Resource_ExtractedFromArgs(string argsJson, string expected)
    {
        var args = JsonDocument.Parse(argsJson).RootElement;
        AccessGapParser.TryParse("t", args, DenialJson, out var gap);
        Assert.Equal(expected, gap.Resource);
    }

    [Fact]
    public void TryParse_NoUsableArgs_ResourceIsNull()
    {
        var args = JsonDocument.Parse("""{"limit":5}""").RootElement;
        AccessGapParser.TryParse("t", args, DenialJson, out var gap);
        Assert.Null(gap.Resource);
    }

    [Fact]
    public void TryParse_BlankToolName_ToolIsNull()
    {
        AccessGapParser.TryParse("", EmptyArgs, DenialJson, out var gap);
        Assert.Null(gap.Tool);
    }

    [Fact]
    public void SanitizeDetail_LongDetail_Capped()
    {
        var detail = new string('x', 600);
        var sanitized = AccessGapParser.SanitizeDetail(detail);
        Assert.Equal(501, sanitized.Length); // 500 + ellipsis
    }
}
